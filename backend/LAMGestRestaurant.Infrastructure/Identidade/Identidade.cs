using Microsoft.AspNetCore.Identity;

namespace LAMGestRestaurant.Infrastructure.Identidade;

public class Utilizador : IdentityUser<int>
{
    public string NomeCompleto { get; set; } = string.Empty;
    public bool Activo { get; set; } = true;
    /// <summary>Obriga a definir nova palavra-passe no próximo login (ex.: após reposição pelo administrador).</summary>
    public bool DeveAlterarPalavraPasse { get; set; }
    public DateTime CriadoEmUtc { get; set; }
    public DateTime? UltimoLoginUtc { get; set; }
    /// <summary>Código numérico (4 a 6 algarismos) para o ecrã de entrada táctil, cifrado como a palavra-passe. Null = sem código.</summary>
    public string? PinHash { get; set; }
    /// <summary>Aparece como botão no ecrã de entrada táctil dos postos.</summary>
    public bool EcraTactil { get; set; }
}

/// <summary>Perfil (role). As permissões são claims "perm" do perfil.</summary>
public class Perfil : IdentityRole<int>
{
    public Perfil() { }
    public Perfil(string nome) : base(nome) { }

    public string? Descricao { get; set; }
    /// <summary>Perfis de sistema não podem ser eliminados.</summary>
    public bool Sistema { get; set; }
}
