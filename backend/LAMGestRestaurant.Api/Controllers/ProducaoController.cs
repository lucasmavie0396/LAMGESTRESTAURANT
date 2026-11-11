using System.ComponentModel.DataAnnotations;
using LAMGestRestaurant.Api.Seguranca;
using LAMGestRestaurant.Application.Comum;
using LAMGestRestaurant.Application.Seguranca;
using LAMGestRestaurant.Application.Sistema;
using LAMGestRestaurant.Infrastructure.Identidade;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace LAMGestRestaurant.Api.Controllers;

/// <summary><see cref="Confirmacao"/>: a frase «APAGAR DADOS DE TESTE», obrigatória quando se apagam os dados.</summary>
public sealed record PedidoInicioProducao(
    [Required(ErrorMessage = "Indique a sua palavra-passe.")] string PalavraPasse,
    bool ApagarDadosTeste,
    string? Confirmacao);

/// <summary>Modo de testes e início da produção (Configurações → Sistema).</summary>
[ApiController]
[Route("api/sistema/producao")]
public class ProducaoController(ServicoProducao producao) : ControllerBase
{
    /// <summary>Estado (para a faixa «Modo de testes»); a contagem dos dados só para quem gere as configurações.</summary>
    [HttpGet]
    [Authorize]
    public Task<EstadoProducao> Estado([FromServices] IUtilizadorActual actual, CancellationToken ct) =>
        producao.EstadoAsync(actual.TemPermissao(Permissoes.ConfiguracoesGerir), ct);

    /// <summary>Inicia a produção (uma única vez), apagando ou não os dados de teste. Pede de novo a palavra-passe.</summary>
    [HttpPost]
    [ExigePermissao(Permissoes.ConfiguracoesGerir)]
    public async Task<ResultadoProducao> Iniciar(PedidoInicioProducao dados, [FromServices] IUtilizadorActual actual,
        [FromServices] UserManager<Utilizador> utilizadores, CancellationToken ct)
    {
        var utilizador = actual.Id is int id ? await utilizadores.FindByIdAsync(id.ToString(System.Globalization.CultureInfo.InvariantCulture)) : null;
        if (utilizador is null || !await utilizadores.CheckPasswordAsync(utilizador, dados.PalavraPasse))
            throw new RegraNegocioException("Palavra-passe incorrecta.");
        return await producao.IniciarAsync(dados.ApagarDadosTeste, dados.Confirmacao, ct);
    }
}
