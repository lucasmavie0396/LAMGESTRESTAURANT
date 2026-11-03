using LAMGestRestaurant.Application.Comum;
using LAMGestRestaurant.Application.Seguranca;
using LAMGestRestaurant.Infrastructure.Dados;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace LAMGestRestaurant.Infrastructure.Identidade;

public enum ResultadoPin { Valido, Invalido, Bloqueado, SoPalavraPasse }

/// <summary>
/// Código de entrada (PIN) do ecrã táctil. Regras:
/// - cifrado com o mesmo algoritmo da palavra-passe (nunca guardado em claro);
/// - tentativas erradas contam para o mesmo bloqueio da palavra-passe (5 → 15 minutos);
/// - quem gere utilizadores, perfis ou configurações (ex.: Administrador) entra só com a palavra-passe.
/// </summary>
public class ServicoPin(
    AppDbContext db,
    UserManager<Utilizador> utilizadores,
    IPasswordHasher<Utilizador> cifra,
    IUtilizadorActual actual,
    IAuditoria auditoria)
{
    /// <summary>Utilizadores com botão no ecrã táctil: activos, com código, marcados e autorizados a usar código.</summary>
    public async Task<IReadOnlyList<UtilizadorTactil>> ListarTactilAsync(CancellationToken ct)
    {
        var candidatos = await db.Users.AsNoTracking()
            .Where(u => u.Activo && u.EcraTactil && u.PinHash != null)
            .OrderBy(u => u.NomeCompleto).Select(u => new UtilizadorTactil(u.Id, u.NomeCompleto)).ToListAsync(ct);
        var excluidos = await SoPalavraPasseAsync(candidatos.Select(c => c.Id).ToList(), ct);
        return [.. candidatos.Where(c => !excluidos.Contains(c.Id))];
    }

    /// <summary>Verifica o código; erros contam para o bloqueio da conta. Não inicia a sessão.</summary>
    public async Task<(ResultadoPin Resultado, Utilizador? Utilizador)> VerificarAsync(int utilizadorId, string pin)
    {
        var u = await utilizadores.FindByIdAsync(utilizadorId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (u is null || !u.Activo || u.PinHash is null || !u.EcraTactil)
            return (ResultadoPin.Invalido, u);
        if ((await SoPalavraPasseAsync([u.Id], default)).Count > 0)
            return (ResultadoPin.SoPalavraPasse, u);
        if (await utilizadores.IsLockedOutAsync(u))
            return (ResultadoPin.Bloqueado, u);

        if (cifra.VerifyHashedPassword(u, u.PinHash, pin) == PasswordVerificationResult.Failed)
        {
            await utilizadores.AccessFailedAsync(u);
            return (await utilizadores.IsLockedOutAsync(u) ? ResultadoPin.Bloqueado : ResultadoPin.Invalido, u);
        }
        await utilizadores.ResetAccessFailedCountAsync(u);
        return (ResultadoPin.Valido, u);
    }

    /// <summary>Administrador: define (ou retira, com Pin null) o código e a presença no ecrã táctil.</summary>
    public async Task DefinirAsync(int id, DefinirPin dados)
    {
        var u = await utilizadores.FindByIdAsync(id.ToString(System.Globalization.CultureInfo.InvariantCulture))
                ?? throw new NaoEncontradoException("Utilizador não encontrado.");
        string accao;
        if (dados.Retirar)
        {
            u.PinHash = null;
            u.EcraTactil = false;
            accao = "utilizador.retirar-pin";
        }
        else
        {
            if (dados.Pin is not null)
            {
                Validar(dados.Pin);
                if ((await SoPalavraPasseAsync([u.Id], default)).Count > 0)
                    throw new RegraNegocioException("Este utilizador gere utilizadores, perfis ou configurações: entra só com a palavra-passe.");
                u.PinHash = cifra.HashPassword(u, dados.Pin);
            }
            if (dados.EcraTactil && u.PinHash is null)
                throw new RegraNegocioException("Defina primeiro o código para o utilizador aparecer no ecrã táctil.");
            u.EcraTactil = dados.EcraTactil;
            accao = dados.Pin is null ? "utilizador.ecra-tactil" : "utilizador.definir-pin";
        }
        Verificar(await utilizadores.UpdateAsync(u));
        auditoria.Registar(accao, "Utilizador", u.Id, new { u.UserName, u.EcraTactil });
        await db.SaveChangesAsync();
    }

    /// <summary>O próprio utilizador muda o código, confirmando com a palavra-passe.</summary>
    public async Task AlterarMeuAsync(AlterarMeuPin dados)
    {
        var u = await utilizadores.FindByIdAsync(actual.Id?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "")
                ?? throw new NaoEncontradoException("Utilizador não encontrado.");
        if (!await utilizadores.CheckPasswordAsync(u, dados.PalavraPasseActual))
            throw new RegraNegocioException("A palavra-passe actual está errada.");
        Validar(dados.NovoPin);
        if ((await SoPalavraPasseAsync([u.Id], default)).Count > 0)
            throw new RegraNegocioException("O seu perfil gere utilizadores, perfis ou configurações: entra só com a palavra-passe.");
        u.PinHash = cifra.HashPassword(u, dados.NovoPin);
        Verificar(await utilizadores.UpdateAsync(u));
        auditoria.Registar("utilizador.alterar-pin", "Utilizador", u.Id, new { u.UserName });
        await db.SaveChangesAsync();
    }

    /// <summary>Dos utilizadores indicados, os que têm (por algum perfil) permissões que exigem palavra-passe.</summary>
    private async Task<HashSet<int>> SoPalavraPasseAsync(IReadOnlyCollection<int> ids, CancellationToken ct)
    {
        var excluidas = RegrasPin.SoComPalavraPasse.ToList();
        var lista = await (from ur in db.UserRoles
                           join c in db.RoleClaims on ur.RoleId equals c.RoleId
                           where ids.Contains(ur.UserId) && c.ClaimType == Permissoes.ClaimType && excluidas.Contains(c.ClaimValue!)
                           select ur.UserId).Distinct().ToListAsync(ct);
        return [.. lista];
    }

    private static void Validar(string pin)
    {
        if (RegrasPin.Validar(pin) is { } erro)
            throw new RegraNegocioException(erro);
    }

    private static void Verificar(IdentityResult resultado)
    {
        if (!resultado.Succeeded)
            throw new RegraNegocioException(string.Join(" ", resultado.Errors.Select(e => e.Description)));
    }
}
