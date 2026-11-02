using LAMGestRestaurant.Infrastructure.Dados;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LAMGestRestaurant.Api.Controllers;

public record EstadoSaude(bool Api, bool BaseDados, bool MigracoesPendentes, DateTime HoraServidorUtc);

/// <summary>Usado pelo frontend para o indicador de ligação. Não expõe detalhes da configuração.</summary>
[ApiController]
[Route("api/saude")]
[AllowAnonymous]
public class SaudeController(AppDbContext db, TimeProvider relogio, ILogger<SaudeController> log) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<EstadoSaude>> Get(CancellationToken ct)
    {
        var baseDados = false;
        var pendentes = false;
        try
        {
            baseDados = await db.Database.CanConnectAsync(ct);
            if (baseDados)
                pendentes = (await db.Database.GetPendingMigrationsAsync(ct)).Any();
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Verificação de saúde: base de dados inacessível");
        }

        var estado = new EstadoSaude(true, baseDados, pendentes, relogio.GetUtcNow().UtcDateTime);
        return baseDados && !pendentes ? Ok(estado) : StatusCode(StatusCodes.Status503ServiceUnavailable, estado);
    }
}
