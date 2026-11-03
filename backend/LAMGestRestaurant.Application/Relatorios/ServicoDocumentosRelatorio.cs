using System.Globalization;
using LAMGestRestaurant.Application.Caixa;
using LAMGestRestaurant.Application.Comum;
using LAMGestRestaurant.Application.Impressao;
using LAMGestRestaurant.Application.Seguranca;
using LAMGestRestaurant.Domain.Comum;
using LAMGestRestaurant.Domain.Sistema;
using Microsoft.EntityFrameworkCore;

namespace LAMGestRestaurant.Application.Relatorios;

public enum TipoDocumentoRelatorio { Pagamentos, Vendas, Produtos, Despesas, Financeiro, Stock, FechoCaixa, ListaVendas, Clientes, Impostos, Anulacoes }

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
    ServicoRelatoriosDetalhe detalhe)
{
    private string moeda = "MT";
    private TimeZoneInfo fuso = TimeZoneInfo.Utc;

    public static string Permissao(TipoDocumentoRelatorio tipo) => tipo switch
    {
        TipoDocumentoRelatorio.Pagamentos or TipoDocumentoRelatorio.Vendas or TipoDocumentoRelatorio.Produtos or TipoDocumentoRelatorio.ListaVendas
            or TipoDocumentoRelatorio.Clientes or TipoDocumentoRelatorio.Impostos or TipoDocumentoRelatorio.Anulacoes => Permissoes.RelatoriosVendas,
        TipoDocumentoRelatorio.Despesas or TipoDocumentoRelatorio.Financeiro => Permissoes.RelatoriosFinanceiros,
        TipoDocumentoRelatorio.Stock => Permissoes.RelatoriosStock,
        _ => Permissoes.CaixaOperar,
    };

    public async Task<DocumentoImpressao> GerarAsync(TipoDocumentoRelatorio tipo, DateOnly desde, DateOnly ate, int? sessaoId, CancellationToken ct = default)
    {
        var autorizado = tipo == TipoDocumentoRelatorio.FechoCaixa
            ? actual.TemPermissao(Permissoes.CaixaOperar) || actual.TemPermissao(Permissoes.CaixaVerTodas)
            : actual.TemPermissao(Permissao(tipo));
        if (!autorizado)
            throw new AcessoNegadoException("Não tem permissão para este relatório.");
        if (tipo != TipoDocumentoRelatorio.FechoCaixa)
            ServicoRelatorios.ValidarPeriodo(desde, ate);
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
            _ => await FechoCaixaAsync(sessaoId ?? throw new RegraNegocioException("Indique a sessão de caixa."), ct),
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
        return new DocumentoImpressao("Pagamentos " + Periodo(desde, ate), l, UsoLogotipo.SoA4);
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
        return new DocumentoImpressao("Vendas " + Periodo(desde, ate), l, UsoLogotipo.SoA4);
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
        return new DocumentoImpressao("Produtos vendidos " + Periodo(desde, ate), l, UsoLogotipo.SoA4);
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
        return new DocumentoImpressao("Despesas " + Periodo(desde, ate), l, UsoLogotipo.SoA4);
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
        return new DocumentoImpressao("Financeiro " + Periodo(desde, ate), l, UsoLogotipo.SoA4);
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
        return new DocumentoImpressao($"Stock {hoje:dd-MM-yyyy}", l, UsoLogotipo.SoA4);
    }

    private async Task<DocumentoImpressao> ListaVendasAsync(DateOnly desde, DateOnly ate, CancellationToken ct)
    {
        var r = await detalhe.ListaVendasAsync(new FiltroListaVendas(desde, ate, Todas: true), ct);
        var variosDias = desde != ate;
        var l = await CabecalhoAsync("Lista de vendas", Periodo(desde, ate));
        if (r.Totais.Vendas == 0) l.Add(new("Sem vendas no período."));
        foreach (var v in r.Vendas.Itens)
        {
            var onde = v.Tipo switch { TipoPedido.Mesa => $"Mesa {v.Mesa}", TipoPedido.TakeAway => "Take Away", _ => "Delivery" };
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
        return new DocumentoImpressao("Lista de vendas " + Periodo(desde, ate), l, UsoLogotipo.SoA4);
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
        return new DocumentoImpressao("Clientes " + Periodo(desde, ate), l, UsoLogotipo.SoA4);
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
        return new DocumentoImpressao("IVA " + Periodo(desde, ate), l, UsoLogotipo.SoA4);
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
        return new DocumentoImpressao("Anulações " + Periodo(desde, ate), l, UsoLogotipo.SoA4);
    }

    /// <summary>Fecho (ou ponto de situação, se ainda aberta) de uma sessão de caixa, com espaço para assinaturas.</summary>
    private async Task<DocumentoImpressao> FechoCaixaAsync(int sessaoId, CancellationToken ct)
    {
        var s = await caixa.ObterAsync(sessaoId); // aplica a regra: o próprio operador ou caixa.ver-todas
        var fechada = s.Estado == EstadoSessaoCaixa.Fechada;
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
            l.Add(new("AINDA ABERTA — valores parciais", Negrito: true));
        }
        l.Add(new("Fundo inicial", Valor(s.FundoInicial)));
        l.Add(new($"Pedidos pagos ({s.PedidosPagos})", Valor(s.TotalRecebido)));
        // Só métodos com movimento ou com algo contado (o fecho regista 0 nos restantes; não interessa no papel).
        foreach (var m in s.PorMetodo.Where(m => m.Fundo != 0 || m.Recebimentos != 0 || m.Entradas != 0 || m.Saidas != 0 || m.Contado is > 0 || m.Diferenca is not (null or 0)))
        {
            l.Add(LinhaImpressao.Traco);
            l.Add(new(m.Metodo, Negrito: true));
            if (m.Fundo != 0) l.Add(new("  Fundo", Valor(m.Fundo)));
            if (m.Recebimentos != 0) l.Add(new("  Recebido", Valor(m.Recebimentos)));
            if (m.Entradas != 0) l.Add(new("  Entradas", Valor(m.Entradas)));
            if (m.Saidas != 0) l.Add(new("  Saídas", "-" + Valor(m.Saidas)));
            l.Add(new("  Esperado", Valor(m.Esperado), Negrito: true));
            if (m.Contado is decimal contado)
            {
                l.Add(new("  Contado", Valor(contado)));
                l.Add(new("  Diferença", (m.Diferenca > 0 ? "+" : "") + Valor(m.Diferenca ?? 0), Negrito: m.Diferenca != 0));
            }
        }
        if (s.Movimentos.Count > 0)
        {
            l.Add(LinhaImpressao.Traco);
            l.Add(new("Movimentos manuais", Negrito: true));
            foreach (var mv in s.Movimentos)
                l.Add(new($"{Hora(mv.DataUtc, false)} {mv.Tipo} · {mv.Motivo}", (mv.Tipo == TipoMovimentoCaixa.Entrada ? "" : "-") + Valor(mv.Valor)));
        }
        l.Add(LinhaImpressao.Traco);
        l.Add(new("TOTAL ESPERADO", Valor(s.TotalEsperado), Negrito: true, Grande: true));
        if (s.TotalContado is decimal total)
        {
            l.Add(new("Total contado", Valor(total)));
            l.Add(new("DIFERENÇA", (s.TotalDiferenca > 0 ? "+" : "") + Valor(s.TotalDiferenca ?? 0), Negrito: true));
        }
        if (!string.IsNullOrWhiteSpace(s.ObservacoesFecho)) l.Add(new($"Obs.: {s.ObservacoesFecho}"));
        l.Add(new(""));
        l.Add(new("Operador: ____________________"));
        l.Add(new(""));
        l.Add(new("Responsável: _________________"));
        Rodape(l);
        return new DocumentoImpressao($"Caixa {s.Id}", l, UsoLogotipo.SoA4);
    }

    // ---------------- Auxiliares ----------------

    private async Task<List<LinhaImpressao>> CabecalhoAsync(string titulo, string periodo)
    {
        var nome = await config.TextoAsync(ChavesConfiguracao.NomeEstabelecimento, "LAMGest Restaurant");
        var nuit = await config.TextoAsync(ChavesConfiguracao.Nuit);
        var l = new List<LinhaImpressao> { new(string.IsNullOrWhiteSpace(nome) ? "LAMGest Restaurant" : nome, Alinhamento: Alinhamento.Centro, Negrito: true, Grande: true) };
        if (!string.IsNullOrWhiteSpace(nuit)) l.Add(new($"NUIT {nuit}", Alinhamento: Alinhamento.Centro));
        l.Add(LinhaImpressao.Traco);
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
