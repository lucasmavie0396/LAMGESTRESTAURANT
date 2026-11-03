using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using LAMGestRestaurant.Application.Comum;
using LAMGestRestaurant.Application.Seguranca;
using LAMGestRestaurant.Infrastructure.Dados;
using LAMGestRestaurant.Infrastructure.Identidade;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace LAMGestRestaurant.Api.Controllers;

public record PedidoLogin([Required] string NomeUtilizador, [Required] string PalavraPasse);

public record PedidoAlterarPalavraPasse([Required] string PalavraPasseActual, [Required] string NovaPalavraPasse);

public record SessaoUtilizador(
    int Id,
    string NomeUtilizador,
    string NomeCompleto,
    IReadOnlyList<string> Perfis,
    IReadOnlyList<string> Permissoes,
    bool DeveAlterarPalavraPasse);

[ApiController]
[Route("api/auth")]
public class AuthController(
    SignInManager<Utilizador> signIn,
    UserManager<Utilizador> utilizadores,
    AppDbContext db,
    IAuditoria auditoria,
    TimeProvider relogio) : ControllerBase
{
    private const string MensagemCredenciaisInvalidas = "Utilizador ou palavra-passe inválidos.";

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<ActionResult<SessaoUtilizador>> Login(PedidoLogin pedido)
    {
        var utilizador = await utilizadores.FindByNameAsync(pedido.NomeUtilizador.Trim());

        // A mesma mensagem para utilizador inexistente, desactivado ou palavra-passe errada:
        // não revelar que contas existem.
        if (utilizador is null)
            return await Recusar(pedido.NomeUtilizador, "inexistente");

        if (!utilizador.Activo)
            return await Recusar(utilizador.UserName, "conta desactivada", utilizador.Id);

        var resultado = await signIn.PasswordSignInAsync(utilizador, pedido.PalavraPasse, isPersistent: false, lockoutOnFailure: true);
        if (resultado.IsLockedOut)
        {
            auditoria.Registar("login.bloqueado", "Utilizador", utilizador.Id, new { utilizador.UserName });
            await db.SaveChangesAsync();
            return Problem(statusCode: StatusCodes.Status423Locked, title: "Conta bloqueada",
                detail: "Conta bloqueada temporariamente por tentativas falhadas. Tente mais tarde ou contacte o administrador.");
        }
        if (!resultado.Succeeded)
            return await Recusar(utilizador.UserName, "palavra-passe errada", utilizador.Id);

        utilizador.UltimoLoginUtc = relogio.GetUtcNow().UtcDateTime;
        await utilizadores.UpdateAsync(utilizador);
        auditoria.Registar("login", "Utilizador", utilizador.Id, new { utilizador.UserName });
        await db.SaveChangesAsync();

        return await CriarSessao(utilizador);
    }

    /// <summary>Botões do ecrã de entrada táctil (só nomes de quem tem código e foi marcado pelo administrador).</summary>
    [HttpGet("tactil")]
    [AllowAnonymous]
    public Task<IReadOnlyList<UtilizadorTactil>> Tactil([FromServices] ServicoPin pin, CancellationToken ct) => pin.ListarTactilAsync(ct);

    /// <summary>Entrada com código no ecrã táctil. Erros contam para o mesmo bloqueio da palavra-passe.</summary>
    [HttpPost("login-pin")]
    [AllowAnonymous]
    public async Task<ActionResult<SessaoUtilizador>> LoginPin(LoginPin pedido, [FromServices] ServicoPin pin)
    {
        var (resultado, utilizador) = await pin.VerificarAsync(pedido.UtilizadorId, pedido.Pin);
        switch (resultado)
        {
            case ResultadoPin.Bloqueado:
                auditoria.Registar("login.bloqueado", "Utilizador", utilizador!.Id, new { utilizador.UserName, Via = "código" });
                await db.SaveChangesAsync();
                return Problem(statusCode: StatusCodes.Status423Locked, title: "Conta bloqueada",
                    detail: "Conta bloqueada temporariamente por tentativas falhadas. Tente mais tarde ou contacte o administrador.");
            case ResultadoPin.SoPalavraPasse:
                await Recusar(utilizador!.UserName, "código não permitido", utilizador.Id);
                return Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Autenticação falhou",
                    detail: "Este utilizador tem de entrar com a palavra-passe.");
            case ResultadoPin.Invalido:
                await Recusar(utilizador?.UserName, "código errado", utilizador?.Id);
                return Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Autenticação falhou", detail: "Código errado.");
        }

        var u = utilizador!;
        await signIn.SignInAsync(u, isPersistent: false);
        u.UltimoLoginUtc = relogio.GetUtcNow().UtcDateTime;
        await utilizadores.UpdateAsync(u);
        auditoria.Registar("login", "Utilizador", u.Id, new { u.UserName, Via = "código" });
        await db.SaveChangesAsync();
        return await CriarSessao(u);
    }

    /// <summary>O utilizador muda o seu código de entrada (confirma com a palavra-passe).</summary>
    [HttpPost("alterar-pin")]
    [Authorize]
    public async Task<IActionResult> AlterarPin(AlterarMeuPin pedido, [FromServices] ServicoPin pin)
    {
        await pin.AlterarMeuAsync(pedido);
        return NoContent();
    }

    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout()
    {
        auditoria.Registar("logout", "Utilizador", User.FindFirstValue(ClaimTypes.NameIdentifier));
        await db.SaveChangesAsync();
        await signIn.SignOutAsync();
        return NoContent();
    }

    [HttpGet("me")]
    [Authorize]
    public async Task<ActionResult<SessaoUtilizador>> Me()
    {
        var utilizador = await utilizadores.GetUserAsync(User);
        if (utilizador is null || !utilizador.Activo)
            return Unauthorized();
        return await CriarSessao(utilizador);
    }

    [HttpPost("alterar-palavra-passe")]
    [Authorize]
    public async Task<IActionResult> AlterarPalavraPasse(PedidoAlterarPalavraPasse pedido)
    {
        var utilizador = await utilizadores.GetUserAsync(User) ?? throw new NaoEncontradoException("Utilizador não encontrado.");
        var resultado = await utilizadores.ChangePasswordAsync(utilizador, pedido.PalavraPasseActual, pedido.NovaPalavraPasse);
        if (!resultado.Succeeded)
            throw new RegraNegocioException(string.Join(" ", resultado.Errors.Select(e => e.Description)));

        utilizador.DeveAlterarPalavraPasse = false;
        await utilizadores.UpdateAsync(utilizador);
        auditoria.Registar("utilizador.alterar-palavra-passe", "Utilizador", utilizador.Id);
        await db.SaveChangesAsync();
        // A alteração renova o carimbo de segurança; renova-se também o cookie desta sessão.
        await signIn.RefreshSignInAsync(utilizador);
        return NoContent();
    }

    private async Task<ActionResult<SessaoUtilizador>> Recusar(string? nomeUtilizador, string motivo, int? utilizadorId = null)
    {
        auditoria.Registar("login.falhado", "Utilizador", utilizadorId, new { NomeUtilizador = nomeUtilizador, Motivo = motivo });
        await db.SaveChangesAsync();
        return Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Autenticação falhou", detail: MensagemCredenciaisInvalidas);
    }

    private async Task<SessaoUtilizador> CriarSessao(Utilizador utilizador)
    {
        var principal = await signIn.CreateUserPrincipalAsync(utilizador);
        return new SessaoUtilizador(
            utilizador.Id,
            utilizador.UserName!,
            utilizador.NomeCompleto,
            [.. principal.FindAll(ClaimTypes.Role).Select(c => c.Value).Order()],
            [.. principal.FindAll(Permissoes.ClaimType).Select(c => c.Value).Distinct().Order()],
            utilizador.DeveAlterarPalavraPasse);
    }
}
