using System.Net;
using LAMGestRestaurant.Application.Catalogo;
using LAMGestRestaurant.Application.Comum;
using LAMGestRestaurant.Application.Stock;
using LAMGestRestaurant.Domain.Catalogo;
using LAMGestRestaurant.Domain.Comum;
using LAMGestRestaurant.Infrastructure.Dados;
using LAMGestRestaurant.Tests.Infraestrutura;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static LAMGestRestaurant.Tests.Infraestrutura.ExtensoesHttp;

namespace LAMGestRestaurant.Tests;

/// <summary>Todos os produtos controlam stock por omissão: têm artigo (ficha 1:1) e aparecem no Stock e nos inventários.</summary>
[Collection(ColeccaoBaseDados.Nome)]
public class StockProdutosTests(AplicacaoTeste app)
{
    private static GuardarProduto Produto(int categoriaId, string nome, bool controlaStock = true, byte[]? versao = null, string? codigo = null) =>
        new(codigo ?? Unico("S", 10), null, nome, null, categoriaId, TipoProduto.Bar, 80m, 30m, null, null, false, true, versao, ControlaStock: controlaStock);

    private static async Task<decimal> SaldoAsync(AplicacaoTeste app, int artigoId)
    {
        await using var db = app.CriarContexto();
        return await db.StockSaldos.Where(s => s.ArtigoId == artigoId).SumAsync(s => s.Quantidade);
    }

    [Fact]
    public async Task Produto_novo_tem_artigo_no_stock_e_a_venda_baixa_sem_bloquear_mesmo_sem_stock_inicial()
    {
        var c = await CenarioVenda.CriarAsync(app);
        // Os produtos do cenário (criados sem stock inicial) já têm artigo.
        Assert.NotNull(c.Bebida.ArtigoId);
        var artigos = await c.Gerente.Obter<Pagina<ArtigoDto>>($"/api/stock/artigos?pesquisa={c.Bebida.Codigo}");
        var artigo = Assert.Single(artigos.Itens);
        Assert.Equal((c.Bebida.Nome, TipoArtigo.Revenda, true), (artigo.Nome, artigo.Tipo, artigo.PermiteStockNegativo));

        // Sem stock lançado a venda não é bloqueada: o saldo fica negativo até haver uma entrada/inventário.
        var pedido = await c.AbrirMesaAsync(c.Gerente, c.Mesa1, (c.Bebida, 2));
        await c.PagarAsync(c.Gerente, pedido.Id, "DINHEIRO", pedido.Total).Esperar(HttpStatusCode.Created);
        Assert.Equal(-2m, await SaldoAsync(app, artigo.Id));
    }

    [Fact]
    public async Task Produto_que_nao_controla_stock_nao_tem_artigo_e_activar_depois_cria_o_artigo()
    {
        var c = await CenarioVenda.CriarAsync(app);
        var categoria = c.Bebida.CategoriaId;
        var servico = await (await c.Gerente.Post("/api/produtos", Produto(categoria, "Taxa de rolha", controlaStock: false)).Esperar(HttpStatusCode.Created)).Ler<ProdutoDto>();
        Assert.Equal((false, (int?)null), (servico.ControlaStock, servico.ArtigoId));
        await c.Gerente.Post("/api/produtos", Produto(categoria, "X", controlaStock: false) with { StockInicial = new StockInicialProduto(1, 1, 0, null) })
            .Esperar(HttpStatusCode.UnprocessableEntity);

        var pedido = await c.AbrirMesaAsync(c.Gerente, c.Mesa1, (servico, 1));
        await c.PagarAsync(c.Gerente, pedido.Id, "DINHEIRO", pedido.Total).Esperar(HttpStatusCode.Created);
        await using (var db = app.CriarContexto())
            Assert.False(await db.Receitas.AnyAsync(r => r.ProdutoId == servico.Id));

        // Passar a controlar stock: o artigo aparece; renomear o produto renomeia o artigo 1:1.
        var activado = await (await c.Gerente.Put($"/api/produtos/{servico.Id}", Produto(categoria, "Rolha", versao: servico.RowVersion, codigo: servico.Codigo))
            .Esperar(HttpStatusCode.OK)).Ler<ProdutoDto>();
        Assert.True(activado.ControlaStock);
        var artigo = await c.Gerente.Obter<ArtigoDto>($"/api/stock/artigos/{activado.ArtigoId}");
        Assert.Equal(("Rolha", servico.Codigo), (artigo.Nome, artigo.Codigo));

        // Desligar de novo: a venda deixa de mexer no stock (o artigo e o histórico ficam).
        var desligado = await (await c.Gerente.Put($"/api/produtos/{servico.Id}", Produto(categoria, "Rolha", controlaStock: false, versao: activado.RowVersion, codigo: servico.Codigo))
            .Esperar(HttpStatusCode.OK)).Ler<ProdutoDto>();
        var outro = await c.AbrirMesaAsync(c.Gerente, c.Mesa2, (desligado, 3));
        await c.PagarAsync(c.Gerente, outro.Id, "DINHEIRO", outro.Total).Esperar(HttpStatusCode.Created);
        Assert.Equal(0m, await SaldoAsync(app, artigo.Id));
    }

    [Fact]
    public async Task Produtos_antigos_sem_artigo_sao_activados_no_arranque_e_a_sincronizacao_e_idempotente()
    {
        var c = await CenarioVenda.CriarAsync(app);
        int antigoId;
        await using (var db = app.CriarContexto())
        {
            // Produto criado antes desta regra: sem ficha técnica nem artigo.
            var antigo = new Produto { Codigo = Unico("OLD", 10), Nome = "Cerveja antiga", CategoriaId = c.Bebida.CategoriaId, Tipo = TipoProduto.Bar, PrecoVenda = 70, Custo = 35 };
            db.Produtos.Add(antigo);
            await db.SaveChangesAsync();
            antigoId = antigo.Id;
        }
        var activados = await app.ComServicos(s => s.GetRequiredService<ServicoStockProdutos>().SincronizarTodosAsync());
        Assert.True(activados >= 1);
        Assert.Equal(0, await app.ComServicos(s => s.GetRequiredService<ServicoStockProdutos>().SincronizarTodosAsync()));

        var produto = await c.Gerente.Obter<ProdutoDto>($"/api/produtos/{antigoId}");
        var artigo = await c.Gerente.Obter<ArtigoDto>($"/api/stock/artigos/{produto.ArtigoId}");
        Assert.Equal(("Cerveja antiga", 35m, true), (artigo.Nome, artigo.CustoMedio, artigo.PermiteStockNegativo));
    }
}
