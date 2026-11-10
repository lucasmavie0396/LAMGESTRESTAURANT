using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using LAMGestRestaurant.Application.Seguranca;
using LAMGestRestaurant.Tests.Infraestrutura;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

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
    public async Task Comando_migrar_cria_uma_base_vazia_e_depois_nada_fica_pendente()
    {
        // Base própria, criada e eliminada aqui (o sufixo _Tests é a salvaguarda).
        var ligacao = new SqlConnectionStringBuilder(app.ConnectionString) { InitialCatalog = "LAMGestRestaurantDB_Migrar_Tests" }.ConnectionString;
        Assert.EndsWith("_Tests", new SqlConnectionStringBuilder(ligacao).InitialCatalog);
        try
        {
            var (codigo, saida) = await CorrerApiAsync(ligacao, "migrar");
            Assert.True(codigo == 0, saida);
            Assert.Contains("Base de dados actualizada.", saida);

            var (codigoListar, pendentes) = await CorrerApiAsync(ligacao, "migrar", "--listar");
            Assert.Equal(0, codigoListar);
            Assert.DoesNotContain(Api.Comandos.Migrar.PrefixoPendente, pendentes);

            await using var db = AplicacaoTeste.CriarContexto(ligacao);
            Assert.True(await db.MetodosPagamento.AnyAsync()); // dados de referência semeados
        }
        finally
        {
            await using var db = AplicacaoTeste.CriarContexto(ligacao);
            await db.Database.EnsureDeletedAsync();
        }
    }

    /// <summary>Corre a API compilada como o instalador faz (processo separado, connection string por variável de ambiente).</summary>
    private static async Task<(int Codigo, string Saida)> CorrerApiAsync(string ligacao, params string[] argumentos)
    {
        var inicio = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = AppContext.BaseDirectory };
        inicio.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "LAMGestRestaurant.Api.dll"));
        foreach (var a in argumentos)
            inicio.ArgumentList.Add(a);
        inicio.Environment["ConnectionStrings__Default"] = ligacao;
        inicio.Environment["ASPNETCORE_ENVIRONMENT"] = "Testing";
        using var processo = Process.Start(inicio)!;
        var saida = processo.StandardOutput.ReadToEndAsync();
        var erros = processo.StandardError.ReadToEndAsync();
        await processo.WaitForExitAsync();
        return (processo.ExitCode, await saida + await erros);
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

    /// <summary>Depois de instalar uma versão nova, o navegador tem de ir buscar o index.html novo (senão ficam os menus antigos).</summary>
    [Fact]
    public async Task Frontend_index_html_e_sempre_revalidado_e_assets_ficam_em_cache()
    {
        var pasta = Path.Combine(Path.GetTempPath(), "lamgest-www-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(pasta, "assets"));
        await File.WriteAllTextAsync(Path.Combine(pasta, "index.html"), "<!doctype html><title>LAMGest</title>");
        await File.WriteAllTextAsync(Path.Combine(pasta, "assets", "index-abc123.js"), "console.log(1)");
        try
        {
            await using var www = app.WithWebHostBuilder(b => b.UseWebRoot(pasta));
            var cliente = www.CreateClient();
            foreach (var caminho in new[] { "/", "/index.html", "/cartoes", "/configuracoes" })
            {
                var r = await cliente.GetAsync(caminho);
                Assert.Equal(HttpStatusCode.OK, r.StatusCode);
                Assert.Contains("LAMGest", await r.Content.ReadAsStringAsync());
                Assert.Equal("no-cache", r.Headers.CacheControl?.ToString());
            }
            var js = await cliente.GetAsync("/assets/index-abc123.js");
            Assert.Equal(HttpStatusCode.OK, js.StatusCode);
            Assert.Equal("public, max-age=31536000, immutable", js.Headers.CacheControl?.ToString());
        }
        finally
        {
            Directory.Delete(pasta, recursive: true);
        }
    }
}
