using LAMGestRestaurant.Api.Seguranca;
using LAMGestRestaurant.Application.Seguranca;
using LAMGestRestaurant.Application.Sistema;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LAMGestRestaurant.Api.Controllers;

/// <summary>Identidade desta loja: pedida pelos outros servidores (ao ligar lojas) e pelas apps. Sem sessão.</summary>
[ApiController]
[Route("api/loja")]
public class LojaController(ServicoLojas lojas) : ControllerBase
{
    [HttpGet]
    [AllowAnonymous]
    public Task<IdentidadeLoja> Identidade(CancellationToken ct) => lojas.IdentidadeAsync(ct);
}

/// <summary>
/// Lojas ligadas pela VPN (outros servidores do mesmo dono). A lista é pública para o seletor do ecrã de entrada
/// (só contém endereços da VPN); acrescentar, importar e retirar exigem configuracoes.gerir.
/// </summary>
[ApiController]
[Route("api/lojas")]
public class LojasController(ServicoLojas lojas) : ControllerBase
{
    [HttpGet]
    [AllowAnonymous]
    public Task<ListaLojas> Listar(CancellationToken ct) => lojas.ListarAsync(ct);

    [HttpPost]
    [ExigePermissao(Permissoes.ConfiguracoesGerir)]
    public Task<LojaDto> Acrescentar(NovaLoja dados, CancellationToken ct) => lojas.AcrescentarAsync(dados, ct);

    /// <summary>Copia a lista de lojas de outro servidor (essa loja e as que ela conhece).</summary>
    [HttpPost("importar")]
    [ExigePermissao(Permissoes.ConfiguracoesGerir)]
    public Task<ListaLojas> Importar(NovaLoja dados, CancellationToken ct) => lojas.ImportarAsync(dados, ct);

    [HttpDelete("{id:int}")]
    [ExigePermissao(Permissoes.ConfiguracoesGerir)]
    public async Task<IActionResult> Retirar(int id, CancellationToken ct)
    {
        await lojas.RetirarAsync(id, ct);
        return NoContent();
    }
}
