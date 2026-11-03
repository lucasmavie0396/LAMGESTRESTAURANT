using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LAMGestRestaurant.Licenciamento;

/// <summary>Conteúdo de uma licença. Id único por emissão (uma renovação é uma licença nova do mesmo estabelecimento).</summary>
public sealed record DadosLicenca(Guid Id, string Nome, string Nuit, DateOnly EmitidaEm, DateOnly ValidaAte, int Versao = 1);

/// <summary>
/// Texto da licença: «LAMGEST1.» + dados (JSON, base64url) + «.» + assinatura ECDSA P-256/SHA-256 dos bytes dos dados (base64url).
/// A assinatura cobre exactamente os bytes transmitidos, por isso não depende da forma do JSON. Espaços e quebras de linha
/// (de copiar/colar) são ignorados.
/// </summary>
public static partial class FormatoLicenca
{
    public const string Prefixo = "LAMGEST1";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>NUIT moçambicano: 9 algarismos.</summary>
    public static bool NuitValido(string? nuit) => nuit is { Length: 9 } && nuit.All(char.IsAsciiDigit);

    public static string Codificar(DadosLicenca dados, ECDsa chavePrivada)
    {
        if (string.IsNullOrWhiteSpace(dados.Nome))
            throw new ArgumentException("Indique o nome comercial.", nameof(dados));
        if (!NuitValido(dados.Nuit))
            throw new ArgumentException("O NUIT tem 9 algarismos.", nameof(dados));
        if (dados.ValidaAte < dados.EmitidaEm)
            throw new ArgumentException("A validade é anterior à data de emissão.", nameof(dados));
        var bytes = JsonSerializer.SerializeToUtf8Bytes(dados with { Nome = dados.Nome.Trim() }, Json);
        var assinatura = chavePrivada.SignData(bytes, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        return $"{Prefixo}.{Base64Url(bytes)}.{Base64Url(assinatura)}";
    }

    /// <summary>Lê e verifica a assinatura. Devolve os dados, ou null com o motivo em <paramref name="erro"/>.</summary>
    public static DadosLicenca? Ler(string? texto, ECDsa chavePublica, out string? erro)
    {
        erro = null;
        var limpo = Espacos().Replace(texto ?? "", "");
        var partes = limpo.Split('.');
        if (partes.Length != 3 || partes[0] != Prefixo)
        {
            erro = "Texto de licença inválido (deve começar por «LAMGEST1.»).";
            return null;
        }
        try
        {
            var bytes = DeBase64Url(partes[1]);
            var assinatura = DeBase64Url(partes[2]);
            if (!chavePublica.VerifyData(bytes, assinatura, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
            {
                erro = "A licença não é válida para este sistema (assinatura errada ou texto alterado).";
                return null;
            }
            var dados = JsonSerializer.Deserialize<DadosLicenca>(bytes, Json);
            if (dados is null || dados.Id == Guid.Empty || !NuitValido(dados.Nuit) || string.IsNullOrWhiteSpace(dados.Nome))
            {
                erro = "A licença está incompleta.";
                return null;
            }
            return dados;
        }
        catch (Exception ex) when (ex is FormatException or JsonException or CryptographicException or ArgumentException)
        {
            erro = "Texto de licença inválido (incompleto ou com caracteres a mais).";
            return null;
        }
    }

    /// <summary>Texto normalizado (sem espaços), para guardar e comparar.</summary>
    public static string Normalizar(string texto) => Espacos().Replace(texto, "");

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] DeBase64Url(string texto)
    {
        var b = texto.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(b.PadRight(b.Length + (4 - b.Length % 4) % 4, '='));
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Espacos();
}

/// <summary>Chaves ECDSA P-256 em texto (base64 do SubjectPublicKeyInfo para a pública).</summary>
public static class ChavesLicenca
{
    /// <summary>Chave pública do fabricante, embutida no sistema.</summary>
    public static ECDsa PublicaEmbutida()
    {
        using var stream = typeof(ChavesLicenca).Assembly.GetManifestResourceStream("chave-publica.txt")
                           ?? throw new InvalidOperationException("Chave pública das licenças em falta.");
        using var leitor = new StreamReader(stream);
        return Publica(leitor.ReadToEnd());
    }

    public static ECDsa Publica(string base64)
    {
        var chave = ECDsa.Create();
        chave.ImportSubjectPublicKeyInfo(Convert.FromBase64String(base64.Trim()), out _);
        return chave;
    }

    public static string ExportarPublica(ECDsa chave) => Convert.ToBase64String(chave.ExportSubjectPublicKeyInfo());

    public static ECDsa Nova() => ECDsa.Create(ECCurve.NamedCurves.nistP256);
}
