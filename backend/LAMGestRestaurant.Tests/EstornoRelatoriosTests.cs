using System.Net;
using LAMGestRestaurant.Application.Caixa;
using LAMGestRestaurant.Application.Relatorios;
using LAMGestRestaurant.Application.Seguranca;
using LAMGestRestaurant.Application.Vendas;
using LAMGestRestaurant.Domain.Comum;
using LAMGestRestaurant.Domain.Stock;
using LAMGestRestaurant.Tests.Infraestrutura;
using Microsoft.EntityFrameworkCore;

namespace LAMGestRestaurant.Tests;

[Collection(ColeccaoBaseDados.Nome)]
public class EstornoRelatoriosTests(AplicacaoTeste app)
{
    private static async Task<PedidoDto> VenderAsync(CenarioVenda c, string metodo, params (Application.Catalogo.ProdutoDto p, decimal q)[] itens) =>
        await VenderAsync(c, c.Mesa1, metodo, itens);

    private static async Task<PedidoDto> VenderAsync(CenarioVenda c, Application.Sala.MesaDto mesa, string metodo, params (Application.Catalogo.ProdutoDto p, decimal q)[] itens)
    {
        var pedido = await c.AbrirMesaAsync(c.Gerente, mesa, itens);
        var referencia = metodo == "DINHEIRO" ? null : "REF-TESTE";
        return await (await c.PagarAsync(c.Gerente, pedido.Id, metodo, pedido.Total, referencia: referencia).Esperar(HttpStatusCode.Created)).Ler<PedidoDto>();
    }

    private static EstornarPedido Estorno(CenarioVenda c, PedidoDto p, string metodo = "DINHEIRO") =>
        new(c.Sessao.Id, c.Metodos[metodo], "Prato devolvido pelo cliente", p.RowVersion);

    [Fact]
    public async Task Estorno_anula_recibo_repoe_stock_e_devolve_pela_caixa()
    {
        var c = await CenarioVenda.CriarAsync(app);
        int artigoId;
        await using (var db = app.CriarContexto())
        {
            var kg = await db.UnidadesMedida.SingleAsync(u => u.Codigo == "kg");
            var artigo = new Artigo { Codigo = ExtensoesHttp.Unico("ART", 12), Nome = "Frango", Tipo = TipoArtigo.Ingrediente, UnidadeMedidaId = kg.Id, CustoMedio = 200m };
            db.Artigos.Add(artigo);
            await db.SaveChangesAsync();
            db.StockSaldos.Add(new StockSaldo { ArtigoId = artigo.Id, ArmazemId = (await db.Armazens.SingleAsync(a => a.Principal)).Id, Quantidade = 2m });
            db.Receitas.Add(new Receita { ProdutoId = c.Prato.Id, Versao = await FichaTecnicaTeste.ProximaVersaoAsync(db, c.Prato.Id), Activa = true, ValidaDesdeUtc = DateTime.UtcNow,
                Itens = [new ReceitaItem { ArtigoId = artigo.Id, Quantidade = 0.5m, UnidadeMedidaId = kg.Id }] });
            await db.SaveChangesAsync();
            artigoId = artigo.Id;
        }

        var pago = await VenderAsync(c, "DINHEIRO", (c.Prato, 1));
        var estornado = await (await c.Gerente.Post($"/api/pedidos/{pago.Id}/estornar", Estorno(c, pago)).Esperar(HttpStatusCode.OK)).Ler<PedidoDto>();

        Assert.Equal(EstadoPedido.Estornado, estornado.Estado);
        Assert.Null(estornado.ReciboNumero); // recibo anulado
        await using var dados = app.CriarContexto();
        Assert.Equal(2m, (await dados.StockSaldos.SingleAsync(s => s.ArtigoId == artigoId)).Quantidade);
        Assert.Equal(1, await dados.MovimentosStock.CountAsync(m => m.ArtigoId == artigoId && m.Tipo == TipoMovimentoStock.EstornoVenda));
        Assert.True((await dados.Recibos.SingleAsync(r => r.PedidoId == pago.Id)).Anulado);

        // Caixa: o dinheiro recebido saiu como reembolso → volta ao fundo inicial.
        var sessao = await c.Gerente.Obter<SessaoCaixaDto>($"/api/caixa/sessoes/{c.Sessao.Id}");
        Assert.Equal(1000m, await app.EsperadoAsync(c.Sessao.Id, c.Metodos["DINHEIRO"]));
        Assert.Contains(sessao.Movimentos, m => m.Tipo == TipoMovimentoCaixa.Reembolso && m.Valor == 450.50m);

        // Não se estorna duas vezes.
        await c.Gerente.Post($"/api/pedidos/{pago.Id}/estornar", Estorno(c, estornado)).Esperar(HttpStatusCode.UnprocessableEntity);
        Assert.Contains(dados.RegistosAuditoria, a => a.Accao == "pedido.estornar" && a.EntidadeId == pago.Id.ToString());
    }

    [Fact]
    public async Task Pedido_pago_ou_estornado_nao_volta_ao_ecra_da_cozinha()
    {
        var c = await CenarioVenda.CriarAsync(app);
        var pedido = await c.AbrirMesaAsync(c.Gerente, c.Mesa1, (c.Prato, 1));
        pedido = await (await c.Gerente.Post($"/api/pedidos/{pedido.Id}/enviar-cozinha", new ComVersao(pedido.RowVersion)).Esperar(HttpStatusCode.OK)).Ler<PedidoDto>();
        pedido = await (await c.PagarAsync(c.Gerente, pedido.Id, "DINHEIRO", pedido.Total).Esperar(HttpStatusCode.Created)).Ler<PedidoDto>();
        var (cozinha, _) = await app.ClienteAutenticadoAsync(PerfisPadrao.Cozinha); // conta paga: já saiu do ecrã
        Assert.DoesNotContain(await cozinha.Obter<List<Application.Cozinha.TicketCozinha>>("/api/cozinha"), t => t.PedidoId == pedido.Id);

        await c.Gerente.Post($"/api/pedidos/{pedido.Id}/estornar", Estorno(c, pedido)).Esperar(HttpStatusCode.OK);

        Assert.DoesNotContain(await cozinha.Obter<List<Application.Cozinha.TicketCozinha>>("/api/cozinha"), t => t.PedidoId == pedido.Id);
    }

    [Fact]
    public async Task Estorno_exige_permissao_pedido_pago_e_dinheiro_suficiente()
    {
        var c = await CenarioVenda.CriarAsync(app);
        var aberto = await c.AbrirMesaAsync(c.Gerente, c.Mesa1, (c.Prato, 1));
        await c.Gerente.Post($"/api/pedidos/{aberto.Id}/estornar", Estorno(c, aberto)).Esperar(HttpStatusCode.UnprocessableEntity);

        var pago = await VenderAsync(c, c.Mesa2, "MPESA", (c.Prato, 3)); // 1351,50 por M-Pesa (a mesa 1 tem um pedido aberto)
        var (caixa, _) = await app.ClienteAutenticadoAsync(PerfisPadrao.Caixa);
        await caixa.Post($"/api/pedidos/{pago.Id}/estornar", Estorno(c, pago)).Esperar(HttpStatusCode.Forbidden);

        // Reembolso em dinheiro superior ao que está na gaveta (fundo 1000).
        await c.Gerente.Post($"/api/pedidos/{pago.Id}/estornar", Estorno(c, pago)).Esperar(HttpStatusCode.UnprocessableEntity);
        // Pelo mesmo método do pagamento é possível.
        await c.Gerente.Post($"/api/pedidos/{pago.Id}/estornar", Estorno(c, pago, "MPESA")).Esperar(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Relatorio_de_vendas_conta_so_pedidos_pagos_e_separa_cancelamentos_e_estornos()
    {
        var c = await CenarioVenda.CriarAsync(app);
        var hoje = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("Africa/Maputo")));
        var url = $"/api/relatorios/vendas?desde={hoje:yyyy-MM-dd}&ate={hoje:yyyy-MM-dd}";
        var antes = await c.Gerente.Obter<RelatorioVendas>(url);

        await VenderAsync(c, "DINHEIRO", (c.Prato, 2), (c.Bebida, 1)); // 961
        var estornar = await VenderAsync(c, "DINHEIRO", (c.Bebida, 1)); // 60, depois estornado
        await c.Gerente.Post($"/api/pedidos/{estornar.Id}/estornar", Estorno(c, estornar)).Esperar(HttpStatusCode.OK);
        var cancelar = await c.AbrirMesaAsync(c.Gerente, c.Mesa2, (c.Prato, 1));
        await c.Gerente.Post($"/api/pedidos/{cancelar.Id}/cancelar", new ComMotivo("Cliente saiu", cancelar.RowVersion)).Esperar(HttpStatusCode.OK);

        var depois = await c.Gerente.Obter<RelatorioVendas>(url);

        Assert.Equal(961m, depois.TotalVendas - antes.TotalVendas);
        Assert.Equal(1, depois.NumeroPedidos - antes.NumeroPedidos);
        Assert.Equal(1, depois.Cancelamentos.PedidosCancelados - antes.Cancelamentos.PedidosCancelados);
        Assert.Equal(450.50m, depois.Cancelamentos.ValorPedidosCancelados - antes.Cancelamentos.ValorPedidosCancelados);
        Assert.Equal(1, depois.Cancelamentos.PedidosEstornados - antes.Cancelamentos.PedidosEstornados);
        Assert.Equal(60m, depois.Cancelamentos.ValorEstornado - antes.Cancelamentos.ValorEstornado);

        var prato = depois.Produtos.Single(p => p.ProdutoId == c.Prato.Id);
        Assert.Equal((2m, 901m, 360m), (prato.Quantidade, prato.Total, prato.CustoEstimado)); // custo de referência 180 × 2
        var bebida = depois.Produtos.Single(p => p.ProdutoId == c.Bebida.Id);
        Assert.Equal(1m, bebida.Quantidade); // a bebida estornada não conta
        Assert.Equal(depois.TotalVendas, depois.PorMetodo.Sum(m => m.Valor));
        Assert.Equal(depois.TotalVendas, depois.PorDia.Sum(d => d.Total));
        Assert.Single(depois.PorDia);
    }

    [Fact]
    public async Task Relatorio_valida_o_periodo_e_exige_permissao()
    {
        var c = await CenarioVenda.CriarAsync(app);
        await c.Gerente.GetAsync("/api/relatorios/vendas?desde=2026-02-10&ate=2026-02-01")
            .ContinueWith(t => Assert.Equal(HttpStatusCode.UnprocessableEntity, t.Result.StatusCode));
        var (garcom, _) = await app.ClienteAutenticadoAsync(PerfisPadrao.Garcom);
        Assert.Equal(HttpStatusCode.Forbidden, (await garcom.GetAsync("/api/relatorios/vendas?desde=2026-02-01&ate=2026-02-10")).StatusCode);
    }

    [Fact]
    public async Task Dashboard_mostra_seccoes_conforme_as_permissoes()
    {
        var c = await CenarioVenda.CriarAsync(app);
        var gerente = await c.Gerente.Obter<Dashboard>("/api/dashboard");
        Assert.NotNull(gerente.Caixas);
        Assert.Contains(gerente.Caixas!, s => s.SessaoId == c.Sessao.Id);
        Assert.NotNull(gerente.StockBaixo);
        Assert.Equal(7, gerente.Ultimos7Dias.Count);

        var (caixa, _) = await app.ClienteAutenticadoAsync(PerfisPadrao.Caixa);
        var doCaixa = await caixa.Obter<Dashboard>("/api/dashboard");
        Assert.Null(doCaixa.StockBaixo);
        Assert.DoesNotContain(doCaixa.Caixas!, s => s.SessaoId == c.Sessao.Id); // só vê a própria sessão

        var (cozinha, _) = await app.ClienteAutenticadoAsync(PerfisPadrao.Cozinha);
        Assert.Equal(HttpStatusCode.Forbidden, (await cozinha.GetAsync("/api/dashboard")).StatusCode);
    }

    [Fact]
    public async Task Ecra_de_delivery_lista_so_entregas_activas()
    {
        var c = await CenarioVenda.CriarAsync(app);
        var r = await c.Gerente.PostComChave("/api/pedidos", CenarioVenda.Chave(),
            new NovoPedido(TipoPedido.Delivery, null, null, null, 1, "840000001", "Rua 1", null, false, [new NovoItem(c.Prato.Id, 1, null)], "Ana Delivery")).Esperar(HttpStatusCode.Created);
        var pedido = await r.Ler<PedidoDto>();
        await c.AbrirMesaAsync(c.Gerente, c.Mesa1, (c.Prato, 1)); // pedido de mesa não aparece

        var activos = await c.Gerente.Obter<Application.Comum.Pagina<PedidoResumo>>("/api/pedidos?entregaActiva=true&tamanho=200");
        Assert.Contains(activos.Itens, p => p.Id == pedido.Id);
        Assert.DoesNotContain(activos.Itens, p => p.Tipo == TipoPedido.Mesa);

        // Recebido → Pronto → Saiu → Entregue; depois sai da lista.
        foreach (var estado in new[] { EstadoEntrega.Pronto, EstadoEntrega.SaiuParaEntrega, EstadoEntrega.Entregue })
            pedido = await (await c.Gerente.Post($"/api/pedidos/{pedido.Id}/entrega", new ActualizarEntrega(estado, null, pedido.RowVersion)).Esperar(HttpStatusCode.OK)).Ler<PedidoDto>();
        activos = await c.Gerente.Obter<Application.Comum.Pagina<PedidoResumo>>("/api/pedidos?entregaActiva=true&tamanho=200");
        Assert.DoesNotContain(activos.Itens, p => p.Id == pedido.Id);
        // Transição inválida (já entregue).
        await c.Gerente.Post($"/api/pedidos/{pedido.Id}/entrega", new ActualizarEntrega(EstadoEntrega.Pronto, null, pedido.RowVersion)).Esperar(HttpStatusCode.UnprocessableEntity);
    }
}
