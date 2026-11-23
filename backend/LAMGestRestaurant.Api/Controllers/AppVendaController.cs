using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LAMGestRestaurant.Api.Controllers;

/// <summary>
/// APK da app de venda (Android) servida pelo próprio servidor: os telemóveis e tablets no Wi-Fi do restaurante descarregam-na
/// pelo QR de Configurações → Postos, sem WhatsApp nem internet. O Setup põe-na em <c>android\LAMGest-Venda.apk</c> da instalação.
/// </summary>
public static class AppVenda
{
    public const string NomeFicheiro = "LAMGest-Venda.apk";
    public const string Rota = "/api/app-venda/" + NomeFicheiro;
    public const string TipoConteudo = "application/vnd.android.package-archive";

    /// <summary><c>AppVenda:Ficheiro</c> (testes) ou a pasta android junto do programa.</summary>
    public static string Ficheiro(IConfiguration c) =>
        c["AppVenda:Ficheiro"] is { Length: > 0 } f ? f : Path.Combine(AppContext.BaseDirectory, "android", NomeFicheiro);
}

/// <summary>Descarga da APK. Sem sessão: o telemóvel ainda não tem utilizador (e a APK não tem segredos).</summary>
[ApiController]
[Route("api/app-venda")]
[AllowAnonymous]
public class AppVendaController(IConfiguration configuracao) : ControllerBase
{
    [HttpGet(AppVenda.NomeFicheiro)]
    [HttpHead(AppVenda.NomeFicheiro)]
    public IActionResult Descarregar()
    {
        var ficheiro = AppVenda.Ficheiro(configuracao);
        if (!System.IO.File.Exists(ficheiro))
            return NotFound();
        Response.Headers.CacheControl = "no-cache";
        return PhysicalFile(Path.GetFullPath(ficheiro), AppVenda.TipoConteudo, AppVenda.NomeFicheiro, enableRangeProcessing: true);
    }
}
