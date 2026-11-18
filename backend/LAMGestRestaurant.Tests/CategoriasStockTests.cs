using System.Net;
using LAMGestRestaurant.Application.Catalogo;
using LAMGestRestaurant.Application.Comum;
using LAMGestRestaurant.Application.Stock;
using LAMGestRestaurant.Domain.Comum;
using LAMGestRestaurant.Tests.Infraestrutura;
using static LAMGestRestaurant.Tests.Infraestrutura.ExtensoesHttp;

namespace LAMGestRestaurant.Tests;

/// <summary>Categorias só de stock (matérias-primas): agrupam os artigos sem aparecer no POS.</summary>
[Collection(ColeccaoBaseDados.Nome)]
public class CategoriasStockTests(AplicacaoTeste app)
{
    [Fact]
    public async Task Categoria_so_de_stock_agrupa_os_artigos_e_nao_aparece_no_POS()
    {
        var c = await CenarioVenda.CriarAsync(app);
        var g = c.Gerente;
        var carnes = await (await g.Post("/api/categorias", new GuardarCategoria(Unico("Carnes "), null, null, null, null, 0, OcultaNoPos: true))
            .Esperar(HttpStatusCode.Created)).Ler<CategoriaDto>();
        Assert.True(carnes.OcultaNoPos);
        // A subcategoria herda: a categoria principal é só de stock.
        var bovino = await (await g.Post("/api/categorias", new GuardarCategoria(Unico("Bovino "), null, carnes.Id, null, null, 0))
            .Esperar(HttpStatusCode.Created)).Ler<CategoriaDto>();
        Assert.False(bovino.OcultaNoPos);

        var kg = (await g.Obter<List<UnidadeDto>>("/api/stock/unidades")).Single(u => u.Codigo == "kg").Id;
        async Task<ArtigoDto> ArtigoAsync(string nome, int? categoria) =>
            await (await g.Post("/api/stock/artigos", new GuardarArtigo(Unico("MP", 12), nome, TipoArtigo.Ingrediente, kg, 0, false, categoria))
                .Esperar(HttpStatusCode.OK)).Ler<ArtigoDto>();
        var picanha = await ArtigoAsync(Unico("Picanha "), bovino.Id);
        var carvao = await ArtigoAsync(Unico("Carvão "), carnes.Id);
        var semCategoria = await ArtigoAsync(Unico("Sal "), null);

        // Stock → Artigos: filtro pela categoria (com as subcategorias), pela subcategoria e «sem categoria».
        Task<Pagina<ArtigoDto>> Artigos(string filtro) => g.Obter<Pagina<ArtigoDto>>($"/api/stock/artigos?tamanho=200&{filtro}");
        Assert.Equal(new[] { carvao.Id, picanha.Id }.Order(), (await Artigos($"categoriaId={carnes.Id}")).Itens.Select(a => a.Id).Order());
        Assert.Equal([picanha.Id], (await Artigos($"categoriaId={bovino.Id}")).Itens.Select(a => a.Id));
        var sem = (await Artigos("categoriaId=0")).Itens;
        Assert.Contains(sem, a => a.Id == semCategoria.Id);
        Assert.DoesNotContain(sem, a => a.CategoriaId != null);

        // Um produto posto por engano numa categoria só de stock não aparece no POS (mas aparece em Produtos).
        var produto = await (await g.Post("/api/produtos", new GuardarProduto(Unico("P", 10), null, Unico("Engano ", 14), null, bovino.Id, TipoProduto.Restaurante,
            100m, 10m, null, null, false, true, null)).Esperar(HttpStatusCode.Created)).Ler<ProdutoDto>();
        Task<Pagina<ProdutoDto>> Produtos(string filtro) => g.Obter<Pagina<ProdutoDto>>($"/api/produtos?activo=true&tamanho=200&{filtro}");
        Assert.DoesNotContain((await Produtos("pos=true")).Itens, p => p.Id == produto.Id);
        Assert.DoesNotContain((await Produtos($"pos=true&pesquisa={produto.Nome}")).Itens, p => p.Id == produto.Id);
        Assert.Contains((await Produtos("pos=true")).Itens, p => p.Id == c.Prato.Id);
        Assert.Contains((await Produtos($"pesquisa={produto.Nome}")).Itens, p => p.Id == produto.Id);

        // Voltar a mostrar no POS.
        await g.Put($"/api/categorias/{carnes.Id}", new GuardarCategoria(carnes.Nome, null, null, null, null, 0)).Esperar(HttpStatusCode.OK);
        Assert.Contains((await Produtos($"pos=true&pesquisa={produto.Nome}")).Itens, p => p.Id == produto.Id);
    }
}
