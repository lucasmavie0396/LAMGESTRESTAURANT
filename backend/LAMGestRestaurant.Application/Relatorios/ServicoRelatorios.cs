using LAMGestRestaurant.Application.Caixa;
using LAMGestRestaurant.Application.Comum;
using LAMGestRestaurant.Application.Seguranca;
using LAMGestRestaurant.Application.Stock;
using LAMGestRestaurant.Application.Vendas;
using LAMGestRestaurant.Domain.Comum;
using Microsoft.EntityFrameworkCore;

namespace LAMGestRestaurant.Application.Relatorios;

public record LinhaValor(string Rotulo, decimal Valor, int Quantidade);
public record VendaDia(DateOnly Dia, decimal Total, int Pedidos);
/// <summary><see cref="Unidade"/>: unidade de venda (un, kg…): as quantidades só se comparam dentro da mesma unidade.</summary>
public record VendaProduto(int ProdutoId, string Produto, string Categoria, decimal Quantidade, decimal Total, decimal CustoEstimado, string Codigo = "", string Unidade = "un");
public record ProdutoSemVenda(int ProdutoId, string Codigo, string Produto, string Categoria);

public record AcompanhamentoVendido(string Grupo, string Opcao, decimal Vezes, int Pratos);
public record AcompanhamentoPorPrato(string Prato, string Opcao, decimal Vezes);
public record ArtigoAcompanhamento(int ArtigoId, string Artigo, string Unidade, decimal Quantidade, decimal Custo);
/// <summary><see cref="Total"/>: total de escolhas (pratos × acompanhamentos).</summary>
public record RelatorioAcompanhamentos(DateOnly Desde, DateOnly Ate, decimal Total, IReadOnlyList<AcompanhamentoVendido> PorOpcao,
    IReadOnlyList<AcompanhamentoPorPrato> PorPrato, IReadOnlyList<ArtigoAcompanhamento> Artigos, string Metodologia);
/// <summary>Quem registou o pedido: vendas, facturação, descontos concedidos e como foram pagas.</summary>
public record VendaOperador(string Operador, int Vendas, decimal Total, decimal Descontos, IReadOnlyList<LinhaValor> Metodos);
public record VendaCategoria(string Categoria, decimal Quantidade, decimal Total);
public record ResumoCancelamentos(int PedidosCancelados, decimal ValorPedidosCancelados, int ItensCancelados, decimal ValorItensCancelados,
    int PedidosEstornados, decimal ValorEstornado);

public record RelatorioVendas(
    DateOnly Desde,
    DateOnly Ate,
    decimal TotalVendas,
    int NumeroPedidos,
    decimal TicketMedio,
    decimal Descontos,
    decimal Impostos,
    decimal TaxasEntrega,
    decimal ReceitaLiquida,
    decimal CustoEstimado,
    decimal MargemEstimada,
    decimal? MargemPercentagem,
    string MetodologiaMargem,
    IReadOnlyList<VendaDia> PorDia,
    IReadOnlyList<LinhaValor> PorMetodo,
    IReadOnlyList<LinhaValor> PorTipo,
    IReadOnlyList<VendaCategoria> PorCategoria,
    IReadOnlyList<VendaProduto> Produtos,
    IReadOnlyList<LinhaValor> PorUtilizador,
    IReadOnlyList<LinhaValor> PorGarcom,
    ResumoCancelamentos Cancelamentos,
    IReadOnlyList<ProdutoSemVenda>? ProdutosSemVendas = null,
    IReadOnlyList<VendaOperador>? Operadores = null);

/// <summary>Caixa aberto no painel. Sem valores: o esperado só se conhece depois do fecho (fecho cego).</summary>
public record CaixaAberto(int SessaoId, string Terminal, string Operador, DateTime AbertaEmUtc);
public record AlertaStock(int ArtigoId, string Artigo, decimal Saldo, decimal Minimo, string Unidade);

public record Dashboard(
    DateOnly Dia,
    decimal VendasHoje,
    int PedidosHoje,
    decimal TicketMedioHoje,
    decimal VendasOntem,
    IReadOnlyList<VendaDia> Ultimos7Dias,
    IReadOnlyList<LinhaValor> PorMetodoHoje,
    IReadOnlyList<VendaProduto> TopProdutosHoje,
    int PedidosAbertos,
    decimal ValorEmAberto,
    int ItensCozinhaPendentes,
    int ItensCozinhaEmPreparacao,
    DateTime? EnvioMaisAntigoPendenteUtc,
    IReadOnlyList<CaixaAberto>? Caixas,
    IReadOnlyList<AlertaStock>? StockBaixo,
    ResumoCancelamentos CancelamentosHoje,
    decimal VendasMes = 0,
    int PedidosMes = 0,
    /// <summary>Mês anterior, do dia 1 ao mesmo dia do mês (comparação justa com o mês em curso).</summary>
    decimal VendasMesAnterior = 0);

/// <summary>
/// Relatórios calculados a partir dos dados persistidos. Uma «venda» é um pedido Pago, contado na data
/// local (fuso configurado) em que fechou. Pedidos estornados não contam como venda: aparecem nos estornos.
/// </summary>
public class ServicoRelatorios(
    IAppDbContext db,
    IUtilizadorActual actual,
    IConsultaUtilizadores utilizadores,
    ConfiguracoesSistema config,
    ServicoCaixa caixa,
    TimeProvider relogio)
{
    public const string Metodologia =
        "Receita líquida = vendas − impostos incluídos − taxas de entrega. Custo estimado por item vendido: custo dos ingredientes " +
        "registado na baixa de stock (ficha técnica, custo médio na data da venda) quando existe; caso contrário, o custo de referência " +
        "actual do produto × quantidade. Valores dos produtos e categorias antes dos descontos ao nível do pedido.";

    internal static void ValidarPeriodo(DateOnly desde, DateOnly ate)
    {
        if (ate < desde)
            throw new RegraNegocioException("A data final é anterior à inicial.");
        if (ate.DayNumber - desde.DayNumber > 366)
            throw new RegraNegocioException("O período máximo do relatório é de um ano.");
    }

    public async Task<RelatorioVendas> VendasAsync(DateOnly desde, DateOnly ate, CancellationToken ct = default)
    {
        ValidarPeriodo(desde, ate);
        var (inicio, fim) = await config.IntervaloUtcAsync(desde, ate);
        var fuso = await config.FusoAsync();

        var pedidos = await db.Pedidos.AsNoTracking()
            .Where(p => p.Estado == EstadoPedido.Pago && p.FechadoEmUtc >= inicio && p.FechadoEmUtc < fim)
            .Select(p => new { p.Id, Fechado = p.FechadoEmUtc!.Value, p.Total, p.Desconto, p.Impostos, p.TaxaEntrega, p.Tipo, p.CriadoPorId, Garcom = p.Garcom != null ? p.Garcom.Nome : null })
            .ToListAsync(ct);

        // Linhas vendidas agrupadas por produto no próprio SQL (um ano tem centenas de milhares de linhas: não vêm para a memória).
        // Custo de cada linha: o da baixa de stock (ficha técnica) quando existe, senão o custo de referência do produto × quantidade.
        var itensPeriodo = db.PedidoItens.AsNoTracking()
            .Where(i => !i.Cancelado && i.Pedido.Estado == EstadoPedido.Pago && i.Pedido.FechadoEmUtc >= inicio && i.Pedido.FechadoEmUtc < fim);
        var saidas = db.MovimentosStock.AsNoTracking()
            .Where(m => m.Tipo == TipoMovimentoStock.SaidaVenda && m.OrigemTipo == ServicoBaixaStock.OrigemPedidoItem);
        // Três somas por produto (o SQL Server não soma uma condição com subconsulta): custo = referência de todas as linhas
        // − referência das linhas com baixa + custo real dessas baixas.
        var porProduto = await itensPeriodo
            .Select(i => new { i.ProdutoId, i.Quantidade, i.Total, Referencia = i.Produto.Custo * i.Quantidade })
            .GroupBy(x => x.ProdutoId)
            .Select(g => new { ProdutoId = g.Key, Quantidade = g.Sum(x => x.Quantidade), Total = g.Sum(x => x.Total), Referencia = g.Sum(x => x.Referencia) })
            .ToListAsync(ct);
        var referenciaComBaixa = await itensPeriodo
            .Where(i => saidas.Any(m => m.OrigemId == i.Id))
            .Select(i => new { i.ProdutoId, Referencia = i.Produto.Custo * i.Quantidade })
            .GroupBy(x => x.ProdutoId)
            .Select(g => new { ProdutoId = g.Key, Referencia = g.Sum(x => x.Referencia) })
            .ToDictionaryAsync(x => x.ProdutoId, x => x.Referencia, ct);
        var custoBaixas = await (
                from m in saidas
                join i in itensPeriodo on m.OrigemId equals i.Id
                select new { i.ProdutoId, Custo = -m.Quantidade * m.CustoUnitario })
            .GroupBy(x => x.ProdutoId)
            .Select(g => new { ProdutoId = g.Key, Custo = g.Sum(x => x.Custo) })
            .ToDictionaryAsync(x => x.ProdutoId, x => x.Custo, ct);
        var idsVendidos = porProduto.Select(p => p.ProdutoId).ToList();
        var fichas = await db.Produtos.AsNoTracking().Where(p => idsVendidos.Contains(p.Id))
            .Select(p => new { p.Id, p.Codigo, p.Nome, Categoria = p.Categoria.Nome, Unidade = p.UnidadeMedida != null ? p.UnidadeMedida.Codigo : "un" })
            .ToDictionaryAsync(p => p.Id, ct);
        var itens = porProduto.Select(p => new
        {
            p.ProdutoId, fichas[p.ProdutoId].Codigo, Produto = fichas[p.ProdutoId].Nome, fichas[p.ProdutoId].Categoria, fichas[p.ProdutoId].Unidade,
            p.Quantidade, p.Total,
            Custo = p.Referencia - referenciaComBaixa.GetValueOrDefault(p.ProdutoId) + custoBaixas.GetValueOrDefault(p.ProdutoId),
        }).ToList();

        var pagamentos = await db.Pagamentos.AsNoTracking()
            .Where(p => p.Estado == EstadoPagamento.Valido && p.Pedido.Estado == EstadoPedido.Pago && p.Pedido.FechadoEmUtc >= inicio && p.Pedido.FechadoEmUtc < fim)
            .GroupBy(p => new { Metodo = p.MetodoPagamento.Nome, p.Pedido.CriadoPorId })
            .Select(g => new { g.Key.Metodo, g.Key.CriadoPorId, Valor = g.Sum(p => p.Valor), Numero = g.Count() })
            .ToListAsync(ct);
        var porMetodo = pagamentos.GroupBy(p => p.Metodo).Select(g => new LinhaValor(g.Key, g.Sum(p => p.Valor), g.Sum(p => p.Numero))).ToList();

        var total = pedidos.Sum(p => p.Total);
        var impostos = pedidos.Sum(p => p.Impostos);
        var taxas = pedidos.Sum(p => p.TaxaEntrega);
        var receitaLiquida = total - impostos - taxas;
        var custo = decimal.Round(itens.Sum(i => i.Custo), 2, MidpointRounding.AwayFromZero);

        // Dias locais (inclui dias sem vendas, para os gráficos).
        var porDia = pedidos
            .GroupBy(p => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(p.Fechado, fuso)))
            .ToDictionary(g => g.Key, g => (Total: g.Sum(p => p.Total), Pedidos: g.Count()));
        var dias = Enumerable.Range(0, ate.DayNumber - desde.DayNumber + 1).Select(desde.AddDays)
            .Select(d => porDia.TryGetValue(d, out var v) ? new VendaDia(d, v.Total, v.Pedidos) : new VendaDia(d, 0m, 0)).ToList();

        var nomes = await utilizadores.NomesAsync(pedidos.Where(p => p.CriadoPorId is not null).Select(p => p.CriadoPorId!.Value), ct);
        string NomeOperador(int? id) => id is int u ? nomes.GetValueOrDefault(u, "?") : "—";

        // Produtos activos sem nenhuma venda no período (sem os das categorias só de stock, que nunca se vendem).
        var vendidos = itens.Select(i => i.ProdutoId).ToHashSet();
        var semVendas = (await db.Produtos.AsNoTracking()
                .Where(p => p.Activo && !p.Categoria.OcultaNoPos && (p.Categoria.CategoriaPai == null || !p.Categoria.CategoriaPai.OcultaNoPos))
                .OrderBy(p => p.Categoria.Nome).ThenBy(p => p.Nome)
                .Select(p => new ProdutoSemVenda(p.Id, p.Codigo, p.Nome, p.Categoria.Nome)).ToListAsync(ct))
            .Where(p => !vendidos.Contains(p.ProdutoId)).ToList();

        var operadores = pedidos.GroupBy(p => p.CriadoPorId)
            .Select(g => new VendaOperador(NomeOperador(g.Key), g.Count(), g.Sum(p => p.Total), g.Sum(p => p.Desconto),
                [.. pagamentos.Where(pg => pg.CriadoPorId == g.Key).GroupBy(pg => pg.Metodo)
                    .Select(m => new LinhaValor(m.Key, m.Sum(x => x.Valor), m.Sum(x => x.Numero))).OrderByDescending(m => m.Valor)]))
            .OrderByDescending(o => o.Total).ToList();

        return new RelatorioVendas(
            desde, ate, total, pedidos.Count,
            pedidos.Count == 0 ? 0m : decimal.Round(total / pedidos.Count, 2, MidpointRounding.AwayFromZero),
            pedidos.Sum(p => p.Desconto), impostos, taxas, receitaLiquida, custo, receitaLiquida - custo,
            receitaLiquida == 0 ? null : decimal.Round((receitaLiquida - custo) / receitaLiquida * 100m, 1, MidpointRounding.AwayFromZero),
            Metodologia,
            dias,
            [.. porMetodo.OrderByDescending(m => m.Valor)],
            [.. pedidos.GroupBy(p => p.Tipo).Select(g => new LinhaValor(NomeTipo(g.Key), g.Sum(p => p.Total), g.Count())).OrderByDescending(l => l.Valor)],
            [.. itens.GroupBy(i => i.Categoria).Select(g => new VendaCategoria(g.Key, g.Sum(i => i.Quantidade), g.Sum(i => i.Total))).OrderByDescending(c => c.Total)],
            [.. itens.GroupBy(i => new { i.ProdutoId, i.Produto, i.Categoria, i.Codigo, i.Unidade })
                .Select(g => new VendaProduto(g.Key.ProdutoId, g.Key.Produto, g.Key.Categoria, g.Sum(i => i.Quantidade), g.Sum(i => i.Total),
                    decimal.Round(g.Sum(i => i.Custo), 2, MidpointRounding.AwayFromZero), g.Key.Codigo, g.Key.Unidade))
                .OrderByDescending(p => p.Total).ThenBy(p => p.Produto)],
            [.. pedidos.GroupBy(p => NomeOperador(p.CriadoPorId))
                .Select(g => new LinhaValor(g.Key, g.Sum(p => p.Total), g.Count())).OrderByDescending(l => l.Valor)],
            [.. pedidos.Where(p => p.Garcom != null).GroupBy(p => p.Garcom!)
                .Select(g => new LinhaValor(g.Key, g.Sum(p => p.Total), g.Count())).OrderByDescending(l => l.Valor)],
            await CancelamentosAsync(inicio, fim, ct),
            semVendas,
            operadores);
    }

    public const string MetodologiaAcompanhamentos =
        "Vezes = número de pratos vendidos (pagos) com o acompanhamento; um prato com quantidade 2 conta 2. Pratos anulados e pedidos estornados não contam. " +
        "Artigos gastos: estimados com as quantidades actuais de cada opção × vezes, ao custo médio actual (a baixa real está no relatório de stock, junta com a ficha técnica).";

    /// <summary>Acompanhamentos escolhidos nas vendas pagas do período: por opção, por prato e os artigos que gastam (estimados).</summary>
    public async Task<RelatorioAcompanhamentos> AcompanhamentosAsync(DateOnly desde, DateOnly ate, CancellationToken ct = default)
    {
        ValidarPeriodo(desde, ate);
        var (inicio, fim) = await config.IntervaloUtcAsync(desde, ate);
        var escolhas = await db.PedidoItensAcompanhamentos.AsNoTracking()
            .Where(a => !a.PedidoItem.Cancelado && a.PedidoItem.Pedido.Estado == EstadoPedido.Pago
                        && a.PedidoItem.Pedido.FechadoEmUtc >= inicio && a.PedidoItem.Pedido.FechadoEmUtc < fim)
            .Select(a => new { a.OpcaoAcompanhamentoId, a.Nome, Grupo = a.OpcaoAcompanhamento.Grupo.Nome, Prato = a.PedidoItem.NomeProduto, a.PedidoItem.Quantidade })
            .ToListAsync(ct);

        var porOpcao = escolhas.GroupBy(e => new { e.OpcaoAcompanhamentoId, e.Grupo })
            .Select(g => new AcompanhamentoVendido(g.Key.Grupo, g.First().Nome, g.Sum(e => e.Quantidade), g.Select(e => e.Prato).Distinct().Count()))
            .OrderBy(o => o.Grupo).ThenByDescending(o => o.Vezes).ThenBy(o => o.Opcao).ToList();
        var porPrato = escolhas.GroupBy(e => new { e.Prato, e.Nome })
            .Select(g => new AcompanhamentoPorPrato(g.Key.Prato, g.Key.Nome, g.Sum(e => e.Quantidade)))
            .OrderBy(p => p.Prato).ThenByDescending(p => p.Vezes).ToList();

        // Artigos gastos pelas opções (configuração actual), convertidos para a unidade de stock do artigo.
        var vezes = escolhas.GroupBy(e => e.OpcaoAcompanhamentoId).ToDictionary(g => g.Key, g => g.Sum(e => e.Quantidade));
        var opcaoIds = vezes.Keys.ToList();
        var itens = await db.OpcoesAcompanhamento.AsNoTracking().Where(o => opcaoIds.Contains(o.Id))
            .SelectMany(o => o.Itens.Select(i => new
            {
                OpcaoId = o.Id, i.ArtigoId, Artigo = i.Artigo.Nome, Unidade = i.Artigo.UnidadeMedida.Codigo, i.Artigo.CustoMedio,
                i.Quantidade, FactorLinha = i.UnidadeMedida.FactorParaBase, FactorArtigo = i.Artigo.UnidadeMedida.FactorParaBase,
            }))
            .ToListAsync(ct);
        var artigos = itens.Where(i => i.FactorArtigo > 0)
            .Select(i => new { i.ArtigoId, i.Artigo, i.Unidade, i.CustoMedio, Quantidade = vezes[i.OpcaoId] * i.Quantidade * i.FactorLinha / i.FactorArtigo })
            .GroupBy(i => new { i.ArtigoId, i.Artigo, i.Unidade, i.CustoMedio })
            .Select(g =>
            {
                var q = decimal.Round(g.Sum(i => i.Quantidade), 3, MidpointRounding.AwayFromZero);
                return new ArtigoAcompanhamento(g.Key.ArtigoId, g.Key.Artigo, g.Key.Unidade, q, decimal.Round(q * g.Key.CustoMedio, 2, MidpointRounding.AwayFromZero));
            })
            .OrderByDescending(a => a.Custo).ThenBy(a => a.Artigo).ToList();

        return new RelatorioAcompanhamentos(desde, ate, escolhas.Sum(e => e.Quantidade), porOpcao, porPrato, artigos, MetodologiaAcompanhamentos);
    }

    public async Task<Dashboard> DashboardAsync(CancellationToken ct = default)
    {
        var hoje = await config.HojeAsync(relogio);
        var semana = await VendasAsync(hoje.AddDays(-6), hoje, ct);
        var diaHoje = await VendasAsync(hoje, hoje, ct);
        var ontem = semana.PorDia.FirstOrDefault(d => d.Dia == hoje.AddDays(-1))?.Total ?? 0m;

        var abertos = await db.Pedidos.AsNoTracking().Where(p => p.Estado == EstadoPedido.Aberto)
            .GroupBy(_ => 1).Select(g => new { Numero = g.Count(), Valor = g.Sum(p => p.Total - p.TotalPago) }).FirstOrDefaultAsync(ct);
        var cozinha = db.PedidoItens.AsNoTracking()
            .Where(i => i.EnviaCozinha && !i.Cancelado && i.Pedido.Estado == EstadoPedido.Aberto);
        var pendentes = await cozinha.CountAsync(i => i.EstadoCozinha == EstadoItemCozinha.Pendente, ct);
        var emPreparacao = await cozinha.CountAsync(i => i.EstadoCozinha == EstadoItemCozinha.EmPreparacao, ct);
        var maisAntigo = await cozinha.Where(i => i.EstadoCozinha == EstadoItemCozinha.Pendente || i.EstadoCozinha == EstadoItemCozinha.EmPreparacao)
            .MinAsync(i => i.EnviadoCozinhaEmUtc, ct);

        List<CaixaAberto>? caixas = null;
        if (actual.TemPermissao(Permissoes.CaixaVerTodas) || actual.TemPermissao(Permissoes.CaixaOperar))
        {
            var verTodas = actual.TemPermissao(Permissoes.CaixaVerTodas);
            var ids = await db.SessoesCaixa.AsNoTracking()
                .Where(s => s.Estado == EstadoSessaoCaixa.Aberta && (verTodas || s.UtilizadorAberturaId == actual.Id))
                .OrderBy(s => s.AbertaEmUtc).Select(s => s.Id).ToListAsync(ct);
            caixas = [];
            foreach (var id in ids)
            {
                var s = await caixa.ObterAsync(id);
                caixas.Add(new CaixaAberto(s.Id, s.Terminal, s.AbertaPor, s.AbertaEmUtc));
            }
        }

        List<AlertaStock>? stockBaixo = null;
        if (actual.TemPermissao(Permissoes.StockVer))
        {
            stockBaixo = await db.Artigos.AsNoTracking()
                .Where(a => a.Activo && a.StockMinimo > 0)
                .Select(a => new { a.Id, a.Nome, a.StockMinimo, Unidade = a.UnidadeMedida.Codigo, Saldo = db.StockSaldos.Where(s => s.ArtigoId == a.Id).Sum(s => (decimal?)s.Quantidade) ?? 0m })
                .Where(a => a.Saldo <= a.StockMinimo)
                .OrderBy(a => a.Saldo - a.StockMinimo)
                .Take(20)
                .Select(a => new AlertaStock(a.Id, a.Nome, a.Saldo, a.StockMinimo, a.Unidade))
                .ToListAsync(ct);
        }

        var inicioMes = new DateOnly(hoje.Year, hoje.Month, 1);
        var mes = await TotalVendasAsync(inicioMes, hoje, ct);
        var inicioAnterior = inicioMes.AddMonths(-1);
        var fimAnterior = inicioAnterior.AddDays(Math.Min(hoje.Day, DateTime.DaysInMonth(inicioAnterior.Year, inicioAnterior.Month)) - 1);
        var anterior = await TotalVendasAsync(inicioAnterior, fimAnterior, ct);

        return new Dashboard(
            hoje, diaHoje.TotalVendas, diaHoje.NumeroPedidos, diaHoje.TicketMedio, ontem,
            semana.PorDia, diaHoje.PorMetodo, [.. diaHoje.Produtos.Take(5)],
            abertos?.Numero ?? 0, abertos?.Valor ?? 0m,
            pendentes, emPreparacao, maisAntigo, caixas, stockBaixo, diaHoje.Cancelamentos,
            mes.Total, mes.Pedidos, anterior.Total);
    }

    /// <summary>Total e número de vendas (pedidos pagos) entre dois dias locais, sem o detalhe do relatório.</summary>
    private async Task<(decimal Total, int Pedidos)> TotalVendasAsync(DateOnly desde, DateOnly ate, CancellationToken ct)
    {
        var (inicio, fim) = await config.IntervaloUtcAsync(desde, ate);
        var r = await db.Pedidos.AsNoTracking()
            .Where(p => p.Estado == EstadoPedido.Pago && p.FechadoEmUtc >= inicio && p.FechadoEmUtc < fim)
            .GroupBy(_ => 1).Select(g => new { Total = g.Sum(p => p.Total), Pedidos = g.Count() }).FirstOrDefaultAsync(ct);
        return (r?.Total ?? 0m, r?.Pedidos ?? 0);
    }

    private async Task<ResumoCancelamentos> CancelamentosAsync(DateTime inicio, DateTime fim, CancellationToken ct)
    {
        var cancelados = await db.Pedidos.AsNoTracking()
            .Where(p => p.Estado == EstadoPedido.Cancelado && p.CanceladoEmUtc >= inicio && p.CanceladoEmUtc < fim)
            .Select(p => p.Itens.Where(i => i.MotivoCancelamento != ServicoPedidos.MotivoRemovidoAntesEnvio).Sum(i => (decimal?)i.Total) ?? 0m)
            .ToListAsync(ct);
        // Itens cancelados depois de enviados, em pedidos que continuaram.
        var itens = await db.PedidoItens.AsNoTracking()
            .Where(i => i.Cancelado && i.EstadoCozinha != EstadoItemCozinha.NaoEnviado && i.CanceladoEmUtc >= inicio && i.CanceladoEmUtc < fim
                        && i.Pedido.Estado != EstadoPedido.Cancelado)
            .Select(i => i.Total).ToListAsync(ct);
        var estornados = await db.Pedidos.AsNoTracking()
            .Where(p => p.Estado == EstadoPedido.Estornado && p.CanceladoEmUtc >= inicio && p.CanceladoEmUtc < fim)
            .Select(p => p.TotalPago).ToListAsync(ct);
        return new ResumoCancelamentos(cancelados.Count, cancelados.Sum(), itens.Count, itens.Sum(), estornados.Count, estornados.Sum());
    }

    private static string NomeTipo(TipoPedido tipo) => tipo switch
    {
        TipoPedido.Mesa => "Mesa",
        TipoPedido.TakeAway => "Take Away",
        TipoPedido.Balcao => "Balcão",
        _ => "Delivery",
    };
}
