using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using LAMGestRestaurant.Infrastructure.Sistema;
using LAMGestRestaurant.Tests.Infraestrutura;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace LAMGestRestaurant.Tests;

/// <summary>HTTPS na rede local: a autoridade local publicada para os postos e o modo «só HTTPS».</summary>
[Collection(ColeccaoBaseDados.Nome)]
public class LigacaoCifradaTests(AplicacaoTeste app)
{
    private static (string Ficheiro, string Impressao) AutoridadeDeTeste()
    {
        using var chave = RSA.Create(2048);
        var pedido = new CertificateRequest("CN=LAMGest Restaurant - autoridade local (TESTE), O=LAMGest Restaurant", chave, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        pedido.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        using var ca = pedido.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
        var ficheiro = Path.Combine(Path.GetTempPath(), $"lamgest-ca-{Guid.NewGuid():N}.cer");
        File.WriteAllBytes(ficheiro, ca.Export(X509ContentType.Cert));
        return (ficheiro, Convert.ToHexString(SHA256.HashData(ca.RawData)));
    }

    [Fact]
    public async Task Sem_https_configurado_o_servidor_diz_que_so_tem_http()
    {
        var r = await app.CreateClient().GetFromJsonAsync<JsonElement>("/api/sistema/certificado");
        Assert.False(r.GetProperty("https").GetBoolean());
        Assert.Equal(HttpStatusCode.NotFound, (await app.CreateClient().GetAsync("/api/sistema/certificado.cer")).StatusCode);
    }

    [Fact]
    public async Task Com_https_publica_a_autoridade_local_e_com_so_https_reencaminha_o_http()
    {
        var (ficheiro, impressao) = AutoridadeDeTeste();
        try
        {
            await using var cifrada = app.WithWebHostBuilder(b => b
                .UseSetting("Seguranca:PortaHttps", "8443")
                .UseSetting("Seguranca:CertificadoCa", ficheiro)
                .UseSetting("Seguranca:SoHttps", "true"));
            var http = cifrada.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

            // A autoridade continua disponível em HTTP (é assim que um posto novo passa a confiar no servidor).
            var info = await http.GetFromJsonAsync<JsonElement>("/api/sistema/certificado");
            Assert.True(info.GetProperty("https").GetBoolean());
            Assert.Equal(8443, info.GetProperty("porta").GetInt32());
            Assert.True(info.GetProperty("soHttps").GetBoolean());
            Assert.Equal(impressao, info.GetProperty("impressaoDigital").GetString());
            var der = Convert.FromBase64String(info.GetProperty("certificado").GetString()!);
            Assert.Equal(impressao, Convert.ToHexString(SHA256.HashData(der)));
            var cer = await http.GetAsync("/api/sistema/certificado.cer");
            Assert.Equal("application/pkix-cert", cer.Content.Headers.ContentType?.MediaType);
            Assert.Equal(HttpStatusCode.OK, (await http.GetAsync("/api/saude")).StatusCode);

            // O resto passa para HTTPS, com o mesmo método (308).
            var pos = await http.GetAsync("/pos?posto=3");
            Assert.Equal(HttpStatusCode.PermanentRedirect, pos.StatusCode);
            Assert.Equal("https://localhost:8443/pos?posto=3", pos.Headers.Location?.ToString());
            Assert.Equal(HttpStatusCode.PermanentRedirect, (await http.PostAsJsonAsync("/api/auth/login", new { nomeUtilizador = "x", palavraPasse = "y" })).StatusCode);

            // Em HTTPS atende normalmente e o cookie de sessão só segue em HTTPS.
            var https = cifrada.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
            Assert.Equal(HttpStatusCode.OK, (await https.GetAsync("/api/saude")).StatusCode);
            var nome = await app.CriarUtilizadorAsync(Application.Seguranca.PerfisPadrao.Garcom);
            var login = await https.PostAsJsonAsync("/api/auth/login", new { nomeUtilizador = nome, palavraPasse = AplicacaoTeste.PalavraPasseTeste });
            Assert.Equal(HttpStatusCode.OK, login.StatusCode);
            Assert.Contains(login.Headers.GetValues("Set-Cookie"), c => c.StartsWith("LAMGest.Sessao=", StringComparison.Ordinal) && c.Contains("secure", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            File.Delete(ficheiro);
        }
    }

    [Fact]
    public async Task Https_sem_so_https_mantem_o_http_a_funcionar()
    {
        var (ficheiro, _) = AutoridadeDeTeste();
        try
        {
            await using var transicao = app.WithWebHostBuilder(b => b
                .UseSetting("Seguranca:PortaHttps", "8443")
                .UseSetting("Seguranca:CertificadoCa", ficheiro));
            var http = transicao.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
            Assert.False((await http.GetFromJsonAsync<JsonElement>("/api/sistema/certificado")).GetProperty("soHttps").GetBoolean());
            Assert.NotEqual(HttpStatusCode.PermanentRedirect, (await http.GetAsync("/pos")).StatusCode);
        }
        finally
        {
            File.Delete(ficheiro);
        }
    }

    [Fact]
    public void Certificado_a_expirar_ou_sem_o_endereco_actual_da_aviso()
    {
        var agora = new DateTime(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc);
        LeituraSistema Leitura(EstadoCertificado c) => new("LAMGestRestaurantDB", agora.AddDays(-30),
            new EstadoCopias(agora.AddHours(-12), true, "ok", agora.AddHours(-12), "x.bak", 10),
            new EstadoEspaco(500, ServicoEstadoSistema.LimiteExpressMb, "C:", 50_000, 100_000), c);

        Assert.Empty(ServicoEstadoSistema.Avisos(Leitura(new EstadoCertificado(agora.AddYears(2), ["localhost"], [])), agora));
        Assert.Equal("Certificado HTTPS a expirar", Assert.Single(ServicoEstadoSistema.Avisos(Leitura(new EstadoCertificado(agora.AddDays(20), [], [])), agora)).Titulo);
        Assert.Equal("critico", Assert.Single(ServicoEstadoSistema.Avisos(Leitura(new EstadoCertificado(agora.AddDays(-1), [], [])), agora)).Nivel);
        var mudou = Assert.Single(ServicoEstadoSistema.Avisos(Leitura(new EstadoCertificado(agora.AddYears(2), [], ["192.168.1.20"])), agora));
        Assert.Contains("192.168.1.20", mudou.Texto);
    }
}
