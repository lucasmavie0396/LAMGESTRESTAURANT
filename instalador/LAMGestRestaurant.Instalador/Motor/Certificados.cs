using System.Net;
using System.Net.Http.Json;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;

namespace LAMGestRestaurant.Instalador.Motor;

/// <summary>Certificado HTTPS do servidor (pfx lido pelo serviço) e a autoridade local que o assinou.</summary>
public sealed record CertificadoServidor(string FicheiroPfx, string Senha, string FicheiroCa, string ImpressaoDigitalCa, DateTime ValidoAte, IReadOnlyList<string> Nomes);

/// <summary>O que o servidor publica em /api/sistema/certificado (HTTP, sem sessão): a autoridade local e a porta HTTPS.</summary>
public sealed record HttpsServidor(int Porta, X509Certificate2 Ca, string ImpressaoDigital, bool SoHttps);

/// <summary>
/// HTTPS na rede local sem depender da internet: cada servidor tem a sua autoridade de certificação («LAMGest Restaurant -
/// autoridade local»), criada pelo Setup e guardada só para administradores. Ela assina o certificado do servidor (nome do
/// computador e todos os endereços IP, incluindo o da VPN). Os postos instalam a autoridade como confiável; o PDA guarda-a.
/// </summary>
public static class Certificados
{
    public const string Organizacao = "LAMGest Restaurant";
    public const string NomeCa = "LAMGest Restaurant - autoridade local";
    public const int PortaHttpsPadrao = 8443;
    private const string OidAutenticacaoServidor = "1.3.6.1.5.5.7.3.1";

    /// <summary>Validade do certificado do servidor (a autoridade local dura 20 anos).</summary>
    public static readonly TimeSpan ValidadeServidor = TimeSpan.FromDays(3 * 365);

    public static string Pasta(string pastaDados) => Path.Combine(pastaDados, "certificados");

    /// <summary>Impressão digital SHA-256 do certificado (hexadecimal, sem separadores), a mesma que o PDA confirma.</summary>
    public static string ImpressaoDigital(X509Certificate2 c) => Convert.ToHexString(SHA256.HashData(c.RawData));

    /// <summary>Impressão digital legível (grupos de 4), para comparar no ecrã do servidor e do posto.</summary>
    public static string Legivel(string impressao) =>
        string.Join(' ', Enumerable.Range(0, Math.Min(impressao.Length, 32) / 4).Select(i => impressao.Substring(i * 4, 4)));

    /// <summary>Autoridade local deste servidor: reutiliza a existente (os postos já confiam nela) ou cria uma nova.</summary>
    public static X509Certificate2 ObterOuCriarCa(string pastaCa, Func<DateTime>? agora = null)
    {
        var hoje = (agora ?? (() => DateTime.UtcNow))();
        var ficheiro = Path.Combine(pastaCa, "autoridade.pfx");
        if (File.Exists(ficheiro))
        {
            var existente = X509CertificateLoader.LoadPkcs12FromFile(ficheiro, null, X509KeyStorageFlags.Exportable);
            if (existente.HasPrivateKey && existente.NotAfter.ToUniversalTime() > hoje.AddYears(4))
                return existente;
            existente.Dispose();
        }
        using var chave = RSA.Create(3072);
        var pedido = new CertificateRequest($"CN={NomeCa} ({Environment.MachineName}), O={Organizacao}", chave, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        pedido.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, true, 0, true));
        pedido.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
        pedido.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(pedido.PublicKey, false));
        using var ca = pedido.CreateSelfSigned(hoje.AddDays(-1), hoje.AddYears(20));
        Directory.CreateDirectory(pastaCa);
        File.WriteAllBytes(ficheiro, ca.Export(X509ContentType.Pkcs12));
        return X509CertificateLoader.LoadPkcs12(ca.Export(X509ContentType.Pkcs12), null, X509KeyStorageFlags.Exportable);
    }

    /// <summary>Nomes e endereços que o certificado do servidor cobre: o computador, localhost e todos os IPv4 activos.</summary>
    public static IReadOnlyList<string> NomesDoServidor(IEnumerable<string> enderecos)
    {
        List<string> nomes = [Environment.MachineName.ToLowerInvariant(), "localhost"];
        try
        {
            var fqdn = Dns.GetHostEntry("").HostName.ToLowerInvariant();
            if (!nomes.Contains(fqdn))
                nomes.Add(fqdn);
        }
        catch (System.Net.Sockets.SocketException)
        {
        }
        nomes.Add("127.0.0.1");
        nomes.AddRange(enderecos.Where(e => !nomes.Contains(e)));
        return nomes;
    }

    /// <summary>
    /// Emite o certificado do servidor, assinado pela autoridade local, e grava-o em <paramref name="pastaConfig"/>
    /// (servidor.pfx, com palavra-passe aleatória, e autoridade.cer, a parte pública que os postos descarregam).
    /// </summary>
    public static CertificadoServidor EmitirServidor(X509Certificate2 ca, IReadOnlyList<string> nomes, string pastaConfig, Func<DateTime>? agora = null)
    {
        var hoje = (agora ?? (() => DateTime.UtcNow))();
        using var chave = RSA.Create(2048);
        var pedido = new CertificateRequest($"CN={nomes[0]}, O={Organizacao}", chave, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var san = new SubjectAlternativeNameBuilder();
        foreach (var n in nomes)
        {
            if (IPAddress.TryParse(n, out var ip))
                san.AddIpAddress(ip);
            else
                san.AddDnsName(n);
        }
        pedido.CertificateExtensions.Add(san.Build());
        pedido.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        pedido.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
        pedido.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid(OidAutenticacaoServidor)], false));
        pedido.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(pedido.PublicKey, false));
        pedido.CertificateExtensions.Add(X509AuthorityKeyIdentifierExtension.CreateFromCertificate(ca, true, false));

        var fim = hoje.Add(ValidadeServidor);
        if (fim > ca.NotAfter.ToUniversalTime())
            fim = ca.NotAfter.ToUniversalTime();
        var serie = RandomNumberGenerator.GetBytes(16);
        serie[0] &= 0x7F;
        using var publico = pedido.Create(ca, hoje.AddDays(-1), fim, serie);
        using var completo = publico.CopyWithPrivateKey(chave);

        var senha = Segredos.GerarPalavraPasse(32);
        Directory.CreateDirectory(pastaConfig);
        var pfx = Path.Combine(pastaConfig, "servidor.pfx");
        var cer = Path.Combine(pastaConfig, "autoridade.cer");
        File.WriteAllBytes(pfx, completo.Export(X509ContentType.Pkcs12, senha));
        File.WriteAllBytes(cer, ca.Export(X509ContentType.Cert));
        return new CertificadoServidor(pfx, senha, cer, ImpressaoDigital(ca), fim, nomes);
    }

    /// <summary>Confia na autoridade local neste computador (Autoridades de Certificação de Raiz Fidedigna do computador).</summary>
    public static void ConfiarNaAutoridade(X509Certificate2 ca)
    {
        using var raiz = new X509Store(StoreName.Root, StoreLocation.LocalMachine);
        raiz.Open(OpenFlags.ReadWrite);
        using var publico = X509CertificateLoader.LoadCertificate(ca.Export(X509ContentType.Cert));
        if (raiz.Certificates.Find(X509FindType.FindByThumbprint, publico.Thumbprint, false).Count == 0)
            raiz.Add(publico);
    }

    /// <summary>Retira deste computador todas as autoridades locais do LAMGest. Devolve quantas foram retiradas.</summary>
    public static int RetirarAutoridades()
    {
        using var raiz = new X509Store(StoreName.Root, StoreLocation.LocalMachine);
        raiz.Open(OpenFlags.ReadWrite);
        var nossas = raiz.Certificates.Cast<X509Certificate2>().Where(EDoLamgest).ToList();
        foreach (var c in nossas)
            raiz.Remove(c);
        return nossas.Count;
    }

    public static bool ConfiaNoLamgest()
    {
        using var raiz = new X509Store(StoreName.Root, StoreLocation.LocalMachine);
        raiz.Open(OpenFlags.ReadOnly);
        return raiz.Certificates.Cast<X509Certificate2>().Any(EDoLamgest);
    }

    private static bool EDoLamgest(X509Certificate2 c) =>
        c.Subject.Contains(NomeCa, StringComparison.Ordinal) && c.Subject.Contains("O=" + Organizacao, StringComparison.Ordinal);

    /// <summary>
    /// Validação do certificado do servidor só pela autoridade indicada (antes de ela estar instalada no computador):
    /// tem de ser assinado por ela, estar dentro da validade e incluir o endereço usado.
    /// </summary>
    public static Func<HttpRequestMessage, X509Certificate2?, X509Chain?, SslPolicyErrors, bool> ValidarCom(X509Certificate2 ca) =>
        (_, certificado, _, erros) =>
        {
            if (certificado is null || erros.HasFlag(SslPolicyErrors.RemoteCertificateNameMismatch) || erros.HasFlag(SslPolicyErrors.RemoteCertificateNotAvailable))
                return false;
            using var cadeia = new X509Chain();
            cadeia.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
            cadeia.ChainPolicy.CustomTrustStore.Add(ca);
            cadeia.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
            return cadeia.Build(certificado);
        };

    /// <summary>Cliente HTTP para o servidor: em HTTPS, aceita só certificados da autoridade local indicada.</summary>
    public static HttpClient Cliente(HttpsServidor? https, TimeSpan espera, CookieContainer? cookies = null)
    {
        var m = new HttpClientHandler();
        if (cookies is not null)
        {
            m.CookieContainer = cookies;
            m.UseCookies = true;
        }
        if (https is not null)
            m.ServerCertificateCustomValidationCallback = ValidarCom(https.Ca);
        return new HttpClient(m, disposeHandler: true) { Timeout = espera };
    }

    /// <summary>
    /// Lê do servidor (em HTTP) a autoridade local e a porta HTTPS. Null se o servidor não tiver HTTPS (ou for de uma versão
    /// anterior). A impressão digital mostra-se no fim da instalação do servidor, para comparar.
    /// </summary>
    public static async Task<HttpsServidor?> LerDoServidorAsync(string servidor, int porta, CancellationToken ct = default)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(6) };
        using var r = await http.GetAsync(InstalacaoPosto.Endereco(servidor, porta, "api/sistema/certificado"), ct);
        if (r.StatusCode == HttpStatusCode.NotFound)
            return null;
        r.EnsureSuccessStatusCode();
        var d = await r.Content.ReadFromJsonAsync<JsonElement>(ct);
        if (!d.TryGetProperty("https", out var h) || !h.GetBoolean())
            return null;
        var ca = X509CertificateLoader.LoadCertificate(Convert.FromBase64String(d.GetProperty("certificado").GetString()!));
        var impressao = ImpressaoDigital(ca);
        if (!string.Equals(impressao, d.GetProperty("impressaoDigital").GetString(), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("O certificado recebido do servidor não confere. Instale o posto de novo; se repetir, verifique a rede.");
        return new HttpsServidor(d.GetProperty("porta").GetInt32(), ca, impressao, d.TryGetProperty("soHttps", out var s) && s.GetBoolean());
    }
}
