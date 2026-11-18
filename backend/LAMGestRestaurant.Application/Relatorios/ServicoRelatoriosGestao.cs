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

/// <summary><see cref="Armazem"/>: armazém do movimento (consumo, ajustes e entradas são por artigo e armazém).</summary>
public record ArtigoValor(int ArtigoId, string Codigo, string Artigo, string Unidade, decimal Quantidade, decimal Valor, string? Armazem = null);
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
    string Metodologia,
    /// <summary>Armazém filtrado (null = todos).</summary>
    string? Armazem = null);

/// <summary>Artigo num armazém. Localização e mínimo só no armazém habitual do artigo.</summary>
public record ExistenciaArtigo(int ArtigoId, string Codigo, string Artigo, string? Categoria, string Unidade, int ArmazemId, string Armazem, string? Localizacao,
    decimal Quantidade, decimal Minimo, bool AbaixoMinimo, decimal CustoMedio, decimal Valor);

public record RelatorioExistencias(DateOnly Data, string? Armazem, IReadOnlyList<ExistenciaArtigo> Linhas, IReadOnlyList<LinhaValor> PorArmazem,
    decimal ValorTotal, int AbaixoMinimo);

public record TransferenciaArmazem(DateTime DataUtc, int ArtigoId, string Codigo, string Artigo, string Unidade, decimal Quantidade, string Origem, string Destino,
    string? Utilizador, string? Motivo, decimal Valor);

public record RelatorioTransferencias(DateOnly Desde, DateOnly Ate, string? Armazem, IReadOnlyList<TransferenciaArmazem> Linhas, IReadOnlyList<LinhaValor> PorPercurso);

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
        "(negativo = falta). Entradas = compras (menos estornos) e stock inicial. Compras por fornecedor: compras confirmadas no período e não anuladas, com IVA. " +
        "Com um armazém escolhido, tudo (valor, movimentos e compras) é desse armazém; as transferências estão no relatório próprio.";

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

    public async Task<RelatorioStock> StockAsync(DateOnly desde, DateOnly ate, CancellationToken ct = default, int? armazemId = null)
    {
        ServicoRelatorios.ValidarPeriodo(desde, ate);
        var (inicio, fim) = await config.IntervaloUtcAsync(desde, ate);
        var nomeArmazem = armazemId is int a0
            ? await db.Armazens.AsNoTracking().Where(a => a.Id == a0).Select(a => a.Nome).FirstOrDefaultAsync(ct) ?? throw new NaoEncontradoException("Armazém não encontrado.")
            : null;

        var saldos = await db.StockSaldos.AsNoTracking()
            .Where(s => s.Quantidade != 0 && (armazemId == null || s.ArmazemId == armazemId))
            .Select(s => new { s.ArtigoId, s.Artigo.Tipo, Armazem = s.Armazem.Nome, Valor = s.Quantidade * s.Artigo.CustoMedio })
            .ToListAsync(ct);
        var artigos = await db.Artigos.AsNoTracking()
            .Where(a => a.Activo)
            .Select(a => new { a.StockMinimo, Saldo = db.StockSaldos.Where(s => s.ArtigoId == a.Id && (armazemId == null || s.ArmazemId == armazemId)).Sum(s => (decimal?)s.Quantidade) ?? 0m })
            .ToListAsync(ct);

        var movimentos = db.MovimentosStock.AsNoTracking().Where(m => m.DataUtc >= inicio && m.DataUtc < fim && (armazemId == null || m.ArmazemId == armazemId));
        var porTipo = await movimentos
            .GroupBy(m => m.Tipo)
            .Select(g => new MovimentoTipo(g.Key, g.Count(), g.Sum(m => m.Quantidade * m.CustoUnitario)))
            .ToListAsync(ct);

        async Task<List<ArtigoValor>> PorArtigoAsync(TipoMovimentoStock[] tipos, decimal sinal)
        {
            var linhas = await movimentos.Where(m => tipos.Contains(m.Tipo))
                .GroupBy(m => new { m.ArtigoId, m.Artigo.Codigo, m.Artigo.Nome, Unidade = m.Artigo.UnidadeMedida.Codigo, Armazem = m.Armazem.Nome })
                .Select(g => new { g.Key.ArtigoId, g.Key.Codigo, g.Key.Nome, g.Key.Unidade, g.Key.Armazem, Quantidade = g.Sum(m => m.Quantidade), Valor = g.Sum(m => m.Quantidade * m.CustoUnitario) })
                .ToListAsync(ct);
            return [.. linhas
                .Where(l => l.Quantidade != 0 || l.Valor != 0)
                .Select(l => new ArtigoValor(l.ArtigoId, l.Codigo, l.Nome, l.Unidade, sinal * l.Quantidade, Arredondar(sinal * l.Valor), l.Armazem))
                .OrderByDescending(l => Math.Abs(l.Valor)).ThenBy(l => l.Artigo)];
        }

        // Consumo e entradas positivos; ajustes com o sinal real (negativo = perda/falta).
        var consumo = await PorArtigoAsync([TipoMovimentoStock.SaidaVenda, TipoMovimentoStock.EstornoVenda], -1m);
        var ajustes = await PorArtigoAsync([TipoMovimentoStock.Ajuste, TipoMovimentoStock.Perda, TipoMovimentoStock.Inventario], 1m);
        var entradas = await PorArtigoAsync([TipoMovimentoStock.EntradaCompra, TipoMovimentoStock.EstornoCompra, TipoMovimentoStock.StockInicial], 1m);

        var compras = await db.Compras.AsNoTracking()
            .Where(c => c.Estado == EstadoCompra.Confirmada && c.ConfirmadaEmUtc >= inicio && c.ConfirmadaEmUtc < fim && (armazemId == null || c.ArmazemId == armazemId))
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
            MetodologiaStock,
            nomeArmazem);
    }

    // ---------- Existências por armazém ----------

    /// <summary>Saldo, mínimo, localização e valor de cada artigo activo em cada armazém (só linhas com saldo, salvo os abaixo do mínimo).</summary>
    public async Task<RelatorioExistencias> ExistenciasAsync(int? armazemId, int? categoriaId, CancellationToken ct = default)
    {
        var artigos = db.Artigos.AsNoTracking().Where(a => a.Activo);
        if (categoriaId is int cat)
            artigos = artigos.Where(a => a.CategoriaId == cat || (a.Categoria != null && a.Categoria.CategoriaPaiId == cat));
        var lista = await artigos
            .Select(a => new
            {
                a.Id, a.Codigo, a.Nome, Unidade = a.UnidadeMedida.Codigo, a.CustoMedio, a.StockMinimo, a.Localizacao, a.ArmazemId,
                Categoria = a.Categoria == null ? null : a.Categoria.CategoriaPai != null ? a.Categoria.CategoriaPai.Nome + " › " + a.Categoria.Nome : a.Categoria.Nome,
            })
            .ToListAsync(ct);
        var ids = lista.Select(a => a.Id).ToList();
        var armazens = await db.Armazens.AsNoTracking().Where(a => armazemId == null ? a.Activo : a.Id == armazemId).ToListAsync(ct);
        if (armazemId is not null && armazens.Count == 0) throw new NaoEncontradoException("Armazém não encontrado.");
        var principal = await db.Armazens.AsNoTracking().Where(a => a.Principal).Select(a => (int?)a.Id).FirstOrDefaultAsync(ct);
        var saldos = await db.StockSaldos.AsNoTracking().Where(s => ids.Contains(s.ArtigoId))
            .ToDictionaryAsync(s => (s.ArtigoId, s.ArmazemId), s => s.Quantidade, ct);

        var linhas = new List<ExistenciaArtigo>();
        foreach (var armazem in armazens.OrderByDescending(a => a.Principal).ThenBy(a => a.Nome))
            foreach (var a in lista)
            {
                var saldo = saldos.GetValueOrDefault((a.Id, armazem.Id));
                var habitual = (a.ArmazemId ?? principal) == armazem.Id;
                // O mínimo só se avalia no armazém habitual do artigo.
                var abaixo = habitual && a.StockMinimo > 0 && saldo <= a.StockMinimo;
                if (saldo == 0 && !abaixo) continue;
                linhas.Add(new ExistenciaArtigo(a.Id, a.Codigo, a.Nome, a.Categoria, a.Unidade, armazem.Id, armazem.Nome, habitual ? a.Localizacao : null,
                    saldo, habitual ? a.StockMinimo : 0, abaixo, a.CustoMedio, Arredondar(Math.Max(0, saldo) * a.CustoMedio)));
            }
        var ordenadas = linhas.OrderBy(l => l.Armazem == armazens.FirstOrDefault(a => a.Principal)?.Nome ? 0 : 1).ThenBy(l => l.Armazem)
            .ThenBy(l => l.Categoria ?? "￿").ThenBy(l => l.Artigo).ToList();
        return new RelatorioExistencias(
            DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, await config.FusoAsync())),
            armazemId is null ? null : armazens[0].Nome,
            ordenadas,
            [.. ordenadas.GroupBy(l => l.Armazem).Select(g => new LinhaValor(g.Key, g.Sum(l => l.Valor), g.Count()))],
            ordenadas.Sum(l => l.Valor),
            ordenadas.Count(l => l.AbaixoMinimo));
    }

    // ---------- Transferências entre armazéns ----------

    /// <summary>
    /// Transferências do período: cada saída de transferência com a entrada correspondente (mesmo artigo, quantidade e utilizador,
    /// gravadas no mesmo instante). Com armazém, só as que saem ou entram nele.
    /// </summary>
    public async Task<RelatorioTransferencias> TransferenciasAsync(DateOnly desde, DateOnly ate, int? armazemId, CancellationToken ct = default)
    {
        ServicoRelatorios.ValidarPeriodo(desde, ate);
        var (inicio, fim) = await config.IntervaloUtcAsync(desde, ate);
        var tipos = new[] { TipoMovimentoStock.TransferenciaSaida, TipoMovimentoStock.TransferenciaEntrada };
        var movimentos = await db.MovimentosStock.AsNoTracking()
            .Where(m => tipos.Contains(m.Tipo) && m.DataUtc >= inicio.AddMinutes(-1) && m.DataUtc < fim.AddMinutes(1))
            .OrderBy(m => m.DataUtc).ThenBy(m => m.Id)
            .Select(m => new
            {
                m.Id, m.Tipo, m.ArtigoId, m.Artigo.Codigo, Artigo = m.Artigo.Nome, Unidade = m.Artigo.UnidadeMedida.Codigo, m.ArmazemId, Armazem = m.Armazem.Nome,
                m.Quantidade, m.CustoUnitario, m.UtilizadorId, m.DataUtc, m.Motivo,
            })
            .ToListAsync(ct);
        var entradas = movimentos.Where(m => m.Tipo == TipoMovimentoStock.TransferenciaEntrada).ToList();
        var usadas = new HashSet<long>();
        var nomes = await utilizadores.NomesAsync(movimentos.Select(m => m.UtilizadorId).OfType<int>().Distinct(), ct);
        var linhas = new List<TransferenciaArmazem>();
        foreach (var s in movimentos.Where(m => m.Tipo == TipoMovimentoStock.TransferenciaSaida && m.DataUtc >= inicio && m.DataUtc < fim))
        {
            var e = entradas.FirstOrDefault(x => !usadas.Contains(x.Id) && x.ArtigoId == s.ArtigoId && x.Quantidade == -s.Quantidade
                                                 && x.UtilizadorId == s.UtilizadorId && x.Id > s.Id && (x.DataUtc - s.DataUtc).Duration() < TimeSpan.FromSeconds(30));
            if (e is not null) usadas.Add(e.Id);
            if (armazemId is int a && s.ArmazemId != a && e?.ArmazemId != a) continue;
            linhas.Add(new TransferenciaArmazem(s.DataUtc, s.ArtigoId, s.Codigo, s.Artigo, s.Unidade, -s.Quantidade, s.Armazem, e?.Armazem ?? "?",
                s.UtilizadorId is int u ? nomes.GetValueOrDefault(u) : null, s.Motivo, Arredondar(-s.Quantidade * s.CustoUnitario)));
        }
        return new RelatorioTransferencias(desde, ate,
            armazemId is int f ? await db.Armazens.AsNoTracking().Where(x => x.Id == f).Select(x => x.Nome).FirstOrDefaultAsync(ct) : null,
            linhas,
            [.. linhas.GroupBy(l => $"{l.Origem} → {l.Destino}").Select(g => new LinhaValor(g.Key, g.Sum(l => l.Valor), g.Count())).OrderByDescending(l => l.Valor)]);
    }

    private static decimal Arredondar(decimal valor) => decimal.Round(valor, 2, MidpointRounding.AwayFromZero);

    private static string NomeTipoArtigo(TipoArtigo tipo) => tipo switch
    {
        TipoArtigo.Ingrediente => "Ingredientes",
        TipoArtigo.Revenda => "Revenda",
        _ => "Consumíveis",
    };
}
