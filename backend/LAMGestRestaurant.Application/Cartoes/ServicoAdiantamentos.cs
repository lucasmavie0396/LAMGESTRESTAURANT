using LAMGestRestaurant.Application.Comum;
using LAMGestRestaurant.Domain.Cartoes;
using Microsoft.EntityFrameworkCore;

namespace LAMGestRestaurant.Application.Cartoes;

public sealed record CartaoDoCliente(int Id, string Numero, decimal Saldo, bool Activo, DateTime? UltimoMovimentoUtc);

/// <summary><see cref="Saldo"/>: saldo da conta de adiantamentos do cliente depois deste movimento (todos os cartões dele).</summary>
public sealed record MovimentoAdiantamento(int Id, DateTime DataUtc, string Cartao, TipoMovimentoCartao Tipo, decimal Valor, decimal Saldo,
    string? Metodo, string? Pedido, string? Motivo, string? Operador);

/// <summary>
/// Extrato da conta de adiantamentos do cliente num período: saldo anterior, recargas (entradas), consumos (pagamentos com o
/// cartão menos anulações e estornos), outros (descargas, transferências, mudanças de cliente) e saldo final; mais os cartões
/// do cliente e o saldo de hoje.
/// </summary>
public sealed record ExtratoAdiantamentos(int ClienteId, string ClienteNome, string? ClienteNuit, string? ClienteTelefone, bool ClienteActivo,
    DateOnly Desde, DateOnly Ate, decimal SaldoAnterior, decimal Recargas, decimal Consumos, decimal Outros, decimal SaldoFinal,
    IReadOnlyList<MovimentoAdiantamento> Movimentos, bool Cortado, decimal SaldoActual, IReadOnlyList<CartaoDoCliente> Cartoes);

public sealed record LinhaAdiantamentoCliente(int ClienteId, string Cliente, string? Telefone, int Cartoes, decimal SaldoInicial, decimal Recargas,
    decimal Consumos, decimal Outros, decimal SaldoFinal, DateTime? UltimaRecargaUtc, DateTime? UltimoConsumoUtc);

public sealed record LinhaOperadorRecargas(string Operador, decimal Valor, int Quantidade);

/// <summary>
/// Adiantamentos dos clientes no período: por cliente (saldo no início, recargas, consumos, outros e saldo no fim) e as recargas
/// por método e por operador. O saldo final total é o que o restaurante deve aos clientes em consumos.
/// </summary>
public sealed record RelatorioAdiantamentos(DateOnly Desde, DateOnly Ate, decimal SaldoInicial, decimal Recargas, decimal Consumos, decimal Outros,
    decimal SaldoFinal, IReadOnlyList<LinhaAdiantamentoCliente> Clientes, IReadOnlyList<LinhaMetodoCartoes> RecargasPorMetodo,
    IReadOnlyList<LinhaOperadorRecargas> RecargasPorOperador);

/// <summary>
/// Conta de adiantamentos dos clientes: a soma dos movimentos dos cartões com o cliente do momento (ver
/// <see cref="MovimentoCartao.ClienteId"/>). Nada é guardado à parte. Fica separada da conta corrente das facturas a crédito
/// (documentos fiscais): a recarga não é venda, a VD sai no consumo.
/// </summary>
public class ServicoAdiantamentos(IAppDbContext db, ConfiguracoesSistema config, IConsultaUtilizadores utilizadores)
{
    public const int MaximoLinhas = 5000;
    private const int MaximoDias = 3660;

    private static readonly TipoMovimentoCartao[] TiposRecarga = [TipoMovimentoCartao.Carregamento, TipoMovimentoCartao.Recarga];
    private static readonly TipoMovimentoCartao[] TiposConsumo = [TipoMovimentoCartao.Pagamento, TipoMovimentoCartao.AnulacaoPagamento, TipoMovimentoCartao.Estorno];

    public async Task<ExtratoAdiantamentos> ExtratoAsync(int clienteId, DateOnly desde, DateOnly ate, CancellationToken ct = default)
    {
        Validar(desde, ate);
        var cliente = await db.Clientes.AsNoTracking().FirstOrDefaultAsync(c => c.Id == clienteId, ct) ?? throw new NaoEncontradoException("Cliente não encontrado.");
        var (inicio, fim) = await config.IntervaloUtcAsync(desde, ate);
        var conta = db.MovimentosCartao.AsNoTracking().Where(m => m.ClienteId == clienteId);

        var anterior = await conta.Where(m => m.DataUtc < inicio).SumAsync(m => m.Valor, ct);
        var periodo = conta.Where(m => m.DataUtc >= inicio && m.DataUtc < fim);
        var porTipo = await periodo.GroupBy(m => m.Tipo).Select(g => new { Tipo = g.Key, Valor = g.Sum(m => m.Valor) }).ToListAsync(ct);
        var recargas = porTipo.Where(t => TiposRecarga.Contains(t.Tipo)).Sum(t => t.Valor);
        var consumos = -porTipo.Where(t => TiposConsumo.Contains(t.Tipo)).Sum(t => t.Valor);
        var outros = porTipo.Where(t => !TiposRecarga.Contains(t.Tipo) && !TiposConsumo.Contains(t.Tipo)).Sum(t => t.Valor);

        var linhas = await periodo.Where(m => m.Valor != 0).OrderBy(m => m.DataUtc).ThenBy(m => m.Id).Take(MaximoLinhas + 1)
            .Select(m => new
            {
                m.Id, m.DataUtc, m.CartaoEvento!.Numero, m.Tipo, m.Valor, Metodo = m.MetodoPagamento != null ? m.MetodoPagamento.Nome : null,
                Pedido = db.Pedidos.Where(p => p.Id == m.PedidoId).Select(p => p.Numero).FirstOrDefault(), m.Motivo, m.UtilizadorId,
            })
            .ToListAsync(ct);
        var nomes = await utilizadores.NomesAsync(linhas.Select(l => l.UtilizadorId).Distinct(), ct);
        var saldo = anterior;
        var movimentos = linhas.Take(MaximoLinhas)
            .Select(l => new MovimentoAdiantamento(l.Id, l.DataUtc, l.Numero, l.Tipo, l.Valor, saldo += l.Valor, l.Metodo, l.Pedido, l.Motivo, nomes.GetValueOrDefault(l.UtilizadorId)))
            .ToList();

        var cartoes = await db.CartoesEvento.AsNoTracking().Where(c => c.ClienteId == clienteId).OrderByDescending(c => c.Activo).ThenBy(c => c.Numero)
            .Select(c => new CartaoDoCliente(c.Id, c.Numero, c.Saldo, c.Activo, db.MovimentosCartao.Where(m => m.CartaoEventoId == c.Id).Max(m => (DateTime?)m.DataUtc)))
            .ToListAsync(ct);
        var actual = await conta.SumAsync(m => m.Valor, ct);
        return new ExtratoAdiantamentos(cliente.Id, cliente.Nome, cliente.Nuit, cliente.Telefone, cliente.Activo, desde, ate,
            anterior, recargas, consumos, outros, anterior + recargas - consumos + outros, movimentos, linhas.Count > MaximoLinhas, actual, cartoes);
    }

    public async Task<RelatorioAdiantamentos> RelatorioAsync(DateOnly desde, DateOnly ate, CancellationToken ct = default)
    {
        Validar(desde, ate);
        var (inicio, fim) = await config.IntervaloUtcAsync(desde, ate);
        var ate_ = db.MovimentosCartao.AsNoTracking().Where(m => m.ClienteId != null && m.DataUtc < fim);
        var porCliente = await ate_.GroupBy(m => m.ClienteId!.Value).Select(g => new
        {
            ClienteId = g.Key,
            Inicial = g.Sum(m => m.DataUtc < inicio ? m.Valor : 0m),
            Recargas = g.Sum(m => m.DataUtc >= inicio && (m.Tipo == TipoMovimentoCartao.Carregamento || m.Tipo == TipoMovimentoCartao.Recarga) ? m.Valor : 0m),
            Consumos = -g.Sum(m => m.DataUtc >= inicio && (m.Tipo == TipoMovimentoCartao.Pagamento || m.Tipo == TipoMovimentoCartao.AnulacaoPagamento || m.Tipo == TipoMovimentoCartao.Estorno) ? m.Valor : 0m),
            Final = g.Sum(m => m.Valor),
            NoPeriodo = g.Count(m => m.DataUtc >= inicio && m.Valor != 0),
            UltimaRecarga = g.Max(m => m.Tipo == TipoMovimentoCartao.Carregamento || m.Tipo == TipoMovimentoCartao.Recarga ? (DateTime?)m.DataUtc : null),
            UltimoConsumo = g.Max(m => m.Tipo == TipoMovimentoCartao.Pagamento ? (DateTime?)m.DataUtc : null),
        }).ToListAsync(ct);
        porCliente = [.. porCliente.Where(c => c.Final != 0 || c.NoPeriodo > 0)];

        var ids = porCliente.Select(c => c.ClienteId).ToList();
        var clientes = await db.Clientes.AsNoTracking().Where(c => ids.Contains(c.Id)).Select(c => new { c.Id, c.Nome, c.Telefone }).ToDictionaryAsync(c => c.Id, ct);
        var cartoes = await db.CartoesEvento.AsNoTracking().Where(c => c.ClienteId != null && ids.Contains(c.ClienteId.Value))
            .GroupBy(c => c.ClienteId!.Value).Select(g => new { Id = g.Key, N = g.Count() }).ToDictionaryAsync(c => c.Id, c => c.N, ct);
        var linhas = porCliente
            .Select(c => new LinhaAdiantamentoCliente(c.ClienteId, clientes.GetValueOrDefault(c.ClienteId)?.Nome ?? "?", clientes.GetValueOrDefault(c.ClienteId)?.Telefone,
                cartoes.GetValueOrDefault(c.ClienteId), c.Inicial, c.Recargas, c.Consumos, c.Final - c.Inicial - c.Recargas + c.Consumos, c.Final, c.UltimaRecarga, c.UltimoConsumo))
            .OrderByDescending(l => l.SaldoFinal).ThenBy(l => l.Cliente).ToList();

        var recargasPeriodo = db.MovimentosCartao.AsNoTracking()
            .Where(m => m.ClienteId != null && m.DataUtc >= inicio && m.DataUtc < fim && (m.Tipo == TipoMovimentoCartao.Carregamento || m.Tipo == TipoMovimentoCartao.Recarga) && m.Valor > 0);
        var porMetodo = await recargasPeriodo.Where(m => m.MetodoPagamentoId != null)
            .GroupBy(m => m.MetodoPagamento!.Nome).Select(g => new LinhaMetodoCartoes(g.Key, g.Sum(m => m.Valor), g.Count())).ToListAsync(ct);
        var porOperador = await recargasPeriodo.GroupBy(m => m.UtilizadorId).Select(g => new { Id = g.Key, Valor = g.Sum(m => m.Valor), N = g.Count() }).ToListAsync(ct);
        var nomes = await utilizadores.NomesAsync(porOperador.Select(o => o.Id), ct);

        return new RelatorioAdiantamentos(desde, ate, linhas.Sum(l => l.SaldoInicial), linhas.Sum(l => l.Recargas), linhas.Sum(l => l.Consumos),
            linhas.Sum(l => l.Outros), linhas.Sum(l => l.SaldoFinal), linhas,
            [.. porMetodo.OrderByDescending(m => m.Valor)],
            [.. porOperador.Select(o => new LinhaOperadorRecargas(nomes.GetValueOrDefault(o.Id) ?? "—", o.Valor, o.N)).OrderByDescending(o => o.Valor)]);
    }

    private static void Validar(DateOnly desde, DateOnly ate)
    {
        if (ate < desde)
            throw new RegraNegocioException("A data final é anterior à inicial.");
        if (ate.DayNumber - desde.DayNumber > MaximoDias)
            throw new RegraNegocioException("O período não pode passar de 10 anos.");
    }
}
