using System.Net;
using System.Net.Http.Json;
using LAMGestRestaurant.Application.Seguranca;
using LAMGestRestaurant.Tests.Infraestrutura;
using Microsoft.AspNetCore.Mvc.Testing;

namespace LAMGestRestaurant.Tests;

[Collection(ColeccaoBaseDados.Nome)]
public class ProducaoTests(AplicacaoTeste app)
{
    [Fact]
    public async Task Respostas_levam_cabecalhos_de_seguranca()
    {
        var resposta = await app.CreateClient().GetAsync("/api/saude");
        Assert.Equal("nosniff", resposta.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", resposta.Headers.GetValues("X-Frame-Options").Single());
        Assert.Equal("no-referrer", resposta.Headers.GetValues("Referrer-Policy").Single());
    }

    [Fact]
    public async Task Sessao_sobrevive_a_um_reinicio_com_chaves_persistidas()
    {
        var pasta = Path.Combine(Path.GetTempPath(), "lamgest-chaves-" + Guid.NewGuid().ToString("N"));
        try
        {
            var nome = await app.CriarUtilizadorAsync(PerfisPadrao.Caixa);
            string cookie;
            await using (var primeira = app.WithWebHostBuilder(b => b.UseSetting("DataProtection:Pasta", pasta)))
            {
                var cliente = primeira.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
                var login = await cliente.PostAsJsonAsync("/api/auth/login", new { nomeUtilizador = nome, palavraPasse = AplicacaoTeste.PalavraPasseTeste });
                login.EnsureSuccessStatusCode();
                cookie = login.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("LAMGest.Sessao=")).Split(';')[0];
            }
            Assert.NotEmpty(Directory.GetFiles(pasta, "key-*.xml"));

            // Nova instância (equivale a reiniciar o serviço) com as mesmas chaves: o cookie continua válido.
            await using var segunda = app.WithWebHostBuilder(b => b.UseSetting("DataProtection:Pasta", pasta));
            var pedido = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
            pedido.Headers.Add("Cookie", cookie);
            var resposta = await segunda.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false }).SendAsync(pedido);
            Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        }
        finally
        {
            if (Directory.Exists(pasta))
                Directory.Delete(pasta, recursive: true);
        }
    }
}
