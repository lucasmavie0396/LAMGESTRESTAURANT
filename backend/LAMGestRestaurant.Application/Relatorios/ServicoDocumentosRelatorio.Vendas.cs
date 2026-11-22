using System.Globalization;
using LAMGestRestaurant.Application.Caixa;
using LAMGestRestaurant.Application.Comum;
using LAMGestRestaurant.Application.Impressao;
using LAMGestRestaurant.Application.Seguranca;
using LAMGestRestaurant.Domain.Comum;
using LAMGestRestaurant.Domain.Sistema;
using Microsoft.EntityFrameworkCore;

namespace LAMGestRestaurant.Application.Relatorios;

public partial class ServicoDocumentosRelatorio
{
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
}
