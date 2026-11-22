using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace LAMGestRestaurant.Api.Seguranca;

/// <summary>
/// HTTPS na rede local (configurado pelo LAMGest-Setup em servidor.json):
/// <list type="bullet">
/// <item><c>Seguranca:PortaHttps</c> — porta HTTPS (o serviço atende também em HTTP, na porta de sempre);</item>
/// <item><c>Seguranca:CertificadoCa</c> — parte pública da autoridade local que assinou o certificado do servidor;</item>
/// <item><c>Seguranca:SoHttps</c> — depois de todos os postos e tablets passarem a HTTPS: o HTTP só reencaminha e o cookie
/// de sessão deixa de passar sem cifra.</item>
/// </list>
/// </summary>
public static class LigacaoCifrada
{
    /// <summary>Continuam em HTTP com «só HTTPS»: é por aqui que um posto ou tablet novo obtém a autoridade local para confiar nela.</summary>
    private static readonly PathString[] SempreEmHttp = ["/api/saude", "/api/sistema/certificado", "/api/sistema/certificado.cer"];

    public static int? PortaHttps(IConfiguration c) => c.GetValue<int?>("Seguranca:PortaHttps") is > 0 and <= 65535 and var p ? p : null;

    public static bool SoHttps(IConfiguration c) => c.GetValue<bool>("Seguranca:SoHttps") && PortaHttps(c) is not null;

    /// <summary>Autoridade local (null se o servidor não tiver HTTPS configurado ou o ficheiro não existir).</summary>
    public static X509Certificate2? Autoridade(IConfiguration c)
    {
        if (PortaHttps(c) is null || c["Seguranca:CertificadoCa"] is not { Length: > 0 } ficheiro || !File.Exists(ficheiro))
            return null;
        return X509CertificateLoader.LoadCertificateFromFile(ficheiro);
    }

    public static string ImpressaoDigital(X509Certificate2 c) => Convert.ToHexString(SHA256.HashData(c.RawData));

    /// <summary>Com «só HTTPS», os pedidos em HTTP são reencaminhados (308, mantém o método) para a porta HTTPS.</summary>
    public static IApplicationBuilder UseSoHttps(this IApplicationBuilder app, IConfiguration configuracao)
    {
        if (!SoHttps(configuracao))
            return app;
        var porta = PortaHttps(configuracao)!.Value;
        return app.Use(async (ctx, next) =>
        {
            var pedido = ctx.Request;
            if (pedido.IsHttps || SempreEmHttp.Any(c => pedido.Path.StartsWithSegments(c)))
            {
                await next();
                return;
            }
            var destino = $"https://{pedido.Host.Host}:{porta}{pedido.PathBase}{pedido.Path}{pedido.QueryString}";
            ctx.Response.StatusCode = StatusCodes.Status308PermanentRedirect;
            ctx.Response.Headers.Location = destino;
        });
    }
}
