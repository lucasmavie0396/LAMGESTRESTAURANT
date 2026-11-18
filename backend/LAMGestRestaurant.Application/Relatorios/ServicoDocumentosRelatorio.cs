using System.Globalization;
using LAMGestRestaurant.Application.Caixa;
using LAMGestRestaurant.Application.Comum;
using LAMGestRestaurant.Application.Impressao;
using LAMGestRestaurant.Application.Seguranca;
using LAMGestRestaurant.Domain.Comum;
using LAMGestRestaurant.Domain.Sistema;
using Microsoft.EntityFrameworkCore;

namespace LAMGestRestaurant.Application.Relatorios;

/// <summary>Ordem dos artigos na folha de contagem.</summary>
public enum OrdemFolhaContagem { Alfabetica, Codigo, Categoria, Tipo, Localizacao }

/// <summary>Opções da folha de contagem: ordem, saldo do sistema, só algumas categorias, uma página por grupo (A4), sem os de saldo zero.</summary>
public sealed record OpcoesFolhaContagem(OrdemFolhaContagem Ordem = OrdemFolhaContagem.Alfabetica, bool ComSaldo = false, IReadOnlyList<int>? Categorias = null,
    bool PaginaPorGrupo = false, bool EsconderZero = false);

public enum TipoDocumentoRelatorio
{
    Pagamentos, Vendas, Produtos, Despesas, Financeiro, Stock, FechoCaixa, ListaVendas, Clientes, Impostos, Anulacoes, CartoesEvento,
    FacturasCredito, HistoricoCliente, ExtratoCliente, DescargasCartoes, FechoDia, FolhaContagem,
    Acompanhamentos, Existencias, Transferencias,
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
            or TipoDocumentoRelatorio.Clientes or TipoDocumentoRelatorio.Impostos or TipoDocumentoRelatorio.Anulacoes or TipoDocumentoRelatorio.Acompanhamentos => Permissoes.RelatoriosVendas,
        TipoDocumentoRelatorio.Despesas or TipoDocumentoRelatorio.Financeiro or TipoDocumentoRelatorio.CartoesEvento or TipoDocumentoRelatorio.DescargasCartoes => Permissoes.RelatoriosFinanceiros,
        TipoDocumentoRelatorio.Stock or TipoDocumentoRelatorio.Existencias or TipoDocumentoRelatorio.Transferencias => Permissoes.RelatoriosStock,
        TipoDocumentoRelatorio.FacturasCredito or TipoDocumentoRelatorio.HistoricoCliente or TipoDocumentoRelatorio.ExtratoCliente => Permissoes.FacturacaoVer,
        TipoDocumentoRelatorio.FechoDia => Permissoes.CaixaDia,
        TipoDocumentoRelatorio.FolhaContagem => Permissoes.StockVer,
        _ => Permissoes.CaixaOperar,
    };

    private static bool DeCliente(TipoDocumentoRelatorio tipo) => tipo is TipoDocumentoRelatorio.HistoricoCliente or TipoDocumentoRelatorio.ExtratoCliente;

    public async Task<DocumentoImpressao> GerarAsync(TipoDocumentoRelatorio tipo, DateOnly desde, DateOnly ate, int? sessaoId, CancellationToken ct = default,
        int? clienteId = null, int? diaId = null, int? inventarioId = null, OpcoesFolhaContagem? folha = null, int? armazemId = null)
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
        if (tipo is not (TipoDocumentoRelatorio.FechoCaixa or TipoDocumentoRelatorio.FechoDia or TipoDocumentoRelatorio.FolhaContagem or TipoDocumentoRelatorio.Existencias) && !DeCliente(tipo))
            ServicoRelatorios.ValidarPeriodo(desde, ate);
        if (DeCliente(tipo) && clienteId is null)
            throw new RegraNegocioException("Escolha o cliente.");
        moeda = await config.TextoAsync(ChavesConfiguracao.SimboloMoeda, "MT");
        fuso = await config.FusoAsync();

        var documento = tipo switch
        {
            TipoDocumentoRelatorio.Pagamentos => await PagamentosAsync(desde, ate, ct),
            TipoDocumentoRelatorio.Vendas => await VendasAsync(desde, ate, ct),
            TipoDocumentoRelatorio.Produtos => await ProdutosAsync(desde, ate, ct),
            TipoDocumentoRelatorio.Despesas => await DespesasAsync(desde, ate, ct),
            TipoDocumentoRelatorio.Financeiro => await FinanceiroAsync(desde, ate, ct),
            TipoDocumentoRelatorio.Stock => await StockAsync(ct),
            TipoDocumentoRelatorio.Acompanhamentos => await AcompanhamentosAsync(desde, ate, ct),
            TipoDocumentoRelatorio.Existencias => await ExistenciasAsync(armazemId, folha?.Categorias is [var categoria] ? categoria : null, ct),
            TipoDocumentoRelatorio.Transferencias => await TransferenciasAsync(desde, ate, armazemId, ct),
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
            TipoDocumentoRelatorio.FolhaContagem => await FolhaContagemAsync(inventarioId ?? throw new RegraNegocioException("Indique o inventário."), folha ?? new OpcoesFolhaContagem()),
            _ => await FechoCaixaAsync(sessaoId ?? throw new RegraNegocioException("Indique a sessão de caixa."), ct),
        };
        // Totais grandes («texto … valor») saem num quadro em A4.
        return documento with
        {
            Linhas = [.. documento.Linhas.Select(x => x.Grande && x.Direita is not null && x.Celulas is null ? x with { Total = true } : x)],
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
        var recebido = validos.Sum(p => p.Valor);
        var totalReembolsos = reembolsos.Sum(r => r.Valor);

        var l = await CabecalhoAsync("Relatório de pagamentos", Periodo(desde, ate));
        l.Add(LinhaImpressao.NovoCartao($"Recebido ({validos.Count})", Valor(recebido)));
        l.Add(LinhaImpressao.NovoCartao($"Anulado ({anulados.Count})", Valor(anulados.Sum(p => p.Valor)), alerta: anulados.Count > 0));
        l.Add(LinhaImpressao.NovoCartao($"Reembolsado ({reembolsos.Count})", Valor(totalReembolsos), alerta: reembolsos.Count > 0));
        l.Add(LinhaImpressao.NovoCartao("Líquido", Valor(recebido - totalReembolsos)));

        Grelha(l, "Pagamentos recebidos", Resumo(validos.Count, "pagamento", recebido),
            [variosDias ? "Data" : "Hora", "Pedido", "Método", "Ref. / troco", "Valor"],
            validos.Select(p => Lin(Hora(p.DataUtc, variosDias), p.Pedido, p.Metodo,
                string.Join(" · ", new[] { p.Referencia is null ? null : $"Ref. {p.Referencia}", p.Troco > 0 ? $"troco {Valor(p.Troco)}" : null }.Where(x => x is not null)),
                Valor(p.Valor))),
            subtotal: ["Total recebido", "", "", "", Valor(recebido)], texto: 4, vazio: "Sem pagamentos no período.");
        Grelha(l, "Totais por método", null, ["Método", "Pagamentos", "Valor", "%"],
            validos.GroupBy(p => (p.Metodo, p.Ordem)).OrderBy(g => g.Key.Ordem)
                .Select(g => Lin(g.Key.Metodo, Inteiro(g.Count()), Valor(g.Sum(p => p.Valor)), Pct(g.Sum(p => p.Valor), recebido))),
            subtotal: ["Total", Inteiro(validos.Count), Valor(recebido), "100,0"]);
        if (anulados.Count > 0)
            Grelha(l, "Pagamentos anulados", Resumo(anulados.Count, "pagamento", anulados.Sum(p => p.Valor)),
                [variosDias ? "Data" : "Hora", "Pedido", "Método", "Motivo", "Valor"],
                anulados.Select(p => Lin(Hora(p.DataUtc, variosDias), p.Pedido, p.Metodo, p.MotivoAnulacao ?? "—", Valor(p.Valor))),
                subtotal: ["Total anulado", "", "", "", Valor(anulados.Sum(p => p.Valor))], texto: 4);
        if (reembolsos.Count > 0)
            Grelha(l, "Reembolsos de estornos", Resumo(reembolsos.Count, "reembolso", totalReembolsos),
                [variosDias ? "Data" : "Hora", "Método", "Motivo", "Valor"],
                reembolsos.Select(r => Lin(Hora(r.DataUtc, variosDias), r.Metodo, r.Motivo ?? "—", "-" + Valor(r.Valor))),
                subtotal: ["Total reembolsado", "", "", "-" + Valor(totalReembolsos)], texto: 3);
        l.Add(new("LÍQUIDO (recebido − reembolsos)", Valor(recebido - totalReembolsos), Negrito: true, Grande: true));
        Rodape(l);
        return new DocumentoImpressao("Pagamentos " + Periodo(desde, ate), l, cabecalho);
    }

    private async Task<DocumentoImpressao> VendasAsync(DateOnly desde, DateOnly ate, CancellationToken ct)
    {
        var r = await relatorios.VendasAsync(desde, ate, ct);
        var l = await CabecalhoAsync("Resumo de vendas", Periodo(desde, ate));
        l.Add(LinhaImpressao.NovoCartao("Total de vendas", Valor(r.TotalVendas)));
        l.Add(LinhaImpressao.NovoCartao("Pedidos pagos", Inteiro(r.NumeroPedidos)));
        l.Add(LinhaImpressao.NovoCartao("Ticket médio", Valor(r.TicketMedio)));
        l.Add(LinhaImpressao.NovoCartao("Margem estimada", Valor(r.MargemEstimada)));

        var resumo = new List<LinhaImpressao> { Lin("Descontos", Valor(r.Descontos)), Lin("Impostos incluídos", Valor(r.Impostos)) };
        if (r.TaxasEntrega > 0) resumo.Add(Lin("Taxas de entrega", Valor(r.TaxasEntrega)));
        Grelha(l, "Resumo", null, ["Descrição", "Valor"], resumo);
        var totalMetodos = r.PorMetodo.Sum(m => m.Valor);
        Grelha(l, "Por método de pagamento", null, ["Método", "Pedidos", "Valor", "%"],
            r.PorMetodo.Select(m => Lin(m.Rotulo, Inteiro(m.Quantidade), Valor(m.Valor), Pct(m.Valor, totalMetodos))),
            subtotal: ["Total", Inteiro(r.PorMetodo.Sum(m => m.Quantidade)), Valor(totalMetodos), "100,0"]);
        var totalTipos = r.PorTipo.Sum(m => m.Valor);
        Grelha(l, "Por tipo de pedido", null, ["Tipo", "Pedidos", "Valor", "%"],
            r.PorTipo.Select(m => Lin(m.Rotulo, Inteiro(m.Quantidade), Valor(m.Valor), Pct(m.Valor, totalTipos))));
        var totalCategorias = r.PorCategoria.Sum(c => c.Total);
        Grelha(l, "Por categoria", null, ["Categoria", "Valor", "%"],
            r.PorCategoria.Select(c => Lin(c.Categoria, Valor(c.Total), Pct(c.Total, totalCategorias))));
        Grelha(l, "Produtos mais vendidos", "os 10 primeiros", ["Produto", "Quantidade", "Valor"],
            r.Produtos.Take(10).Select(p => Lin(p.Produto, QuantidadeVendida(p), Valor(p.Total))));
        if (r.PorGarcom.Count > 0)
            Grelha(l, "Por garçom", null, ["Garçom", "Pedidos", "Valor"],
                r.PorGarcom.Select(m => Lin(m.Rotulo, Inteiro(m.Quantidade), Valor(m.Valor))));
        var c = r.Cancelamentos;
        Grelha(l, "Cancelamentos e estornos", null, ["Tipo", "Quantidade", "Valor"],
        [
            Lin("Pedidos cancelados", Inteiro(c.PedidosCancelados), Valor(c.ValorPedidosCancelados)),
            Lin("Itens anulados após envio", Inteiro(c.ItensCancelados), Valor(c.ValorItensCancelados)),
            Lin("Pedidos estornados", Inteiro(c.PedidosEstornados), Valor(c.ValorEstornado)),
        ]);
        l.Add(new("TOTAL DE VENDAS", Valor(r.TotalVendas), Negrito: true, Grande: true));
        Rodape(l);
        return new DocumentoImpressao("Vendas " + Periodo(desde, ate), l, cabecalho);
    }

    /// <summary>«3 x» para unidades; «1,485 kg» para produtos vendidos a peso ou noutra unidade.</summary>
    private static string QuantidadeVendida(VendaProduto p) =>
        p.Unidade == "un" ? $"{ComposicaoImpressao.Quantidade(p.Quantidade)} x" : $"{ComposicaoImpressao.Quantidade(p.Quantidade)} {p.Unidade}";

    private async Task<DocumentoImpressao> ProdutosAsync(DateOnly desde, DateOnly ate, CancellationToken ct)
    {
        var r = await relatorios.VendasAsync(desde, ate, ct);
        var total = r.Produtos.Sum(p => p.Total);
        var l = await CabecalhoAsync("Produtos vendidos", Periodo(desde, ate));
        l.Add(LinhaImpressao.NovoCartao("Total (antes de descontos)", Valor(total)));
        l.Add(LinhaImpressao.NovoCartao("Produtos diferentes", Inteiro(r.Produtos.Count)));
        l.Add(LinhaImpressao.NovoCartao("Categorias", Inteiro(r.Produtos.Select(p => p.Categoria).Distinct().Count())));
        if (r.Produtos.Count == 0) l.Add(new("Sem vendas no período."));
        foreach (var categoria in r.Produtos.GroupBy(p => p.Categoria).OrderByDescending(g => g.Sum(p => p.Total)))
            Grelha(l, categoria.Key, Resumo(categoria.Count(), "produto", categoria.Sum(p => p.Total)), ["Código", "Produto", "Quantidade", "Valor", "%"],
                categoria.OrderByDescending(p => p.Total).Select(p => Lin(p.Codigo, p.Produto, QuantidadeVendida(p), Valor(p.Total), Pct(p.Total, total))),
                subtotal: [$"Subtotal {categoria.Key}", "", "", Valor(categoria.Sum(p => p.Total)), Pct(categoria.Sum(p => p.Total), total)], texto: 2);
        // Quantidades só se somam dentro da mesma unidade (unidades, kg…).
        if (r.Produtos.Count > 0)
            Grelha(l, "Quantidades vendidas", null, ["Unidade", "Quantidade"],
                r.Produtos.GroupBy(p => p.Unidade).OrderBy(u => u.Key != "un").ThenBy(u => u.Key)
                    .Select(u => Lin(u.Key == "un" ? "Unidades" : u.Key, ComposicaoImpressao.Quantidade(u.Sum(p => p.Quantidade)))));
        l.Add(new("TOTAL (antes de descontos)", Valor(total), Negrito: true, Grande: true));
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
        var total = confirmadas.Sum(d => d.Valor);

        var l = await CabecalhoAsync("Relatório de despesas", Periodo(desde, ate));
        l.Add(LinhaImpressao.NovoCartao($"Despesas ({confirmadas.Count})", Valor(total)));
        l.Add(LinhaImpressao.NovoCartao("Categorias", Inteiro(confirmadas.Select(d => d.Categoria).Distinct().Count())));
        l.Add(LinhaImpressao.NovoCartao($"Anuladas ({anuladas.Count})", Valor(anuladas.Sum(d => d.Valor)), alerta: anuladas.Count > 0));
        Grelha(l, "Despesas confirmadas", Resumo(confirmadas.Count, "despesa", total), ["Data", "Categoria", "Descrição", "Pagamento", "Valor"],
            confirmadas.Select(d => Lin(Hora(d.DataUtc, true), d.Categoria, d.Descricao,
                string.Join(" · ", new[] { d.Metodo, d.Referencia is null ? null : $"Ref. {d.Referencia}", d.Caixa is int s ? $"caixa n.º {s}" : null }.Where(x => x is not null)),
                Valor(d.Valor))),
            subtotal: ["Total", "", "", "", Valor(total)], texto: 4, vazio: "Sem despesas no período.");
        Grelha(l, "Por categoria", null, ["Categoria", "Despesas", "Valor", "%"],
            confirmadas.GroupBy(d => d.Categoria).OrderByDescending(g => g.Sum(d => d.Valor))
                .Select(g => Lin(g.Key, Inteiro(g.Count()), Valor(g.Sum(d => d.Valor)), Pct(g.Sum(d => d.Valor), total))));
        l.Add(new($"TOTAL ({confirmadas.Count})", Valor(total), Negrito: true, Grande: true));
        if (anuladas.Count > 0)
            l.Add(new($"Anuladas (não contam): {anuladas.Count}", Valor(anuladas.Sum(d => d.Valor))));
        Rodape(l);
        return new DocumentoImpressao("Despesas " + Periodo(desde, ate), l, cabecalho);
    }

    private async Task<DocumentoImpressao> FinanceiroAsync(DateOnly desde, DateOnly ate, CancellationToken ct)
    {
        var r = await gestao.FinanceiroAsync(desde, ate, ct);
        var l = await CabecalhoAsync("Relatório financeiro", Periodo(desde, ate));
        l.Add(LinhaImpressao.NovoCartao("Vendas (com impostos)", Valor(r.TotalVendas)));
        l.Add(LinhaImpressao.NovoCartao("Despesas", Valor(r.Despesas)));
        l.Add(LinhaImpressao.NovoCartao("Resultado estimado", Valor(r.ResultadoEstimado), alerta: r.ResultadoEstimado < 0));
        Grelha(l, "Resultado", null, ["Descrição", "Valor"],
        [
            Lin("Vendas (com impostos)", Valor(r.TotalVendas)),
            Lin("Impostos incluídos", "-" + Valor(r.Impostos)),
            LinhaImpressao.Tabela(["Vendas sem impostos", Valor(r.VendasSemImpostos)], negrito: true),
            Lin("Custo estimado das vendas", "-" + Valor(r.CustoEstimadoVendas)),
            Lin("Despesas", "-" + Valor(r.Despesas)),
        ], subtotal: ["Resultado estimado", Valor(r.ResultadoEstimado)]);
        l.Add(new($"Fora do resultado: estornos {Valor(r.Estornos)} ({r.PedidosEstornados}); compras {Valor(r.ComprasConfirmadas)} ({r.NumeroCompras})"));
        Grelha(l, "Recebimentos por método", null, ["Método", "Quantidade", "Valor"],
            r.RecebimentosPorMetodo.Select(m => Lin(m.Rotulo, Inteiro(m.Quantidade), Valor(m.Valor))),
            subtotal: ["Total", Inteiro(r.RecebimentosPorMetodo.Sum(m => m.Quantidade)), Valor(r.RecebimentosPorMetodo.Sum(m => m.Valor))]);
        Grelha(l, "Despesas por categoria", null, ["Categoria", "Quantidade", "Valor"],
            r.DespesasPorCategoria.Select(m => Lin(m.Rotulo, Inteiro(m.Quantidade), Valor(m.Valor))),
            subtotal: ["Total", Inteiro(r.DespesasPorCategoria.Sum(m => m.Quantidade)), Valor(r.DespesasPorCategoria.Sum(m => m.Valor))]);
        Grelha(l, "Fechos de caixa", r.SessoesAbertas > 0 ? $"caixas ainda abertos: {r.SessoesAbertas}" : null, ["Sessão", "Terminal", "Aberta por", "Diferença"],
            r.SessoesFechadas.Select(s => Lin(s.Diferenca != 0, $"N.º {s.SessaoId}", s.Terminal, s.AbertaPor, (s.Diferenca > 0 ? "+" : "") + Valor(s.Diferenca))),
            subtotal: ["Diferença total", "", "", Valor(r.SessoesFechadas.Sum(s => s.Diferenca))], texto: 3, vazio: "Sem caixas fechados no período.");
        l.Add(new("RESULTADO ESTIMADO", Valor(r.ResultadoEstimado), Negrito: true, Grande: true));
        Rodape(l);
        return new DocumentoImpressao("Financeiro " + Periodo(desde, ate), l, cabecalho);
    }

    private async Task<DocumentoImpressao> AcompanhamentosAsync(DateOnly desde, DateOnly ate, CancellationToken ct)
    {
        var r = await relatorios.AcompanhamentosAsync(desde, ate, ct);
        var l = await CabecalhoAsync("Acompanhamentos", Periodo(desde, ate));
        l.Add(LinhaImpressao.NovoCartao("Escolhas", ComposicaoImpressao.Quantidade(r.PorOpcao.Sum(o => o.Vezes))));
        l.Add(LinhaImpressao.NovoCartao("Grupos", Inteiro(r.PorOpcao.Select(o => o.Grupo).Distinct().Count())));
        l.Add(LinhaImpressao.NovoCartao("Custo estimado", Valor(r.Artigos.Sum(a => a.Custo))));
        if (r.PorOpcao.Count == 0) l.Add(new("Sem acompanhamentos escolhidos no período."));
        foreach (var grupo in r.PorOpcao.GroupBy(o => o.Grupo))
        {
            var vezes = grupo.Sum(o => o.Vezes);
            Grelha(l, grupo.Key, $"{ComposicaoImpressao.Quantidade(vezes)} escolhas", ["Opção", "Vezes", "%"],
                grupo.Select(o => Lin(o.Opcao, ComposicaoImpressao.Quantidade(o.Vezes), Percentagem(o.Vezes, vezes))));
        }
        if (r.PorPrato.Count > 0)
            Grelha(l, "Por prato", null, ["Prato", "Acompanhamento", "Vezes"],
                r.PorPrato.Select(p => Lin(p.Prato, p.Opcao, ComposicaoImpressao.Quantidade(p.Vezes))), texto: 2);
        if (r.Artigos.Count > 0)
            Grelha(l, "Artigos gastos (estimado)", null, ["Artigo", "Quantidade", "Custo"],
                r.Artigos.Select(a => Lin(a.Artigo, $"{ComposicaoImpressao.Quantidade(a.Quantidade)} {a.Unidade}", Valor(a.Custo))),
                subtotal: ["Custo estimado", "", Valor(r.Artigos.Sum(a => a.Custo))]);
        Rodape(l);
        return new DocumentoImpressao("Acompanhamentos " + Periodo(desde, ate), l, cabecalho);
    }

    private static string Percentagem(decimal parte, decimal total) =>
        total == 0 ? "0%" : (parte / total * 100m).ToString("0", CultureInfo.InvariantCulture) + "%";

    private async Task<DocumentoImpressao> ExistenciasAsync(int? armazemId, int? categoriaId, CancellationToken ct)
    {
        var r = await gestao.ExistenciasAsync(armazemId, categoriaId, ct);
        var l = await CabecalhoAsync("Existências por armazém", $"{(r.Armazem is null ? "Todos os armazéns" : r.Armazem)} · em {r.Data:dd/MM/yyyy}");
        l.Add(LinhaImpressao.NovoCartao("Valor total", Valor(r.ValorTotal)));
        l.Add(LinhaImpressao.NovoCartao("Linhas", Inteiro(r.Linhas.Count)));
        l.Add(LinhaImpressao.NovoCartao("Abaixo do mínimo", Inteiro(r.AbaixoMinimo), alerta: r.AbaixoMinimo > 0));
        if (r.Linhas.Count == 0) l.Add(new("Sem existências."));
        foreach (var armazem in r.Linhas.GroupBy(x => x.Armazem))
            Grelha(l, armazem.Key, Resumo(armazem.Count(), "artigo", armazem.Sum(x => x.Valor)), ["Código", "Artigo", "Localização", "Quantidade", "Mínimo", "Valor"],
                armazem.Select(a => Lin(a.AbaixoMinimo, a.Codigo, a.Artigo, a.Localizacao ?? "", $"{ComposicaoImpressao.Quantidade(a.Quantidade)} {a.Unidade}",
                    a.Minimo > 0 ? $"{ComposicaoImpressao.Quantidade(a.Minimo)} {a.Unidade}" : "—", Valor(a.Valor))),
                subtotal: [$"Subtotal {armazem.Key}", "", "", "", "", Valor(armazem.Sum(x => x.Valor))], texto: 3);
        l.Add(new("VALOR TOTAL", Valor(r.ValorTotal), Negrito: true, Grande: true));
        Legenda(l, "abaixo do stock mínimo");
        Rodape(l);
        return new DocumentoImpressao($"Existências {r.Data:dd-MM-yyyy}", l, cabecalho);
    }

    private async Task<DocumentoImpressao> TransferenciasAsync(DateOnly desde, DateOnly ate, int? armazemId, CancellationToken ct)
    {
        var r = await gestao.TransferenciasAsync(desde, ate, armazemId, ct);
        var l = await CabecalhoAsync("Transferências entre armazéns", $"{Periodo(desde, ate)}{(r.Armazem is null ? "" : $" · {r.Armazem}")}");
        l.Add(LinhaImpressao.NovoCartao("Transferências", Inteiro(r.Linhas.Count)));
        l.Add(LinhaImpressao.NovoCartao("Valor transferido", Valor(r.Linhas.Sum(t => t.Valor))));
        l.Add(LinhaImpressao.NovoCartao("Percursos", Inteiro(r.PorPercurso.Count)));
        Grelha(l, "Transferências", Resumo(r.Linhas.Count, "transferência", r.Linhas.Sum(t => t.Valor)),
            ["Data", "Artigo", "De → Para", "Por", "Quantidade", "Valor"],
            r.Linhas.Select(t => Lin($"{TimeZoneInfo.ConvertTimeFromUtc(t.DataUtc, fuso):dd/MM HH:mm}", t.Artigo, $"{t.Origem} → {t.Destino}", t.Utilizador ?? "—",
                $"{ComposicaoImpressao.Quantidade(t.Quantidade)} {t.Unidade}", Valor(t.Valor))),
            subtotal: ["Total", "", "", "", "", Valor(r.Linhas.Sum(t => t.Valor))], texto: 4, vazio: "Sem transferências no período.");
        if (r.PorPercurso.Count > 0)
            Grelha(l, "Por percurso", null, ["Percurso", "Transferências", "Valor"],
                r.PorPercurso.Select(p => Lin(p.Rotulo, Inteiro(p.Quantidade), Valor(p.Valor))));
        Rodape(l);
        return new DocumentoImpressao("Transferências " + Periodo(desde, ate), l, cabecalho);
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
        decimal ValorDe(decimal saldo, decimal custo) => Math.Round(Math.Max(0, saldo) * custo, 2);
        var total = artigos.Sum(a => ValorDe(a.Saldo, a.CustoMedio));
        var abaixo = artigos.Count(a => a.StockMinimo > 0 && a.Saldo <= a.StockMinimo);
        var negativos = artigos.Count(a => a.Saldo < 0);

        var l = await CabecalhoAsync("Existências em stock", $"Em {hoje:dd/MM/yyyy}");
        l.Add(LinhaImpressao.NovoCartao("Valor do stock", Valor(total)));
        l.Add(LinhaImpressao.NovoCartao("Artigos", Inteiro(artigos.Count)));
        l.Add(LinhaImpressao.NovoCartao("Abaixo do mínimo", Inteiro(abaixo), alerta: abaixo > 0));
        l.Add(LinhaImpressao.NovoCartao("Stock negativo", Inteiro(negativos), alerta: negativos > 0));
        foreach (var tipo in artigos.GroupBy(a => a.Tipo))
        {
            var subtotal = tipo.Sum(a => ValorDe(a.Saldo, a.CustoMedio));
            Grelha(l, NomeTipo(tipo.Key), Resumo(tipo.Count(), "artigo", subtotal), ["Código", "Artigo", "Quantidade", "Mínimo", "Custo médio", "Valor"],
                tipo.Select(a => Lin(a.StockMinimo > 0 && a.Saldo <= a.StockMinimo, a.Codigo, a.Nome, $"{ComposicaoImpressao.Quantidade(a.Saldo)} {a.Unidade}",
                    a.StockMinimo > 0 ? $"{ComposicaoImpressao.Quantidade(a.StockMinimo)} {a.Unidade}" : "—", $"{Valor(a.CustoMedio)}/{a.Unidade}",
                    Valor(ValorDe(a.Saldo, a.CustoMedio)))),
                subtotal: [$"Subtotal {NomeTipo(tipo.Key)}", "", "", "", "", Valor(subtotal)], texto: 2);
        }
        if (artigos.Count == 0) l.Add(new("Sem artigos de stock."));
        l.Add(new("VALOR TOTAL DO STOCK", Valor(total), Negrito: true, Grande: true));
        Legenda(l, "abaixo do stock mínimo. Quantidades negativas não somam ao valor");
        Rodape(l);
        return new DocumentoImpressao($"Stock {hoje:dd-MM-yyyy}", l, cabecalho);
    }

    private async Task<DocumentoImpressao> ListaVendasAsync(DateOnly desde, DateOnly ate, CancellationToken ct)
    {
        var r = await detalhe.ListaVendasAsync(new FiltroListaVendas(desde, ate, Todas: true), ct);
        var variosDias = desde != ate;
        var t = r.Totais;
        var l = await CabecalhoAsync("Lista de vendas", Periodo(desde, ate));
        l.Add(LinhaImpressao.NovoCartao("Vendas", Inteiro(t.Vendas)));
        l.Add(LinhaImpressao.NovoCartao("Total", Valor(t.Total)));
        l.Add(LinhaImpressao.NovoCartao("Descontos", Valor(t.Desconto)));
        l.Add(LinhaImpressao.NovoCartao("Impostos incluídos", Valor(t.Impostos)));
        Grelha(l, "Vendas", Resumo(t.Vendas, "venda", t.Total), [variosDias ? "Data" : "Hora", "Pedido", "Local", "Operador / cliente", "Pagamento", "Desc.", "Total"],
            r.Vendas.Itens.Select(v => Lin(Hora(v.DataUtc, variosDias), v.Numero,
                v.Tipo switch { TipoPedido.Mesa => $"Mesa {v.Mesa}", TipoPedido.TakeAway => "Take Away", TipoPedido.Balcao => "Balcão", _ => "Delivery" },
                string.Join(" · ", new[] { v.Operador, v.Cliente }.Where(x => !string.IsNullOrEmpty(x))), v.Metodos ?? "",
                v.Desconto > 0 ? Valor(v.Desconto) : "", Valor(v.Total))),
            subtotal: ["Total", "", "", "", "", Valor(t.Desconto), Valor(t.Total)], texto: 5, vazio: "Sem vendas no período.");
        var totais = new List<LinhaImpressao> { Lin("Subtotal", Valor(t.Subtotal)), Lin("Descontos", "-" + Valor(t.Desconto)) };
        if (t.TaxaEntrega > 0) totais.Add(Lin("Taxas de entrega", Valor(t.TaxaEntrega)));
        totais.Add(Lin("Impostos incluídos", Valor(t.Impostos)));
        totais.Add(Lin("Recebido / troco", $"{Valor(t.Recebido)} / {Valor(t.Troco)}"));
        Grelha(l, "Totais", null, ["Descrição", "Valor"], totais);
        l.Add(new($"TOTAL ({t.Vendas} vendas)", Valor(t.Total), Negrito: true, Grande: true));
        Rodape(l);
        return new DocumentoImpressao("Lista de vendas " + Periodo(desde, ate), l, cabecalho);
    }

    private async Task<DocumentoImpressao> ClientesAsync(DateOnly desde, DateOnly ate, CancellationToken ct)
    {
        var r = await detalhe.ClientesAsync(desde, ate, null, ct);
        var l = await CabecalhoAsync("Vendas por cliente", Periodo(desde, ate));
        l.Add(LinhaImpressao.NovoCartao($"Com cliente ({r.VendasComCliente})", Valor(r.TotalComCliente)));
        l.Add(LinhaImpressao.NovoCartao($"Sem cliente ({r.VendasSemCliente})", Valor(r.TotalSemCliente)));
        l.Add(LinhaImpressao.NovoCartao("Clientes", Inteiro(r.Clientes.Count)));
        Grelha(l, "Clientes", null, ["Cliente", "Última compra", "Compras", "Descontos", "Total"],
            r.Clientes.Select(c => Lin(c.Nome, DataHora(c.UltimaCompraUtc), Inteiro(c.Compras), c.Descontos > 0 ? Valor(c.Descontos) : "—", Valor(c.Total))),
            subtotal: ["Total com cliente", "", Inteiro(r.VendasComCliente), "", Valor(r.TotalComCliente)], texto: 2, vazio: "Sem vendas com cliente associado.");
        Grelha(l, "Resumo", null, ["Vendas", "Quantidade", "Valor"],
        [
            Lin("Com cliente", Inteiro(r.VendasComCliente), Valor(r.TotalComCliente)),
            Lin("Sem cliente associado", Inteiro(r.VendasSemCliente), Valor(r.TotalSemCliente)),
        ]);
        Rodape(l);
        return new DocumentoImpressao("Clientes " + Periodo(desde, ate), l, cabecalho);
    }

    private async Task<DocumentoImpressao> ImpostosAsync(DateOnly desde, DateOnly ate, CancellationToken ct)
    {
        var r = await detalhe.ImpostosAsync(desde, ate, null, ct);
        var l = await CabecalhoAsync("IVA e descontos", Periodo(desde, ate));
        l.Add(LinhaImpressao.NovoCartao($"Vendas ({r.Vendas})", Valor(r.Total)));
        l.Add(LinhaImpressao.NovoCartao("Base sem imposto", Valor(r.BaseSemImposto)));
        l.Add(LinhaImpressao.NovoCartao("Imposto incluído", Valor(r.Impostos)));
        l.Add(LinhaImpressao.NovoCartao("Descontos", Valor(r.Descontos)));
        var resumo = new List<LinhaImpressao>
        {
            Lin($"Vendas ({r.Vendas})", Valor(r.Total)),
            Lin("Subtotal (antes de descontos)", Valor(r.Subtotal)),
            Lin("Descontos", "-" + Valor(r.Descontos)),
            Lin("Base sem imposto", Valor(r.BaseSemImposto)),
            LinhaImpressao.Tabela(["Imposto incluído", Valor(r.Impostos)], negrito: true),
        };
        if (r.TaxasEntrega > 0) resumo.Add(Lin("Taxas de entrega (sem imposto)", Valor(r.TaxasEntrega)));
        Grelha(l, "Resumo", null, ["Descrição", "Valor"], resumo);
        Grelha(l, "Por taxa", null, ["Taxa", "Base", "Imposto", "Com imposto"],
            r.PorTaxa.Select(t => Lin(t.Taxa, Valor(t.BaseSemImposto), Valor(t.Imposto), Valor(t.ComImposto))),
            subtotal: ["Total", Valor(r.PorTaxa.Sum(t => t.BaseSemImposto)), Valor(r.PorTaxa.Sum(t => t.Imposto)), Valor(r.PorTaxa.Sum(t => t.ComImposto))]);
        Grelha(l, r.Agrupamento == "mes" ? "Por mês" : "Por dia", null, [r.Agrupamento == "mes" ? "Mês" : "Dia", "Vendas", "Descontos", "Imposto", "Total"],
            r.PorPeriodo.Select(p => Lin(p.Periodo, Inteiro(p.Vendas), p.Descontos > 0 ? Valor(p.Descontos) : "—", Valor(p.Imposto), Valor(p.Total))));
        Rodape(l);
        return new DocumentoImpressao("IVA " + Periodo(desde, ate), l, cabecalho);
    }

    private async Task<DocumentoImpressao> AnulacoesAsync(DateOnly desde, DateOnly ate, CancellationToken ct)
    {
        var r = await detalhe.AnulacoesAsync(desde, ate, ct);
        var variosDias = desde != ate;
        var quando = variosDias ? "Data" : "Hora";
        var l = await CabecalhoAsync("Anulações e devoluções", Periodo(desde, ate));
        l.Add(LinhaImpressao.NovoCartao($"Estornos ({r.Estornos.Count})", Valor(r.Estornos.Sum(p => p.Valor)), alerta: r.Estornos.Count > 0));
        l.Add(LinhaImpressao.NovoCartao($"Pedidos cancelados ({r.PedidosCancelados.Count})", Valor(r.PedidosCancelados.Sum(p => p.Valor))));
        l.Add(LinhaImpressao.NovoCartao($"Itens anulados ({r.ItensCancelados.Count})", Valor(r.ItensCancelados.Sum(i => i.Valor))));
        l.Add(LinhaImpressao.NovoCartao($"Pagamentos anulados ({r.PagamentosAnulados.Count})", Valor(r.PagamentosAnulados.Sum(p => p.Valor))));
        void Pedidos(string titulo, IReadOnlyList<PedidoAnulado> lista) =>
            Grelha(l, titulo, Resumo(lista.Count, "pedido", lista.Sum(p => p.Valor)), [quando, "Pedido", "Por", "Motivo", "Valor"],
                lista.Select(p => Lin(Hora(p.DataUtc, variosDias), $"{p.Numero}{(p.Recibo is null ? "" : $" ({p.Recibo})")}", p.Por ?? "—", p.Motivo ?? "sem motivo", Valor(p.Valor))),
                subtotal: ["Total", "", "", "", Valor(lista.Sum(p => p.Valor))], texto: 4);
        Pedidos("Estornos (devoluções)", r.Estornos);
        Pedidos("Pedidos cancelados", r.PedidosCancelados);
        Grelha(l, "Itens cancelados após envio", Resumo(r.ItensCancelados.Count, "item", r.ItensCancelados.Sum(i => i.Valor)),
            [quando, "Pedido", "Produto", "Por", "Motivo", "Qtd.", "Valor"],
            r.ItensCancelados.Select(i => Lin(Hora(i.DataUtc, variosDias), i.Pedido, i.Produto, i.Por ?? "—", i.Motivo ?? "sem motivo",
                ComposicaoImpressao.Quantidade(i.Quantidade), Valor(i.Valor))),
            subtotal: ["Total", "", "", "", "", "", Valor(r.ItensCancelados.Sum(i => i.Valor))], texto: 5);
        Grelha(l, "Pagamentos anulados", Resumo(r.PagamentosAnulados.Count, "pagamento", r.PagamentosAnulados.Sum(p => p.Valor)),
            [quando, "Pedido", "Método", "Por", "Motivo", "Valor"],
            r.PagamentosAnulados.Select(p => Lin(Hora(p.DataUtc, variosDias), p.Pedido, p.Metodo, p.Por ?? "—", p.Motivo ?? "sem motivo", Valor(p.Valor))),
            subtotal: ["Total", "", "", "", "", Valor(r.PagamentosAnulados.Sum(p => p.Valor))], texto: 5);
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
        if (fechada && !contagemCega)
        {
            l.Add(LinhaImpressao.NovoCartao($"Pedidos pagos ({s.PedidosPagos})", Valor(s.TotalRecebido ?? 0)));
            l.Add(LinhaImpressao.NovoCartao("Esperado", Valor(s.TotalEsperado ?? 0)));
            l.Add(LinhaImpressao.NovoCartao("Contado", Valor(s.TotalContado ?? 0)));
            l.Add(LinhaImpressao.NovoCartao("Diferença", (s.TotalDiferenca > 0 ? "+" : "") + Valor(s.TotalDiferenca ?? 0), alerta: s.TotalDiferenca is not (null or 0)));
        }
        var sessao = new List<LinhaImpressao> { Lin("Aberta por", s.AbertaPor), Lin("Abertura", DataHora(s.AbertaEmUtc)) };
        if (fechada)
        {
            sessao.Add(Lin("Fechada por", s.FechadaPor ?? "—"));
            sessao.Add(Lin("Fecho", s.FechadaEmUtc is DateTime f ? DataHora(f) : "—"));
        }
        sessao.Add(Lin("Fundo inicial", Valor(s.FundoInicial)));
        Grelha(l, "Sessão", fechada ? null : "ainda aberta", ["Descrição", "Valor"], sessao);
        if (!fechada)
        {
            l.Add(new("Caixa ainda aberto: os valores esperados e as diferenças só aparecem depois do fecho.", Alerta: true));
        }
        if (fechada && contagemCega)
            Grelha(l, "Contagem", null, ["Método", "Contado"],
                s.PorMetodo.Where(m => m.Contar || m.Contado is > 0).Select(m => Lin(m.Metodo, Valor(m.Contado ?? 0))),
                subtotal: ["Total contado", Valor(s.TotalContado ?? 0)]);
        else if (fechada)
        {
            // Só métodos com movimento ou com algo contado (o fecho regista 0 nos restantes; não interessa no papel).
            var metodos = s.PorMetodo.Where(m => m.Fundo != 0 || m.Recebimentos != 0 || m.Entradas != 0 || m.Saidas != 0 || m.Contado is > 0 || m.Diferenca is not (null or 0)).ToList();
            Grelha(l, "Movimento por método", null, ["Método", "Fundo", "Recebido", "Entradas", "Saídas"],
                metodos.Select(m => Lin(m.Metodo, Valor(m.Fundo), Valor(m.Recebimentos ?? 0), Valor(m.Entradas ?? 0), "-" + Valor(m.Saidas ?? 0))));
            Grelha(l, "Contagem por método", null, ["Método", "Esperado", "Contado", "Diferença"],
                metodos.Select(m => Lin(m.Diferenca is not (null or 0), m.Metodo, Valor(m.Esperado ?? 0), Valor(m.Contado ?? 0), (m.Diferenca > 0 ? "+" : "") + Valor(m.Diferenca ?? 0))),
                subtotal: ["Total", Valor(s.TotalEsperado ?? 0), Valor(s.TotalContado ?? 0), (s.TotalDiferenca > 0 ? "+" : "") + Valor(s.TotalDiferenca ?? 0)]);
        }
        else
            Grelha(l, "Métodos a contar no fecho", null, ["Método", "Contado"], s.PorMetodo.Where(m => m.Contar).Select(m => Lin(m.Metodo, "________")));
        if (s.Movimentos.Count > 0)
            Grelha(l, "Movimentos manuais", null, ["Hora", "Tipo", "Motivo", "Valor"],
                s.Movimentos.Select(mv => Lin(Hora(mv.DataUtc, false), mv.Tipo.ToString(), mv.Motivo ?? "—", (mv.Tipo == TipoMovimentoCaixa.Entrada ? "" : "-") + Valor(mv.Valor))),
                texto: 3);
        if (fechada)
        {
            if (contagemCega)
                l.Add(new("TOTAL CONTADO", Valor(s.TotalContado ?? 0), Negrito: true, Grande: true));
            else
            {
                l.Add(new("TOTAL ESPERADO", Valor(s.TotalEsperado ?? 0), Negrito: true, Grande: true));
                l.Add(new("Total contado", Valor(s.TotalContado ?? 0)));
                l.Add(new("DIFERENÇA", (s.TotalDiferenca > 0 ? "+" : "") + Valor(s.TotalDiferenca ?? 0), Negrito: true, Alerta: s.TotalDiferenca is not (null or 0)));
            }
            if (imprimeProdutos)
                await ProdutosDaSessaoAsync(l, s.Id, ct);
        }
        if (!string.IsNullOrWhiteSpace(s.ObservacoesFecho)) l.Add(new($"Obs.: {s.ObservacoesFecho}"));
        Assinaturas(l, "Operador", "Responsável");
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
        l.Add(LinhaImpressao.NovoCartao("Vendas líquidas", Valor(r.VendasLiquidas)));
        l.Add(LinhaImpressao.NovoCartao("Pedidos pagos", Inteiro(r.Pedidos)));
        l.Add(LinhaImpressao.NovoCartao("Ticket médio", Valor(r.TicketMedio)));
        l.Add(LinhaImpressao.NovoCartao("Diferença de caixa", r.Diferenca is decimal d0 ? (d0 > 0 ? "+" : "") + Valor(d0) : "—", alerta: r.Diferenca is not (null or 0)));
        var dia = new List<LinhaImpressao> { Lin("Dia aberto por", r.AbertoPor), Lin("Abertura", DataHora(r.AbertoEmUtc)) };
        if (fechado)
        {
            dia.Add(Lin("Dia fechado por", r.FechadoPor ?? "—"));
            dia.Add(Lin("Fecho", r.FechadoEmUtc is DateTime f ? DataHora(f) : "—"));
        }
        dia.Add(Lin("Caixas no dia", Inteiro(r.Caixas.Count)));
        Grelha(l, "Dia de operação", fechado ? null : "ainda aberto", ["Descrição", "Valor"], dia);
        if (!fechado)
        {
            l.Add(new("Dia ainda aberto: os valores podem mudar até ao fecho do dia.", Alerta: true));
        }

        var resumo = new List<LinhaImpressao> { Lin("Pedidos pagos", Inteiro(r.Pedidos)), Lin("Vendas brutas", Valor(r.VendasBrutas)) };
        if (r.Descontos != 0) resumo.Add(Lin("Descontos", "-" + Valor(r.Descontos)));
        if (r.TaxasEntrega != 0) resumo.Add(Lin("Taxas de entrega", Valor(r.TaxasEntrega)));
        resumo.Add(LinhaImpressao.Tabela(["Vendas líquidas", Valor(r.VendasLiquidas)], negrito: true));
        resumo.Add(Lin("IVA incluído", Valor(r.Impostos)));
        resumo.Add(Lin("Ticket médio", Valor(r.TicketMedio)));
        resumo.Add(Lin("Fundos iniciais", Valor(r.Caixas.Sum(c => c.Fundo))));
        resumo.Add(Lin("Total recebido", Valor(r.TotalRecebido)));
        resumo.Add(Lin("Total entrado", Valor(r.TotalEntrado)));
        resumo.Add(Lin("Total saído", Valor(r.TotalSaido)));
        Grelha(l, "1. Resumo do dia", null, ["Descrição", "Valor"], resumo,
            subtotal: r.Diferenca is decimal dif ? ["Diferença de caixa", (dif > 0 ? "+" : "") + Valor(dif)] : null);

        Grelha(l, "2. Resumo por caixa", null, ["Caixa", "Recebido", "Esperado", "Contado", "Diferença"],
            r.Caixas.Select(c => Lin(c.Diferenca is not (null or 0),
                $"{c.Terminal} · {c.Operador} · {Hora(c.AbertaEmUtc, false)}–{(c.FechadaEmUtc is DateTime fc ? Hora(fc, false) : "aberto")} ({c.Pedidos} ped.)",
                Num(c.Recebido), c.Esperado is decimal e ? Num(e) : "—", c.Contado is decimal ct2 ? Num(ct2) : "—",
                c.Diferenca is decimal d ? (d > 0 ? "+" : "") + Num(d) : "—")),
            subtotal: ["Total do dia", Num(r.Caixas.Sum(c => c.Recebido)),
                r.Caixas.Any(c => c.Aberta) ? "—" : Num(r.Caixas.Sum(c => c.Esperado ?? 0)),
                r.Caixas.Any(c => c.Aberta) ? "—" : Num(r.Caixas.Sum(c => c.Contado ?? 0)),
                r.Diferenca is decimal td ? (td > 0 ? "+" : "") + Num(td) : "—"],
            vazio: "Nenhum caixa aberto neste dia.");

        var totalMetodos = r.Metodos.Sum(m => m.Total);
        if (r.Caixas.Count is > 1 and <= 3)
            Grelha(l, "3. Vendas por meio de pagamento", null, ["Meio de pagamento", .. r.Caixas.Select(c => c.Terminal), "Total"],
                r.Metodos.Select(m => Lin([m.Metodo, .. m.PorCaixa.Select(Num), Num(m.Total)])),
                subtotal: ["Total", .. r.Caixas.Select(c => Num(c.Recebido)), Num(totalMetodos)], vazio: "Sem pagamentos.");
        else
            Grelha(l, "3. Vendas por meio de pagamento", null, ["Meio de pagamento", "Total", "%"],
                r.Metodos.Select(m => Lin(m.Metodo, Num(m.Total), Pct(m.Total, totalMetodos))),
                subtotal: ["Total", Num(totalMetodos), "100,0"], vazio: "Sem pagamentos.");

        Grelha(l, "4. Outros movimentos de caixa", null, ["Movimento", "Quantidade", "Valor"],
            r.Movimentos.Select(m => Lin(NomeMovimento(m.Tipo), Inteiro(m.Quantidade), (m.Valor > 0 ? "+" : "") + Valor(m.Valor))),
            subtotal: ["Saldo dos movimentos", "", Valor(r.Movimentos.Sum(m => m.Valor))], vazio: "Sem movimentos.");

        var k = r.Controlo;
        Grelha(l, "5. Controlo", null, ["Ocorrência", "Quantidade", "Valor"],
        [
            Lin("Descontos concedidos", Inteiro(k.Descontos), Valor(k.ValorDescontos)),
            Lin("Itens anulados", Inteiro(k.ItensAnulados), Valor(k.ValorItensAnulados)),
            Lin("Pedidos estornados", Inteiro(k.Estornos), Valor(k.ValorEstornos)),
            Lin("Pagamentos anulados", Inteiro(k.PagamentosAnulados), Valor(k.ValorPagamentosAnulados)),
            Lin(k.PedidosEmAberto > 0, "Pedidos em aberto", Inteiro(k.PedidosEmAberto), ""),
        ]);
        // Contas que ficaram abertas: transitam para o dia seguinte (no fecho) ou ainda por fechar (ponto de situação).
        if (r.PedidosAbertos is { Count: > 0 } contas)
            Grelha(l, r.Estado == EstadoDia.Fechado ? "Contas transitadas para o dia seguinte" : "Contas ainda abertas", Resumo(contas.Count, "conta", contas.Sum(c => c.Total - c.Pago)),
                ["Conta", "Local", "Responsável", "Por pagar"],
                contas.Select(c => Lin(c.Numero, c.Local, c.Responsavel ?? "—", Valor(c.Total - c.Pago))),
                subtotal: ["Total por pagar", "", "", Valor(contas.Sum(c => c.Total - c.Pago))], texto: 3);

        var totalProdutos = r.Categorias.Sum(c => c.Total);
        var produtos = new List<LinhaImpressao>();
        foreach (var c in r.Categorias)
        {
            produtos.Add(LinhaImpressao.Tabela([c.Categoria, ComposicaoImpressao.Quantidade(c.Quantidade), Num(c.Total), Pct(c.Total, totalProdutos)], negrito: true));
            produtos.AddRange(c.Produtos.Select(p => Lin("  " + p.Produto, ComposicaoImpressao.Quantidade(p.Quantidade), Num(p.Total), Pct(p.Total, totalProdutos))));
        }
        Grelha(l, "6. Produtos vendidos por categoria", null, ["Produto", "Qtd.", "Total", "%"], produtos,
            subtotal: ["Total (antes de descontos)", ComposicaoImpressao.Quantidade(r.Categorias.Sum(c => c.Quantidade)), Num(totalProdutos), "100,0"],
            vazio: "Sem vendas fechadas neste dia.");
        l.Add(new($"Valores em {moeda}, com IVA incluído."));
        if (!string.IsNullOrWhiteSpace(r.Observacoes)) l.Add(new($"Obs.: {r.Observacoes}"));
        Assinaturas(l, "Gerente", "Conferido por");
        Rodape(l);
        return new DocumentoImpressao($"Fecho do dia {r.Data:dd/MM/yyyy}", l, cabecalho);
    }

    /// <summary>
    /// Folha de contagem de um inventário, para contar no armazém em papel: artigo, unidade e espaço para a quantidade contada.
    /// Ordem: alfabética, por código, por categoria e subcategoria, por tipo de artigo ou por localização no armazém (com títulos
    /// por grupo). Opções: só algumas categorias (as subcategorias entram), uma página por grupo (A4), esconder os de saldo zero.
    /// Por omissão é cega (sem o saldo do sistema, para não influenciar quem conta). A contagem escreve-se depois no ecrã do inventário.
    /// </summary>
    private async Task<DocumentoImpressao> FolhaContagemAsync(int inventarioId, OpcoesFolhaContagem o)
    {
        var inv = await stock.ObterInventarioAsync(inventarioId);
        IEnumerable<Stock.InventarioLinhaDto> linhas = inv.Linhas;
        if (o.Categorias is { Count: > 0 } escolhidas)
        {
            var filhas = await db.Categorias.AsNoTracking().Where(c => c.CategoriaPaiId != null && escolhidas.Contains(c.CategoriaPaiId.Value)).Select(c => c.Id).ToListAsync();
            var ids = escolhidas.Concat(filhas).ToHashSet();
            linhas = linhas.Where(x => x.CategoriaId is int c && ids.Contains(c));
        }
        if (o.EsconderZero) linhas = linhas.Where(x => x.QuantidadeSistema != 0);
        var lista = linhas.ToList();

        var l = await CabecalhoAsync("Folha de contagem", $"Inventário n.º {inv.Id} · {inv.Armazem}");
        var dados = new List<LinhaImpressao>
        {
            Lin("Criado em", DataHora(inv.CriadoEmUtc)),
            Lin("Ordem", $"{NomeOrdem(o.Ordem)}{(o.Categorias is { Count: > 0 } ? " · só as categorias escolhidas" : "")}{(o.EsconderZero ? " · sem os de saldo zero" : "")}"),
            Lin("Artigos", Inteiro(lista.Count)),
        };
        if (inv.Estado != EstadoInventario.Aberto)
            dados.Add(LinhaImpressao.Tabela(["Estado", $"INVENTÁRIO {inv.Estado.ToString().ToUpperInvariant()}"], alerta: true));
        Grelha(l, "Inventário", null, ["Descrição", "Valor"], dados);
        l.Add(new(o.ComSaldo ? "Com o saldo do sistema." : "Contagem cega: conte e escreva a quantidade de cada artigo."));

        const string espaco = "__________";
        var porNome = StringComparer.CurrentCultureIgnoreCase;
        void Tabela(string titulo, IReadOnlyCollection<Stock.InventarioLinhaDto> artigos, bool quebra = false)
        {
            Grelha(l, titulo, $"{artigos.Count} artigos", o.ComSaldo ? ["Código", "Artigo", "Un.", "Sistema", "Contado"] : ["Código", "Artigo", "Un.", "Contado"],
                artigos.Select(x => LinhaImpressao.Tabela(o.ComSaldo
                    ? [x.Codigo, x.Artigo, x.Unidade, ComposicaoImpressao.Quantidade(x.QuantidadeSistema), espaco]
                    : [x.Codigo, x.Artigo, x.Unidade, espaco])), texto: 3);
            if (quebra) l[l.FindLastIndex(x => x.Faixa)] = l[l.FindLastIndex(x => x.Faixa)] with { QuebraPagina = true };
        }
        IOrderedEnumerable<Stock.InventarioLinhaDto> Alfabetica(IEnumerable<Stock.InventarioLinhaDto> a) => a.OrderBy(x => x.Artigo, porNome).ThenBy(x => x.Codigo);

        // Grupos (título, subgrupos com título opcional): «sem categoria/localização» ficam no fim.
        var grupos = o.Ordem switch
        {
            OrdemFolhaContagem.Categoria => lista.GroupBy(x => x.Categoria).OrderBy(g => g.Key is null).ThenBy(g => g.Key, porNome)
                .Select(g => (Titulo: g.Key ?? "Sem categoria", Sub: g.GroupBy(x => x.Subcategoria).OrderBy(s => s.Key is not null).ThenBy(s => s.Key, porNome)
                    .Select(s => (Titulo: s.Key, Artigos: Alfabetica(s).ToList())).ToList())).ToList(),
            OrdemFolhaContagem.Tipo => lista.GroupBy(x => x.Tipo).OrderBy(g => g.Key)
                .Select(g => (Titulo: NomeTipo(g.Key), Sub: new List<(string? Titulo, List<Stock.InventarioLinhaDto> Artigos)> { (null, Alfabetica(g).ToList()) })).ToList(),
            OrdemFolhaContagem.Localizacao => lista.GroupBy(x => x.Localizacao).OrderBy(g => g.Key is null).ThenBy(g => g.Key, porNome)
                .Select(g => (Titulo: g.Key ?? "Sem localização", Sub: new List<(string? Titulo, List<Stock.InventarioLinhaDto> Artigos)> { (null, Alfabetica(g).ToList()) })).ToList(),
            _ => [],
        };
        if (grupos.Count == 0 && lista.Count > 0)
            Tabela("Artigos", [.. o.Ordem == OrdemFolhaContagem.Codigo ? lista.OrderBy(x => x.Codigo, StringComparer.OrdinalIgnoreCase) : Alfabetica(lista)]);
        for (var i = 0; i < grupos.Count; i++)
        {
            var (titulo, subs) = grupos[i];
            var total = subs.Sum(s => s.Artigos.Count);
            var quebra = o.PaginaPorGrupo && i > 0;
            // Um só subgrupo sem título: a grelha tem o título do grupo; com subcategorias, uma grelha por subcategoria («Grupo › Sub»).
            if (subs is [(null, var unicos)])
                Tabela($"{titulo.ToUpperInvariant()} ({total})", unicos, quebra);
            else
                for (var k = 0; k < subs.Count; k++)
                    Tabela(subs[k].Titulo is null ? $"{titulo.ToUpperInvariant()} ({total})" : $"{titulo.ToUpperInvariant()} › {subs[k].Titulo} ({subs[k].Artigos.Count})", subs[k].Artigos, quebra && k == 0);
            if (o.PaginaPorGrupo)
            {
                l.Add(new(""));
                l.Add(new("Contado por: __________________   Data: ____/____"));
            }
        }
        if (lista.Count == 0) l.Add(new(inv.Linhas.Count == 0 ? "Sem artigos activos neste armazém." : "Nenhum artigo com estas opções."));
        l.Add(LinhaImpressao.Traco);
        l.Add(new("Data da contagem: ____/____/________"));
        Assinaturas(l, "Contado por", "Conferido por");
        Rodape(l);
        return new DocumentoImpressao($"Contagem inventário {inv.Id}", l, cabecalho);
    }

    private static string NomeOrdem(OrdemFolhaContagem ordem) => ordem switch
    {
        OrdemFolhaContagem.Codigo => "por código",
        OrdemFolhaContagem.Categoria => "por categoria e subcategoria",
        OrdemFolhaContagem.Tipo => "por tipo de artigo",
        OrdemFolhaContagem.Localizacao => "por localização no armazém",
        _ => "alfabética",
    };

    private static string NomeTipo(TipoArtigo tipo) => tipo switch
    {
        TipoArtigo.Ingrediente => "Ingredientes",
        TipoArtigo.Revenda => "Revenda",
        _ => "Consumíveis",
    };

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
        var produtos = new List<LinhaImpressao>();
        foreach (var categoria in itens.GroupBy(i => i.Categoria).OrderByDescending(g => g.Sum(i => i.Total)))
        {
            produtos.Add(LinhaImpressao.Tabela([categoria.Key, ComposicaoImpressao.Quantidade(categoria.Sum(i => i.Quantidade)), Valor(categoria.Sum(i => i.Total))], negrito: true));
            produtos.AddRange(categoria.OrderByDescending(i => i.Total)
                .Select(p => Lin("  " + p.NomeProduto, ComposicaoImpressao.Quantidade(p.Quantidade), Valor(p.Total))));
        }
        Grelha(l, "Produtos vendidos por categoria", null, ["Produto", "Qtd.", "Valor"], produtos,
            subtotal: ["Total dos produtos", ComposicaoImpressao.Quantidade(itens.Sum(i => i.Quantidade)), Valor(itens.Sum(i => i.Total))],
            vazio: "Sem vendas fechadas nesta sessão.");
        if (itens.Count == 0) return;
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
        l.Add(LinhaImpressao.NovoCartao("Saldo em circulação", Valor(s.SaldoEmCirculacao)));
        l.Add(LinhaImpressao.NovoCartao("Cartões com saldo", Inteiro(s.CartoesComSaldo)));
        l.Add(LinhaImpressao.NovoCartao("Cartões activos", Inteiro(s.CartoesActivos)));
        l.Add(LinhaImpressao.NovoCartao("Cartões novos", Inteiro(s.CartoesNovos)));
        var movimentos = new List<LinhaImpressao>
        {
            Lin("Carregamentos", Inteiro(s.Carregamentos), Valor(s.Carregado)),
            Lin("Recargas", Inteiro(s.Recargas), Valor(s.Recarregado)),
            Lin("Pagamentos com cartão", Inteiro(s.Pagamentos), Valor(s.Pago)),
            Lin("Devolvido aos cartões", Inteiro(s.Devolucoes), Valor(s.Devolvido)),
        };
        if (s.Descargas > 0) movimentos.Add(Lin("Saldo descarregado", Inteiro(s.Descargas), Valor(s.Descarregado)));
        Grelha(l, "Movimentos", null, ["Movimento", "Quantidade", "Valor"], movimentos);
        if (r.PorMetodo.Count > 0)
            Grelha(l, "Dinheiro recebido por método", null, ["Método", "Quantidade", "Valor"],
                r.PorMetodo.Select(m => Lin(m.Metodo, Inteiro(m.Quantidade), Valor(m.Valor))),
                subtotal: ["Total", Inteiro(r.PorMetodo.Sum(m => m.Quantidade)), Valor(r.PorMetodo.Sum(m => m.Valor))]);
        if (r.PorOperador.Count > 0)
            Grelha(l, "Por operador", null, ["Operador", "Carregado", "Pago"], r.PorOperador.Select(o => Lin(o.Operador, Valor(o.Carregado), Valor(o.Pago))));
        if (r.Saldos.Count > 0)
            Grelha(l, "Saldos por cartão", null, ["Cartão", "Titular", "Saldo"],
                r.Saldos.Select(c => Lin(!c.Activo, c.Numero + (c.Activo ? "" : " (bloqueado)"), c.Titular ?? "—", Valor(c.Saldo))), texto: 2);
        l.Add(new("SALDO EM CIRCULAÇÃO", Valor(s.SaldoEmCirculacao), Negrito: true, Grande: true));
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
        l.Add(LinhaImpressao.NovoCartao($"Facturado ({s.Facturas})", Valor(s.Facturado)));
        l.Add(LinhaImpressao.NovoCartao("Pago (recibos)", Valor(s.Pago)));
        l.Add(LinhaImpressao.NovoCartao("Creditado", Valor(s.Creditado)));
        l.Add(LinhaImpressao.NovoCartao("Em dívida", Valor(s.EmDivida), alerta: s.Vencido > 0));

        Grelha(l, "Facturas do período", Resumo(r.Facturas.Count, "factura", r.Facturas.Sum(f => f.Total)),
            ["Data", "Número", "Cliente", "Situação", "Total", "Em dívida"],
            r.Facturas.Select(f => Lin(f.Situacao == Facturacao.SituacaoFactura.Vencida, Dia(f.EmitidoEmUtc), f.Numero, f.ClienteNome,
                string.Join(" · ", new[] { NomeSituacao(f.Situacao, f.DiasAtraso), f.Vencimento is DateOnly v ? $"venc. {v:dd/MM/yyyy}" : null }.Where(x => x is not null)),
                Valor(f.Total), f.EmDivida > 0 ? Valor(f.EmDivida) : "—")),
            subtotal: ["Total", "", "", "", Valor(r.Facturas.Sum(f => f.Total)), Valor(r.Facturas.Sum(f => f.EmDivida))], texto: 4,
            vazio: "Sem facturas a crédito no período.");
        if (r.Cortado) l.Add(new($"(Lista limitada às primeiras {Facturacao.ServicoContaCorrente.MaximoLinhas} facturas.)"));

        // Antiguidade da dívida: em A4 todas as colunas; nos talões só o vencido e o total (não cabem 7 valores).
        var titulo = $"Dívida de clientes hoje ({r.Hoje:dd/MM/yyyy})";
        string[] subtotalA4 = ["Total", "", Valor(r.Dividas.Sum(d => d.NaoVencido)), Valor(r.Dividas.Sum(d => d.Ate30)), Valor(r.Dividas.Sum(d => d.De31a60)),
            Valor(r.Dividas.Sum(d => d.De61a90)), Valor(r.Dividas.Sum(d => d.Mais90)), Valor(r.TotalEmDivida)];
        var inicio = l.Count;
        Grelha(l, titulo, null, ["Cliente", "Fact.", "Não vencido", "1-30 d", "31-60 d", "61-90 d", "+90 d", "Em dívida"],
            r.Dividas.Select(d => Lin(d.Ate30 + d.De31a60 + d.De61a90 + d.Mais90 > 0, d.ClienteNome, Inteiro(d.Facturas), Valor(d.NaoVencido), Valor(d.Ate30),
                Valor(d.De31a60), Valor(d.De61a90), Valor(d.Mais90), Valor(d.EmDivida))),
            subtotal: r.Dividas.Count > 0 ? subtotalA4 : null, vazio: "Nenhum cliente em dívida.");
        for (var i = inicio; i < l.Count; i++) l[i] = l[i] with { SoA4 = true };
        inicio = l.Count;
        Grelha(l, titulo, null, ["Cliente", "Vencido", "Em dívida"],
            r.Dividas.Select(d => Lin(d.Ate30 + d.De31a60 + d.De61a90 + d.Mais90 > 0, d.ClienteNome, Valor(d.Ate30 + d.De31a60 + d.De61a90 + d.Mais90), Valor(d.EmDivida))),
            subtotal: r.Dividas.Count > 0 ? ["Total", Valor(r.TotalVencido), Valor(r.TotalEmDivida)] : null, vazio: "Nenhum cliente em dívida.");
        for (var i = inicio; i < l.Count; i++) l[i] = l[i] with { SoTalao = true };
        if (r.Dividas.Count > 0)
            l.Add(new("TOTAL EM DÍVIDA", Valor(r.TotalEmDivida), Negrito: true, Grande: true));
        Rodape(l);
        return new DocumentoImpressao("Facturas a crédito " + Periodo(desde, ate), l, cabecalho);
    }

    /// <summary>Histórico de documentos do cliente (todos os tipos), com os totais dos documentos em vigor.</summary>
    private async Task<DocumentoImpressao> HistoricoClienteAsync(int clienteId, DateOnly desde, DateOnly ate, CancellationToken ct)
    {
        var r = await contas.HistoricoAsync(clienteId, desde, ate, ct: ct);
        var l = await CabecalhoAsync("Histórico de facturas do cliente", Periodo(desde, ate));
        Cliente(l, r.ClienteNome, r.ClienteNuit, null);
        var t = r.Totais;
        l.Add(LinhaImpressao.NovoCartao("Documentos em vigor", Inteiro(t.Documentos)));
        l.Add(LinhaImpressao.NovoCartao("Facturado a crédito", Valor(t.Facturado)));
        l.Add(LinhaImpressao.NovoCartao("Em dívida", Valor(t.EmDivida), alerta: t.EmDivida > 0));
        Grelha(l, "Documentos", Resumo(r.Documentos.Count, "documento", null), ["Data", "Número", "Tipo", "Detalhe", "Em dívida", "Total"],
            r.Documentos.Select(d =>
            {
                var anulado = d.Estado == Domain.Facturacao.EstadoDocumento.Anulado;
                var detalhe = new[] { d.Origem, d.MetodoPagamento, d.Vencimento is DateOnly v ? $"venc. {v:dd/MM/yyyy}" : null }.Where(x => x is not null);
                return Lin(anulado, Dia(d.EmitidoEmUtc), d.Numero, Domain.Facturacao.Documento.Nome(d.Tipo) + (anulado ? " (ANULADO)" : ""), string.Join(" · ", detalhe),
                    d.EmDivida > 0 ? Valor(d.EmDivida) : "—", Valor(d.Total));
            }), texto: 4, vazio: "Sem documentos no período.");
        if (r.Cortado) l.Add(new($"(Lista limitada aos primeiros {Facturacao.ServicoContaCorrente.MaximoLinhas} documentos.)"));
        Grelha(l, $"Totais (documentos em vigor: {t.Documentos})", null, ["Descrição", "Valor"],
        [
            Lin("Vendas a dinheiro", Valor(t.VendasDinheiro)),
            Lin("Facturas a crédito", Valor(t.Facturado)),
            Lin("Recebido (recibos)", Valor(t.Recebido)),
            Lin("Notas de crédito", Valor(t.Creditado)),
        ], subtotal: ["EM DÍVIDA (facturas do período)", Valor(t.EmDivida)]);
        Rodape(l);
        return new DocumentoImpressao($"Histórico {r.ClienteNome} {Periodo(desde, ate)}", l, cabecalho);
    }

    /// <summary>Extrato da conta corrente: saldo anterior, movimentos com saldo acumulado, saldo final e facturas em aberto.</summary>
    private async Task<DocumentoImpressao> ExtratoClienteAsync(int clienteId, DateOnly desde, DateOnly ate, CancellationToken ct)
    {
        var r = await contas.ExtratoAsync(clienteId, desde, ate, ct);
        var l = await CabecalhoAsync("Extrato de conta corrente", Periodo(desde, ate));
        Cliente(l, r.ClienteNome, r.ClienteNuit, r.ClienteTelefone);
        l.Add(LinhaImpressao.NovoCartao($"Saldo anterior a {desde:dd/MM/yyyy}", Valor(r.SaldoAnterior)));
        l.Add(LinhaImpressao.NovoCartao("Débitos", Valor(r.Debitos)));
        l.Add(LinhaImpressao.NovoCartao("Créditos", Valor(r.Creditos)));
        l.Add(LinhaImpressao.NovoCartao($"Saldo em {ate:dd/MM/yyyy}", Valor(r.SaldoFinal), alerta: r.SaldoFinal > 0));
        var movimentos = new List<LinhaImpressao> { LinhaImpressao.Tabela([Dia(desde.ToDateTime(TimeOnly.MinValue)), "", $"Saldo anterior a {desde:dd/MM/yyyy}", "", "", Valor(r.SaldoAnterior)], negrito: true) };
        movimentos.AddRange(r.Movimentos.Select(m => Lin(Dia(m.DataUtc), m.Numero, m.Descricao, m.Debito > 0 ? Valor(m.Debito) : "", m.Credito > 0 ? Valor(m.Credito) : "", Valor(m.Saldo))));
        Grelha(l, "Movimentos", r.Movimentos.Count == 0 ? "sem movimentos no período" : null, ["Data", "Documento", "Descrição", "Débito", "Crédito", "Saldo"], movimentos,
            subtotal: [$"Saldo em {ate:dd/MM/yyyy}", "", "", Valor(r.Debitos), Valor(r.Creditos), Valor(r.SaldoFinal)], texto: 3);
        if (r.Movimentos.Count == 0) l.Add(new("Sem movimentos no período."));
        l.Add(new($"SALDO EM {ate:dd/MM/yyyy}", Valor(r.SaldoFinal), Negrito: true, Grande: true));
        Grelha(l, "Situação hoje: facturas em aberto", Resumo(r.FacturasEmAberto.Count, "factura", r.EmDividaHoje) + (r.VencidoHoje > 0 ? $" · vencido {Valor(r.VencidoHoje)}" : ""),
            ["Factura", "Vencimento", "Em dívida"],
            r.FacturasEmAberto.Select(f => Lin(f.Vencida, f.Numero, f.Vencimento is DateOnly v ? $"{v:dd/MM/yyyy}{(f.Vencida ? " VENCIDA" : "")}" : "—", Valor(f.EmDivida))),
            subtotal: ["Total em dívida", "", Valor(r.EmDividaHoje)], texto: 2, vazio: "Nenhuma factura em aberto.");
        Rodape(l);
        return new DocumentoImpressao($"Extrato {r.ClienteNome} {Periodo(desde, ate)}", l, cabecalho);
    }

    private static void Cliente(List<LinhaImpressao> l, string nome, string? nuit, string? telefone)
    {
        var dados = new[] { nuit is null ? null : $"NUIT {nuit}", telefone is null ? null : $"Tel. {telefone}" }.Where(x => x is not null).ToList();
        l.Add(LinhaImpressao.NovaFaixa($"Cliente: {nome}", dados.Count > 0 ? string.Join(" · ", dados) : null));
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
        l.Add(LinhaImpressao.NovoCartao($"Cartões ({r.Cartoes})", Valor(r.Total)));
        l.Add(LinhaImpressao.NovoCartao("Devolvido em dinheiro", Valor(r.Devolvido)));
        l.Add(LinhaImpressao.NovoCartao("Só retirado do saldo", Valor(r.Retirado)));
        Grelha(l, "Descargas", null, ["Data", "Cartão", "Titular", "Destino", "Operador / motivo", "Saldo", "Acumulado"],
            r.Linhas.Select(d => Lin(Dia(d.DataUtc), d.Numero, d.Titular ?? "—", d.DevolveuDinheiro ? $"devolvido por {d.Metodo}" : "só retirado",
                string.Join(" · ", new[] { d.Operador, d.Motivo }.Where(x => x is not null)), Valor(d.Saldo), Valor(d.Acumulado))),
            texto: 5, vazio: "Sem descargas.");
        if (r.Cortado) l.Add(new($"(Lista limitada aos primeiros {Cartoes.ServicoRelatoriosCartoes.MaximoMovimentos} cartões.)"));
        Grelha(l, "Totais", null, ["Descrição", "Valor"],
            [Lin("Devolvido em dinheiro", Valor(r.Devolvido)), Lin("Só retirado do saldo", Valor(r.Retirado))]);
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
        // Título (A4: grande à esquerda com o período à direita; talões: centrados em duas linhas).
        return [new(titulo.ToUpperInvariant(), periodo, Alinhamento.Centro, Negrito: true, Titulo: true), LinhaImpressao.Traco with { SoTalao = true }];
    }

    private void Rodape(List<LinhaImpressao> l)
    {
        l.Add(LinhaImpressao.Traco);
        l.Add(new($"Emitido em {DataHora(DateTime.UtcNow)}{(actual.Nome is { } n ? $" por {n}" : "")}", Alinhamento: Alinhamento.Centro));
        l.Add(new("Documento interno", Alinhamento: Alinhamento.Centro));
    }

    /// <summary>
    /// Grelha: faixa com o título e o resumo no topo, títulos das colunas, linhas e (opcional) linha de subtotal.
    /// <paramref name="texto"/>: quantas colunas iniciais são texto. Sem linhas, só a faixa e a mensagem <paramref name="vazio"/>.
    /// Grelhas de 2 colunas («descrição → valor»): nos talões não levam a linha de títulos (só a faixa).
    /// </summary>
    private static void Grelha(List<LinhaImpressao> l, string titulo, string? resumo, string[] colunas, IEnumerable<LinhaImpressao> linhas,
        string[]? subtotal = null, int texto = 1, string vazio = "—")
    {
        l.Add(LinhaImpressao.NovaFaixa(titulo, resumo));
        var lista = linhas.ToList();
        if (lista.Count == 0)
        {
            l.Add(new(vazio));
            return;
        }
        l.Add(LinhaImpressao.Tabela(colunas, cabecalho: true, colunasTexto: texto) with { SoA4 = colunas.Length == 2 });
        l.AddRange(lista.Select(x => x with { ColunasTexto = texto }));
        if (subtotal is not null) l.Add(LinhaImpressao.Tabela(subtotal, subtotal: true, colunasTexto: texto));
    }

    private static LinhaImpressao Lin(params string[] celulas) => LinhaImpressao.Tabela(celulas);

    private static LinhaImpressao Lin(bool alerta, params string[] celulas) => LinhaImpressao.Tabela(celulas, alerta: alerta);

    /// <summary>Resumo da faixa: «3 artigos · 1 234,00 MT».</summary>
    private string Resumo(int quantidade, string nome, decimal? valor)
    {
        var plural = quantidade == 1 ? nome : nome.EndsWith('m') ? nome[..^1] + "ns" : nome + "s";
        return $"{quantidade} {plural}{(valor is decimal v ? $" · {Valor(v)}" : "")}";
    }

    /// <summary>Legenda dos alertas: em A4 as linhas a vermelho; nos talões o «(!)».</summary>
    private static void Legenda(List<LinhaImpressao> l, string significado)
    {
        l.Add(new($"Linhas a vermelho: {significado}.", SoA4: true));
        l.Add(new($"(!) {significado}.", SoTalao: true));
    }

    private static void Assinaturas(List<LinhaImpressao> l, params string[] quem)
    {
        foreach (var q in quem)
        {
            l.Add(new(""));
            l.Add(new($"{q}: {new string('_', Math.Max(10, 30 - q.Length))}"));
        }
    }

    private static string Inteiro(int n) => n.ToString(CultureInfo.InvariantCulture);

    private static string Inteiro(decimal n) => ComposicaoImpressao.Quantidade(n);

    private string Valor(decimal v) => ComposicaoImpressao.Valor(v, moeda);

    private string DataHora(DateTime utc) => TimeZoneInfo.ConvertTimeFromUtc(utc, fuso).ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);

    private string Hora(DateTime utc, bool comDia) =>
        TimeZoneInfo.ConvertTimeFromUtc(utc, fuso).ToString(comDia ? "dd/MM HH:mm" : "HH:mm", CultureInfo.InvariantCulture);

    private static string Periodo(DateOnly desde, DateOnly ate) =>
        desde == ate ? $"{desde:dd/MM/yyyy}" : $"{desde:dd/MM/yyyy} a {ate:dd/MM/yyyy}";
}
