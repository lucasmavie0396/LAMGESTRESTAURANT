using System.ComponentModel.DataAnnotations;

namespace LAMGestRestaurant.Application.Seguranca;

public record UtilizadorResumo(
    int Id,
    string NomeUtilizador,
    string NomeCompleto,
    bool Activo,
    bool DeveAlterarPalavraPasse,
    DateTime? BloqueadoAteUtc,
    DateTime? UltimoLoginUtc,
    IReadOnlyList<string> Perfis,
    bool TemPin = false,
    bool EcraTactil = false);

/// <summary>
/// Administrador: Pin novo (ou null para manter o actual), se aparece no ecrã táctil, ou Retirar o código
/// (deixa de poder entrar com código e sai do ecrã táctil).
/// </summary>
public record DefinirPin([StringLength(20, ErrorMessage = "O código tem de ter 4 a 6 algarismos.")] string? Pin, bool EcraTactil, bool Retirar = false);

/// <summary>O próprio utilizador muda o seu código (confirma com a palavra-passe).</summary>
public record AlterarMeuPin(
    [Required(ErrorMessage = "Indique a palavra-passe actual.")] string PalavraPasseActual,
    [Required(ErrorMessage = "Indique o novo código.")][StringLength(20, ErrorMessage = "O código tem de ter 4 a 6 algarismos.")] string NovoPin);

/// <summary>Botão do ecrã de entrada táctil (só o nome; não revela perfis nem o nome de utilizador).</summary>
public record UtilizadorTactil(int Id, string Nome);

public record LoginPin(int UtilizadorId, [Required(ErrorMessage = "Indique o código.")][StringLength(20, ErrorMessage = "O código tem de ter 4 a 6 algarismos.")] string Pin);

public record CriarUtilizador(
    [Required(ErrorMessage = "Indique o nome de utilizador.")]
    [StringLength(50, MinimumLength = 3, ErrorMessage = "O nome de utilizador deve ter entre 3 e 50 caracteres.")]
    [RegularExpression(@"^[a-zA-Z0-9._@-]+$", ErrorMessage = "Use apenas letras, algarismos e . _ - @ (sem espaços).")]
    string NomeUtilizador,
    [Required(ErrorMessage = "Indique o nome completo.")]
    [StringLength(200, ErrorMessage = "O nome completo não pode exceder 200 caracteres.")]
    string NomeCompleto,
    [Required(ErrorMessage = "Indique uma palavra-passe temporária.")]
    string PalavraPasseTemporaria,
    [MinLength(1, ErrorMessage = "Seleccione pelo menos um perfil.")]
    IReadOnlyList<string> Perfis);

public record EditarUtilizador(
    [Required(ErrorMessage = "Indique o nome completo.")]
    [StringLength(200, ErrorMessage = "O nome completo não pode exceder 200 caracteres.")]
    string NomeCompleto,
    [MinLength(1, ErrorMessage = "Seleccione pelo menos um perfil.")]
    IReadOnlyList<string> Perfis);

public record ReporPalavraPasse(
    [Required(ErrorMessage = "Indique a nova palavra-passe temporária.")]
    string NovaPalavraPasse);

public record PerfilResumo(
    int Id,
    string Nome,
    string? Descricao,
    bool Sistema,
    int NumeroUtilizadores,
    IReadOnlyList<string> Permissoes);

public record GuardarPerfil(
    [Required(ErrorMessage = "Indique o nome do perfil.")]
    [StringLength(50, MinimumLength = 2, ErrorMessage = "O nome deve ter entre 2 e 50 caracteres.")]
    string Nome,
    [StringLength(500)]
    string? Descricao,
    IReadOnlyList<string> Permissoes);

public record PermissaoInfo(string Codigo, string Modulo, string Descricao);
