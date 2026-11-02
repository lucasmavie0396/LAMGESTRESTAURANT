using LAMGestRestaurant.Api.Seguranca;
using LAMGestRestaurant.Application.Sala;
using LAMGestRestaurant.Application.Seguranca;
using Microsoft.AspNetCore.Mvc;

namespace LAMGestRestaurant.Api.Controllers;

[ApiController]
[Route("api/zonas")]
public class ZonasController(ServicoSala servico) : ControllerBase
{
    [HttpGet]
    [ExigeAlgumaPermissao(Permissoes.MesasVer, Permissoes.MesasGerir, Permissoes.PosUsar)]
    public Task<IReadOnlyList<ZonaDto>> Listar(bool incluirInactivas = false, CancellationToken ct = default) =>
        servico.ListarZonasAsync(incluirInactivas, ct);

    [HttpPost]
    [ExigePermissao(Permissoes.MesasGerir)]
    public async Task<ActionResult<ZonaDto>> Criar(GuardarZona dados)
    {
        var criada = await servico.CriarZonaAsync(dados);
        return Created($"api/zonas/{criada.Id}", criada);
    }

    [HttpPut("{id:int}")]
    [ExigePermissao(Permissoes.MesasGerir)]
    public Task<ZonaDto> Editar(int id, GuardarZona dados) => servico.EditarZonaAsync(id, dados);

    [HttpPost("{id:int}/activo")]
    [ExigePermissao(Permissoes.MesasGerir)]
    public async Task<IActionResult> DefinirActiva(int id, AlterarActivo dados)
    {
        await servico.DefinirZonaActivaAsync(id, dados.Activo);
        return NoContent();
    }
}

[ApiController]
[Route("api/mesas")]
public class MesasController(ServicoSala servico) : ControllerBase
{
    [HttpGet]
    [ExigeAlgumaPermissao(Permissoes.MesasVer, Permissoes.MesasGerir, Permissoes.PosUsar)]
    public Task<IReadOnlyList<MesaDto>> Listar(int? zonaId, bool incluirInactivas = false, CancellationToken ct = default) =>
        servico.ListarMesasAsync(zonaId, incluirInactivas, ct);

    [HttpGet("{id:int}")]
    [ExigeAlgumaPermissao(Permissoes.MesasVer, Permissoes.MesasGerir, Permissoes.PosUsar)]
    public Task<MesaDto> Obter(int id) => servico.ObterMesaAsync(id);

    [HttpPost]
    [ExigePermissao(Permissoes.MesasGerir)]
    public async Task<ActionResult<MesaDto>> Criar(GuardarMesa dados)
    {
        var criada = await servico.CriarMesaAsync(dados);
        return CreatedAtAction(nameof(Obter), new { id = criada.Id }, criada);
    }

    [HttpPut("{id:int}")]
    [ExigePermissao(Permissoes.MesasGerir)]
    public Task<MesaDto> Editar(int id, GuardarMesa dados) => servico.EditarMesaAsync(id, dados);

    [HttpPost("{id:int}/estado")]
    [ExigePermissao(Permissoes.MesasGerir)]
    public Task<MesaDto> AlterarEstado(int id, AlterarEstadoMesa dados) => servico.AlterarEstadoAsync(id, dados);

    [HttpPost("{id:int}/activo")]
    [ExigePermissao(Permissoes.MesasGerir)]
    public Task<MesaDto> DefinirActiva(int id, AlterarActivo dados) => servico.DefinirMesaActivaAsync(id, dados.Activo, dados.RowVersion);
}
