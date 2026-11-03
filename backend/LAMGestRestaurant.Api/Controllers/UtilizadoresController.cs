using LAMGestRestaurant.Api.Seguranca;
using LAMGestRestaurant.Application.Comum;
using LAMGestRestaurant.Application.Seguranca;
using LAMGestRestaurant.Infrastructure.Identidade;
using Microsoft.AspNetCore.Mvc;

namespace LAMGestRestaurant.Api.Controllers;

[ApiController]
[Route("api/utilizadores")]
[ExigePermissao(Permissoes.UtilizadoresGerir)]
public class UtilizadoresController(ServicoUtilizadores servico) : ControllerBase
{
    [HttpGet]
    public Task<Pagina<UtilizadorResumo>> Listar(
        string? pesquisa, bool? activo, string? ordenar, bool descendente = false, int pagina = 1, int tamanho = 25, CancellationToken ct = default) =>
        servico.ListarAsync(pesquisa, activo, ordenar, descendente, pagina, tamanho, ct);

    [HttpGet("{id:int}")]
    public Task<UtilizadorResumo> Obter(int id) => servico.ObterAsync(id);

    [HttpPost]
    public async Task<ActionResult<UtilizadorResumo>> Criar(CriarUtilizador dados)
    {
        var criado = await servico.CriarAsync(dados);
        return CreatedAtAction(nameof(Obter), new { id = criado.Id }, criado);
    }

    [HttpPut("{id:int}")]
    public Task<UtilizadorResumo> Editar(int id, EditarUtilizador dados) => servico.EditarAsync(id, dados);

    [HttpPost("{id:int}/activar")]
    public async Task<IActionResult> Activar(int id)
    {
        await servico.DefinirActivoAsync(id, true);
        return NoContent();
    }

    [HttpPost("{id:int}/desactivar")]
    public async Task<IActionResult> Desactivar(int id)
    {
        await servico.DefinirActivoAsync(id, false);
        return NoContent();
    }

    [HttpPost("{id:int}/repor-palavra-passe")]
    public async Task<IActionResult> ReporPalavraPasse(int id, ReporPalavraPasse dados)
    {
        await servico.ReporPalavraPasseAsync(id, dados.NovaPalavraPasse);
        return NoContent();
    }

    /// <summary>Código de entrada e presença no ecrã táctil (ou retirar o código).</summary>
    [HttpPost("{id:int}/pin")]
    public async Task<IActionResult> DefinirPin(int id, DefinirPin dados, [FromServices] ServicoPin pin)
    {
        await pin.DefinirAsync(id, dados);
        return NoContent();
    }

    [HttpPost("{id:int}/desbloquear")]
    public async Task<IActionResult> Desbloquear(int id)
    {
        await servico.DesbloquearAsync(id);
        return NoContent();
    }
}

[ApiController]
[Route("api/perfis")]
public class PerfisController(ServicoUtilizadores servico, IUtilizadorActual actual) : ControllerBase
{
    /// <summary>Acessível com perfis.gerir OU utilizadores.gerir (o formulário de utilizadores lista os perfis).</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<PerfilResumo>>> Listar(CancellationToken ct)
    {
        if (!actual.TemPermissao(Permissoes.PerfisGerir) && !actual.TemPermissao(Permissoes.UtilizadoresGerir))
            return Forbid();
        return Ok(await servico.ListarPerfisAsync(ct));
    }

    [HttpGet("permissoes")]
    [ExigePermissao(Permissoes.PerfisGerir)]
    public IReadOnlyList<PermissaoInfo> Catalogo() => ServicoUtilizadores.CatalogoPermissoes();

    [HttpPost]
    [ExigePermissao(Permissoes.PerfisGerir)]
    public async Task<ActionResult<PerfilResumo>> Criar(GuardarPerfil dados)
    {
        var criado = await servico.CriarPerfilAsync(dados);
        return Created($"api/perfis/{criado.Id}", criado);
    }

    [HttpPut("{id:int}")]
    [ExigePermissao(Permissoes.PerfisGerir)]
    public Task<PerfilResumo> Editar(int id, GuardarPerfil dados) => servico.EditarPerfilAsync(id, dados);

    [HttpDelete("{id:int}")]
    [ExigePermissao(Permissoes.PerfisGerir)]
    public async Task<IActionResult> Eliminar(int id)
    {
        await servico.EliminarPerfilAsync(id);
        return NoContent();
    }
}
