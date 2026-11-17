using LAMGestRestaurant.Api.Seguranca;
using LAMGestRestaurant.Application.Caixa;
using LAMGestRestaurant.Application.Impressao;
using LAMGestRestaurant.Application.Seguranca;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LAMGestRestaurant.Api.Controllers;

/// <summary>
/// Visor do cliente: o POS diz que pedido está no ecrã do posto; a página do visor (segundo monitor ou tablet virado para o cliente)
/// vai buscar o conteúdo sem sessão, só com a chave do seu endereço.
/// </summary>
[ApiController]
[Route("api/visor")]
public class VisorController(ServicoVisor visor) : ControllerBase
{
    private static readonly TimeSpan EsperaMaxima = TimeSpan.FromSeconds(25);

    [HttpPost]
    [ExigePermissao(Permissoes.PosUsar)]
    public async Task<IActionResult> Actualizar(ActualizarVisor dados, CancellationToken ct)
    {
        await visor.ActualizarAsync(dados, ct);
        return NoContent();
    }

    /// <summary>Conteúdo do visor; sem mudanças, espera até <paramref name="espera"/> segundos (pedido longo).</summary>
    [HttpGet("ecra")]
    [AllowAnonymous]
    public async Task<IActionResult> Ecra(int terminal, string? chave, long versao = 0, int espera = 0, CancellationToken ct = default)
    {
        try
        {
            return await visor.ParaEcraAsync(terminal, chave, versao, TimeSpan.FromSeconds(Math.Clamp(espera, 0, (int)EsperaMaxima.TotalSeconds)), ct) is { } ecra
                ? Ok(ecra)
                : Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Visor desligado",
                    detail: "Este endereço do visor já não serve (o visor do posto foi desligado ou o endereço foi trocado).");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return NoContent();
        }
    }

    [HttpGet("logotipo")]
    [AllowAnonymous]
    public async Task<IActionResult> Logotipo(int terminal, string? chave, [FromServices] ServicoImpressao impressao, CancellationToken ct)
    {
        if (!await visor.ChaveValidaAsync(terminal, chave, ct))
            return Unauthorized();
        return await impressao.LogotipoAsync(ct) is { } dados ? File(dados, "image/png") : NoContent();
    }
}
