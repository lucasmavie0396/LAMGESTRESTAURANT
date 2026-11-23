using System.Net;
using System.Net.Http.Json;
using LAMGestRestaurant.Api.Controllers;
using LAMGestRestaurant.Application.Seguranca;
using LAMGestRestaurant.Tests.Infraestrutura;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace LAMGestRestaurant.Tests;

/// <summary>APK da app de venda servida pelo próprio servidor (QR em Configurações → Postos), sem WhatsApp nem internet.</summary>
[Collection(ColeccaoBaseDados.Nome)]
public class AppVendaTests(AplicacaoTeste app)
{
    private static string ApkDeTeste()
    {
        var ficheiro = Path.Combine(Path.GetTempPath(), $"lamgest-venda-{Guid.NewGuid():N}.apk");
        File.WriteAllBytes(ficheiro, [0x50, 0x4B, 0x03, 0x04, .. Enumerable.Range(0, 2000).Select(i => (byte)i)]);
        return ficheiro;
    }

    [Fact]
    public async Task Sem_a_APK_no_servidor_a_descarga_da_404_e_o_quadro_diz_que_nao_esta_disponivel()
    {
        await using var semApk = app.WithWebHostBuilder(b => b.UseSetting("AppVenda:Ficheiro", Path.Combine(Path.GetTempPath(), $"nao-existe-{Guid.NewGuid():N}.apk")));
        Assert.Equal(HttpStatusCode.NotFound, (await semApk.CreateClient().GetAsync(AppVenda.Rota)).StatusCode);

        var cliente = semApk.CreateClient();
        var nome = await app.CriarUtilizadorAsync(PerfisPadrao.Administrador);
        (await cliente.PostAsJsonAsync("/api/auth/login", new { nomeUtilizador = nome, palavraPasse = AplicacaoTeste.PalavraPasseTeste })).EnsureSuccessStatusCode();
        var info = await cliente.GetFromJsonAsync<AppVendaServidor>("/api/configuracoes/postos/app-venda", AplicacaoTeste.Json);
        Assert.False(info!.Disponivel);
        Assert.Null(info.Tamanho);
    }

    [Fact]
    public async Task A_APK_descarrega_sem_sessao_como_app_Android_e_o_quadro_da_o_tamanho_e_enderecos_em_http()
    {
        var ficheiro = ApkDeTeste();
        try
        {
            await using var comApk = app.WithWebHostBuilder(b => b.UseSetting("AppVenda:Ficheiro", ficheiro));
            var anonimo = comApk.CreateClient();
            var r = await anonimo.GetAsync(AppVenda.Rota);
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
            Assert.Equal("application/vnd.android.package-archive", r.Content.Headers.ContentType?.MediaType);
            Assert.Equal("LAMGest-Venda.apk", r.Content.Headers.ContentDisposition?.FileName?.Trim('"'));
            Assert.Equal(await File.ReadAllBytesAsync(ficheiro), await r.Content.ReadAsByteArrayAsync());

            // A informação do quadro exige sessão com permissão de configurações.
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonimo.GetAsync("/api/configuracoes/postos/app-venda")).StatusCode);
            var nome = await app.CriarUtilizadorAsync(PerfisPadrao.Garcom);
            var garcom = comApk.CreateClient();
            (await garcom.PostAsJsonAsync("/api/auth/login", new { nomeUtilizador = nome, palavraPasse = AplicacaoTeste.PalavraPasseTeste })).EnsureSuccessStatusCode();
            Assert.Equal(HttpStatusCode.Forbidden, (await garcom.GetAsync("/api/configuracoes/postos/app-venda")).StatusCode);

            var admin = comApk.CreateClient();
            var nomeAdmin = await app.CriarUtilizadorAsync(PerfisPadrao.Administrador);
            (await admin.PostAsJsonAsync("/api/auth/login", new { nomeUtilizador = nomeAdmin, palavraPasse = AplicacaoTeste.PalavraPasseTeste })).EnsureSuccessStatusCode();
            var info = await admin.GetFromJsonAsync<AppVendaServidor>("/api/configuracoes/postos/app-venda", AplicacaoTeste.Json);
            Assert.True(info!.Disponivel);
            Assert.Equal(new FileInfo(ficheiro).Length, info.Tamanho);
            Assert.All(info.Enderecos, e => Assert.Matches(@"^http://[\d.]+:\d+/api/app-venda/LAMGest-Venda\.apk$", e));
        }
        finally
        {
            File.Delete(ficheiro);
        }
    }

    [Fact]
    public async Task Com_so_https_a_descarga_da_APK_continua_em_http()
    {
        var ficheiro = ApkDeTeste();
        var ca = Path.Combine(Path.GetTempPath(), $"lamgest-ca-{Guid.NewGuid():N}.cer");
        try
        {
            using (var chave = System.Security.Cryptography.RSA.Create(2048))
            {
                var pedido = new System.Security.Cryptography.X509Certificates.CertificateRequest("CN=LAMGest TESTE", chave,
                    System.Security.Cryptography.HashAlgorithmName.SHA256, System.Security.Cryptography.RSASignaturePadding.Pkcs1);
                using var cert = pedido.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
                await File.WriteAllBytesAsync(ca, cert.Export(System.Security.Cryptography.X509Certificates.X509ContentType.Cert));
            }
            await using var cifrada = app.WithWebHostBuilder(b => b
                .UseSetting("AppVenda:Ficheiro", ficheiro)
                .UseSetting("Seguranca:PortaHttps", "8443")
                .UseSetting("Seguranca:CertificadoCa", ca)
                .UseSetting("Seguranca:SoHttps", "true"));
            var http = cifrada.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
            Assert.Equal(HttpStatusCode.OK, (await http.GetAsync(AppVenda.Rota)).StatusCode);
            Assert.Equal(HttpStatusCode.PermanentRedirect, (await http.GetAsync("/pos")).StatusCode);
        }
        finally
        {
            File.Delete(ficheiro);
            File.Delete(ca);
        }
    }
}
