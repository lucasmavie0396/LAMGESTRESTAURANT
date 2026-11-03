using System.Drawing;
using System.Drawing.Imaging;
using System.Net;
using System.Net.Http.Headers;
using LAMGestRestaurant.Api.Controllers;
using LAMGestRestaurant.Application.Catalogo;
using LAMGestRestaurant.Application.Impressao;
using LAMGestRestaurant.Application.Seguranca;
using LAMGestRestaurant.Application.Vendas;
using LAMGestRestaurant.Domain.Comum;
using LAMGestRestaurant.Infrastructure.Imagens;
using LAMGestRestaurant.Tests.Infraestrutura;
using Microsoft.EntityFrameworkCore;
using static LAMGestRestaurant.Tests.Infraestrutura.ExtensoesHttp;

namespace LAMGestRestaurant.Tests;

/// <summary>Subcategorias com impressora herdada, impressora por produto, stock inicial na criação e imagens dos produtos.</summary>
[Collection(ColeccaoBaseDados.Nome)]
public class CatalogoAvancadoTests(AplicacaoTeste app)
{
    private static async Task<CategoriaDto> CategoriaAsync(HttpClient c, string nome, int? pai = null, int? impressora = null) =>
        await (await c.Post("/api/categorias", new GuardarCategoria(Unico(nome), null, pai, null, null, 0, impressora)).Esperar(HttpStatusCode.Created)).Ler<CategoriaDto>();

    private static GuardarProduto Produto(int categoria, string nome, decimal preco, bool enviaCozinha = true, int? impressora = null, StockInicialProduto? stock = null) =>
        new(Unico("P", 10), null, nome, null, categoria, TipoProduto.Outro, preco, 0, null, null, enviaCozinha, true, null, impressora, stock);

    private static async Task<ImpressoraDto> ImpressoraAsync(HttpClient c, FuncaoImpressora funcao, string windows) =>
        await (await c.Post("/api/configuracoes/impressoras", new GuardarImpressora(Unico(funcao + " "), funcao, windows, 80, null, null))
            .Esperar(HttpStatusCode.OK)).Ler<ImpressoraDto>();

    [Fact]
    public async Task Subcategoria_herda_a_impressora_da_categoria_e_o_produto_pode_escolher_outra()
    {
        var c = await CenarioVenda.CriarAsync(app);
        var bar = await ImpressoraAsync(c.Gerente, FuncaoImpressora.Bar, ImpressorasTeste.Bar);
        var cozinha = await ImpressoraAsync(c.Gerente, FuncaoImpressora.Cozinha, ImpressorasTeste.Cozinha);
        var caixa = await ImpressoraAsync(c.Gerente, FuncaoImpressora.Caixa, ImpressorasTeste.Caixa);
        try
        {
            var bebidas = await CategoriaAsync(c.Gerente, "Bebidas ", impressora: bar.Id);
            Assert.Equal((bar.Id, bar.Nome), (bebidas.ImpressoraId, bebidas.ImpressoraNome));
            var cervejas = await CategoriaAsync(c.Gerente, "Cervejas ", pai: bebidas.Id);
            Assert.Equal(bebidas.Id, cervejas.CategoriaPaiId);
            await c.Gerente.Post("/api/categorias", new GuardarCategoria(Unico("X "), null, null, null, null, 0, caixa.Id)).Esperar(HttpStatusCode.UnprocessableEntity);

            // Cerveja: subcategoria sem impressora → a das Bebidas, mesmo sem ir ao ecrã da cozinha.
            var cerveja = await (await c.Gerente.Post("/api/produtos", Produto(cervejas.Id, "Cerveja 2M", 90, enviaCozinha: false)).Esperar(HttpStatusCode.Created)).Ler<ProdutoDto>();
            // Sangria: na mesma subcategoria, mas o produto manda para a cozinha.
            var sangria = await (await c.Gerente.Post("/api/produtos", Produto(cervejas.Id, "Sangria", 300, impressora: cozinha.Id)).Esperar(HttpStatusCode.Created)).Ler<ProdutoDto>();
            Assert.Equal(cozinha.Nome, sangria.ImpressoraNome);

            var pedido = await c.AbrirMesaAsync(c.Gerente, c.Mesa1, (cerveja, 2), (sangria, 1));
            await c.Gerente.Post($"/api/pedidos/{pedido.Id}/enviar-cozinha", new ComVersao(pedido.RowVersion)).Esperar(HttpStatusCode.OK);

            var taloes = await app.Impressoras.EsperarAsync(pedido.Numero, quantos: 2);
            var noBar = Assert.Single(taloes, t => t.Impressora == ImpressorasTeste.Bar);
            Assert.Contains("2 x Cerveja 2M", noBar.Texto);
            Assert.DoesNotContain("Sangria", noBar.Texto);
            Assert.Contains("1 x Sangria", Assert.Single(taloes, t => t.Impressora == ImpressorasTeste.Cozinha).Texto);
        }
        finally
        {
            foreach (var i in new[] { bar, cozinha, caixa })
                await c.Gerente.Post($"/api/configuracoes/impressoras/{i.Id}/activo", new { activo = false }).Esperar(HttpStatusCode.NoContent);
        }
    }

    [Fact]
    public async Task Stock_inicial_cria_artigo_ficha_e_entrada_e_a_venda_baixa_o_stock()
    {
        var c = await CenarioVenda.CriarAsync(app);
        var categoria = await CategoriaAsync(c.Gerente, "Revenda ");
        var produto = await (await c.Gerente.Post("/api/produtos", Produto(categoria.Id, "Água 500 ml", 50, enviaCozinha: false,
            stock: new StockInicialProduto(24, 18.5m, 6, null))).Esperar(HttpStatusCode.Created)).Ler<ProdutoDto>();
        Assert.Equal(18.5m, produto.Custo); // custo de referência preenchido pelo custo do stock inicial

        await using (var db = app.CriarContexto())
        {
            var artigo = await db.Artigos.Include(a => a.UnidadeMedida).SingleAsync(a => a.Codigo == produto.Codigo);
            Assert.Equal((TipoArtigo.Revenda, "un", 18.5m, 6m), (artigo.Tipo, artigo.UnidadeMedida.Codigo, artigo.CustoMedio, artigo.StockMinimo));
            Assert.Equal(24m, await db.StockSaldos.Where(s => s.ArtigoId == artigo.Id).SumAsync(s => s.Quantidade));
            var movimento = await db.MovimentosStock.SingleAsync(m => m.ArtigoId == artigo.Id);
            Assert.Equal((TipoMovimentoStock.StockInicial, 24m, 18.5m), (movimento.Tipo, movimento.Quantidade, movimento.CustoUnitario));
            Assert.True(await db.Receitas.AnyAsync(r => r.ProdutoId == produto.Id && r.Activa && r.Itens.Count == 1));
        }

        // Vender 2 baixa o stock pela ficha criada.
        var pedido = await c.AbrirMesaAsync(c.Gerente, c.Mesa1, (produto, 2));
        await c.PagarAsync(c.Gerente, pedido.Id, "DINHEIRO", pedido.Total).Esperar(HttpStatusCode.Created);
        await using (var db = app.CriarContexto())
            Assert.Equal(22m, await db.StockSaldos.Where(s => s.Artigo.Codigo == produto.Codigo).SumAsync(s => s.Quantidade));

        // Só na criação; o mesmo código de artigo não se repete; exige permissão de ajustes de stock.
        await c.Gerente.Put($"/api/produtos/{produto.Id}", Produto(categoria.Id, "Água", 50, stock: new StockInicialProduto(1, 1, 0, null))
            with { Codigo = produto.Codigo, RowVersion = (await c.Gerente.Obter<ProdutoDto>($"/api/produtos/{produto.Id}")).RowVersion })
            .Esperar(HttpStatusCode.UnprocessableEntity);
        var codigoOcupado = Unico("Q", 10);
        await c.Gerente.Post("/api/stock/artigos", new Application.Stock.GuardarArtigo(codigoOcupado, "Já existe", TipoArtigo.Revenda, 1, 0, false)).Esperar(HttpStatusCode.OK);
        await c.Gerente.Post("/api/produtos", Produto(categoria.Id, "Refresco", 60, stock: new StockInicialProduto(5, 20, 0, null)) with { Codigo = codigoOcupado })
            .Esperar(HttpStatusCode.Conflict);
        await using (var db = app.CriarContexto())
            Assert.False(await db.Produtos.AnyAsync(p => p.Codigo == codigoOcupado)); // tudo ou nada: o produto não ficou criado
        var semAjustes = await app.ClienteComPermissoesAsync(Permissoes.ProdutosVer, Permissoes.ProdutosGerir);
        await semAjustes.Post("/api/produtos", Produto(categoria.Id, "Sumo", 60, stock: new StockInicialProduto(10, 20, 0, null))).Esperar(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Imagem_carregada_e_reduzida_guardada_na_base_e_lida_pelo_endereco()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(6, 1))
            return;
        var (admin, _) = await app.ClienteAutenticadoAsync(PerfisPadrao.Administrador);
        var png = Png(1600, 1000);

        var guardada = await (await admin.PostAsync("/api/imagens/carregar", Formulario(png, "foto.png")).Esperar(HttpStatusCode.OK)).Ler<ImagemGuardada>();
        Assert.Equal((640, 400), (guardada.Largura, guardada.Altura));
        Assert.StartsWith("/api/imagens/", guardada.Url);

        var resposta = await admin.GetAsync(guardada.Url).Esperar(HttpStatusCode.OK);
        Assert.Equal("image/jpeg", resposta.Content.Headers.ContentType?.MediaType);
        using (var lida = Image.FromStream(new MemoryStream(await resposta.Content.ReadAsByteArrayAsync())))
            Assert.Equal((640, 400), (lida.Width, lida.Height));

        // Sem sessão não se lê; um ficheiro que não é imagem é recusado; o garçom não carrega imagens.
        Assert.Equal(HttpStatusCode.Unauthorized, (await app.CreateClient().GetAsync(guardada.Url)).StatusCode);
        await admin.PostAsync("/api/imagens/carregar", Formulario("não sou uma imagem"u8.ToArray(), "x.png")).Esperar(HttpStatusCode.UnprocessableEntity);
        var (garcom, _) = await app.ClienteAutenticadoAsync(PerfisPadrao.Garcom);
        Assert.Equal(HttpStatusCode.Forbidden, (await garcom.PostAsync("/api/imagens/carregar", Formulario(png, "foto.png"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await garcom.GetAsync(guardada.Url)).StatusCode); // o POS mostra as imagens
    }

    [Theory]
    [InlineData("http://127.0.0.1/imagem.png")]
    [InlineData("http://localhost/imagem.png")]
    [InlineData("http://10.0.0.47:8080/api/saude")]
    [InlineData("http://192.168.1.1/admin.png")]
    [InlineData("http://169.254.169.254/latest/meta-data")]
    [InlineData("file:///C:/Windows/win.ini")]
    [InlineData("ftp://exemplo.com/imagem.png")]
    public async Task Importar_nunca_descarrega_da_rede_local_nem_de_outros_protocolos(string url)
    {
        var (admin, _) = await app.ClienteAutenticadoAsync(PerfisPadrao.Administrador);
        await admin.Post("/api/imagens/importar", new ImportarImagem(url, null, null)).Esperar(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Miniaturas_so_do_openverse()
    {
        var (admin, _) = await app.ClienteAutenticadoAsync(PerfisPadrao.Administrador);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await admin.GetAsync("/api/imagens/miniatura?url=" + Uri.EscapeDataString("https://exemplo.com/v1/images/x"))).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await admin.GetAsync("/api/imagens/miniatura?url=" + Uri.EscapeDataString("http://127.0.0.1/v1/images/x"))).StatusCode);
    }

    [Theory]
    [InlineData("8.8.8.8", true)]
    [InlineData("151.101.1.140", true)]
    [InlineData("2a00:1450:4003:80e::200e", true)]
    [InlineData("127.0.0.1", false)]
    [InlineData("10.1.2.3", false)]
    [InlineData("172.20.0.1", false)]
    [InlineData("192.168.0.10", false)]
    [InlineData("100.64.0.1", false)]
    [InlineData("::1", false)]
    [InlineData("fe80::1", false)]
    [InlineData("fd00::1", false)]
    [InlineData("::ffff:192.168.0.1", false)]
    public void So_enderecos_publicos_sao_permitidos(string ip, bool publico) =>
        Assert.Equal(publico, ServicoImagens.EnderecoPublico(IPAddress.Parse(ip)));

    private static byte[] Png(int largura, int altura)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(6, 1))
            return [];
        using var bmp = new Bitmap(largura, altura);
        using (var g = Graphics.FromImage(bmp))
            g.Clear(Color.SteelBlue);
        using var m = new MemoryStream();
        bmp.Save(m, ImageFormat.Png);
        return m.ToArray();
    }

    private static MultipartFormDataContent Formulario(byte[] dados, string nome)
    {
        var ficheiro = new ByteArrayContent(dados);
        ficheiro.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        return new MultipartFormDataContent { { ficheiro, "ficheiro", nome } };
    }
}
