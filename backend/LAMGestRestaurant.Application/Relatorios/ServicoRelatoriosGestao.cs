using LAMGestRestaurant.Application.Comum;
using LAMGestRestaurant.Domain.Comum;
using Microsoft.EntityFrameworkCore;

namespace LAMGestRestaurant.Application.Relatorios;

public record FechoSessao(int SessaoId, string Terminal, string AbertaPor, string? FechadaPor, DateTime AbertaEmUtc, DateTime FechadaEmUtc,
    decimal Esperado, decimal Contado, decimal Diferenca, string? Observacoes);
public record DiferencaMetodo(string Metodo, decimal Esperado, decimal Contado, decimal Diferenca);

public record RelatorioFinanceiro(
    DateOnly Desde,
    DateOnly Ate,
    decimal TotalVendas,
    int NumeroPedidos,
    decimal Impostos,
    decimal VendasSemImpostos,
    decimal CustoEstimadoVendas,
    decimal Despesas,
    decimal ResultadoEstimado,
    decimal Estornos,
    int PedidosEstornados,
    decimal ComprasConfirmadas,
    int NumeroCompras,
    IReadOnlyList<LinhaValor> RecebimentosPorMetodo,
    IReadOnlyList<LinhaValor> DespesasPorCategoria,
    IReadOnlyList<LinhaValor> DespesasPorMetodo,
    IReadOnlyList<FechoSessao> SessoesFechadas,
    IReadOnlyList<DiferencaMetodo> DiferencasPorMetodo,
    int SessoesAbertas,
    string Metodologia);

public record ArtigoValor(int ArtigoId, string Codigo, string Artigo, string Unidade, decimal Quantidade, decimal Valor);
public record MovimentoTipo(TipoMovimentoStock Tipo, int Movimentos, decimal Valor);

public record RelatorioStock(
    DateOnly Desde,
    DateOnly Ate,
    decimal ValorStockActual,
    int ArtigosActivos,
    int ArtigosAbaixoMinimo,
    IReadOnlyList<LinhaValor> ValorPorTipo,
    IReadOnlyList<LinhaValor> ValorPorArmazem,
    IReadOnlyList<MovimentoTipo> MovimentosPorTipo,
    IReadOnlyList<ArtigoValor> Consumo,
    IReadOnlyList<ArtigoValor> AjustesPerdas,
    IReadOnlyList<ArtigoValor> Entradas,
    IReadOnlyList<LinhaValor> ComprasPorFornecedor,
    string Metodologia);

/// <summary>Relatórios financeiros (resultado, despesas, fechos de caixa) e de stock/compras.</summary>
public class ServicoRelatoriosGestao(
    IAppDbContext db,
    IConsultaUtilizadores utilizadores,
    ConfiguracoesSistema config,
    ServicoRelatorios relatorios)
{
    public const string MetodologiaFinanceira =
        "Resultado estimado = vendas sem impostos (inclui taxas de entrega) − custo estimado das vendas − despesas confirmadas. " +
        "O custo das vendas usa a mesma regra do relatório de vendas (baixa de stock pela ficha técnica ou custo de referência). " +
        "As compras são apresentadas à parte e não entram no resultado: o seu custo entra quando os artigos são consumidos. " +
        "Pedidos estornados não contam como venda. Fechos de caixa: sessões fechadas no período.";

    public const string MetodologiaStock =
        "Valor do stock = saldo actual × custo médio ponderado de cada artigo. Valores dos movimentos = quantidade × custo unitário " +
        "registado no movimento (sem IVA). Consumo = saídas por venda menos estornos de venda. Ajustes e perdas incluem os acertos de inventário " +
        "(negativo = falta). Compras por fornecedor: compras confirmadas no período e não anuladas, com IVA.";

    public async Task<RelatorioFinanceiro> FinanceiroAsync(DateOnly desde, DateOnly ate, CancellationToken ct = default)
    {
        ServicoRelatorios.ValidarPeriodo(desde, ate);
        var vendas = await relatorios.VendasAsync(desde, ate, ct);
        var (inicio, fim) = await config.IntervaloUtcAsync(desde, ate);

        var despesas = await db.Despesas.AsNoTracking()
            .Where(d => d.Estado == EstadoDespesa.Confirmada && d.DataUtc >= inicio && d.DataUtc < fim)
            .Select(d => new { d.Valor, Categoria = d.CategoriaDespesa.Nome, Metodo = d.MetodoPagamento.Nome })
            .ToListAsync(ct);

        var compras = await db.Compras.AsNoTracking()
            .Where(c => c.Estado == EstadoCompra.Confirmada && c.ConfirmadaEmUtc >= inicio && c.ConfirmadaEmUtc < fim)
            .Select(c => c.Total).ToListAsync(ct);

        var sessoes = await db.SessoesCaixa.AsNoTracking()
            .Where(s => s.Estado == EstadoSessaoCaixa.Fechada && s.FechadaEmUtc >= inicio && s.FechadaEmUtc < fim)
            .OrderBy(s => s.FechadaEmUtc)
            .Select(s => new
            {
                s.Id,
                Terminal = s.Terminal.Nome,
                s.UtilizadorAberturaId,
                s.UtilizadorFechoId,
                s.AbertaEmUtc,
                Fechada = s.FechadaEmUtc!.Value,
                s.ObservacoesFecho,
                Esperado = s.LinhasFecho.Sum(l => (decimal?)l.ValorEsperado) ?? 0m,
                Contado = s.LinhasFecho.Sum(l => (decimal?)l.ValorContado) ?? 0m,
                Diferenca = s.LinhasFecho.Sum(l => (decimal?)l.Diferenca) ?? 0m,
            })
            .ToListAsync(ct);
        var porMetodo = await db.FechoCaixaLinhas.AsNoTracking()
            .Where(l => l.SessaoCaixa.Estado == EstadoSessaoCaixa.Fechada && l.SessaoCaixa.FechadaEmUtc >= inicio && l.SessaoCaixa.FechadaEmUtc < fim)
            .GroupBy(l => new { l.MetodoPagamento.Nome, l.MetodoPagamento.Ordem })
            .Select(g => new { g.Key.Nome, g.Key.Ordem, Esperado = g.Sum(l => l.ValorEsperado), Contado = g.Sum(l => l.ValorContado), Diferenca = g.Sum(l => l.Diferenca) })
            .ToListAsync(ct);
        var abertas = await db.SessoesCaixa.CountAsync(s => s.Estado == EstadoSessaoCaixa.Aberta, ct);

        var ids = sessoes.SelectMany(s => s.UtilizadorFechoId is int f ? new[] { s.UtilizadorAberturaId, f } : [s.UtilizadorAberturaId]);
        var nomes = await utilizadores.NomesAsync(ids, ct);

        var vendasSemImpostos = vendas.TotalVendas - vendas.Impostos;
        var totalDespesas = despesas.Sum(d => d.Valor);

        return new RelatorioFinanceiro(
            desde, ate, vendas.TotalVendas, vendas.NumeroPedidos, vendas.Impostos, vendasSemImpostos, vendas.CustoEstimado,
            totalDespesas, vendasSemImpostos - vendas.CustoEstimado - totalDespesas,
            vendas.Cancelamentos.ValorEstornado, vendas.Cancelamentos.PedidosEstornados,
            compras.Sum(), compras.Count,
            vendas.PorMetodo,
            [.. despesas.GroupBy(d => d.Categoria).Select(g => new LinhaValor(g.Key, g.Sum(d => d.Valor), g.Count())).OrderByDescending(l => l.Valor)],
            [.. despesas.GroupBy(d => d.Metodo).Select(g => new LinhaValor(g.Key, g.Sum(d => d.Valor), g.Count())).OrderByDescending(l => l.Valor)],
            [.. sessoes.Select(s => new FechoSessao(s.Id, s.Terminal, nomes.GetValueOrDefault(s.UtilizadorAberturaId, "?"),
                s.UtilizadorFechoId is int f ? nomes.GetValueOrDefault(f, "?") : null, s.AbertaEmUtc, s.Fechada, s.Esperado, s.Contado, s.Diferenca, s.ObservacoesFecho))],
            [.. porMetodo.OrderBy(m => m.Ordem).Select(m => new DiferencaMetodo(m.Nome, m.Esperado, m.Contado, m.Diferenca))],
            abertas,
            MetodologiaFinanceira);
    }

    public async Task<RelatorioStock> StockAsync(DateOnly desde, DateOnly ate, CancellationToken ct = default)
    {
        ServicoRelatorios.ValidarPeriodo(desde, ate);
        var (inicio, fim) = await config.IntervaloUtcAsync(desde, ate);

        var saldos = await db.StockSaldos.AsNoTracking()
            .Where(s => s.Quantidade != 0)
            .Select(s => new { s.ArtigoId, s.Artigo.Tipo, Armazem = s.Armazem.Nome, Valor = s.Quantidade * s.Artigo.CustoMedio })
            .ToListAsync(ct);
        var artigos = await db.Artigos.AsNoTracking()
            .Where(a => a.Activo)
            .Select(a => new { a.StockMinimo, Saldo = db.StockSaldos.Where(s => s.ArtigoId == a.Id).Sum(s => (decimal?)s.Quantidade) ?? 0m })
            .ToListAsync(ct);

        var movimentos = db.MovimentosStock.AsNoTracking().Where(m => m.DataUtc >= inicio && m.DataUtc < fim);
        var porTipo = await movimentos
            .GroupBy(m => m.Tipo)
            .Select(g => new MovimentoTipo(g.Key, g.Count(), g.Sum(m => m.Quantidade * m.CustoUnitario)))
            .ToListAsync(ct);

        async Task<List<ArtigoValor>> PorArtigoAsync(TipoMovimentoStock[] tipos, decimal sinal)
        {
            var linhas = await movimentos.Where(m => tipos.Contains(m.Tipo))
                .GroupBy(m => new { m.ArtigoId, m.Artigo.Codigo, m.Artigo.Nome, Unidade = m.Artigo.UnidadeMedida.Codigo })
                .Select(g => new { g.Key.ArtigoId, g.Key.Codigo, g.Key.Nome, g.Key.Unidade, Quantidade = g.Sum(m => m.Quantidade), Valor = g.Sum(m => m.Quantidade * m.CustoUnitario) })
                .ToListAsync(ct);
            return [.. linhas
                .Where(l => l.Quantidade != 0 || l.Valor != 0)
                .Select(l => new ArtigoValor(l.ArtigoId, l.Codigo, l.Nome, l.Unidade, sinal * l.Quantidade, Arredondar(sinal * l.Valor)))
                .OrderByDescending(l => Math.Abs(l.Valor)).ThenBy(l => l.Artigo)];
        }

        // Consumo e entradas positivos; ajustes com o sinal real (negativo = perda/falta).
        var consumo = await PorArtigoAsync([TipoMovimentoStock.SaidaVenda, TipoMovimentoStock.EstornoVenda], -1m);
        var ajustes = await PorArtigoAsync([TipoMovimentoStock.Ajuste, TipoMovimentoStock.Perda, TipoMovimentoStock.Inventario], 1m);
        var entradas = await PorArtigoAsync([TipoMovimentoStock.EntradaCompra, TipoMovimentoStock.EstornoCompra], 1m);

        var compras = await db.Compras.AsNoTracking()
            .Where(c => c.Estado == EstadoCompra.Confirmada && c.ConfirmadaEmUtc >= inicio && c.ConfirmadaEmUtc < fim)
            .GroupBy(c => c.Fornecedor.Nome)
            .Select(g => new LinhaValor(g.Key, g.Sum(c => c.Total), g.Count()))
            .ToListAsync(ct);

        return new RelatorioStock(
            desde, ate,
            Arredondar(saldos.Sum(s => s.Valor)),
            artigos.Count,
            artigos.Count(a => a.StockMinimo > 0 && a.Saldo <= a.StockMinimo),
            [.. saldos.GroupBy(s => s.Tipo).Select(g => new LinhaValor(NomeTipoArtigo(g.Key), Arredondar(g.Sum(s => s.Valor)), g.Select(s => s.ArtigoId).Distinct().Count()))
                .OrderByDescending(l => l.Valor)],
            [.. saldos.GroupBy(s => s.Armazem).Select(g => new LinhaValor(g.Key, Arredondar(g.Sum(s => s.Valor)), g.Count())).OrderByDescending(l => l.Valor)],
            [.. porTipo.Select(t => t with { Valor = Arredondar(t.Valor) }).OrderBy(t => t.Tipo)],
            consumo, ajustes, entradas,
            [.. compras.OrderByDescending(c => c.Valor)],
            MetodologiaStock);
    }

    private static decimal Arredondar(decimal valor) => decimal.Round(valor, 2, MidpointRounding.AwayFromZero);

    private static string NomeTipoArtigo(TipoArtigo tipo) => tipo switch
    {
        TipoArtigo.Ingrediente => "Ingredientes",
        TipoArtigo.Revenda => "Revenda",
        _ => "Consumíveis",
    };
}
