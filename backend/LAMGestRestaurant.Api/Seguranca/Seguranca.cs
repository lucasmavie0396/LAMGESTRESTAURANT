using System.Security.Claims;
using LAMGestRestaurant.Application.Comum;
using LAMGestRestaurant.Application.Seguranca;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace LAMGestRestaurant.Api.Seguranca;

/// <summary>Utilizador do pedido HTTP actual, a partir das claims da sessão.</summary>
public class UtilizadorActual(IHttpContextAccessor acessor) : IUtilizadorActual
{
    private ClaimsPrincipal? Principal => acessor.HttpContext?.User;

    public int? Id => int.TryParse(Principal?.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
    public string? Nome => Principal?.Identity?.IsAuthenticated == true ? Principal.Identity.Name : null;
    public string? EnderecoIp => acessor.HttpContext?.Connection.RemoteIpAddress?.ToString();

    public bool TemPermissao(string permissao) =>
        Principal?.HasClaim(Permissoes.ClaimType, permissao) == true;
}

/// <summary>Atalho para [Authorize(Policy = "perm:modulo.operacao")].</summary>
public class ExigePermissaoAttribute : AuthorizeAttribute
{
    public ExigePermissaoAttribute(string permissao) => Policy = Permissoes.PrefixoPolicy + permissao;
}

/// <summary>Cria dinamicamente as políticas "perm:xxx" a partir do catálogo de permissões.</summary>
public class ProvedorPoliticasPermissao(IOptions<AuthorizationOptions> opcoes) : DefaultAuthorizationPolicyProvider(opcoes)
{
    public override async Task<AuthorizationPolicy?> GetPolicyAsync(string nome)
    {
        if (nome.StartsWith(Permissoes.PrefixoPolicy, StringComparison.Ordinal))
        {
            var permissao = nome[Permissoes.PrefixoPolicy.Length..];
            if (!Permissoes.Todas.ContainsKey(permissao))
                throw new InvalidOperationException($"Permissão desconhecida: {permissao}");
            return new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .RequireClaim(Permissoes.ClaimType, permissao)
                .Build();
        }
        return await base.GetPolicyAsync(nome);
    }
}
