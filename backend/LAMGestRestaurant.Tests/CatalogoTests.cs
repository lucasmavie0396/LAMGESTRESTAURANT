using System.Net;
using LAMGestRestaurant.Application.Catalogo;
using LAMGestRestaurant.Application.Comum;
using LAMGestRestaurant.Application.Seguranca;
using LAMGestRestaurant.Domain.Comum;
using LAMGestRestaurant.Tests.Infraestrutura;
using static LAMGestRestaurant.Tests.Infraestrutura.ExtensoesHttp;

namespace LAMGestRestaurant.Tests;

[Collection(ColeccaoBaseDados.Nome)]
public class CatalogoTests(AplicacaoTeste app)
{
    private async Task<(HttpClient gerente, CategoriaDto categoria)> PrepararAsync()
    {
        var (gerente, _) = await app.ClienteAutenticadoAsync(PerfisPadrao.Gerente);
        var r = await gerente.Post("/api/categorias", new GuardarCategoria(Unico("Cat "), null, null, null, "#2563eb", 1)).Esperar(HttpStatusCode.Created);
        return (gerente, await r.Ler<CategoriaDto>());
    }

    private static GuardarProduto NovoProduto(int categoriaId, decimal preco = 250m, byte[]? versao = null, string? codigo = null) =>
        new(codigo ?? Unico("T", 10), null, "Frango grelhado", null, categoriaId, TipoProduto.Restaurante, preco, 120m, null, null, true, true, versao);

    [Fact]
    public async Task Cria_produto_e_recusa_codigo_duplicado()
    {
        var (gerente, cat) = await PrepararAsync();
        var r = await gerente.Post("/api/produtos", NovoProduto(cat.Id)).Esperar(HttpStatusCode.Created);
        var produto = await r.Ler<ProdutoDto>();
        Assert.Equal(250m, produto.PrecoVenda);
        Assert.Equal(cat.Nome, produto.CategoriaNome);

        await gerente.Post("/api/produtos", NovoProduto(cat.Id, codigo: produto.Codigo)).Esperar(HttpStatusCode.Conflict);
    }

    [Theory]
    [InlineData("400638133393", "4006381333931")]
    [InlineData("590123412345", "5901234123457")]
    public void Ean13_calcula_o_digito_de_controlo(string doze, string esperado) => Assert.Equal(esperado, ServicoCatalogo.Ean13(doze));

    [Fact]
    public async Task Gera_codigo_de_barras_EAN13_interno_livre_que_fica_no_produto_ao_guardar()
    {
        var (gerente, cat) = await PrepararAsync();
        var gerado = (await (await gerente.GetAsync("/api/produtos/gerar-codigo-barras").Esperar(HttpStatusCode.OK)).Ler<CodigoGerado>()).CodigoBarras;
        Assert.Matches("^20[0-9]{11}$", gerado);
        Assert.Equal(gerado, ServicoCatalogo.Ean13(gerado[..12]));

        // Só se grava ao guardar o produto; depois o mesmo código já não é gerado nem aceite noutro produto.
        var produto = await (await gerente.Post("/api/produtos", NovoProduto(cat.Id) with { CodigoBarras = gerado }).Esperar(HttpStatusCode.Created)).Ler<ProdutoDto>();
        Assert.Equal(gerado, produto.CodigoBarras);
        await gerente.Post("/api/produtos", NovoProduto(cat.Id) with { CodigoBarras = gerado }).Esperar(HttpStatusCode.Conflict);
        for (var i = 0; i < 20; i++)
            Assert.NotEqual(gerado, (await (await gerente.GetAsync("/api/produtos/gerar-codigo-barras").Esperar(HttpStatusCode.OK)).Ler<CodigoGerado>()).CodigoBarras);

        var soVer = await app.ClienteComPermissoesAsync(Permissoes.ProdutosVer);
        await soVer.GetAsync("/api/produtos/gerar-codigo-barras").Esperar(HttpStatusCode.Forbidden);
    }

    private sealed record CodigoGerado(string CodigoBarras);

    [Fact]
    public async Task Preco_negativo_e_recusado()
    {
        var (gerente, cat) = await PrepararAsync();
        await gerente.Post("/api/produtos", NovoProduto(cat.Id, preco: -1)).Esperar(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Alterar_preco_exige_permissao_propria()
    {
        var (gerente, cat) = await PrepararAsync();
        var produto = await (await gerente.Post("/api/produtos", NovoProduto(cat.Id)).Esperar(HttpStatusCode.Created)).Ler<ProdutoDto>();
        var semPreco = await app.ClienteComPermissoesAsync(Permissoes.ProdutosGerir, Permissoes.ProdutosVer);

        // Editar outros campos é permitido...
        var nome = NovoProduto(cat.Id, 250m, produto.RowVersion, produto.Codigo) with { Nome = "Frango piri-piri" };
        var editado = await (await semPreco.Put($"/api/produtos/{produto.Id}", nome).Esperar(HttpStatusCode.OK)).Ler<ProdutoDto>();

        // ...mas o preço não.
        var preco = NovoProduto(cat.Id, 300m, editado.RowVersion, produto.Codigo);
        await semPreco.Put($"/api/produtos/{produto.Id}", preco).Esperar(HttpStatusCode.UnprocessableEntity);

        // O gerente pode, e fica auditado.
        await gerente.Put($"/api/produtos/{produto.Id}", preco).Esperar(HttpStatusCode.OK);
        await using var db = app.CriarContexto();
        Assert.Contains(db.RegistosAuditoria, a => a.Accao == "produto.alterar-preco" && a.EntidadeId == produto.Id.ToString());
    }

    [Fact]
    public async Task Edicao_com_versao_desactualizada_devolve_409()
    {
        var (gerente, cat) = await PrepararAsync();
        var produto = await (await gerente.Post("/api/produtos", NovoProduto(cat.Id)).Esperar(HttpStatusCode.Created)).Ler<ProdutoDto>();

        await gerente.Put($"/api/produtos/{produto.Id}", NovoProduto(cat.Id, 250m, produto.RowVersion, produto.Codigo) with { Nome = "Primeira" })
            .Esperar(HttpStatusCode.OK);
        // Segunda edição com a versão antiga (outro terminal tinha o formulário aberto).
        await gerente.Put($"/api/produtos/{produto.Id}", NovoProduto(cat.Id, 250m, produto.RowVersion, produto.Codigo) with { Nome = "Segunda" })
            .Esperar(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Categoria_com_produtos_activos_nao_se_desactiva()
    {
        var (gerente, cat) = await PrepararAsync();
        var produto = await (await gerente.Post("/api/produtos", NovoProduto(cat.Id)).Esperar(HttpStatusCode.Created)).Ler<ProdutoDto>();

        await gerente.Post($"/api/categorias/{cat.Id}/activo", new { activo = false }).Esperar(HttpStatusCode.UnprocessableEntity);

        await gerente.Post($"/api/produtos/{produto.Id}/activo", new { activo = false, rowVersion = produto.RowVersion }).Esperar(HttpStatusCode.OK);
        await gerente.Post($"/api/categorias/{cat.Id}/activo", new { activo = false }).Esperar(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Categoria_nao_pode_ser_subcategoria_de_si_propria()
    {
        var (gerente, cat) = await PrepararAsync();
        await gerente.Put($"/api/categorias/{cat.Id}", new GuardarCategoria(cat.Nome, null, cat.Id, null, null, 0))
            .Esperar(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Garcom_consulta_produtos_mas_nao_os_cria()
    {
        var (gerente, cat) = await PrepararAsync();
        var (garcom, _) = await app.ClienteAutenticadoAsync(PerfisPadrao.Garcom);

        var lista = await garcom.Obter<Pagina<ProdutoDto>>($"/api/produtos?categoriaId={cat.Id}");
        Assert.NotNull(lista);
        await garcom.Post("/api/produtos", NovoProduto(cat.Id)).Esperar(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Cozinha_marca_produto_esgotado()
    {
        var (gerente, cat) = await PrepararAsync();
        var produto = await (await gerente.Post("/api/produtos", NovoProduto(cat.Id)).Esperar(HttpStatusCode.Created)).Ler<ProdutoDto>();
        var (cozinha, _) = await app.ClienteAutenticadoAsync(PerfisPadrao.Cozinha);

        var r = await cozinha.Post($"/api/produtos/{produto.Id}/disponibilidade", new { disponivel = false }).Esperar(HttpStatusCode.OK);
        Assert.False((await r.Ler<ProdutoDto>()).Disponivel);
    }
}
