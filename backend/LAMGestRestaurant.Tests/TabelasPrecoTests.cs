using System.Net;
using LAMGestRestaurant.Api.Controllers;
using LAMGestRestaurant.Application.Catalogo;
using LAMGestRestaurant.Application.Sala;
using LAMGestRestaurant.Application.Seguranca;
using LAMGestRestaurant.Application.Vendas;
using LAMGestRestaurant.Domain.Comum;
using LAMGestRestaurant.Domain.Sistema;
using LAMGestRestaurant.Tests.Infraestrutura;
using static LAMGestRestaurant.Tests.Infraestrutura.ExtensoesHttp;

namespace LAMGestRestaurant.Tests;

/// <summary>
/// Tabelas de preço: preço por zona (a mesa vende com a tabela da sua zona; ao transferir, os itens lançados mantêm o preço) e
/// «Retalho» no balcão (recalcula o carrinho, só sem pagamentos e com a permissão pos.tabela-precos).
/// </summary>
[Collection(ColeccaoBaseDados.Nome)]
public class TabelasPrecoTests(AplicacaoTeste app)
{
    private static async Task<TabelaPrecoDto> TabelaAsync(HttpClient c, string nome) =>
        await (await c.Post("/api/tabelas-preco", new GuardarTabelaPreco(Unico(nome), null)).Esperar(HttpStatusCode.OK)).Ler<TabelaPrecoDto>();

    /// <summary>Produto com 100 de preço normal e os preços indicados por tabela.</summary>
    private static async Task<ProdutoDto> ProdutoAsync(CenarioVenda c, params PrecoTabelaProduto[] precos) =>
        await (await c.Gerente.Post("/api/produtos", new GuardarProduto(Unico("TP", 10), null, Unico("Cerveja ", 14), null, c.Prato.CategoriaId, TipoProduto.Bar,
            100m, 40m, null, null, false, true, null, Precos: precos)).Esperar(HttpStatusCode.Created)).Ler<ProdutoDto>();

    private static Task Definir(HttpClient c, string chave, string valor) =>
        c.Put("/api/configuracoes", new GuardarConfiguracoes(new Dictionary<string, string> { [chave] = valor })).Esperar(HttpStatusCode.OK);

    [Fact]
    public async Task A_mesa_vende_com_a_tabela_da_sua_zona_e_os_itens_lancados_mantem_o_preco_ao_transferir()
    {
        var c = await CenarioVenda.CriarAsync(app);
        var esplanada = await TabelaAsync(c.Gerente, "Esplanada ");
        var produto = await ProdutoAsync(c, new PrecoTabelaProduto(esplanada.Id, 130m));
        Assert.Equal(130m, Assert.Single(produto.Precos!).Preco);

        var zonaEsplanada = await (await c.Gerente.Post("/api/zonas", new GuardarZona(Unico("Esplanada "), 5, esplanada.Id)).Esperar(HttpStatusCode.Created)).Ler<ZonaDto>();
        Assert.Equal(esplanada.Nome, zonaEsplanada.TabelaPreco);
        var mesaEsplanada = await (await c.Gerente.Post("/api/mesas", new GuardarMesa(Unico("E", 8), zonaEsplanada.Id, 4, null)).Esperar(HttpStatusCode.Created)).Ler<MesaDto>();

        // Na esplanada: 130 (e o produto sem preço na tabela vende ao normal).
        var pedido = await c.AbrirMesaAsync(c.Gerente, mesaEsplanada, (produto, 1), (c.Bebida, 1));
        Assert.Equal((esplanada.Id, 130m, 60m), (pedido.TabelaPrecoId, pedido.Itens.Single(i => i.ProdutoId == produto.Id).PrecoUnitario,
            pedido.Itens.Single(i => i.ProdutoId == c.Bebida.Id).PrecoUnitario));

        // Para a sala normal: o item já lançado fica a 130; o novo é ao preço normal.
        pedido = await (await c.Gerente.Post($"/api/pedidos/{pedido.Id}/transferir", new TransferirMesa(c.Mesa2.Id, "Mudou de mesa", pedido.RowVersion))
            .Esperar(HttpStatusCode.OK)).Ler<PedidoDto>();
        Assert.Null(pedido.TabelaPrecoId);
        pedido = await (await c.Gerente.Post($"/api/pedidos/{pedido.Id}/itens", new AdicionarItens([new NovoItem(produto.Id, 1, null)], pedido.RowVersion))
            .Esperar(HttpStatusCode.OK)).Ler<PedidoDto>();
        Assert.Equal([100m, 130m], pedido.Itens.Where(i => i.ProdutoId == produto.Id).Select(i => i.PrecoUnitario).Order());

        // Tabela usada numa zona não se desactiva.
        await c.Gerente.Post($"/api/tabelas-preco/{esplanada.Id}/activo", new AlterarActivo(false, null)).Esperar(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Balcao_muda_para_retalho_e_volta_ao_normal_so_sem_pagamentos_e_com_permissao()
    {
        var c = await CenarioVenda.CriarAsync(app);
        var retalho = await TabelaAsync(c.Gerente, "Retalho ");
        var produto = await ProdutoAsync(c, new PrecoTabelaProduto(retalho.Id, 80m));
        await Definir(c.Gerente, ChavesConfiguracao.PosTabelaRetalho, retalho.Id.ToString());
        try
        {
            // Criada já em retalho.
            var direto = await (await c.Gerente.PostComChave("/api/pedidos", CenarioVenda.Chave(), new NovoPedido(TipoPedido.Balcao, null, null, null, 0, null, null, null, false,
                [new NovoItem(produto.Id, 2, null)], Retalho: true)).Esperar(HttpStatusCode.Created)).Ler<PedidoDto>();
            Assert.Equal((retalho.Id, 160m), (direto.TabelaPrecoId, direto.Total));

            // Normal → retalho → normal, recalculando o carrinho.
            var p = await (await c.Gerente.PostComChave("/api/pedidos", CenarioVenda.Chave(), new NovoPedido(TipoPedido.Balcao, null, null, null, 0, null, null, null, false,
                [new NovoItem(produto.Id, 2, null), new NovoItem(c.Bebida.Id, 1, null)])).Esperar(HttpStatusCode.Created)).Ler<PedidoDto>();
            Assert.Equal(260m, p.Total);
            p = await (await c.Gerente.Post($"/api/pedidos/{p.Id}/tabela-precos", new MudarTabelaPreco(true, p.RowVersion)).Esperar(HttpStatusCode.OK)).Ler<PedidoDto>();
            Assert.Equal((retalho.Id, 220m), (p.TabelaPrecoId, p.Total));
            p = await (await c.Gerente.Post($"/api/pedidos/{p.Id}/tabela-precos", new MudarTabelaPreco(false, p.RowVersion)).Esperar(HttpStatusCode.OK)).Ler<PedidoDto>();
            Assert.Equal(((int?)null, 260m), (p.TabelaPrecoId, p.Total));

            // Com um pagamento já não muda.
            await c.PagarAsync(c.Gerente, p.Id, "DINHEIRO", 10m).Esperar(HttpStatusCode.Created);
            p = await c.Gerente.Obter<PedidoDto>($"/api/pedidos/{p.Id}");
            await c.Gerente.Post($"/api/pedidos/{p.Id}/tabela-precos", new MudarTabelaPreco(true, p.RowVersion)).Esperar(HttpStatusCode.UnprocessableEntity);

            // Mesa não usa o retalho; o garçom (sem a permissão) não o liga.
            var mesa = await c.AbrirMesaAsync(c.Gerente, c.Mesa1, (produto, 1));
            await c.Gerente.Post($"/api/pedidos/{mesa.Id}/tabela-precos", new MudarTabelaPreco(true, mesa.RowVersion)).Esperar(HttpStatusCode.UnprocessableEntity);
            var (garcom, _) = await app.ClienteAutenticadoAsync(PerfisPadrao.Garcom);
            Assert.Equal(HttpStatusCode.Forbidden, (await garcom.Post($"/api/pedidos/{direto.Id}/tabela-precos", new MudarTabelaPreco(false, direto.RowVersion))).StatusCode);
        }
        finally
        {
            await Definir(c.Gerente, ChavesConfiguracao.PosTabelaRetalho, "");
        }
    }
}
