using System.Globalization;
using LAMGestRestaurant.Application.Caixa;
using LAMGestRestaurant.Application.Comum;
using LAMGestRestaurant.Application.Impressao;
using LAMGestRestaurant.Application.Seguranca;
using LAMGestRestaurant.Domain.Comum;
using LAMGestRestaurant.Domain.Sistema;
using Microsoft.EntityFrameworkCore;

namespace LAMGestRestaurant.Application.Relatorios;

public enum TipoDocumentoRelatorio
{
    Pagamentos, Vendas, Produtos, Despesas, Financeiro, Stock, FechoCaixa, ListaVendas, Clientes, Impostos, Anulacoes, CartoesEvento,
    FacturasCredito, HistoricoCliente, ExtratoCliente, DescargasCartoes, FechoDia, FolhaContagem,
}

/// <summary>
/// Relatórios em formato de documento imprimível (talão 80/58 mm ou A4), com os mesmos números dos ecrãs.
/// O mesmo documento serve a impressão pelo servidor e pela janela do navegador.
/// </summary>
public class ServicoDocumentosRelatorio(
    IAppDbContext db,
    IUtilizadorActual actual,
    ConfiguracoesSistema config,
    ServicoRelatorios relatorios,
    ServicoRelatoriosGestao gestao,
    ServicoCaixa caixa,
    ServicoRelatoriosDetalhe detalhe,
    Cartoes.ServicoRelatoriosCartoes cartoes,
    Facturacao.ServicoContaCorrente contas,
    ServicoDia dias,
    Stock.ServicoStock stock)
{
    private string moeda = "MT";
    private TimeZoneInfo fuso = TimeZoneInfo.Utc;
    private IReadOnlyList<LinhaImpressao>? cabecalho;

    /// <summary>Contas correntes dos clientes: quem consulta a facturação ou os relatórios de vendas/financeiros.</summary>
    public static readonly string[] PermissoesContas = [Permissoes.FacturacaoVer, Permissoes.RelatoriosVendas, Permissoes.RelatoriosFinanceiros];

    public static string Permissao(TipoDocumentoRelatorio tipo) => tipo switch
    {
        TipoDocumentoRelatorio.Pagamentos or TipoDocumentoRelatorio.Vendas or TipoDocumentoRelatorio.Produtos or TipoDocumentoRelatorio.ListaVendas
            or TipoDocumentoRelatorio.Clientes or TipoDocumentoRelatorio.Impostos or TipoDocumentoRelatorio.Anulacoes => Permissoes.RelatoriosVendas,
        TipoDocumentoRelatorio.Despesas or TipoDocumentoRelatorio.Financeiro or TipoDocumentoRelatorio.CartoesEvento or TipoDocumentoRelatorio.DescargasCartoes => Permissoes.RelatoriosFinanceiros,
        TipoDocumentoRelatorio.Stock => Permissoes.RelatoriosStock,
        TipoDocumentoRelatorio.FacturasCredito or TipoDocumentoRelatorio.HistoricoCliente or TipoDocumentoRelatorio.ExtratoCliente => Permissoes.FacturacaoVer,
        TipoDocumentoRelatorio.FechoDia => Permissoes.CaixaDia,
        TipoDocumentoRelatorio.FolhaContagem => Permissoes.StockVer,
        _ => Permissoes.CaixaOperar,
    };

    private static bool DeCliente(TipoDocumentoRelatorio tipo) => tipo is TipoDocumentoRelatorio.HistoricoCliente or TipoDocumentoRelatorio.ExtratoCliente;

    public async Task<DocumentoImpressao> GerarAsync(TipoDocumentoRelatorio tipo, DateOnly desde, DateOnly ate, int? sessaoId, CancellationToken ct = default,
        int? clienteId = null, int? diaId = null, int? inventarioId = null, bool comSaldo = false)
    {
        var autorizado = tipo switch
        {
            TipoDocumentoRelatorio.FechoCaixa => actual.TemPermissao(Permissoes.CaixaOperar) || actual.TemPermissao(Permissoes.CaixaVerTodas),
            TipoDocumentoRelatorio.FechoDia => actual.TemPermissao(Permissoes.CaixaDia) || actual.TemPermissao(Permissoes.CaixaVerTodas),
            TipoDocumentoRelatorio.FacturasCredito or TipoDocumentoRelatorio.HistoricoCliente or TipoDocumentoRelatorio.ExtratoCliente =>
                PermissoesContas.Any(actual.TemPermissao),
            _ => actual.TemPermissao(Permissao(tipo)),
        };
        if (!autorizado)
            throw new AcessoNegadoException("Não tem permissão para este relatório.");
        // O histórico e o extrato do cliente podem abranger vários anos (validados no serviço das contas).
        if (tipo is not (TipoDocumentoRelatorio.FechoCaixa or TipoDocumentoRelatorio.FechoDia or TipoDocumentoRelatorio.FolhaContagem) && !DeCliente(tipo))
            ServicoRelatorios.ValidarPeriodo(desde, ate);
        if (DeCliente(tipo) && clienteId is null)
            throw new RegraNegocioException("Escolha o cliente.");
        moeda = await config.TextoAsync(ChavesConfiguracao.SimboloMoeda, "MT");
        fuso = await config.FusoAsync();

        return tipo switch
        {
            TipoDocumentoRelatorio.Pagamentos => await PagamentosAsync(desde, ate, ct),
            TipoDocumentoRelatorio.Vendas => await VendasAsync(desde, ate, ct),
            TipoDocumentoRelatorio.Produtos => await ProdutosAsync(desde, ate, ct),
            TipoDocumentoRelatorio.Despesas => await DespesasAsync(desde, ate, ct),
            TipoDocumentoRelatorio.Financeiro => await FinanceiroAsync(desde, ate, ct),
            TipoDocumentoRelatorio.Stock => await StockAsync(ct),
            TipoDocumentoRelatorio.ListaVendas => await ListaVendasAsync(desde, ate, ct),
            TipoDocumentoRelatorio.Clientes => await ClientesAsync(desde, ate, ct),
            TipoDocumentoRelatorio.Impostos => await ImpostosAsync(desde, ate, ct),
            TipoDocumentoRelatorio.Anulacoes => await AnulacoesAsync(desde, ate, ct),
            TipoDocumentoRelatorio.CartoesEvento => await CartoesEventoAsync(desde, ate, sessaoId, ct),
            TipoDocumentoRelatorio.DescargasCartoes => await DescargasCartoesAsync(desde, ate, sessaoId, ct),
            TipoDocumentoRelatorio.FacturasCredito => await FacturasCreditoAsync(desde, ate, clienteId, ct),
            TipoDocumentoRelatorio.HistoricoCliente => await HistoricoClienteAsync(clienteId!.Value, desde, ate, ct),
            TipoDocumentoRelatorio.ExtratoCliente => await ExtratoClienteAsync(clienteId!.Value, desde, ate, ct),
            TipoDocumentoRelatorio.FechoDia => await FechoDiaAsync(diaId ?? throw new RegraNegocioException("Indique o dia."), ct),
            TipoDocumentoRelatorio.FolhaContagem => await FolhaContagemAsync(inventarioId ?? throw new RegraNegocioException("Indique o inventário."), comSaldo),
            _ =>await FechoCaixaAsync(sessaoId ?? throw new RegraNegocioException("Indique a sessão de caixa."), ct),
        };
    }

    // ---------------- Documentos ----------------

    private async Task<DocumentoImpressao> PagamentosAsync(DateOnly desde, DateOnly ate, CancellationToken ct)
    {
        var (inicio, fim) = await config.IntervaloUtcAsync(desde, ate);
        var pagamentos = await db.Pagamentos.AsNoTracking()
            .Where(p => p.DataUtc >= inicio && p.DataUtc < fim)
            .OrderBy(p => p.DataUtc).ThenBy(p => p.Id)
            .Select(p => new { p.DataUtc, Pedido = p.Pedido.Numero, Metodo = p.MetodoPagamento.Nome, Ordem = p.MetodoPagamento.Ordem, p.Valor, p.Troco, p.Referencia, p.Estado, p.MotivoAnulacao })
            .ToListAsync(ct);
        var reembolsos = await db.MovimentosCaixa.AsNoTracking()
            .Where(m => m.Tipo == TipoMovimentoCaixa.Reembolso && m.DataUtc >= inicio && m.DataUtc < fim)
            .OrderBy(m => m.DataUtc)
            .Select(m => new { m.DataUtc, Metodo = m.MetodoPagamento.Nome, m.Valor, m.Motivo })
            .ToListAsync(ct);
        var validos = pagamentos.Where(p => p.Estado == EstadoPagamento.Valido).ToList();
        var anulados = pagamentos.Where(p => p.Estado == EstadoPagamento.Anulado).ToList();
        var variosDias = desde != ate;

        var l = await CabecalhoAsync("Relatório de pagamentos", Periodo(desde, ate));
        l.Add(new("Pagamentos recebidos", Negrito: true));
        if (validos.Count == 0) l.Add(new("Sem pagamentos no período."));
        foreach (var p in validos)
        {
            l.Add(new($"{Hora(p.DataUtc, variosDias)} {p.Pedido} · {p.Metodo}", Valor(p.Valor)));
            if (p.Referencia is not null || p.Troco > 0)
                l.Add(new("   " + string.Join(" · ", new[] { p.Referencia is null ? null : $"Ref. {p.Referencia}", p.Troco > 0 ? $"troco {Valor(p.Troco)}" : null }.Where(x => x is not null))));
        }
        l.Add(LinhaImpressao.Traco);
        l.Add(new("Totais por método", Negrito: true));
        foreach (var g in validos.GroupBy(p => (p.Metodo, p.Ordem)).OrderBy(g => g.Key.Ordem))
            l.Add(new($"{g.Key.Metodo} ({g.Count()})", Valor(g.Sum(p => p.Valor))));
        var recebido = validos.Sum(p => p.Valor);
        l.Add(new($"TOTAL RECEBIDO ({validos.Count})", Valor(recebido), Negrito: true, Grande: true));
        if (anulados.Count > 0)
        {
            l.Add(LinhaImpressao.Traco);
            l.Add(new($"Pagamentos anulados ({anulados.Count})", Negrito: true));
            foreach (var p in anulados)
            {
                l.Add(new($"{Hora(p.DataUtc, variosDias)} {p.Pedido} · {p.Metodo}", Valor(p.Valor)));
                if (p.MotivoAnulacao is not null) l.Add(new($"   Motivo: {p.MotivoAnulacao}"));
            }
            l.Add(new("Total anulado", Valor(anulados.Sum(p => p.Valor))));
        }
        var totalReembolsos = reembolsos.Sum(r => r.Valor);
        if (reembolsos.Count > 0)
        {
            l.Add(LinhaImpressao.Traco);
            l.Add(new($"Reembolsos de estornos ({reembolsos.Count})", Negrito: true));
            foreach (var r in reembolsos)
                l.Add(new($"{Hora(r.DataUtc, variosDias)} {r.Metodo} · {r.Motivo}", "-" + Valor(r.Valor)));
            l.Add(new("Total reembolsado", "-" + Valor(totalReembolsos)));
        }
        l.Add(LinhaImpressao.Traco);
        l.Add(new("LÍQUIDO (recebido − reembolsos)", Valor(recebido - totalReembolsos), Negrito: true));
        Rodape(l);
        return new DocumentoImpressao("Pagamentos " + Periodo(desde, ate), l, cabecalho);
    }

    private async Task<DocumentoImpressao> VendasAsync(DateOnly desde, DateOnly ate, CancellationToken ct)
    {
        var r = await relatorios.VendasAsync(desde, ate, ct);
        var l = await CabecalhoAsync("Resumo de vendas", Periodo(desde, ate));
        l.Add(new("Total de vendas", Valor(r.TotalVendas), Negrito: true, Grande: true));
        l.Add(new("Pedidos pagos", r.NumeroPedidos.ToString(CultureInfo.InvariantCulture)));
        l.Add(new("Ticket médio", Valor(r.TicketMedio)));
        l.Add(new("Descontos", Valor(r.Descontos)));
        l.Add(new("Impostos incluídos", Valor(r.Impostos)));
        if (r.TaxasEntrega > 0) l.Add(new("Taxas de entrega", Valor(r.TaxasEntrega)));
        Seccao(l, "Por método de pagamento", r.PorMetodo.Select(m => ($"{m.Rotulo} ({m.Quantidade})", m.Valor)));
        Seccao(l, "Por tipo de pedido", r.PorTipo.Select(m => ($"{m.Rotulo} ({m.Quantidade})", m.Valor)));
        Seccao(l, "Por categoria", r.PorCategoria.Select(c => (c.Categoria, c.Total)));
        Seccao(l, "Produtos mais vendidos", r.Produtos.Take(10).Select(p => ($"{ComposicaoImpressao.Quantidade(p.Quantidade)} x {p.Produto}", p.Total)));
        if (r.PorGarcom.Count > 0)
            Seccao(l, "Por garçom", r.PorGarcom.Select(m => ($"{m.Rotulo} ({m.Quantidade})", m.Valor)));
        var c = r.Cancelamentos;
        l.Add(LinhaImpressao.Traco);
        l.Add(new("Cancelamentos e estornos", Negrito: true));
        l.Add(new($"Pedidos cancelados ({c.PedidosCancelados})", Valor(c.ValorPedidosCancelados)));
        l.Add(new($"Itens anulados após envio ({c.ItensCancelados})", Valor(c.ValorItensCancelados)));
        l.Add(new($"Pedidos estornados ({c.PedidosEstornados})", Valor(c.ValorEstornado)));
        l.Add(LinhaImpressao.Traco);
        l.Add(new("Margem estimada", Valor(r.MargemEstimada)));
        Rodape(l);
        return new DocumentoImpressao("Vendas " + Periodo(desde, ate), l, cabecalho);
    }

    private async Task<DocumentoImpressao> ProdutosAsync(DateOnly desde, DateOnly ate, CancellationToken ct)
    {
        var r = await relatorios.VendasAsync(desde, ate, ct);
        var l = await CabecalhoAsync("Produtos vendidos", Periodo(desde, ate));
        if (r.Produtos.Count == 0) l.Add(new("Sem vendas no período."));
        foreach (var categoria in r.Produtos.GroupBy(p => p.Categoria).OrderByDescending(g => g.Sum(p => p.Total)))
        {
            l.Add(new(categoria.Key, Valor(categoria.Sum(p => p.Total)), Negrito: true));
            foreach (var p in categoria.OrderByDescending(p => p.Total))
                l.Add(new($"  {ComposicaoImpressao.Quantidade(p.Quantidade)} x {p.Produto}", Valor(p.Total)));
        }
        l.Add(LinhaImpressao.Traco);
        l.Add(new("TOTAL (antes de descontos)", Valor(r.Produtos.Sum(p => p.Total)), Negrito: true));
        l.Add(new("Quantidade total", ComposicaoImpressao.Quantidade(r.Produtos.Sum(p => p.Quantidade))));
        Rodape(l);
        return new DocumentoImpressao("Produtos vendidos " + Periodo(desde, ate), l, cabecalho);
    }

    private async Task<DocumentoImpressao> DespesasAsync(DateOnly desde, DateOnly ate, CancellationToken ct)
    {
        var (inicio, fim) = await config.IntervaloUtcAsync(desde, ate);
        var despesas = await db.Despesas.AsNoTracking()
            .Where(d => d.DataUtc >= inicio && d.DataUtc < fim)
            .OrderBy(d => d.DataUtc)
            .Select(d => new { d.DataUtc, Categoria = d.CategoriaDespesa.Nome, d.Descricao, d.Valor, Metodo = d.MetodoPagamento.Nome, d.Referencia, d.Estado, Caixa = d.SessaoCaixaId })
            .ToListAsync(ct);
        var confirmadas = despesas.Where(d => d.Estado == EstadoDespesa.Confirmada).ToList();
        var anuladas = despesas.Where(d => d.Estado == EstadoDespesa.Anulada).ToList();

        var l = await CabecalhoAsync("Relatório de despesas", Periodo(desde, ate));
        if (confirmadas.Count == 0) l.Add(new("Sem despesas no período."));
        foreach (var d in confirmadas)
        {
            l.Add(new($"{Hora(d.DataUtc, true)} {d.Categoria} — {d.Descricao}", Valor(d.Valor)));
            l.Add(new("   " + string.Join(" · ", new[] { d.Metodo, d.Referencia is null ? null : $"Ref. {d.Referencia}", d.Caixa is int s ? $"caixa n.º {s}" : null }.Where(x => x is not null))));
        }
        Seccao(l, "Por categoria", confirmadas.GroupBy(d => d.Categoria).OrderByDescending(g => g.Sum(d => d.Valor)).Select(g => ($"{g.Key} ({g.Count()})", g.Sum(d => d.Valor))));
        l.Add(LinhaImpressao.Traco);
        l.Add(new($"TOTAL ({confirmadas.Count})", Valor(confirmadas.Sum(d => d.Valor)), Negrito: true, Grande: true));
        if (anuladas.Count > 0)
            l.Add(new($"Anuladas (não contam): {anuladas.Count}", Valor(anuladas.Sum(d => d.Valor))));
        Rodape(l);
        return new DocumentoImpressao("Despesas " + Periodo(desde, ate), l, cabecalho);
    }

    private async Task<DocumentoImpressao> FinanceiroAsync(DateOnly desde, DateOnly ate, CancellationToken ct)
    {
        var r = await gestao.FinanceiroAsync(desde, ate, ct);
        var l = await CabecalhoAsync("Relatório financeiro", Periodo(desde, ate));
        l.Add(new("Vendas (com impostos)", Valor(r.TotalVendas)));
        l.Add(new("Impostos incluídos", "-" + Valor(r.Impostos)));
        l.Add(new("Vendas sem impostos", Valor(r.VendasSemImpostos), Negrito: true));
        l.Add(new("Custo estimado das vendas", "-" + Valor(r.CustoEstimadoVendas)));
        l.Add(new("Despesas", "-" + Valor(r.Despesas)));
        l.Add(new("RESULTADO ESTIMADO", Valor(r.ResultadoEstimado), Negrito: true, Grande: true));
        l.Add(new($"Fora do resultado: estornos {Valor(r.Estornos)} ({r.PedidosEstornados}); compras {Valor(r.ComprasConfirmadas)} ({r.NumeroCompras})"));
        Seccao(l, "Recebimentos por método", r.RecebimentosPorMetodo.Select(m => ($"{m.Rotulo} ({m.Quantidade})", m.Valor)));
        Seccao(l, "Despesas por categoria", r.DespesasPorCategoria.Select(m => ($"{m.Rotulo} ({m.Quantidade})", m.Valor)));
        l.Add(LinhaImpressao.Traco);
        l.Add(new($"Fechos de caixa ({r.SessoesFechadas.Count})", Negrito: true));
        foreach (var s in r.SessoesFechadas)
            l.Add(new($"N.º {s.SessaoId} {s.Terminal} · {s.AbertaPor}", (s.Diferenca > 0 ? "+" : "") + Valor(s.Diferenca)));
        if (r.SessoesFechadas.Count > 0)
            l.Add(new("Diferença total", Valor(r.SessoesFechadas.Sum(s => s.Diferenca)), Negrito: true));
        if (r.SessoesAbertas > 0)
            l.Add(new($"Caixas ainda abertos: {r.SessoesAbertas}"));
        Rodape(l);
        return new DocumentoImpressao("Financeiro " + Periodo(desde, ate), l, cabecalho);
    }

    private async Task<DocumentoImpressao> StockAsync(CancellationToken ct)
    {
        var artigos = await db.Artigos.AsNoTracking().Where(a => a.Activo)
            .OrderBy(a => a.Tipo).ThenBy(a => a.Nome)
            .Select(a => new
            {
                a.Codigo, a.Nome, a.Tipo, Unidade = a.UnidadeMedida.Codigo, a.CustoMedio, a.StockMinimo,
                Saldo = db.StockSaldos.Where(s => s.ArtigoId == a.Id).Sum(s => (decimal?)s.Quantidade) ?? 0m,
            })
            .ToListAsync(ct);
        var hoje = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, fuso));
        var l = await CabecalhoAsync("Existências em stock", $"Em {hoje:dd/MM/yyyy}");
        foreach (var tipo in artigos.GroupBy(a => a.Tipo))
        {
            l.Add(new(tipo.Key switch { TipoArtigo.Ingrediente => "Ingredientes", TipoArtigo.Revenda => "Revenda", _ => "Consumíveis" },
                Valor(tipo.Sum(a => Math.Max(0, a.Saldo) * a.CustoMedio)), Negrito: true));
            foreach (var a in tipo)
            {
                var baixo = a.StockMinimo > 0 && a.Saldo <= a.StockMinimo;
                l.Add(new($"  {a.Nome}{(baixo ? " (!)" : "")}", $"{ComposicaoImpressao.Quantidade(a.Saldo)} {a.Unidade}", Negrito: baixo));
                l.Add(new($"     {a.Codigo} · {Valor(a.CustoMedio)}/{a.Unidade}", Valor(Math.Round(Math.Max(0, a.Saldo) * a.CustoMedio, 2))));
            }
        }
        if (artigos.Count == 0) l.Add(new("Sem artigos de stock."));
        l.Add(LinhaImpressao.Traco);
        l.Add(new("VALOR TOTAL DO STOCK", Valor(Math.Round(artigos.Sum(a => Math.Max(0, a.Saldo) * a.CustoMedio), 2)), Negrito: true, Grande: true));
        var abaixo = artigos.Count(a => a.StockMinimo > 0 && a.Saldo <= a.StockMinimo);
        l.Add(new($"Artigos: {artigos.Count} · abaixo do mínimo (!): {abaixo}"));
        Rodape(l);
        return new DocumentoImpressao($"Stock {hoje:dd-MM-yyyy}", l, cabecalho);
    }

    private async Task<DocumentoImpressao> ListaVendasAsync(DateOnly desde, DateOnly ate, CancellationToken ct)
    {
        var r = await detalhe.ListaVendasAsync(new FiltroListaVendas(desde, ate, Todas: true), ct);
        var variosDias = desde != ate;
        var l = await CabecalhoAsync("Lista de vendas", Periodo(desde, ate));
        if (r.Totais.Vendas == 0) l.Add(new("Sem vendas no período."));
        foreach (var v in r.Vendas.Itens)
        {
            var onde = v.Tipo switch { TipoPedido.Mesa => $"Mesa {v.Mesa}", TipoPedido.TakeAway => "Take Away", TipoPedido.Balcao => "Balcão", _ => "Delivery" };
            l.Add(new($"{Hora(v.DataUtc, variosDias)} {v.Numero} · {onde}", Valor(v.Total)));
            var detalheLinha = new[] { v.Operador, v.Cliente, v.Metodos, v.Desconto > 0 ? $"desc. {Valor(v.Desconto)}" : null }.Where(x => !string.IsNullOrEmpty(x));
            l.Add(new("   " + string.Join(" · ", detalheLinha)));
        }
        var t = r.Totais;
        l.Add(LinhaImpressao.Traco);
        l.Add(new("Subtotal", Valor(t.Subtotal)));
        l.Add(new("Descontos", "-" + Valor(t.Desconto)));
        if (t.TaxaEntrega > 0) l.Add(new("Taxas de entrega", Valor(t.TaxaEntrega)));
        l.Add(new("Impostos incluídos", Valor(t.Impostos)));
        l.Add(new($"TOTAL ({t.Vendas} vendas)", Valor(t.Total), Negrito: true, Grande: true));
        l.Add(new("Recebido / troco", $"{Valor(t.Recebido)} / {Valor(t.Troco)}"));
        Rodape(l);
        return new DocumentoImpressao("Lista de vendas " + Periodo(desde, ate), l, cabecalho);
    }

    private async Task<DocumentoImpressao> ClientesAsync(DateOnly desde, DateOnly ate, CancellationToken ct)
    {
        var r = await detalhe.ClientesAsync(desde, ate, null, ct);
        var l = await CabecalhoAsync("Vendas por cliente", Periodo(desde, ate));
        if (r.Clientes.Count == 0) l.Add(new("Sem vendas com cliente associado."));
        foreach (var c in r.Clientes)
        {
            l.Add(new($"{c.Nome} ({c.Compras})", Valor(c.Total), Negrito: true));
            l.Add(new($"   Última compra {DataHora(c.UltimaCompraUtc)}" + (c.Descontos > 0 ? $" · descontos {Valor(c.Descontos)}" : "")));
        }
        l.Add(LinhaImpressao.Traco);
        l.Add(new($"Com cliente ({r.VendasComCliente})", Valor(r.TotalComCliente), Negrito: true));
        l.Add(new($"Sem cliente associado ({r.VendasSemCliente})", Valor(r.TotalSemCliente)));
        Rodape(l);
        return new DocumentoImpressao("Clientes " + Periodo(desde, ate), l, cabecalho);
    }

    private async Task<DocumentoImpressao> ImpostosAsync(DateOnly desde, DateOnly ate, CancellationToken ct)
    {
        var r = await detalhe.ImpostosAsync(desde, ate, null, ct);
        var l = await CabecalhoAsync("IVA e descontos", Periodo(desde, ate));
        l.Add(new($"Vendas ({r.Vendas})", Valor(r.Total), Negrito: true));
        l.Add(new("Subtotal (antes de descontos)", Valor(r.Subtotal)));
        l.Add(new("Descontos", "-" + Valor(r.Descontos)));
        l.Add(new("Base sem imposto", Valor(r.BaseSemImposto)));
        l.Add(new("Imposto incluído", Valor(r.Impostos), Negrito: true));
        if (r.TaxasEntrega > 0) l.Add(new("Taxas de entrega (sem imposto)", Valor(r.TaxasEntrega)));
        l.Add(LinhaImpressao.Traco);
        l.Add(new("Por taxa", Negrito: true));
        foreach (var t in r.PorTaxa)
        {
            l.Add(new(t.Taxa, Valor(t.ComImposto), Negrito: true));
            l.Add(new($"   Base {Valor(t.BaseSemImposto)}", $"Imposto {Valor(t.Imposto)}"));
        }
        l.Add(LinhaImpressao.Traco);
        l.Add(new(r.Agrupamento == "mes" ? "Por mês" : "Por dia", Negrito: true));
        foreach (var p in r.PorPeriodo)
        {
            l.Add(new($"{p.Periodo} ({p.Vendas})", Valor(p.Total)));
            l.Add(new($"   Imposto {Valor(p.Imposto)}" + (p.Descontos > 0 ? $" · desc. {Valor(p.Descontos)}" : "")));
        }
        Rodape(l);
        return new DocumentoImpressao("IVA " + Periodo(desde, ate), l, cabecalho);
    }

    private async Task<DocumentoImpressao> AnulacoesAsync(DateOnly desde, DateOnly ate, CancellationToken ct)
    {
        var r = await detalhe.AnulacoesAsync(desde, ate, ct);
        var variosDias = desde != ate;
        var l = await CabecalhoAsync("Anulações e devoluções", Periodo(desde, ate));
        void Pedidos(string titulo, IReadOnlyList<PedidoAnulado> lista)
        {
            l.Add(new($"{titulo} ({lista.Count})", lista.Count == 0 ? "—" : Valor(lista.Sum(p => p.Valor)), Negrito: true));
            foreach (var p in lista)
            {
                l.Add(new($"{Hora(p.DataUtc, variosDias)} {p.Numero}{(p.Recibo is null ? "" : $" ({p.Recibo})")}", Valor(p.Valor)));
                l.Add(new($"   {p.Por ?? "—"}: {p.Motivo ?? "sem motivo"}"));
            }
            l.Add(LinhaImpressao.Traco);
        }
        Pedidos("Estornos (devoluções)", r.Estornos);
        Pedidos("Pedidos cancelados", r.PedidosCancelados);
        l.Add(new($"Itens cancelados após envio ({r.ItensCancelados.Count})", r.ItensCancelados.Count == 0 ? "—" : Valor(r.ItensCancelados.Sum(i => i.Valor)), Negrito: true));
        foreach (var i in r.ItensCancelados)
        {
            l.Add(new($"{Hora(i.DataUtc, variosDias)} {i.Pedido} · {ComposicaoImpressao.Quantidade(i.Quantidade)} x {i.Produto}", Valor(i.Valor)));
            l.Add(new($"   {i.Por ?? "—"}: {i.Motivo ?? "sem motivo"}"));
        }
        l.Add(LinhaImpressao.Traco);
        l.Add(new($"Pagamentos anulados ({r.PagamentosAnulados.Count})", r.PagamentosAnulados.Count == 0 ? "—" : Valor(r.PagamentosAnulados.Sum(p => p.Valor)), Negrito: true));
        foreach (var p in r.PagamentosAnulados)
        {
            l.Add(new($"{Hora(p.DataUtc, variosDias)} {p.Pedido} · {p.Metodo}", Valor(p.Valor)));
            l.Add(new($"   {p.Por ?? "—"}: {p.Motivo ?? "sem motivo"}"));
        }
        l.Add(new("Nenhum destes valores conta como venda."));
        Rodape(l);
        return new DocumentoImpressao("Anulações " + Periodo(desde, ate), l, cabecalho);
    }

    /// <summary>
    /// Fecho de uma sessão de caixa, com espaço para assinaturas. Fecho cego: numa sessão ainda aberta (ponto de situação)
    /// não aparecem recebimentos, esperado nem diferenças, só o fundo, os movimentos manuais e os métodos a contar.
    /// Depois do fecho: valores por método (esperado, contado, diferença) e os produtos vendidos por categoria.
    /// Configurações → Documentos e POS: «contagem cega» imprime só o valor contado; os produtos podem ficar de fora.
    /// </summary>
    private async Task<DocumentoImpressao> FechoCaixaAsync(int sessaoId, CancellationToken ct)
    {
        var s = await caixa.ObterAsync(sessaoId); // aplica a regra: o próprio operador ou caixa.ver-todas
        var fechada = s.Estado == EstadoSessaoCaixa.Fechada;
        var contagemCega = await config.BoolAsync(ChavesConfiguracao.FechoContagemCega);
        var imprimeProdutos = await config.BoolAsync(ChavesConfiguracao.FechoImprimeProdutos, padrao: true);
        var l = await CabecalhoAsync(fechada ? "Fecho de caixa" : "Caixa — ponto de situação", $"Sessão n.º {s.Id} · {s.Terminal}");
        l.Add(new("Aberta por", s.AbertaPor));
        l.Add(new("Abertura", DataHora(s.AbertaEmUtc)));
        if (fechada)
        {
            l.Add(new("Fechada por", s.FechadaPor ?? "—"));
            l.Add(new("Fecho", s.FechadaEmUtc is DateTime f ? DataHora(f) : "—"));
        }
        else
        {
            l.Add(new("AINDA ABERTA", Negrito: true));
            l.Add(new("Os valores esperados e as diferenças só aparecem depois do fecho."));
        }
        l.Add(new("Fundo inicial", Valor(s.FundoInicial)));
        if (fechada && contagemCega)
        {
            l.Add(LinhaImpressao.Traco);
            l.Add(new("Contagem", Negrito: true));
            foreach (var m in s.PorMetodo.Where(m => m.Contar || m.Contado is > 0))
                l.Add(new($"  {m.Metodo}", Valor(m.Contado ?? 0)));
        }
        else if (fechada)
        {
            l.Add(new($"Pedidos pagos ({s.PedidosPagos})", Valor(s.TotalRecebido ?? 0)));
            // Só métodos com movimento ou com algo contado (o fecho regista 0 nos restantes; não interessa no papel).
            foreach (var m in s.PorMetodo.Where(m => m.Fundo != 0 || m.Recebimentos != 0 || m.Entradas != 0 || m.Saidas != 0 || m.Contado is > 0 || m.Diferenca is not (null or 0)))
            {
                l.Add(LinhaImpressao.Traco);
                l.Add(new(m.Metodo, Negrito: true));
                if (m.Fundo != 0) l.Add(new("  Fundo", Valor(m.Fundo)));
                if (m.Recebimentos is decimal r and not 0) l.Add(new("  Recebido", Valor(r)));
                if (m.Entradas is decimal e and not 0) l.Add(new("  Entradas", Valor(e)));
                if (m.Saidas is decimal sa and not 0) l.Add(new("  Saídas", "-" + Valor(sa)));
                l.Add(new("  Esperado", Valor(m.Esperado ?? 0), Negrito: true));
                l.Add(new("  Contado", Valor(m.Contado ?? 0)));
                l.Add(new("  Diferença", (m.Diferenca > 0 ? "+" : "") + Valor(m.Diferenca ?? 0), Negrito: m.Diferenca is not (null or 0)));
            }
        }
        else
        {
            l.Add(LinhaImpressao.Traco);
            l.Add(new("Métodos a contar no fecho", Negrito: true));
            foreach (var m in s.PorMetodo.Where(m => m.Contar))
                l.Add(new($"  {m.Metodo}", "________"));
        }
        if (s.Movimentos.Count > 0)
        {
            l.Add(LinhaImpressao.Traco);
            l.Add(new("Movimentos manuais", Negrito: true));
            foreach (var mv in s.Movimentos)
                l.Add(new($"{Hora(mv.DataUtc, false)} {mv.Tipo} · {mv.Motivo}", (mv.Tipo == TipoMovimentoCaixa.Entrada ? "" : "-") + Valor(mv.Valor)));
        }
        if (fechada)
        {
            l.Add(LinhaImpressao.Traco);
            if (contagemCega)
                l.Add(new("TOTAL CONTADO", Valor(s.TotalContado ?? 0), Negrito: true, Grande: true));
            else
            {
                l.Add(new("TOTAL ESPERADO", Valor(s.TotalEsperado ?? 0), Negrito: true, Grande: true));
                l.Add(new("Total contado", Valor(s.TotalContado ?? 0)));
                l.Add(new("DIFERENÇA", (s.TotalDiferenca > 0 ? "+" : "") + Valor(s.TotalDiferenca ?? 0), Negrito: true));
            }
            if (imprimeProdutos)
                await ProdutosDaSessaoAsync(l, s.Id, ct);
        }
        if (!string.IsNullOrWhiteSpace(s.ObservacoesFecho)) l.Add(new($"Obs.: {s.ObservacoesFecho}"));
        l.Add(new(""));
        l.Add(new("Operador: ____________________"));
        l.Add(new(""));
        l.Add(new("Responsável: _________________"));
        Rodape(l);
        return new DocumentoImpressao($"Caixa {s.Id}", l, cabecalho);
    }

    /// <summary>
    /// Fecho do dia: resumo de todos os caixas do dia de operação (o gravado no fecho; com o dia aberto, ponto de situação),
    /// vendas por meio de pagamento, outros movimentos, controlo e produtos vendidos por categoria. Valores das tabelas em moeda.
    /// </summary>
    private async Task<DocumentoImpressao> FechoDiaAsync(int diaId, CancellationToken ct)
    {
        var r = await dias.ResumoAsync(diaId, ct);
        var fechado = r.Estado == EstadoDia.Fechado;
        var l = await CabecalhoAsync(fechado ? "Fecho do dia" : "Dia — ponto de situação",
            $"Dia de operação {r.Data:dd/MM/yyyy}" + (r.Numero is null ? "" : $" · N.º {r.Numero}"));
        l.Add(new("Dia aberto por", r.AbertoPor));
        l.Add(new("Abertura", DataHora(r.AbertoEmUtc)));
        if (fechado)
        {
            l.Add(new("Dia fechado por", r.FechadoPor ?? "—"));
            l.Add(new("Fecho", r.FechadoEmUtc is DateTime f ? DataHora(f) : "—"));
        }
        else
        {
            l.Add(new("DIA AINDA ABERTO", Negrito: true));
            l.Add(new("Os valores podem mudar até ao fecho do dia."));
        }
        l.Add(new("Caixas no dia", r.Caixas.Count.ToString(CultureInfo.InvariantCulture)));

        l.Add(LinhaImpressao.Traco);
        l.Add(new("1. RESUMO DO DIA", Negrito: true));
        l.Add(new($"Pedidos pagos ({r.Pedidos})", ""));
        l.Add(new("Vendas brutas", Valor(r.VendasBrutas)));
        if (r.Descontos != 0) l.Add(new("Descontos", "-" + Valor(r.Descontos)));
        if (r.TaxasEntrega != 0) l.Add(new("Taxas de entrega", Valor(r.TaxasEntrega)));
        l.Add(new("VENDAS LÍQUIDAS", Valor(r.VendasLiquidas), Negrito: true, Grande: true));
        l.Add(new("IVA incluído", Valor(r.Impostos)));
        l.Add(new("Ticket médio", Valor(r.TicketMedio)));
        l.Add(new("Fundos iniciais", Valor(r.Caixas.Sum(c => c.Fundo))));
        l.Add(new("Total recebido", Valor(r.TotalRecebido)));
        l.Add(new("Total entrado", Valor(r.TotalEntrado)));
        l.Add(new("Total saído", Valor(r.TotalSaido)));
        if (r.Diferenca is decimal dif)
            l.Add(new("DIFERENÇA DE CAIXA", (dif > 0 ? "+" : "") + Valor(dif), Negrito: true));

        l.Add(LinhaImpressao.Traco);
        l.Add(new("2. RESUMO POR CAIXA", Negrito: true));
        if (r.Caixas.Count == 0) l.Add(new("Nenhum caixa aberto neste dia."));
        else
        {
            l.Add(LinhaImpressao.Tabela(["Caixa", "Recebido", "Esperado", "Contado", "Diferença"], cabecalho: true));
            foreach (var c in r.Caixas)
                l.Add(LinhaImpressao.Tabela([
                    $"{c.Terminal} · {c.Operador} · {Hora(c.AbertaEmUtc, false)}–{(c.FechadaEmUtc is DateTime fc ? Hora(fc, false) : "aberto")} ({c.Pedidos} ped.)",
                    Num(c.Recebido), c.Esperado is decimal e ? Num(e) : "—", c.Contado is decimal ct2 ? Num(ct2) : "—",
                    c.Diferenca is decimal d ? (d > 0 ? "+" : "") + Num(d) : "—"]));
            l.Add(LinhaImpressao.Tabela(["Total do dia", Num(r.Caixas.Sum(c => c.Recebido)),
                r.Caixas.Any(c => c.Aberta) ? "—" : Num(r.Caixas.Sum(c => c.Esperado ?? 0)),
                r.Caixas.Any(c => c.Aberta) ? "—" : Num(r.Caixas.Sum(c => c.Contado ?? 0)),
                r.Diferenca is decimal td ? (td > 0 ? "+" : "") + Num(td) : "—"], negrito: true));
        }

        l.Add(LinhaImpressao.Traco);
        l.Add(new("3. VENDAS POR MEIO DE PAGAMENTO", Negrito: true));
        var totalMetodos = r.Metodos.Sum(m => m.Total);
        if (r.Metodos.Count == 0) l.Add(new("Sem pagamentos."));
        else if (r.Caixas.Count is > 1 and <= 3)
        {
            l.Add(LinhaImpressao.Tabela(["Meio de pagamento", .. r.Caixas.Select(c => c.Terminal), "Total"], cabecalho: true));
            foreach (var m in r.Metodos)
                l.Add(LinhaImpressao.Tabela([m.Metodo, .. m.PorCaixa.Select(Num), Num(m.Total)]));
            l.Add(LinhaImpressao.Tabela(["Total", .. r.Caixas.Select(c => Num(c.Recebido)), Num(totalMetodos)], negrito: true));
        }
        else
        {
            l.Add(LinhaImpressao.Tabela(["Meio de pagamento", "Total", "%"], cabecalho: true));
            foreach (var m in r.Metodos)
                l.Add(LinhaImpressao.Tabela([m.Metodo, Num(m.Total), Pct(m.Total, totalMetodos)]));
            l.Add(LinhaImpressao.Tabela(["Total", Num(totalMetodos), "100,0"], negrito: true));
        }

        l.Add(LinhaImpressao.Traco);
        l.Add(new("4. OUTROS MOVIMENTOS DE CAIXA", Negrito: true));
        if (r.Movimentos.Count == 0) l.Add(new("Sem movimentos."));
        foreach (var m in r.Movimentos)
            l.Add(new($"{NomeMovimento(m.Tipo)} ({m.Quantidade})", (m.Valor > 0 ? "+" : "") + Valor(m.Valor)));
        if (r.Movimentos.Count > 0)
            l.Add(new("Saldo dos movimentos", Valor(r.Movimentos.Sum(m => m.Valor)), Negrito: true));

        l.Add(LinhaImpressao.Traco);
        l.Add(new("5. CONTROLO", Negrito: true));
        var k = r.Controlo;
        l.Add(new($"Descontos concedidos ({k.Descontos})", Valor(k.ValorDescontos)));
        l.Add(new($"Itens anulados ({k.ItensAnulados})", Valor(k.ValorItensAnulados)));
        l.Add(new($"Pedidos estornados ({k.Estornos})", Valor(k.ValorEstornos)));
        l.Add(new($"Pagamentos anulados ({k.PagamentosAnulados})", Valor(k.ValorPagamentosAnulados)));
        l.Add(new("Pedidos em aberto", k.PedidosEmAberto.ToString(CultureInfo.InvariantCulture), Negrito: k.PedidosEmAberto > 0));

        l.Add(LinhaImpressao.Traco);
        l.Add(new("6. PRODUTOS VENDIDOS POR CATEGORIA", Negrito: true));
        var totalProdutos = r.Categorias.Sum(c => c.Total);
        if (r.Categorias.Count == 0) l.Add(new("Sem vendas fechadas neste dia."));
        else
        {
            l.Add(LinhaImpressao.Tabela(["Produto", "Qtd.", "Total", "%"], cabecalho: true));
            foreach (var c in r.Categorias)
            {
                l.Add(LinhaImpressao.Tabela([c.Categoria, ComposicaoImpressao.Quantidade(c.Quantidade), Num(c.Total), Pct(c.Total, totalProdutos)], negrito: true));
                foreach (var p in c.Produtos)
                    l.Add(LinhaImpressao.Tabela(["  " + p.Produto, ComposicaoImpressao.Quantidade(p.Quantidade), Num(p.Total), Pct(p.Total, totalProdutos)]));
            }
            l.Add(LinhaImpressao.Tabela(["Total (antes de descontos)", ComposicaoImpressao.Quantidade(r.Categorias.Sum(c => c.Quantidade)), Num(totalProdutos), "100,0"], negrito: true));
        }
        l.Add(new($"Valores em {moeda}, com IVA incluído."));
        if (!string.IsNullOrWhiteSpace(r.Observacoes)) l.Add(new($"Obs.: {r.Observacoes}"));
        l.Add(new(""));
        l.Add(new("Gerente: ______________________"));
        l.Add(new(""));
        l.Add(new("Conferido por: ________________"));
        Rodape(l);
        return new DocumentoImpressao($"Fecho do dia {r.Data:dd/MM/yyyy}", l, cabecalho);
    }

    /// <summary>
    /// Folha de contagem de um inventário, para contar no armazém em papel: artigos por ordem alfabética, unidade e espaço para
    /// a quantidade contada. Por omissão é cega (sem o saldo do sistema, para não influenciar quem conta); <paramref name="comSaldo"/>
    /// acrescenta o saldo do sistema. A contagem escreve-se depois no ecrã do inventário.
    /// </summary>
    private async Task<DocumentoImpressao> FolhaContagemAsync(int inventarioId, bool comSaldo)
    {
        var inv = await stock.ObterInventarioAsync(inventarioId);
        var l = await CabecalhoAsync("Folha de contagem", $"Inventário n.º {inv.Id} · {inv.Armazem}");
        l.Add(new("Criado em", DataHora(inv.CriadoEmUtc)));
        if (inv.Estado != EstadoInventario.Aberto)
            l.Add(new($"INVENTÁRIO {inv.Estado.ToString().ToUpperInvariant()}", Negrito: true));
        l.Add(new(comSaldo ? "Com o saldo do sistema." : "Contagem cega: conte e escreva a quantidade de cada artigo."));
        l.Add(new($"Artigos: {inv.Linhas.Count}"));
        l.Add(LinhaImpressao.Traco);
        const string espaco = "__________";
        l.Add(LinhaImpressao.Tabela(comSaldo ? ["Artigo", "Un.", "Sistema", "Contado"] : ["Artigo", "Un.", "Contado"], cabecalho: true));
        foreach (var x in inv.Linhas.OrderBy(x => x.Artigo, StringComparer.CurrentCultureIgnoreCase).ThenBy(x => x.Codigo))
        {
            var artigo = $"{x.Artigo} ({x.Codigo})";
            l.Add(LinhaImpressao.Tabela(comSaldo
                ? [artigo, x.Unidade, ComposicaoImpressao.Quantidade(x.QuantidadeSistema), espaco]
                : [artigo, x.Unidade, espaco]));
        }
        if (inv.Linhas.Count == 0) l.Add(new("Sem artigos activos neste armazém."));
        l.Add(LinhaImpressao.Traco);
        l.Add(new("Data da contagem: ____/____/________"));
        l.Add(new(""));
        l.Add(new("Contado por: __________________"));
        l.Add(new(""));
        l.Add(new("Conferido por: ________________"));
        Rodape(l);
        return new DocumentoImpressao($"Contagem inventário {inv.Id}", l, cabecalho);
    }

    private static string NomeMovimento(TipoMovimentoCaixa tipo) => tipo switch
    {
        TipoMovimentoCaixa.Entrada => "Entradas (recargas, recebimentos, reforços)",
        TipoMovimentoCaixa.Saida => "Saídas",
        TipoMovimentoCaixa.Despesa => "Despesas pagas pelo caixa",
        _ => "Reembolsos (estornos)",
    };

    private static string Num(decimal v) => ComposicaoImpressao.Valor(v, "").Trim();

    private static string Pct(decimal parte, decimal total) =>
        total == 0 ? "—" : decimal.Round(parte * 100m / total, 1, MidpointRounding.AwayFromZero).ToString("0.0", CultureInfo.GetCultureInfo("pt-PT"));

    /// <summary>Produtos dos pedidos fechados nesta sessão (pagos, não estornados), por categoria, com quantidade e valor.</summary>
    private async Task ProdutosDaSessaoAsync(List<LinhaImpressao> l, int sessaoId, CancellationToken ct)
    {
        var itens = await db.PedidoItens.AsNoTracking()
            .Where(i => !i.Cancelado && i.Pedido.SessaoCaixaId == sessaoId && i.Pedido.Estado == EstadoPedido.Pago)
            .GroupBy(i => new { Categoria = i.Produto.Categoria.Nome, i.NomeProduto })
            .Select(g => new { g.Key.Categoria, g.Key.NomeProduto, Quantidade = g.Sum(i => i.Quantidade), Total = g.Sum(i => i.Total) })
            .ToListAsync(ct);
        var pedidos = await db.Pedidos.AsNoTracking().Where(p => p.SessaoCaixaId == sessaoId && p.Estado == EstadoPedido.Pago)
            .GroupBy(_ => 1).Select(g => new { Desconto = g.Sum(p => p.Desconto), Entrega = g.Sum(p => p.TaxaEntrega), Total = g.Sum(p => p.Total) })
            .FirstOrDefaultAsync(ct);
        l.Add(LinhaImpressao.Traco);
        l.Add(new("PRODUTOS VENDIDOS POR CATEGORIA", Negrito: true));
        if (itens.Count == 0)
        {
            l.Add(new("Sem vendas fechadas nesta sessão."));
            return;
        }
        foreach (var categoria in itens.GroupBy(i => i.Categoria).OrderByDescending(g => g.Sum(i => i.Total)))
        {
            l.Add(new(categoria.Key, Valor(categoria.Sum(i => i.Total)), Negrito: true));
            foreach (var p in categoria.OrderByDescending(i => i.Total))
                l.Add(new($"  {ComposicaoImpressao.Quantidade(p.Quantidade)} x {p.NomeProduto}", Valor(p.Total)));
        }
        l.Add(new("Total dos produtos", Valor(itens.Sum(i => i.Total)), Negrito: true));
        if (pedidos is { Desconto: > 0 }) l.Add(new("Descontos", "-" + Valor(pedidos.Desconto)));
        if (pedidos is { Entrega: > 0 }) l.Add(new("Taxas de entrega", Valor(pedidos.Entrega)));
        if (pedidos is not null) l.Add(new("Total das vendas", Valor(pedidos.Total), Negrito: true));
    }

    /// <summary>Cartões Evento: resumo do período (ou do caixa), dinheiro recebido por método e saldos actuais por cartão.</summary>
    private async Task<DocumentoImpressao> CartoesEventoAsync(DateOnly desde, DateOnly ate, int? sessaoId, CancellationToken ct)
    {
        var r = await cartoes.GerarAsync(desde, ate, ct, sessaoId);
        var origem = sessaoId is int s0
            ? await db.SessoesCaixa.AsNoTracking().Where(x => x.Id == s0).Select(x => $"Caixa n.º {x.Id} · {x.Terminal.Nome}").FirstOrDefaultAsync(ct)
              ?? throw new NaoEncontradoException("Sessão de caixa não encontrada.")
            : Periodo(desde, ate);
        var l = await CabecalhoAsync("Cartões Evento", origem);
        var s = r.Resumo;
        l.Add(new($"Carregamentos ({s.Carregamentos})", Valor(s.Carregado)));
        l.Add(new($"Recargas ({s.Recargas})", Valor(s.Recarregado)));
        l.Add(new($"Pagamentos com cartão ({s.Pagamentos})", Valor(s.Pago)));
        l.Add(new($"Devolvido aos cartões ({s.Devolucoes})", Valor(s.Devolvido)));
        if (s.Descargas > 0) l.Add(new($"Saldo descarregado ({s.Descargas})", Valor(s.Descarregado)));
        l.Add(new($"Cartões novos", s.CartoesNovos.ToString(CultureInfo.InvariantCulture)));
        l.Add(LinhaImpressao.Traco);
        l.Add(new("SALDO EM CIRCULAÇÃO", Valor(s.SaldoEmCirculacao), Negrito: true, Grande: true));
        l.Add(new($"{s.CartoesComSaldo} cartão(ões) com saldo · {s.CartoesActivos} activos"));
        if (r.PorMetodo.Count > 0)
        {
            l.Add(LinhaImpressao.Traco);
            l.Add(new("Dinheiro recebido por método", Negrito: true));
            foreach (var m in r.PorMetodo)
                l.Add(new($"  {m.Metodo} ({m.Quantidade})", Valor(m.Valor)));
        }
        if (r.PorOperador.Count > 0)
        {
            l.Add(LinhaImpressao.Traco);
            l.Add(LinhaImpressao.Tabela(["Operador", "Carregado", "Pago"], cabecalho: true));
            foreach (var o in r.PorOperador)
                l.Add(LinhaImpressao.Tabela([o.Operador, Valor(o.Carregado), Valor(o.Pago)]));
        }
        if (r.Saldos.Count > 0)
        {
            l.Add(LinhaImpressao.Traco);
            l.Add(LinhaImpressao.Tabela(["Cartão", "Titular", "Saldo"], cabecalho: true));
            foreach (var c in r.Saldos)
                l.Add(LinhaImpressao.Tabela([c.Numero + (c.Activo ? "" : " (bloqueado)"), c.Titular ?? "—", Valor(c.Saldo)]));
        }
        Rodape(l);
        return new DocumentoImpressao("Cartões Evento " + origem, l, cabecalho);
    }

    // ---------------- Contas correntes dos clientes ----------------

    /// <summary>Facturas a crédito do período (situação de cada uma) e dívida de hoje por cliente, por antiguidade.</summary>
    private async Task<DocumentoImpressao> FacturasCreditoAsync(DateOnly desde, DateOnly ate, int? clienteId, CancellationToken ct)
    {
        var r = await contas.FacturasCreditoAsync(desde, ate, clienteId, ct: ct);
        var l = await CabecalhoAsync("Facturas a crédito", Periodo(desde, ate));
        if (clienteId is int id && await db.Clientes.AsNoTracking().Where(c => c.Id == id).Select(c => new { c.Nome, c.Nuit, c.Telefone }).FirstOrDefaultAsync(ct) is { } cliente)
            Cliente(l, cliente.Nome, cliente.Nuit, cliente.Telefone);
        var s = r.Resumo;
        l.Add(new($"Facturas emitidas ({s.Facturas})", Valor(s.Facturado), Negrito: true));
        l.Add(new("Pago (recibos)", Valor(s.Pago)));
        l.Add(new("Creditado (notas de crédito)", Valor(s.Creditado)));
        l.Add(new("EM DÍVIDA", Valor(s.EmDivida), Negrito: true, Grande: true));
        if (s.Vencido > 0) l.Add(new("dos quais vencido", Valor(s.Vencido)));

        l.Add(LinhaImpressao.Traco);
        l.Add(new("Facturas do período", Negrito: true));
        if (r.Facturas.Count == 0) l.Add(new("Sem facturas a crédito no período."));
        foreach (var f in r.Facturas)
        {
            l.Add(new($"{Dia(f.EmitidoEmUtc)} {f.Numero} · {f.ClienteNome}", Valor(f.Total)));
            var detalhe = new List<string> { NomeSituacao(f.Situacao, f.DiasAtraso) };
            if (f.Vencimento is DateOnly v) detalhe.Add($"venc. {v:dd/MM/yyyy}");
            if (f.ValorPago > 0) detalhe.Add($"pago {Valor(f.ValorPago)}");
            if (f.ValorCreditado > 0) detalhe.Add($"creditado {Valor(f.ValorCreditado)}");
            l.Add(new("   " + string.Join(" · ", detalhe), f.EmDivida > 0 ? Valor(f.EmDivida) : null));
        }
        if (r.Cortado) l.Add(new($"(Lista limitada às primeiras {Facturacao.ServicoContaCorrente.MaximoLinhas} facturas.)"));

        l.Add(LinhaImpressao.Traco);
        l.Add(new($"Dívida de clientes hoje ({r.Hoje:dd/MM/yyyy})", Negrito: true));
        if (r.Dividas.Count == 0) l.Add(new("Nenhum cliente em dívida."));
        foreach (var d in r.Dividas)
        {
            l.Add(new($"{d.ClienteNome} ({d.Facturas})", Valor(d.EmDivida), Negrito: true));
            var partes = new[] { ("não vencido", d.NaoVencido), ("1-30 d", d.Ate30), ("31-60 d", d.De31a60), ("61-90 d", d.De61a90), ("+90 d", d.Mais90) }
                .Where(p => p.Item2 > 0).Select(p => $"{p.Item1} {Valor(p.Item2)}");
            l.Add(new("   " + string.Join(" · ", partes)));
        }
        if (r.Dividas.Count > 0)
        {
            l.Add(new("TOTAL EM DÍVIDA", Valor(r.TotalEmDivida), Negrito: true));
            l.Add(new("Total vencido", Valor(r.TotalVencido)));
        }
        Rodape(l);
        return new DocumentoImpressao("Facturas a crédito " + Periodo(desde, ate), l, cabecalho);
    }

    /// <summary>Histórico de documentos do cliente (todos os tipos), com os totais dos documentos em vigor.</summary>
    private async Task<DocumentoImpressao> HistoricoClienteAsync(int clienteId, DateOnly desde, DateOnly ate, CancellationToken ct)
    {
        var r = await contas.HistoricoAsync(clienteId, desde, ate, ct: ct);
        var l = await CabecalhoAsync("Histórico de facturas do cliente", Periodo(desde, ate));
        Cliente(l, r.ClienteNome, r.ClienteNuit, null);
        if (r.Documentos.Count == 0) l.Add(new("Sem documentos no período."));
        foreach (var d in r.Documentos)
        {
            var anulado = d.Estado == Domain.Facturacao.EstadoDocumento.Anulado;
            l.Add(new($"{Dia(d.EmitidoEmUtc)} {d.Numero} · {Domain.Facturacao.Documento.Nome(d.Tipo)}{(anulado ? " (ANULADO)" : "")}", Valor(d.Total)));
            var detalhe = new List<string>();
            if (d.Origem is not null) detalhe.Add(d.Origem);
            if (d.MetodoPagamento is not null) detalhe.Add(d.MetodoPagamento);
            if (d.Vencimento is DateOnly v) detalhe.Add($"venc. {v:dd/MM/yyyy}");
            if (d.EmDivida > 0) detalhe.Add($"em dívida {Valor(d.EmDivida)}");
            if (detalhe.Count > 0) l.Add(new("   " + string.Join(" · ", detalhe)));
        }
        if (r.Cortado) l.Add(new($"(Lista limitada aos primeiros {Facturacao.ServicoContaCorrente.MaximoLinhas} documentos.)"));
        var t = r.Totais;
        l.Add(LinhaImpressao.Traco);
        l.Add(new($"Totais (documentos em vigor: {t.Documentos})", Negrito: true));
        l.Add(new("Vendas a dinheiro", Valor(t.VendasDinheiro)));
        l.Add(new("Facturas a crédito", Valor(t.Facturado)));
        l.Add(new("Recebido (recibos)", Valor(t.Recebido)));
        l.Add(new("Notas de crédito", Valor(t.Creditado)));
        l.Add(new("EM DÍVIDA (facturas do período)", Valor(t.EmDivida), Negrito: true));
        Rodape(l);
        return new DocumentoImpressao($"Histórico {r.ClienteNome} {Periodo(desde, ate)}", l, cabecalho);
    }

    /// <summary>Extrato da conta corrente: saldo anterior, movimentos com saldo acumulado, saldo final e facturas em aberto.</summary>
    private async Task<DocumentoImpressao> ExtratoClienteAsync(int clienteId, DateOnly desde, DateOnly ate, CancellationToken ct)
    {
        var r = await contas.ExtratoAsync(clienteId, desde, ate, ct);
        var l = await CabecalhoAsync("Extrato de conta corrente", Periodo(desde, ate));
        Cliente(l, r.ClienteNome, r.ClienteNuit, r.ClienteTelefone);
        l.Add(new($"Saldo anterior a {desde:dd/MM/yyyy}", Valor(r.SaldoAnterior), Negrito: true));
        l.Add(LinhaImpressao.Traco);
        if (r.Movimentos.Count == 0) l.Add(new("Sem movimentos no período."));
        foreach (var m in r.Movimentos)
        {
            l.Add(new($"{Dia(m.DataUtc)} {m.Numero}", (m.Debito > 0 ? "+" + Valor(m.Debito) : "-" + Valor(m.Credito))));
            l.Add(new("   " + m.Descricao, "saldo " + Valor(m.Saldo)));
        }
        l.Add(LinhaImpressao.Traco);
        l.Add(new("Débitos (facturas)", Valor(r.Debitos)));
        l.Add(new("Créditos (recibos e notas de crédito)", Valor(r.Creditos)));
        l.Add(new($"SALDO EM {ate:dd/MM/yyyy}", Valor(r.SaldoFinal), Negrito: true, Grande: true));
        l.Add(LinhaImpressao.Traco);
        l.Add(new("Situação hoje", Negrito: true));
        l.Add(new($"Facturas em aberto ({r.FacturasEmAberto.Count})", Valor(r.EmDividaHoje), Negrito: true));
        if (r.VencidoHoje > 0) l.Add(new("dos quais vencido", Valor(r.VencidoHoje)));
        foreach (var f in r.FacturasEmAberto)
            l.Add(new($"  {f.Numero}{(f.Vencimento is DateOnly v ? $" venc. {v:dd/MM/yyyy}" : "")}{(f.Vencida ? " VENCIDA" : "")}", Valor(f.EmDivida)));
        Rodape(l);
        return new DocumentoImpressao($"Extrato {r.ClienteNome} {Periodo(desde, ate)}", l, cabecalho);
    }

    private static void Cliente(List<LinhaImpressao> l, string nome, string? nuit, string? telefone)
    {
        l.Add(new($"Cliente: {nome}", Negrito: true));
        var dados = new[] { nuit is null ? null : $"NUIT {nuit}", telefone is null ? null : $"Tel. {telefone}" }.Where(x => x is not null).ToList();
        if (dados.Count > 0) l.Add(new(string.Join(" · ", dados)));
        l.Add(LinhaImpressao.Traco);
    }

    private static string NomeSituacao(Facturacao.SituacaoFactura s, int diasAtraso) => s switch
    {
        Facturacao.SituacaoFactura.Paga => "Paga",
        Facturacao.SituacaoFactura.Creditada => "Creditada",
        Facturacao.SituacaoFactura.Parcial => "Parcial",
        Facturacao.SituacaoFactura.Vencida => $"Vencida há {diasAtraso} d",
        _ => "Em dívida",
    };

    private string Dia(DateTime utc) => TimeZoneInfo.ConvertTimeFromUtc(utc, fuso).ToString("dd/MM/yy", CultureInfo.InvariantCulture);

    /// <summary>Descargas de saldo: cada cartão com o saldo descarregado e o acumulado; totais devolvido e só retirado.</summary>
    private async Task<DocumentoImpressao> DescargasCartoesAsync(DateOnly desde, DateOnly ate, int? sessaoId, CancellationToken ct)
    {
        var r = await cartoes.DescargasAsync(desde, ate, sessaoId, ct);
        var origem = sessaoId is int s
            ? await db.SessoesCaixa.AsNoTracking().Where(x => x.Id == s).Select(x => $"Caixa n.º {x.Id} · {x.Terminal.Nome}").FirstOrDefaultAsync(ct)
              ?? throw new NaoEncontradoException("Sessão de caixa não encontrada.")
            : Periodo(desde, ate);
        var l = await CabecalhoAsync("Descargas de cartões", origem);
        if (r.Linhas.Count == 0) l.Add(new("Sem descargas."));
        else l.Add(LinhaImpressao.Tabela(["Cartão", "Saldo", "Acumulado"], cabecalho: true));
        foreach (var d in r.Linhas)
        {
            l.Add(LinhaImpressao.Tabela([d.Numero, Valor(d.Saldo), Valor(d.Acumulado)]));
            var detalhe = new[] { Dia(d.DataUtc), d.Titular, d.DevolveuDinheiro ? $"devolvido por {d.Metodo}" : "só retirado", d.Operador, d.Motivo }.Where(x => x is not null);
            l.Add(new("   " + string.Join(" · ", detalhe)));
        }
        if (r.Cortado) l.Add(new($"(Lista limitada aos primeiros {Cartoes.ServicoRelatoriosCartoes.MaximoMovimentos} cartões.)"));
        l.Add(LinhaImpressao.Traco);
        l.Add(new("Devolvido em dinheiro", Valor(r.Devolvido)));
        l.Add(new("Só retirado do saldo", Valor(r.Retirado)));
        l.Add(new($"TOTAL DESCARREGADO ({r.Cartoes})", Valor(r.Total), Negrito: true, Grande: true));
        Rodape(l);
        return new DocumentoImpressao("Descargas de cartões " + origem, l, cabecalho);
    }

    // ---------------- Auxiliares ----------------

    private async Task<List<LinhaImpressao>> CabecalhoAsync(string titulo, string periodo)
    {
        var nome = await config.TextoAsync(ChavesConfiguracao.NomeEstabelecimento, "LAMGest Restaurant");
        var nuit = await config.TextoAsync(ChavesConfiguracao.Nuit);
        cabecalho = ComposicaoImpressao.Estabelecimento(string.IsNullOrWhiteSpace(nome) ? "LAMGest Restaurant" : nome, null, null, string.IsNullOrWhiteSpace(nuit) ? null : nuit);
        var l = new List<LinhaImpressao>();
        l.Add(new(titulo.ToUpperInvariant(), Alinhamento: Alinhamento.Centro, Negrito: true));
        l.Add(new(periodo, Alinhamento: Alinhamento.Centro));
        l.Add(LinhaImpressao.Traco);
        return l;
    }

    private void Rodape(List<LinhaImpressao> l)
    {
        l.Add(LinhaImpressao.Traco);
        l.Add(new($"Emitido em {DataHora(DateTime.UtcNow)}{(actual.Nome is { } n ? $" por {n}" : "")}", Alinhamento: Alinhamento.Centro));
        l.Add(new("Documento interno", Alinhamento: Alinhamento.Centro));
    }

    private void Seccao(List<LinhaImpressao> l, string titulo, IEnumerable<(string Rotulo, decimal Valor)> linhas)
    {
        l.Add(LinhaImpressao.Traco);
        l.Add(new(titulo, Negrito: true));
        var lista = linhas.ToList();
        if (lista.Count == 0) l.Add(new("—"));
        l.AddRange(lista.Select(x => new LinhaImpressao(x.Rotulo, Valor(x.Valor))));
    }

    private string Valor(decimal v) => ComposicaoImpressao.Valor(v, moeda);

    private string DataHora(DateTime utc) => TimeZoneInfo.ConvertTimeFromUtc(utc, fuso).ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);

    private string Hora(DateTime utc, bool comDia) =>
        TimeZoneInfo.ConvertTimeFromUtc(utc, fuso).ToString(comDia ? "dd/MM HH:mm" : "HH:mm", CultureInfo.InvariantCulture);

    private static string Periodo(DateOnly desde, DateOnly ate) =>
        desde == ate ? $"{desde:dd/MM/yyyy}" : $"{desde:dd/MM/yyyy} a {ate:dd/MM/yyyy}";
}
