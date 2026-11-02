using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace LAMGestRestaurant.Infrastructure.Identidade;

/// <summary>Acrescenta o nome completo às claims da sessão (perfis e permissões vêm da base do Identity).</summary>
public class FabricaClaims(UserManager<Utilizador> utilizadores, RoleManager<Perfil> perfis, IOptions<IdentityOptions> opcoes)
    : UserClaimsPrincipalFactory<Utilizador, Perfil>(utilizadores, perfis, opcoes)
{
    public const string NomeCompleto = "nome_completo";

    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(Utilizador utilizador)
    {
        var identidade = await base.GenerateClaimsAsync(utilizador);
        identidade.AddClaim(new Claim(NomeCompleto, utilizador.NomeCompleto));
        return identidade;
    }
}
