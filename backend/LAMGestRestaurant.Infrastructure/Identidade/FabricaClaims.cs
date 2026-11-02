using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace LAMGestRestaurant.Infrastructure.Identidade;

/// <summary>Acrescenta o nome completo às claims da sessão (perfis e permissões vêm da base do Identity).</summary>
public class FabricaClaims(UserManager<Utilizador> utilizadores, RoleManager<Perfil> perfis, IOptions<IdentityOptions> opcoes)
    : UserClaimsPrincipalFactory<Utilizador, Perfil>(utilizadores, perfis, opcoes)
{
    public const string NomeCompleto = "nome_completo";
    /// <summary>Presente quando o utilizador tem de alterar a palavra-passe; a API só permite /api/auth até lá.</summary>
    public const string DeveAlterarPalavraPasse = "deve_alterar_palavra_passe";

    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(Utilizador utilizador)
    {
        var identidade = await base.GenerateClaimsAsync(utilizador);
        identidade.AddClaim(new Claim(NomeCompleto, utilizador.NomeCompleto));
        if (utilizador.DeveAlterarPalavraPasse)
            identidade.AddClaim(new Claim(DeveAlterarPalavraPasse, "true"));
        return identidade;
    }
}
