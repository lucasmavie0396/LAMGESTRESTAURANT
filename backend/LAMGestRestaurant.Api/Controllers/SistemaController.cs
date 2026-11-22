using System.ComponentModel.DataAnnotations;
using LAMGestRestaurant.Api.Seguranca;
using LAMGestRestaurant.Application.Comum;
using LAMGestRestaurant.Application.Seguranca;
using LAMGestRestaurant.Infrastructure.Sistema;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace LAMGestRestaurant.Api.Controllers;

/// <summary>Erro do ecrã (navegador) enviado para o log do servidor. Sem dados pessoais: mensagem, pilha e página.</summary>
public sealed record ErroCliente(
    [Required, StringLength(2000)] string Mensagem,
    [StringLength(8000)] string? Pilha,
    [StringLength(500)] string? Pagina,
    [StringLength(100)] string? Origem);

/// <summary>Saúde do servidor (cópias de segurança e espaço) e erros dos ecrãs.</summary>
[ApiController]
[Route("api/sistema")]
public class SistemaController(ILogger<SistemaController> log) : ControllerBase
{
    /// <summary>Estado das cópias de segurança e do espaço, com os avisos para o administrador.</summary>
    [HttpGet("estado")]
    [ExigePermissao(Permissoes.ConfiguracoesGerir)]
    public Task<EstadoSistema> Estado([FromServices] ServicoEstadoSistema estado, CancellationToken ct) => estado.EstadoAsync(ct);

    /// <summary>
    /// HTTPS do servidor: porta e autoridade local que assinou o certificado (sem sessão e também em HTTP, para o Setup do
    /// posto, o configurador e o PDA confiarem nela). <c>https: false</c> se o servidor só tiver HTTP.
    /// </summary>
    [HttpGet("certificado")]
    [AllowAnonymous]
    public IActionResult Certificado([FromServices] IConfiguration configuracao)
    {
        using var ca = LigacaoCifrada.Autoridade(configuracao);
        if (ca is null)
            return Ok(new { https = false });
        return Ok(new
        {
            https = true,
            porta = LigacaoCifrada.PortaHttps(configuracao),
            soHttps = LigacaoCifrada.SoHttps(configuracao),
            certificado = Convert.ToBase64String(ca.RawData),
            impressaoDigital = LigacaoCifrada.ImpressaoDigital(ca),
            nome = ca.GetNameInfo(System.Security.Cryptography.X509Certificates.X509NameType.SimpleName, false),
            validoAte = ca.NotAfter.ToUniversalTime(),
        });
    }

    /// <summary>A autoridade local em ficheiro (.cer), para a instalar à mão noutro aparelho (ex.: um telemóvel ou outro navegador).</summary>
    [HttpGet("certificado.cer")]
    [AllowAnonymous]
    public IActionResult CertificadoFicheiro([FromServices] IConfiguration configuracao)
    {
        using var ca = LigacaoCifrada.Autoridade(configuracao);
        return ca is null ? NotFound() : File(ca.RawData, "application/pkix-cert", "LAMGest-autoridade-local.cer");
    }

    /// <summary>
    /// Regista no log do servidor um erro que aconteceu num ecrã (também antes de entrar, por isso sem sessão obrigatória;
    /// limitado por computador).
    /// </summary>
    [HttpPost("erros-cliente")]
    [AllowAnonymous]
    [EnableRateLimiting(LimitesPedidos.ErrosCliente)]
    public IActionResult RegistarErro(ErroCliente erro, [FromServices] IUtilizadorActual actual)
    {
        log.LogWarning("Erro no ecrã {Origem} em {Pagina} (utilizador {Utilizador}, computador {Ip}): {Mensagem}{NovaLinha}{Pilha}",
            Limpar(erro.Origem, 100), Limpar(erro.Pagina, 500), actual.Id?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "sem sessão",
            HttpContext.Connection.RemoteIpAddress?.ToString(), Limpar(erro.Mensagem, 2000), Environment.NewLine, Limpar(erro.Pilha, 8000));
        return NoContent();
    }

    /// <summary>Sem caracteres de controlo (uma linha falsa no log não pode ser injectada a partir do navegador), excepto mudanças de linha na pilha.</summary>
    private static string Limpar(string? texto, int maximo)
    {
        if (string.IsNullOrEmpty(texto))
            return "";
        var t = texto.Length > maximo ? texto[..maximo] : texto;
        return new string([.. t.Select(c => char.IsControl(c) && c != '\n' ? ' ' : c)]).Replace("\n", Environment.NewLine + "    ");
    }
}
