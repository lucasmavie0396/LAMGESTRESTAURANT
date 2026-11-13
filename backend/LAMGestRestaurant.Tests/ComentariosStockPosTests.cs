using System.Net;
using LAMGestRestaurant.Api.Controllers;
using LAMGestRestaurant.Application.Impressao;
using LAMGestRestaurant.Application.Sistema;
using LAMGestRestaurant.Application.Stock;
using LAMGestRestaurant.Application.Vendas;
using LAMGestRestaurant.Domain.Comum;
using LAMGestRestaurant.Domain.Sistema;
using LAMGestRestaurant.Tests.Infraestrutura;
using static LAMGestRestaurant.Tests.Infraestrutura.ExtensoesHttp;

namespace LAMGestRestaurant.Tests;

/// <summary>
/// Comentários dos produtos e do pedido (depois do envio sai um talão de ALTERAÇÃO), sugestões configuráveis e
/// stock dos produtos para os botões do POS.
/// </summary>
[Collection(ColeccaoBaseDados.Nome)]
public class ComentariosStockPosTests(AplicacaoTeste app)
{
    private static Task Definir(HttpClient c, params (string Chave, string Valor)[] valores) =>
        c.Put("/api/configuracoes", new GuardarConfiguracoes(valores.ToDictionary(v => v.Chave, v => v.Valor))).Esperar(HttpStatusCode.OK);

    [Fact]
    public async Task Comentario_antes_e_depois_do_envio_e_comentario_do_pedido()
    {
        var c = await CenarioVenda.CriarAsync(app);
        var cozinha = await (await c.Gerente.Post("/api/configuracoes/impressoras",
            new GuardarImpressora(Unico("Cozinha "), FuncaoImpressora.Cozinha, ImpressorasTeste.Cozinha, 80, null, null)).Esperar(HttpStatusCode.OK)).Ler<ImpressoraDto>();
        try
        {
            var pedido = await c.AbrirMesaAsync(c.Gerente, c.Mesa1, (c.Prato, 1));
            var item = pedido.Itens.Single();

            // Antes do envio: só grava (sem talão).
            pedido = await (await c.Gerente.Post($"/api/pedidos/{pedido.Id}/itens/{item.Id}/comentario", new Comentario("  sem batata ", pedido.RowVersion))
                .Esperar(HttpStatusCode.OK)).Ler<PedidoDto>();
            Assert.Equal("sem batata", pedido.Itens.Single().Observacoes);
            await c.Gerente.Post($"/api/pedidos/{pedido.Id}/itens/{item.Id}/comentario", new Comentario("sem batata", pedido.RowVersion))
                .Esperar(HttpStatusCode.UnprocessableEntity); // não mudou
            pedido = await (await c.Gerente.Post($"/api/pedidos/{pedido.Id}/comentario", new Comentario("Cliente com pressa", pedido.RowVersion))
                .Esperar(HttpStatusCode.OK)).Ler<PedidoDto>();
            Assert.Equal("Cliente com pressa", pedido.Observacoes);

            pedido = await (await c.Gerente.Post($"/api/pedidos/{pedido.Id}/enviar-cozinha", new ComVersao(pedido.RowVersion)).Esperar(HttpStatusCode.OK)).Ler<PedidoDto>();
            var talao = Assert.Single(await app.Impressoras.EsperarAsync(pedido.Numero));
            Assert.Contains(">> sem batata", talao.Texto);
            Assert.Contains("Obs.: Cliente com pressa", talao.Texto);
            Assert.DoesNotContain(app.Impressoras.Com("ALTERAÇÃO"), t => t.Texto.Contains(pedido.Numero));

            // Depois do envio: talão de ALTERAÇÃO só com esse produto e o comentário novo.
            pedido = await (await c.Gerente.Post($"/api/pedidos/{pedido.Id}/itens/{item.Id}/comentario", new Comentario("sem batata, com arroz", pedido.RowVersion))
                .Esperar(HttpStatusCode.OK)).Ler<PedidoDto>();
            var alteracao = Assert.Single(await app.Impressoras.EsperarAsync("*** ALTERAÇÃO ***"), t => t.Texto.Contains(pedido.Numero));
            Assert.Equal(ImpressorasTeste.Cozinha, alteracao.Impressora);
            Assert.Contains("1 x Frango grelhado", alteracao.Texto);
            Assert.Contains(">> sem batata, com arroz", alteracao.Texto);
        }
        finally
        {
            await c.Gerente.Post($"/api/configuracoes/impressoras/{cozinha.Id}/activo", new { activo = false }).Esperar(HttpStatusCode.NoContent);
        }
    }

    [Fact]
    public async Task Stock_nos_botoes_e_sugestoes_de_comentarios_configuraveis()
    {
        var c = await CenarioVenda.CriarAsync(app);
        try
        {
            var publicas = await c.Gerente.Obter<ConfiguracoesPublicas>("/api/configuracoes/publicas");
            Assert.False(publicas.Pos!.MostrarStock);
            Assert.Contains("Sem cebola", publicas.Pos.Sugestoes!);

            await Definir(c.Gerente, (ChavesConfiguracao.PosMostrarStock, "true"), (ChavesConfiguracao.PosSugestoesComentario, "Sem batata\n\n Molho à parte \nSem batata"));
            publicas = await c.Gerente.Obter<ConfiguracoesPublicas>("/api/configuracoes/publicas");
            Assert.True(publicas.Pos!.MostrarStock);
            Assert.Equal(["Sem batata", "Molho à parte"], publicas.Pos.Sugestoes!);
            await c.Gerente.Put("/api/configuracoes", new GuardarConfiguracoes(new Dictionary<string, string>
            {
                [ChavesConfiguracao.PosSugestoesComentario] = new string('x', 61),
            })).Esperar(HttpStatusCode.UnprocessableEntity);

            // Stock da bebida (ficha técnica 1:1) no armazém principal, em unidades vendidas.
            var principal = (await c.Gerente.Obter<List<ArmazemDto>>("/api/stock/armazens")).Single(a => a.Principal).Id;
            var antes = (await c.Gerente.Obter<List<StockPos>>("/api/pos/stock")).Single(s => s.ProdutoId == c.Bebida.Id);
            Assert.Equal((0m, true), (antes.Quantidade, antes.Baixo));
            await c.Gerente.Post("/api/stock/ajustes", new AjustarStock(c.Bebida.ArtigoId!.Value, principal, TipoMovimentoStock.Ajuste, 12, "Stock inicial"))
                .Esperar(HttpStatusCode.OK);
            var depois = (await c.Gerente.Obter<List<StockPos>>("/api/pos/stock")).Single(s => s.ProdutoId == c.Bebida.Id);
            Assert.Equal((12m, false), (depois.Quantidade, depois.Baixo));
        }
        finally
        {
            await Definir(c.Gerente, (ChavesConfiguracao.PosMostrarStock, "false"),
                (ChavesConfiguracao.PosSugestoesComentario, "Sem cebola\nSem picante\nBem passado\nMal passado\nSem gelo\nPara levar"));
        }
    }
}
