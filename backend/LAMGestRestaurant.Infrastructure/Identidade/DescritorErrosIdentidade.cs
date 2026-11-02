using Microsoft.AspNetCore.Identity;

namespace LAMGestRestaurant.Infrastructure.Identidade;

/// <summary>Mensagens de erro do Identity em Português.</summary>
public class DescritorErrosIdentidade : IdentityErrorDescriber
{
    public override IdentityError PasswordTooShort(int length) =>
        new() { Code = nameof(PasswordTooShort), Description = $"A palavra-passe deve ter pelo menos {length} caracteres." };

    public override IdentityError PasswordRequiresDigit() =>
        new() { Code = nameof(PasswordRequiresDigit), Description = "A palavra-passe deve conter pelo menos um algarismo." };

    public override IdentityError PasswordRequiresLower() =>
        new() { Code = nameof(PasswordRequiresLower), Description = "A palavra-passe deve conter pelo menos uma letra minúscula." };

    public override IdentityError PasswordMismatch() =>
        new() { Code = nameof(PasswordMismatch), Description = "A palavra-passe actual está incorrecta." };

    public override IdentityError DuplicateUserName(string userName) =>
        new() { Code = nameof(DuplicateUserName), Description = $"O nome de utilizador '{userName}' já existe." };

    public override IdentityError InvalidUserName(string? userName) =>
        new() { Code = nameof(InvalidUserName), Description = "Nome de utilizador inválido: use apenas letras, algarismos e . _ - @" };

    public override IdentityError DuplicateRoleName(string role) =>
        new() { Code = nameof(DuplicateRoleName), Description = $"O perfil '{role}' já existe." };

    public override IdentityError UserAlreadyInRole(string role) =>
        new() { Code = nameof(UserAlreadyInRole), Description = $"O utilizador já tem o perfil '{role}'." };
}
