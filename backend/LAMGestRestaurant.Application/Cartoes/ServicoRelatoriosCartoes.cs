using LAMGestRestaurant.Application.Comum;
using LAMGestRestaurant.Domain.Cartoes;
using Microsoft.EntityFrameworkCore;

namespace LAMGestRestaurant.Application.Cartoes;

public sealed record ResumoCartoes(
    decimal Carregado, int Carregamentos, decimal Recarregado, int Recargas, decimal Pago, int Pagamentos,
    decimal Devolvido, int Devolucoes, int CartoesNovos, decimal SaldoEmCirculacao, int CartoesComSaldo, int CartoesActivos,
    decimal Descarregado = 0, int Descargas = 0);

/// <summary>Cartão descarregado, com o acumulado até ele.</summary>
public sealed record LinhaDescargaRelatorio(int DescargaId, DateTime DataUtc, string Numero, string? Titular, decimal Saldo, decimal Acumulado,
    bool DevolveuDinheiro, string? Metodo, string? Operador, string? Motivo, int? SessaoCaixaId);

/// <summary>Descargas de saldo: total, parte devolvida em dinheiro, parte só retirada e cada cartão.</summary>
public sealed record RelatorioDescargas(decimal Total, decimal Devolvido, decimal Retirado, int Cartoes, int Operacoes, IReadOnlyList<LinhaDescargaRelatorio> Linhas, bool Cortado);

public sealed record LinhaMetodoCartoes(string Metodo, decimal Valor, int Quantidade);
public sealed record LinhaOperadorCartoes(string Operador, decimal Carregado, decimal Pago, int Movimentos);
public sealed record LinhaMovimentoRelatorio(DateTime DataUtc, string Numero, string? Titular, TipoMovimentoCartao Tipo, decimal Valor, decimal SaldoApos,
    string? Metodo, string? Pedido, string? Operador, string? Motivo);
public sealed record LinhaSaldoCartao(string Numero, string? Titular, decimal Saldo, bool Activo, DateTime? UltimoMovimentoUtc);

/// <summary>Relatório dos Cartões Evento: resumo, dinheiro recebido por método, operadores, movimentos e saldos por cartão.</summary>
public sealed record RelatorioCartoes(ResumoCartoes Resumo, IReadOnlyList<LinhaMetodoCartoes> PorMetodo, IReadOnlyList<LinhaOperadorCartoes> PorOperador,
    IReadOnlyList<LinhaMovimentoRelatorio> Movimentos, bool MovimentosCortados, IReadOnlyList<LinhaSaldoCartao> Saldos);

/// <summary>
/// Movimentos dos cartões pela data do movimento (hora local). Os saldos são os actuais: «em circulação» é o dinheiro já
/// recebido e ainda por consumir (o restaurante deve-o aos clientes).
/// </summary>
public class ServicoRelatoriosCartoes(IAppDbContext db, ConfiguracoesSistema config, IConsultaUtilizadores utilizadores)
{
    public const int MaximoMovimentos = 5000;

    /// <summary>Descargas de saldo do período ou, com <paramref name="sessaoCaixaId"/>, as devolvidas nesse caixa.</summary>
    public async Task<RelatorioDescargas> DescargasAsync(DateOnly desde, DateOnly ate, int? sessaoCaixaId = null, CancellationToken ct = default)
    {
        if (ate < desde)
            throw new RegraNegocioException("A data final é anterior à inicial.");
        var (inicio, fim) = await config.IntervaloUtcAsync(desde, ate);
        var q = db.MovimentosCartao.AsNoTracking().Where(m => m.Tipo == TipoMovimentoCartao.Descarga);
        q = sessaoCaixaId is int s ? q.Where(m => m.SessaoCaixaId == s) : q.Where(m => m.DataUtc >= inicio && m.DataUtc < fim);

        var totais = await q.GroupBy(m => m.Descarga!.DevolveuDinheiro).Select(g => new { Devolveu = g.Key, Total = -g.Sum(m => m.Valor), N = g.Count() }).ToListAsync(ct);
        var operacoes = await q.Select(m => m.DescargaId).Distinct().CountAsync(ct);
        var linhas = await q.OrderBy(m => m.DataUtc).ThenBy(m => m.Id).Take(MaximoMovimentos + 1)
            .Select(m => new
            {
                DescargaId = m.DescargaId!.Value, m.DataUtc, m.CartaoEvento!.Numero, m.CartaoEvento.Titular, Saldo = -m.Valor, m.Descarga!.DevolveuDinheiro,
                Metodo = m.MetodoPagamento != null ? m.MetodoPagamento.Nome : null, m.UtilizadorId, m.Motivo, m.SessaoCaixaId,
            })
            .ToListAsync(ct);
        var nomes = await utilizadores.NomesAsync(linhas.Select(l => l.UtilizadorId).Distinct(), ct);
        var acumulado = 0m;
        return new RelatorioDescargas(totais.Sum(t => t.Total), totais.Where(t => t.Devolveu).Sum(t => t.Total), totais.Where(t => !t.Devolveu).Sum(t => t.Total),
            totais.Sum(t => t.N), operacoes,
            [.. linhas.Take(MaximoMovimentos).Select(l => new LinhaDescargaRelatorio(l.DescargaId, l.DataUtc, l.Numero, l.Titular, l.Saldo, acumulado += l.Saldo,
                l.DevolveuDinheiro, l.Metodo, nomes.GetValueOrDefault(l.UtilizadorId), l.Motivo, l.SessaoCaixaId))],
            linhas.Count > MaximoMovimentos);
    }

    /// <summary>Caixas (sessões) com movimentos de cartões no período, para o relatório por caixa.</summary>
    public async Task<IReadOnlyList<SessaoCartoesDto>> SessoesAsync(DateOnly desde, DateOnly ate, CancellationToken ct = default)
    {
        if (ate < desde)
            throw new RegraNegocioException("A data final é anterior à inicial.");
        var (inicio, fim) = await config.IntervaloUtcAsync(desde, ate);
        return await ServicoCartoes.SessoesAsync(db, utilizadores, db.MovimentosCartao.AsNoTracking().Where(m => m.DataUtc >= inicio && m.DataUtc < fim), ct);
    }

    /// <summary>Por datas (período) ou por caixa (<paramref name="sessaoCaixaId"/>: o período é ignorado).</summary>
    public async Task<RelatorioCartoes> GerarAsync(DateOnly desde, DateOnly ate, CancellationToken ct = default, int? sessaoCaixaId = null)
    {
        if (ate < desde)
            throw new RegraNegocioException("A data final é anterior à inicial.");
        var (inicio, fim) = await config.IntervaloUtcAsync(desde, ate);
        var q = sessaoCaixaId is int sessao
            ? ServicoCartoes.DaSessao(db.MovimentosCartao.AsNoTracking(), sessao)
            : db.MovimentosCartao.AsNoTracking().Where(m => m.DataUtc >= inicio && m.DataUtc < fim);

        var linhas = await q.OrderBy(m => m.DataUtc).ThenBy(m => m.Id).Take(MaximoMovimentos + 1)
            .Select(m => new
            {
                m.DataUtc, m.CartaoEvento!.Numero, m.CartaoEvento.Titular, m.Tipo, m.Valor, m.SaldoApos,
                Metodo = m.MetodoPagamento != null ? m.MetodoPagamento.Nome : null,
                Pedido = db.Pedidos.Where(p => p.Id == m.PedidoId).Select(p => p.Numero).FirstOrDefault(), m.UtilizadorId, m.Motivo,
            })
            .ToListAsync(ct);
        var porTipo = await q.GroupBy(m => m.Tipo).Select(g => new { Tipo = g.Key, Valor = g.Sum(m => m.Valor), N = g.Count(m => m.Valor != 0) }).ToListAsync(ct);
        decimal Soma(params TipoMovimentoCartao[] t) => porTipo.Where(x => t.Contains(x.Tipo)).Sum(x => x.Valor);
        int Conta(params TipoMovimentoCartao[] t) => porTipo.Where(x => t.Contains(x.Tipo)).Sum(x => x.N);

        var porMetodo = await q.Where(m => m.MetodoPagamentoId != null && m.Tipo != TipoMovimentoCartao.Descarga) // o dinheiro devolvido sai à parte
            .GroupBy(m => m.MetodoPagamento!.Nome).Select(g => new LinhaMetodoCartoes(g.Key, g.Sum(m => m.Valor), g.Count())).ToListAsync(ct);
        var operadores = await q.GroupBy(m => m.UtilizadorId).Select(g => new
        {
            Id = g.Key,
            Carregado = g.Where(m => m.Tipo == TipoMovimentoCartao.Carregamento || m.Tipo == TipoMovimentoCartao.Recarga).Sum(m => m.Valor),
            Pago = -g.Where(m => m.Tipo == TipoMovimentoCartao.Pagamento).Sum(m => m.Valor),
            N = g.Count(),
        }).ToListAsync(ct);
        var nomes = await utilizadores.NomesAsync(operadores.Select(o => o.Id).Concat(linhas.Select(l => l.UtilizadorId)).Distinct(), ct);

        var cartoes = db.CartoesEvento.AsNoTracking();
        var saldos = await cartoes.Where(c => c.Saldo != 0).OrderByDescending(c => c.Saldo)
            .Select(c => new LinhaSaldoCartao(c.Numero, c.Titular, c.Saldo, c.Activo, db.MovimentosCartao.Where(m => m.CartaoEventoId == c.Id).Max(m => (DateTime?)m.DataUtc)))
            .ToListAsync(ct);
        // Por caixa: os cartões registados nesse caixa são os do carregamento inicial.
        var novos = sessaoCaixaId is null ? await cartoes.CountAsync(c => c.CriadoEmUtc >= inicio && c.CriadoEmUtc < fim, ct) : Conta(TipoMovimentoCartao.Carregamento);
        var activos = await cartoes.CountAsync(c => c.Activo, ct);

        var resumo = new ResumoCartoes(
            Soma(TipoMovimentoCartao.Carregamento), Conta(TipoMovimentoCartao.Carregamento),
            Soma(TipoMovimentoCartao.Recarga), Conta(TipoMovimentoCartao.Recarga),
            -Soma(TipoMovimentoCartao.Pagamento), Conta(TipoMovimentoCartao.Pagamento),
            Soma(TipoMovimentoCartao.AnulacaoPagamento, TipoMovimentoCartao.Estorno), Conta(TipoMovimentoCartao.AnulacaoPagamento, TipoMovimentoCartao.Estorno),
            novos, saldos.Sum(s => s.Saldo), saldos.Count(s => s.Saldo > 0), activos,
            -Soma(TipoMovimentoCartao.Descarga), Conta(TipoMovimentoCartao.Descarga));

        return new RelatorioCartoes(resumo,
            [.. porMetodo.OrderByDescending(m => m.Valor)],
            [.. operadores.Select(o => new LinhaOperadorCartoes(nomes.GetValueOrDefault(o.Id) ?? "—", o.Carregado, o.Pago, o.N)).OrderByDescending(o => o.Carregado + o.Pago)],
            [.. linhas.Take(MaximoMovimentos).Select(l => new LinhaMovimentoRelatorio(l.DataUtc, l.Numero, l.Titular, l.Tipo, l.Valor, l.SaldoApos, l.Metodo, l.Pedido,
                nomes.GetValueOrDefault(l.UtilizadorId), l.Motivo))],
            linhas.Count > MaximoMovimentos, saldos);
    }
}
