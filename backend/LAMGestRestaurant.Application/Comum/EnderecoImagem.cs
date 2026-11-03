using System.Text.RegularExpressions;

namespace LAMGestRestaurant.Application.Comum;

/// <summary>Endereço das imagens guardadas na base: /api/imagens/{id em 32 algarismos hexadecimais}.</summary>
public static partial class EnderecoImagem
{
    [GeneratedRegex("^/api/imagens/([0-9a-f]{32})$")]
    private static partial Regex Padrao();

    public static bool TentarId(string? url, out Guid id)
    {
        id = Guid.Empty;
        return url is not null && Padrao().Match(url) is { Success: true } m && Guid.TryParseExact(m.Groups[1].Value, "N", out id);
    }
}
