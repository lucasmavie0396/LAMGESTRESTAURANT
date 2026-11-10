using System.Drawing;
using System.Net;
using LAMGestRestaurant.Api.Controllers;
using LAMGestRestaurant.Application.Catalogo;
using LAMGestRestaurant.Application.Impressao;
using LAMGestRestaurant.Application.Seguranca;
using LAMGestRestaurant.Application.Vendas;
using LAMGestRestaurant.Domain.Comum;
using LAMGestRestaurant.Domain.Sistema;
using LAMGestRestaurant.Infrastructure.Impressao;
using LAMGestRestaurant.Tests.Infraestrutura;
using ZXing;
using ZXing.Common;
using ZXing.OneD;
using static LAMGestRestaurant.Tests.Infraestrutura.ExtensoesHttp;

namespace LAMGestRestaurant.Tests;

/// <summary>Etiquetas de produto: código de barras lido por um leitor independente, configuração, impressão e separação dos talões.</summary>
[Collection(ColeccaoBaseDados.Nome)]
public class EtiquetasTests(AplicacaoTeste app)
{
    private const string Url = "/api/configuracoes/impressoras";

    private static string Ler(byte[] cinzento, int largura, int altura) =>
        new Code128Reader().decode(new BinaryBitmap(new HybridBinarizer(new RGBLuminanceSource(cinzento, largura, altura, RGBLuminanceSource.BitmapFormat.Gray8))))?.Text
        ?? throw new InvalidOperationException("Código de barras ilegível.");

    [Theory]
    [InlineData("7891234567890")]   // 13 algarismos: conjunto B
    [InlineData("12345678")]        // algarismos em número par: conjunto C
    [InlineData("P0001")]
    [InlineData("Abc-12 x/~")]
    public void Code128_e_lido_por_um_leitor_independente(string texto)
    {
        var modulos = Code128.Modulos(texto);
        const int Px = 3, Margem = 10, Altura = 20;
        var largura = (modulos.Count + 2 * Margem) * Px;
        var imagem = Enumerable.Repeat((byte)255, largura * Altura).ToArray();
        for (var m = 0; m < modulos.Count; m++)
            if (modulos[m])
                for (var y = 0; y < Altura; y++)
                    for (var x = 0; x < Px; x++)
                        imagem[y * largura + (Margem + m) * Px + x] = 0;
        Assert.Equal(texto, Ler(imagem, largura, Altura));
        Assert.False(Code128.Valido("ç"));
    }

    [Fact]
    public async Task Previsao_desenha_a_etiqueta_com_um_codigo_de_barras_legivel()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(6, 1))
            return;
        var (admin, _) = await app.ClienteAutenticadoAsync(PerfisPadrao.Administrador);
        var png = await (await admin.GetAsync("/api/etiquetas/previsao?largura=50&altura=30&campos=nome,preco,codigo-barras").Esperar(HttpStatusCode.OK)).Content.ReadAsByteArrayAsync();
        using var imagem = new Bitmap(new MemoryStream(png));
        Assert.Equal((400, 240), (imagem.Width, imagem.Height)); // 50 × 30 mm a 203 ppp (arredondado para cima)
        var cinzento = new byte[imagem.Width * imagem.Height];
        for (var y = 0; y < imagem.Height; y++)
            for (var x = 0; x < imagem.Width; x++)
                cinzento[y * imagem.Width + x] = (byte)(imagem.GetPixel(x, y).GetBrightness() * 255);
        Assert.Equal("7891234567890", Ler(cinzento, imagem.Width, imagem.Height));

        await admin.GetAsync("/api/etiquetas/previsao?campos=nome,peso").Esperar(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Imprime_as_etiquetas_na_impressora_de_etiquetas_e_nunca_la_saem_taloes()
    {
        var c = await CenarioVenda.CriarAsync(app);
        var guardar = (Dictionary<string, string> v) => c.Gerente.Put("/api/configuracoes", new GuardarConfiguracoes(v));
        var item = new ImprimirEtiquetas([new ItemEtiquetas(c.Prato.Id, 3)]);
        await c.Gerente.Post("/api/etiquetas/imprimir", item).Esperar(HttpStatusCode.UnprocessableEntity); // sem impressora

        await c.Gerente.Post(Url, new GuardarImpressora(Unico("Etq "), FuncaoImpressora.Etiquetas, ImpressorasTeste.Bar, 80, null, null, ModoImpressora.Pdf))
            .Esperar(HttpStatusCode.UnprocessableEntity);
        var impressora = await (await c.Gerente.Post(Url, new GuardarImpressora(Unico("Etq "), FuncaoImpressora.Etiquetas, ImpressorasTeste.Bar, 80, null, null))
            .Esperar(HttpStatusCode.OK)).Ler<ImpressoraDto>();
        try
        {
            await guardar(new() { [ChavesConfiguracao.EtiquetaLargura] = "5", [ChavesConfiguracao.EtiquetaCampos] = "nome" }).Esperar(HttpStatusCode.UnprocessableEntity);
            await guardar(new() { [ChavesConfiguracao.EtiquetaCampos] = "nome,peso" }).Esperar(HttpStatusCode.UnprocessableEntity);
            await guardar(new() { [ChavesConfiguracao.EtiquetaLargura] = "40.5", [ChavesConfiguracao.EtiquetaAltura] = "25", [ChavesConfiguracao.EtiquetaCampos] = "preco,nome,codigo-barras" })
                .Esperar(HttpStatusCode.OK);

            // A impressora de etiquetas não recebe talões da cozinha nem do bar (é a única activa com este nome do Windows).
            int Taloes() => app.Impressoras.Com("").Count(i => i.Impressora == ImpressorasTeste.Bar && i.Documento.Etiquetas is null);
            await Task.Delay(500);
            var antes = Taloes();
            var pedido = await c.AbrirMesaAsync(c.Gerente, c.Mesa1, (c.Bebida, 4));
            await c.Gerente.Post($"/api/pedidos/{pedido.Id}/enviar-cozinha", new ComVersao(pedido.RowVersion)).Esperar(HttpStatusCode.OK);

            await c.Gerente.Post("/api/etiquetas/imprimir", item).Esperar(HttpStatusCode.Accepted);
            // Outros testes também imprimem «Frango grelhado» (talões): espera pela etiqueta em concreto.
            Impresso? impresso = null;
            for (var i = 0; i < 100 && impresso is null; i++, await Task.Delay(100))
                impresso = app.Impressoras.Com("Frango grelhado").SingleOrDefault(x => x.Documento.Etiquetas is not null);
            Assert.NotNull(impresso);
            var etiquetas = impresso.Documento.Etiquetas!;
            Assert.Equal(3, etiquetas.Count);
            Assert.Equal(new FormatoEtiqueta(40.5, 25), impresso.Documento.Formato);
            Assert.Null(impresso.Logotipo);
            Assert.Equal(["450,50 MT", "Frango grelhado"], etiquetas[0].Linhas.Select(l => l.Texto));
            Assert.Equal(c.Prato.Codigo, etiquetas[0].CodigoBarras); // sem código de barras próprio: o código interno
            Assert.Equal(antes, Taloes());
        }
        finally
        {
            await guardar(new() { [ChavesConfiguracao.EtiquetaLargura] = "50", [ChavesConfiguracao.EtiquetaAltura] = "30", [ChavesConfiguracao.EtiquetaCampos] = "nome,preco,codigo-barras" })
                .Esperar(HttpStatusCode.OK);
            await c.Gerente.Post($"{Url}/{impressora.Id}/activo", new { activo = false }).Esperar(HttpStatusCode.NoContent);
        }
    }
}
