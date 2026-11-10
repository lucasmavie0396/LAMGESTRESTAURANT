using LAMGestRestaurant.Application.Comum;
using LAMGestRestaurant.Domain.Facturacao;
using Microsoft.EntityFrameworkCore;

namespace LAMGestRestaurant.Application.Facturacao;

/// <summary>Movimento da conta corrente: a factura debita; o recibo e a nota de crédito sobre factura creditam.</summary>
public record MovimentoContaCorrente(int DocumentoId, TipoDocumento Tipo, string Numero, DateTime DataUtc, DateOnly? Vencimento,
    string Descricao, decimal Debito, decimal Credito, decimal Saldo);

/// <summary>
/// Extrato da conta corrente do cliente num período: saldo anterior, movimentos com saldo acumulado e saldo final;
/// mais a situação de hoje (facturas em aberto, em dívida e vencido).
/// </summary>
public record ExtratoContaCorrente(
    int ClienteId, string ClienteNome, string? ClienteNuit, string? ClienteTelefone, bool ClienteActivo,
    DateOnly Desde, DateOnly Ate, decimal SaldoAnterior, decimal Debitos, decimal Creditos, decimal SaldoFinal,
    IReadOnlyList<MovimentoContaCorrente> Movimentos,
    decimal EmDividaHoje, decimal VencidoHoje, IReadOnlyList<FacturaEmDivida> FacturasEmAberto);

/// <summary>Documento do histórico do cliente (todos os tipos).</summary>
public record LinhaHistoricoCliente(int Id, TipoDocumento Tipo, string Numero, EstadoDocumento Estado, DateTime EmitidoEmUtc, DateOnly? Vencimento,
    decimal Total, decimal ValorPago, decimal ValorCreditado, decimal EmDivida, string? Origem, string? MetodoPagamento);

/// <summary>Totais do histórico: só documentos em vigor (as anulações aparecem na lista, mas não somam).</summary>
public record TotaisHistoricoCliente(int Documentos, decimal VendasDinheiro, decimal Facturado, decimal Recebido, decimal Creditado, decimal EmDivida);

public record HistoricoCliente(int ClienteId, string ClienteNome, string? ClienteNuit, DateOnly Desde, DateOnly Ate,
    TotaisHistoricoCliente Totais, IReadOnlyList<LinhaHistoricoCliente> Documentos, bool Cortado);

public enum SituacaoFactura { Paga, Parcial, EmDivida, Vencida, Creditada }

public record LinhaFacturaCredito(int Id, string Numero, DateTime EmitidoEmUtc, DateOnly? Vencimento, int? ClienteId, string ClienteNome, string? ClienteNuit,
    decimal Total, decimal ValorPago, decimal ValorCreditado, decimal EmDivida, SituacaoFactura Situacao, int DiasAtraso);

public record ResumoFacturasCredito(int Facturas, decimal Facturado, decimal Pago, decimal Creditado, decimal EmDivida, decimal Vencido);

/// <summary>Dívida actual de um cliente, por antiguidade do atraso (dias depois do vencimento).</summary>
public record DividaCliente(int ClienteId, string ClienteNome, string? ClienteNuit, int Facturas, decimal EmDivida,
    decimal NaoVencido, decimal Ate30, decimal De31a60, decimal De61a90, decimal Mais90);

/// <summary>Facturas a crédito emitidas no período e, à parte, a dívida de hoje de todos os clientes.</summary>
public record RelatorioFacturasCredito(DateOnly Desde, DateOnly Ate, DateOnly Hoje, ResumoFacturasCredito Resumo,
    IReadOnlyList<LinhaFacturaCredito> Facturas, bool Cortado, IReadOnlyList<DividaCliente> Dividas, decimal TotalEmDivida, decimal TotalVencido);

/// <summary>
/// Conta corrente dos clientes (facturação a crédito). A conta só tem facturas (débito), recibos em vigor e notas de crédito
/// sobre facturas (crédito): as VD são pagas no acto e não passam pela conta. Recibos anulados não contam (a anulação devolve
/// o valor à factura). O saldo é sempre calculado a partir dos documentos — nada é guardado à parte.
/// </summary>
public class ServicoContaCorrente(IAppDbContext db, ConfiguracoesSistema config, TimeProvider relogio)
{
    public const int MaximoLinhas = 5000;
    private const int MaximoDias = 3660;

    public async Task<ExtratoContaCorrente> ExtratoAsync(int clienteId, DateOnly desde, DateOnly ate, CancellationToken ct = default)
    {
        Validar(desde, ate, MaximoDias);
        var cliente = await db.Clientes.AsNoTracking().FirstOrDefaultAsync(c => c.Id == clienteId, ct) ?? throw new NaoEncontradoException("Cliente não encontrado.");
        var (inicio, fim) = await config.IntervaloUtcAsync(desde, ate);
        var conta = Conta(clienteId);

        var anterior = await conta.Where(d => d.EmitidoEmUtc < inicio)
            .SumAsync(d => d.Tipo == TipoDocumento.Factura ? d.Total : -d.Total, ct);
        var documentos = await conta.Where(d => d.EmitidoEmUtc >= inicio && d.EmitidoEmUtc < fim)
            .OrderBy(d => d.EmitidoEmUtc).ThenBy(d => d.Id).Take(MaximoLinhas)
            .Select(d => new
            {
                d.Id, d.Tipo, d.Numero, d.EmitidoEmUtc, d.Vencimento, d.Total, d.Referencia,
                Origem = d.DocumentoOrigem != null ? d.DocumentoOrigem.Numero : null,
                Metodo = d.MetodoPagamento != null ? d.MetodoPagamento.Nome : null,
                Pagas = d.Liquidacoes.Select(l => l.Factura.Numero).ToList(),
            })
            .ToListAsync(ct);

        var saldo = anterior;
        var movimentos = new List<MovimentoContaCorrente>(documentos.Count);
        foreach (var d in documentos)
        {
            var debito = d.Tipo == TipoDocumento.Factura ? d.Total : 0m;
            var credito = d.Tipo == TipoDocumento.Factura ? 0m : d.Total;
            saldo += debito - credito;
            var descricao = d.Tipo switch
            {
                TipoDocumento.Factura => "Factura",
                TipoDocumento.Recibo => $"Recibo{(d.Metodo is null ? "" : $" ({d.Metodo})")}{(d.Pagas.Count > 0 ? " · " + string.Join(", ", d.Pagas) : "")}",
                _ => $"Nota de crédito{(d.Origem is null ? "" : " · " + d.Origem)}",
            };
            movimentos.Add(new(d.Id, d.Tipo, d.Numero, d.EmitidoEmUtc, d.Vencimento, descricao, debito, credito, saldo));
        }

        var hoje = await config.HojeAsync(relogio);
        var abertas = await db.Documentos.AsNoTracking()
            .Where(d => d.ClienteId == clienteId && d.Tipo == TipoDocumento.Factura && d.Estado == EstadoDocumento.Emitido && d.Total - d.ValorPago - d.ValorCreditado > 0)
            .OrderBy(d => d.EmitidoEmUtc).ThenBy(d => d.Id)
            .Select(d => new FacturaEmDivida(d.Id, d.Numero, d.EmitidoEmUtc, d.Vencimento, d.Total, d.ValorPago, d.ValorCreditado,
                d.Total - d.ValorPago - d.ValorCreditado, d.Vencimento != null && d.Vencimento < hoje))
            .ToListAsync(ct);

        return new ExtratoContaCorrente(cliente.Id, cliente.Nome, cliente.Nuit, cliente.Telefone, cliente.Activo, desde, ate,
            anterior, movimentos.Sum(m => m.Debito), movimentos.Sum(m => m.Credito), saldo, movimentos,
            abertas.Sum(f => f.EmDivida), abertas.Where(f => f.Vencida).Sum(f => f.EmDivida), abertas);
    }

    /// <summary>Todos os documentos do cliente no período (VD, facturas, recibos, notas de crédito e cotações), do mais antigo ao mais recente.</summary>
    public async Task<HistoricoCliente> HistoricoAsync(int clienteId, DateOnly desde, DateOnly ate, TipoDocumento? tipo = null, CancellationToken ct = default)
    {
        Validar(desde, ate, MaximoDias);
        var cliente = await db.Clientes.AsNoTracking().FirstOrDefaultAsync(c => c.Id == clienteId, ct) ?? throw new NaoEncontradoException("Cliente não encontrado.");
        var (inicio, fim) = await config.IntervaloUtcAsync(desde, ate);
        var q = db.Documentos.AsNoTracking().Where(d => d.ClienteId == clienteId && d.EmitidoEmUtc >= inicio && d.EmitidoEmUtc < fim);
        if (tipo is TipoDocumento t)
            q = q.Where(d => d.Tipo == t);

        var linhas = await q.OrderBy(d => d.EmitidoEmUtc).ThenBy(d => d.Id).Take(MaximoLinhas + 1)
            .Select(d => new LinhaHistoricoCliente(d.Id, d.Tipo, d.Numero, d.Estado, d.EmitidoEmUtc, d.Vencimento, d.Total, d.ValorPago, d.ValorCreditado,
                d.Tipo == TipoDocumento.Factura && d.Estado == EstadoDocumento.Emitido ? d.Total - d.ValorPago - d.ValorCreditado : 0m,
                d.DocumentoOrigem != null ? d.DocumentoOrigem.Numero : d.Pedido != null ? d.Pedido.Numero : null,
                d.MetodoPagamento != null ? d.MetodoPagamento.Nome : null))
            .ToListAsync(ct);
        var cortado = linhas.Count > MaximoLinhas;
        if (cortado)
            linhas.RemoveAt(linhas.Count - 1);

        var totais = await q.Where(d => d.Estado != EstadoDocumento.Anulado)
            .GroupBy(d => d.Tipo)
            .Select(g => new { Tipo = g.Key, N = g.Count(), Total = g.Sum(d => d.Total), EmDivida = g.Sum(d => d.Total - d.ValorPago - d.ValorCreditado) })
            .ToListAsync(ct);
        decimal Total(TipoDocumento x) => totais.Where(g => g.Tipo == x).Sum(g => g.Total);
        var resumo = new TotaisHistoricoCliente(totais.Sum(g => g.N), Total(TipoDocumento.VendaDinheiro), Total(TipoDocumento.Factura),
            Total(TipoDocumento.Recibo), Total(TipoDocumento.NotaCredito),
            totais.Where(g => g.Tipo == TipoDocumento.Factura).Sum(g => Math.Max(0m, g.EmDivida)));
        return new HistoricoCliente(cliente.Id, cliente.Nome, cliente.Nuit, desde, ate, resumo, linhas, cortado);
    }

    /// <summary>Facturas a crédito emitidas no período (todas ou de um cliente) e a dívida de hoje por cliente, por antiguidade.</summary>
    public async Task<RelatorioFacturasCredito> FacturasCreditoAsync(DateOnly desde, DateOnly ate, int? clienteId = null, bool apenasEmDivida = false,
        CancellationToken ct = default)
    {
        Validar(desde, ate, 366);
        var (inicio, fim) = await config.IntervaloUtcAsync(desde, ate);
        var hoje = await config.HojeAsync(relogio);

        var q = db.Documentos.AsNoTracking().Where(d => d.Tipo == TipoDocumento.Factura && d.Estado == EstadoDocumento.Emitido
                                                         && d.EmitidoEmUtc >= inicio && d.EmitidoEmUtc < fim);
        if (clienteId is int id)
            q = q.Where(d => d.ClienteId == id);
        if (apenasEmDivida)
            q = q.Where(d => d.Total - d.ValorPago - d.ValorCreditado > 0);

        var resumoBd = await q.GroupBy(d => 1).Select(g => new
        {
            N = g.Count(), Total = g.Sum(d => d.Total), Pago = g.Sum(d => d.ValorPago), Creditado = g.Sum(d => d.ValorCreditado),
            EmDivida = g.Sum(d => d.Total - d.ValorPago - d.ValorCreditado),
            Vencido = g.Where(d => d.Vencimento != null && d.Vencimento < hoje).Sum(d => d.Total - d.ValorPago - d.ValorCreditado),
        }).FirstOrDefaultAsync(ct);
        var resumo = resumoBd is null
            ? new ResumoFacturasCredito(0, 0, 0, 0, 0, 0)
            : new ResumoFacturasCredito(resumoBd.N, resumoBd.Total, resumoBd.Pago, resumoBd.Creditado, resumoBd.EmDivida, resumoBd.Vencido);

        var facturas = await q.OrderBy(d => d.EmitidoEmUtc).ThenBy(d => d.Id).Take(MaximoLinhas + 1)
            .Select(d => new { d.Id, d.Numero, d.EmitidoEmUtc, d.Vencimento, d.ClienteId, d.ClienteNome, d.ClienteNuit, d.Total, d.ValorPago, d.ValorCreditado })
            .ToListAsync(ct);
        var cortado = facturas.Count > MaximoLinhas;
        var linhas = facturas.Take(MaximoLinhas).Select(f =>
        {
            var emDivida = Math.Max(0m, f.Total - f.ValorPago - f.ValorCreditado);
            var atraso = emDivida > 0 && f.Vencimento is DateOnly v && v < hoje ? hoje.DayNumber - v.DayNumber : 0;
            var situacao = emDivida <= 0 ? (f.ValorCreditado >= f.Total ? SituacaoFactura.Creditada : SituacaoFactura.Paga)
                : atraso > 0 ? SituacaoFactura.Vencida
                : f.ValorPago + f.ValorCreditado > 0 ? SituacaoFactura.Parcial
                : SituacaoFactura.EmDivida;
            return new LinhaFacturaCredito(f.Id, f.Numero, f.EmitidoEmUtc, f.Vencimento, f.ClienteId, f.ClienteNome, f.ClienteNuit,
                f.Total, f.ValorPago, f.ValorCreditado, emDivida, situacao, atraso);
        }).ToList();

        var dividas = await DividasAsync(hoje, clienteId, ct);
        return new RelatorioFacturasCredito(desde, ate, hoje, resumo, linhas, cortado, dividas,
            dividas.Sum(d => d.EmDivida), dividas.Sum(d => d.EmDivida - d.NaoVencido));
    }

    /// <summary>Dívida de hoje por cliente (todas as facturas em aberto, de qualquer data), das maiores para as menores.</summary>
    private async Task<List<DividaCliente>> DividasAsync(DateOnly hoje, int? clienteId, CancellationToken ct)
    {
        var q = db.Documentos.AsNoTracking().Where(d => d.Tipo == TipoDocumento.Factura && d.Estado == EstadoDocumento.Emitido
                                                         && d.ClienteId != null && d.Total - d.ValorPago - d.ValorCreditado > 0);
        if (clienteId is int id)
            q = q.Where(d => d.ClienteId == id);
        var abertas = await q.Select(d => new { ClienteId = d.ClienteId!.Value, Nome = d.Cliente!.Nome, Nuit = d.Cliente.Nuit, d.Vencimento,
            EmDivida = d.Total - d.ValorPago - d.ValorCreditado }).ToListAsync(ct);
        return [.. abertas.GroupBy(a => (a.ClienteId, a.Nome, a.Nuit)).Select(g =>
        {
            decimal Entre(int min, int max) => g.Where(a => a.Vencimento is DateOnly v && hoje.DayNumber - v.DayNumber >= min && hoje.DayNumber - v.DayNumber <= max).Sum(a => a.EmDivida);
            return new DividaCliente(g.Key.ClienteId, g.Key.Nome, g.Key.Nuit, g.Count(), g.Sum(a => a.EmDivida),
                g.Where(a => a.Vencimento is not DateOnly v || v >= hoje).Sum(a => a.EmDivida),
                Entre(1, 30), Entre(31, 60), Entre(61, 90), Entre(91, int.MaxValue));
        }).OrderByDescending(d => d.EmDivida).ThenBy(d => d.ClienteNome)];
    }

    /// <summary>Documentos que movimentam a conta: facturas e recibos em vigor, e notas de crédito sobre facturas.</summary>
    private IQueryable<Documento> Conta(int clienteId) =>
        db.Documentos.AsNoTracking().Where(d => d.ClienteId == clienteId && d.Estado == EstadoDocumento.Emitido
            && (d.Tipo == TipoDocumento.Factura || d.Tipo == TipoDocumento.Recibo
                || d.Tipo == TipoDocumento.NotaCredito && d.DocumentoOrigem != null && d.DocumentoOrigem.Tipo == TipoDocumento.Factura));

    private static void Validar(DateOnly desde, DateOnly ate, int maximoDias)
    {
        if (ate < desde)
            throw new RegraNegocioException("A data final é anterior à inicial.");
        if (ate.DayNumber - desde.DayNumber > maximoDias)
            throw new RegraNegocioException(maximoDias > 366 ? "O período máximo é de dez anos." : "O período máximo do relatório é de um ano.");
    }
}
