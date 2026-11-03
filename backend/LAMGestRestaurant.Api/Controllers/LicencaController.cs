using LAMGestRestaurant.Api.Seguranca;
using LAMGestRestaurant.Application.Licencas;
using LAMGestRestaurant.Application.Seguranca;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LAMGestRestaurant.Api.Controllers;

/// <summary>Licença do sistema. O estado é público (ecrã de entrada e avisos); activar exige configuracoes.gerir.</summary>
[ApiController]
[Route("api/licenca")]
public class LicencaController(ServicoLicenca licenca) : ControllerBase
{
    [HttpGet("estado")]
    [AllowAnonymous]
    public Task<EstadoLicencaDto> Estado(CancellationToken ct) => licenca.EstadoAsync(ct);

    [HttpGet("historico")]
    [ExigePermissao(Permissoes.ConfiguracoesGerir)]
    public Task<IReadOnlyList<LicencaDto>> Historico(CancellationToken ct) => licenca.HistoricoAsync(ct);

    /// <summary>Activa a licença emitida pelo gerador da LAM company (texto «LAMGEST1.…» ou o conteúdo do ficheiro .lic).</summary>
    [HttpPost("activar")]
    [ExigePermissao(Permissoes.ConfiguracoesGerir)]
    public Task<EstadoLicencaDto> Activar(ActivarLicenca dados) => licenca.ActivarAsync(dados.Texto);
}
