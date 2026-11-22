using System.Net;
using System.Security.Cryptography.X509Certificates;
using LAMGestRestaurant.Instalador.Motor;

namespace LAMGestRestaurant.Instalador.Tests;

/// <summary>Certificados do HTTPS na rede local e a remoção do LAMGest (em pastas temporárias; nunca nas do Windows).</summary>
public class HttpsERemocaoTests
{
    private static string PastaTemporaria() => Path.Combine(Path.GetTempPath(), "lamgest-https-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Autoridade_local_e_reutilizada_e_o_certificado_cobre_os_enderecos_do_servidor()
    {
        var raiz = PastaTemporaria();
        try
        {
            using var ca = Certificados.ObterOuCriarCa(Path.Combine(raiz, "ca"));
            Assert.True(ca.HasPrivateKey);
            Assert.Contains("O=" + Certificados.Organizacao, ca.Subject);
            Assert.True(ca.NotAfter > DateTime.Now.AddYears(19));
            // Segunda instalação: a mesma autoridade (os postos continuam a confiar nela).
            using (var outra = Certificados.ObterOuCriarCa(Path.Combine(raiz, "ca")))
                Assert.Equal(ca.Thumbprint, outra.Thumbprint);

            var nomes = Certificados.NomesDoServidor(["192.168.1.10", "100.64.0.7"]);
            Assert.Contains("localhost", nomes);
            Assert.Contains("127.0.0.1", nomes);
            Assert.Contains("100.64.0.7", nomes);

            var c = Certificados.EmitirServidor(ca, nomes, Path.Combine(raiz, "config"));
            Assert.Equal(Certificados.ImpressaoDigital(ca), c.ImpressaoDigitalCa);
            using var servidor = X509CertificateLoader.LoadPkcs12FromFile(c.FicheiroPfx, c.Senha);
            Assert.True(servidor.HasPrivateKey);
            var san = servidor.Extensions.OfType<X509SubjectAlternativeNameExtension>().Single();
            Assert.Contains(IPAddress.Parse("192.168.1.10"), san.EnumerateIPAddresses());
            Assert.Contains("localhost", san.EnumerateDnsNames());
            Assert.True(servidor.NotAfter < DateTime.Now.AddYears(4));

            // A cadeia fecha só com a autoridade local; outra autoridade não serve.
            Assert.True(ConstroiCadeia(servidor, ca));
            using var estranha = Certificados.ObterOuCriarCa(Path.Combine(raiz, "outra"));
            Assert.False(ConstroiCadeia(servidor, estranha));

            // A parte pública (o que o servidor publica para os postos) não leva a chave privada.
            using var publica = X509CertificateLoader.LoadCertificateFromFile(c.FicheiroCa);
            Assert.False(publica.HasPrivateKey);
            Assert.Equal(ca.Thumbprint, publica.Thumbprint);
        }
        finally
        {
            if (Directory.Exists(raiz))
                Directory.Delete(raiz, true);
        }
    }

    [Fact]
    public void Autoridade_perto_do_fim_e_substituida()
    {
        var raiz = PastaTemporaria();
        try
        {
            string antiga;
            using (var ca = Certificados.ObterOuCriarCa(raiz, () => DateTime.UtcNow.AddYears(-17)))
                antiga = ca.Thumbprint;
            using var nova = Certificados.ObterOuCriarCa(raiz);
            Assert.NotEqual(antiga, nova.Thumbprint);
        }
        finally
        {
            Directory.Delete(raiz, true);
        }
    }

    [Fact]
    public void Impressao_digital_legivel_em_grupos()
    {
        Assert.Equal("ABCD EF01 2345 6789 ABCD EF01 2345 6789", Certificados.Legivel("ABCDEF0123456789ABCDEF0123456789FFFF"));
    }

    [Theory]
    [InlineData("ASPNETCORE_URLS=http://0.0.0.0:8080", new[] { 8080 })]
    [InlineData("ASPNETCORE_URLS=http://0.0.0.0:8080;https://0.0.0.0:8443", new[] { 8080, 8443 })]
    [InlineData("ASPNETCORE_URLS=http://*:5000", new[] { 5000 })]
    [InlineData(null, new int[0])]
    public void Portas_do_servico(string? urls, int[] esperadas) => Assert.Equal(esperadas, Desinstalacao.PortasDe(urls));

    [Fact]
    public void Enderecos_do_servico_com_e_sem_https()
    {
        Assert.Equal("http://0.0.0.0:8080", InstalacaoServidor.Enderecos(8080, null));
        Assert.Equal("http://0.0.0.0:8080;https://0.0.0.0:8443", InstalacaoServidor.Enderecos(8080, 8443));
    }

    [Fact]
    public async Task Remocao_do_posto_retira_atalhos_agente_e_programas_e_deixa_o_resto()
    {
        var raiz = PastaTemporaria();
        var atalhos = new PastasAtalhos(Path.Combine(raiz, "desk"), Path.Combine(raiz, "menu"), Path.Combine(raiz, "arranque"), Path.Combine(raiz, "dados"));
        var programa = Path.Combine(raiz, "ProgramFiles", "LAMGestRestaurant");
        var agente = new PastasAgente(Path.Combine(programa, "Agente"), atalhos.Dados, atalhos.Arranque);
        var locais = new LocaisRemocao(atalhos, agente, programa);
        try
        {
            void Criar(string caminho)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(caminho)!);
                File.WriteAllText(caminho, "x");
            }
            Criar(Path.Combine(atalhos.AmbienteTrabalho, InstalacaoPosto.NomeAtalho));
            Criar(Path.Combine(atalhos.MenuIniciar, InstalacaoPosto.NomeAtalho));
            Criar(Path.Combine(atalhos.MenuIniciar, Configurador.NomeAtalho));
            Criar(Path.Combine(atalhos.Arranque, AgentePosto.NomeAtalho));
            Criar(Path.Combine(agente.Programa, AgentePosto.NomeExecutavel));
            Criar(Path.Combine(programa, Configurador.NomeExecutavel));
            Criar(Path.Combine(atalhos.Dados, "posto.json"));
            Criar(Path.Combine(atalhos.Dados, AgentePosto.NomeConfiguracao));
            Criar(Path.Combine(atalhos.Dados, "navegador", "Default", "Preferences"));
            // Ficam: configuração, chaves e logs do servidor (e a base de dados, que o Setup nunca apaga).
            Criar(Path.Combine(atalhos.Dados, "config", "servidor.json"));
            Criar(Path.Combine(atalhos.Dados, "logs", "lamgest-20261121.log"));
            Criar(Path.Combine(atalhos.AmbienteTrabalho, "Outro programa.lnk"));

            var pecas = Desinstalacao.Procurar(locais, comSistema: false);
            Assert.True(pecas.Posto);
            Assert.False(pecas.Servidor);
            Assert.Equal(4, pecas.Atalhos.Count);
            Assert.True(pecas.Agente);
            Assert.Equal(programa, pecas.PastaPosto);
            Assert.Equal(3, pecas.FicheirosPosto.Count);

            var linhas = new List<string>();
            await Desinstalacao.RemoverAsync(pecas, locais, new Progresso(linhas), comSistema: false);

            Assert.False(Directory.Exists(programa));
            Assert.False(File.Exists(Path.Combine(atalhos.MenuIniciar, InstalacaoPosto.NomeAtalho)));
            Assert.False(File.Exists(Path.Combine(atalhos.Dados, AgentePosto.NomeConfiguracao)));
            Assert.False(Directory.Exists(Path.Combine(atalhos.Dados, "navegador")));
            Assert.True(File.Exists(Path.Combine(atalhos.Dados, "config", "servidor.json")));
            Assert.True(File.Exists(Path.Combine(atalhos.Dados, "logs", "lamgest-20261121.log")));
            Assert.True(File.Exists(Path.Combine(atalhos.AmbienteTrabalho, "Outro programa.lnk")));
            Assert.Contains(linhas, l => l.Contains("base de dados", StringComparison.Ordinal));
            Assert.True(Desinstalacao.Procurar(locais, comSistema: false).Nada);
        }
        finally
        {
            if (Directory.Exists(raiz))
                Directory.Delete(raiz, true);
        }
    }

    [Fact]
    public void Pasta_do_programa_com_o_servidor_nao_conta_como_programas_do_posto()
    {
        var raiz = PastaTemporaria();
        var programa = Path.Combine(raiz, "LAMGestRestaurant");
        try
        {
            Directory.CreateDirectory(programa);
            File.WriteAllText(Path.Combine(programa, "LAMGestRestaurant.Api.exe"), "x");
            File.WriteAllText(Path.Combine(programa, Configurador.NomeExecutavel), "x");
            var atalhos = new PastasAtalhos(Path.Combine(raiz, "d"), Path.Combine(raiz, "m"), Path.Combine(raiz, "a"), Path.Combine(raiz, "dados"));
            var pecas = Desinstalacao.Procurar(new LocaisRemocao(atalhos, new PastasAgente(Path.Combine(raiz, "ag"), atalhos.Dados, atalhos.Arranque), programa), comSistema: false);
            Assert.Null(pecas.PastaPosto);
        }
        finally
        {
            Directory.Delete(raiz, true);
        }
    }

    [Fact]
    public void Atalho_do_posto_em_https_usa_a_porta_https()
    {
        using var ca = Certificados.ObterOuCriarCa(PastaTemporaria());
        var https = new HttpsServidor(8443, ca, Certificados.ImpressaoDigital(ca), false);
        var opcoes = new OpcoesPosto("192.168.1.10", 8080, 3, PaginaInicial.Todas.Single(p => p.Caminho == "pos"), false, false, false, Https: https);
        Assert.Equal("https://192.168.1.10:8443/pos?posto=3&tactil=0", InstalacaoPosto.EnderecoPosto(opcoes).ToString());
        Assert.Equal("http://192.168.1.10:8080/pos?posto=3&tactil=0", InstalacaoPosto.EnderecoPosto(opcoes with { Https = null }).ToString());
    }

    private static bool ConstroiCadeia(X509Certificate2 servidor, X509Certificate2 ca)
    {
        using var cadeia = new X509Chain();
        cadeia.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        cadeia.ChainPolicy.CustomTrustStore.Add(ca);
        cadeia.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        return cadeia.Build(servidor);
    }

    private sealed class Progresso(List<string> linhas) : IProgress<string>
    {
        public void Report(string value) => linhas.Add(value);
    }
}
