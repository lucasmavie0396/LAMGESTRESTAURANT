using LAMGestRestaurant.Application.Impressao;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace LAMGestRestaurant.Api.Controllers;

/// <summary>
/// Agente de impressão dos postos: sem sessão de utilizador; autentica-se com o número do posto (X-Posto)
/// e a chave dada no registo (X-Agente-Token). Imprime nas impressoras instaladas no Windows desse computador.
/// </summary>
[ApiController]
[Route("api/agente")]
[AllowAnonymous]
public class AgenteController(ServicoAgentes agentes) : ControllerBase, IAsyncActionFilter
{
    private static readonly TimeSpan EsperaMaxima = TimeSpan.FromSeconds(25);
    private int terminalId;

    [NonAction]
    public async Task OnActionExecutionAsync(ActionExecutingContext contexto, ActionExecutionDelegate seguinte)
    {
        var numero = int.TryParse(Request.Headers["X-Posto"], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var n) ? n : 0;
        if (numero <= 0 || await agentes.AutenticarAsync(numero, Request.Headers["X-Agente-Token"], HttpContext.RequestAborted) is not int id)
        {
            contexto.Result = Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Agente não autorizado",
                detail: "Posto ou chave do agente inválidos. Volte a instalar o agente com o LAMGest-Setup (modo Posto).");
            return;
        }
        terminalId = id;
        await seguinte();
    }

    /// <summary>Impressoras instaladas no posto e versão do agente.</summary>
    [HttpPost("estado")]
    public async Task<IActionResult> Estado(EstadoAgente estado, CancellationToken ct)
    {
        await agentes.ReportarAsync(terminalId, estado, ct);
        return NoContent();
    }

    /// <summary>Trabalhos por imprimir; sem nenhum, espera até <paramref name="espera"/> segundos (máx. 25) que chegue um.</summary>
    [HttpGet("trabalhos")]
    public async Task<IReadOnlyList<TrabalhoAgenteDto>> Trabalhos(int espera = 0, CancellationToken ct = default)
    {
        try
        {
            return await agentes.ProximosAsync(terminalId, TimeSpan.FromSeconds(Math.Clamp(espera, 0, (int)EsperaMaxima.TotalSeconds)), ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return []; // o agente desligou-se a meio da espera
        }
    }

    [HttpPost("trabalhos/{id:int}/resultado")]
    public async Task<IActionResult> Resultado(int id, ResultadoTrabalho resultado, CancellationToken ct)
    {
        await agentes.RegistarResultadoAsync(terminalId, id, resultado, ct);
        return NoContent();
    }

    /// <summary>Logótipo do estabelecimento (204 sem logótipo).</summary>
    [HttpGet("logotipo")]
    public async Task<IActionResult> Logotipo([FromServices] ServicoImpressao impressao, CancellationToken ct) =>
        await impressao.LogotipoAsync(ct) is { } dados ? File(dados, "application/octet-stream") : NoContent();
}
