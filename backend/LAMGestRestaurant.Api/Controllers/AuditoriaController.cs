using LAMGestRestaurant.Api.Seguranca;
using LAMGestRestaurant.Application.Comum;
using LAMGestRestaurant.Application.Seguranca;
using LAMGestRestaurant.Application.Sistema;
using Microsoft.AspNetCore.Mvc;

namespace LAMGestRestaurant.Api.Controllers;

/// <summary>Menu Auditoria: consulta dos registos (só leitura; não há endpoints para alterar ou apagar).</summary>
[ApiController]
[Route("api/auditoria")]
[ExigePermissao(Permissoes.AuditoriaVer)]
public class AuditoriaController(ServicoConsultaAuditoria auditoria) : ControllerBase
{
    [HttpGet]
    public Task<Pagina<RegistoAuditoriaDto>> Listar([FromQuery] FiltroAuditoria filtro, CancellationToken ct) => auditoria.ListarAsync(filtro, ct);

    [HttpGet("opcoes")]
    public Task<OpcoesAuditoria> Opcoes(CancellationToken ct) => auditoria.OpcoesAsync(ct);
}
