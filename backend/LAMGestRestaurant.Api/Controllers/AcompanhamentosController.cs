using LAMGestRestaurant.Api.Seguranca;
using LAMGestRestaurant.Application.Catalogo;
using LAMGestRestaurant.Application.Seguranca;
using Microsoft.AspNetCore.Mvc;

namespace LAMGestRestaurant.Api.Controllers;

/// <summary>Grupos de acompanhamentos dos pratos (escolhidos no POS, sem preço, gastam stock).</summary>
[ApiController]
[Route("api/acompanhamentos")]
public class AcompanhamentosController(ServicoAcompanhamentos servico) : ControllerBase
{
    [HttpGet]
    [ExigeAlgumaPermissao(Permissoes.ProdutosVer, Permissoes.ProdutosGerir)]
    public Task<IReadOnlyList<GrupoAcompanhamentosDto>> Listar(bool incluirInactivos = false, CancellationToken ct = default) =>
        servico.ListarAsync(incluirInactivos, ct);

    /// <summary>Grupos e opções do prato, para o POS pedir a escolha.</summary>
    [HttpGet("produto/{produtoId:int}")]
    [ExigeAlgumaPermissao(Permissoes.ProdutosVer, Permissoes.PosUsar)]
    public Task<IReadOnlyList<GrupoPos>> DoProduto(int produtoId, CancellationToken ct) => servico.DoProdutoAsync(produtoId, ct);

    [HttpPost]
    [ExigePermissao(Permissoes.ProdutosGerir)]
    public async Task<ActionResult<GrupoAcompanhamentosDto>> Criar(GuardarGrupoAcompanhamentos dados)
    {
        var criado = await servico.CriarAsync(dados);
        return Created($"api/acompanhamentos/{criado.Id}", criado);
    }

    [HttpPut("{id:int}")]
    [ExigePermissao(Permissoes.ProdutosGerir)]
    public Task<GrupoAcompanhamentosDto> Editar(int id, GuardarGrupoAcompanhamentos dados) => servico.EditarAsync(id, dados);

    [HttpPost("{id:int}/activo")]
    [ExigePermissao(Permissoes.ProdutosGerir)]
    public async Task<IActionResult> DefinirActivo(int id, AlterarActivo dados)
    {
        await servico.DefinirActivoAsync(id, dados.Activo);
        return NoContent();
    }
}
