using LAMGestRestaurant.Application.Comum;
using LAMGestRestaurant.Application.Impressao;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LAMGestRestaurant.Api.Controllers;

/// <summary>PDF das impressoras em modo PDF. Só quem pediu a impressão o pode abrir (dentro da aplicação, num visualizador).</summary>
[ApiController]
[Route("api/impressao")]
[Authorize]
public class ImpressaoController(DocumentosPdf documentos, IUtilizadorActual actual) : ControllerBase
{
    [HttpGet("pdf/{id:guid}")]
    public IActionResult Pdf(Guid id)
    {
        if (documentos.Obter(id, actual.Id) is not { } pdf)
            return NotFound();
        // Excepção ao DENY global: o visualizador da aplicação mostra o PDF numa moldura da mesma origem.
        Response.Headers.XFrameOptions = "SAMEORIGIN";
        Response.Headers.CacheControl = "no-store";
        var nome = string.Concat(pdf.Titulo.Split(Path.GetInvalidFileNameChars())).Replace('"', '\'');
        Response.Headers.ContentDisposition = $"inline; filename=\"{Uri.EscapeDataString(nome)}.pdf\"";
        return File(pdf.Dados, "application/pdf");
    }
}
