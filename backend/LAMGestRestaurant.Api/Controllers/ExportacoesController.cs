using System.ComponentModel.DataAnnotations;
using LAMGestRestaurant.Application.Comum;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LAMGestRestaurant.Api.Controllers;

/// <summary>Exportação de uma lista (o ficheiro é gerado no posto com os dados que o utilizador já pode ver).</summary>
public sealed record RegistoExportacao(
    [Required, StringLength(120)] string Lista,
    [Required, RegularExpression("^(xlsx|csv|pdf)$", ErrorMessage = "Formato inválido.")] string Formato,
    [Range(0, 1_000_000)] int Linhas,
    [StringLength(1000)] string? Filtros);

/// <summary>
/// Regista na auditoria cada exportação de listas (Excel, CSV ou PDF): quem, que lista, com que filtros e quantas linhas.
/// Os dados vêm dos endpoints de cada lista, com as permissões de cada um.
/// </summary>
[ApiController]
[Route("api/exportacoes")]
[Authorize]
public class ExportacoesController(IAuditoria auditoria, IAppDbContext db) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Registar(RegistoExportacao dados)
    {
        auditoria.Registar("lista.exportar", "Lista", null, new { dados.Lista, dados.Formato, dados.Linhas, dados.Filtros });
        await db.SaveChangesAsync();
        return NoContent();
    }
}
