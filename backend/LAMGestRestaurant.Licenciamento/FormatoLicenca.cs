using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LAMGestRestaurant.Licenciamento;

/// <summary>Conteúdo de uma licença. Id único por emissão (uma renovação é uma licença nova do mesmo estabelecimento).</summary>
public sealed record DadosLicenca(Guid Id, string Nome, string Nuit, DateOnly EmitidaEm, DateOnly ValidaAte, int Versao = 1);

/// <summary>
/// Código de desbloqueio do relógio, emitido pela LAM Company para um <see cref="Pedido"/> (o que o sistema bloqueado mostra):
/// retira o bloqueio «relógio atrasado» depois de o cliente acertar a data. Vale só para esse pedido e até <see cref="ValidoAte"/>.
/// </summary>
public sealed record DadosDesbloqueio(Guid Id, string Nuit, string Pedido, DateOnly EmitidoEm, DateOnly ValidoAte, int Versao = 1);

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

    // ---------- Código de desbloqueio do relógio ----------

    public const string PrefixoDesbloqueio = "LAMGESTR1";

    /// <summary>
    /// Pedido de desbloqueio que o sistema mostra quando o relógio do servidor está atrasado em relação à última utilização:
    /// «NUIT-AAAAMMDDHHmm» (a última utilização registada, em UTC). O código de desbloqueio só vale para esse pedido.
    /// </summary>
    public static string PedidoDesbloqueio(string nuit, DateTime ultimaUtilizacaoUtc) =>
        nuit + "-" + ultimaUtilizacaoUtc.ToString("yyyyMMddHHmm", System.Globalization.CultureInfo.InvariantCulture);

    public static string CodificarDesbloqueio(DadosDesbloqueio dados, ECDsa chavePrivada)
    {
        if (string.IsNullOrWhiteSpace(dados.Pedido) || !dados.Pedido.StartsWith(dados.Nuit + "-", StringComparison.Ordinal) || !NuitValido(dados.Nuit))
            throw new ArgumentException("Pedido de desbloqueio inválido (NUIT-AAAAMMDDHHmm).", nameof(dados));
        if (dados.ValidoAte < dados.EmitidoEm)
            throw new ArgumentException("A validade é anterior à data de emissão.", nameof(dados));
        var bytes = JsonSerializer.SerializeToUtf8Bytes(dados, Json);
        var assinatura = chavePrivada.SignData(bytes, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        return $"{PrefixoDesbloqueio}.{Base64Url(bytes)}.{Base64Url(assinatura)}";
    }

    /// <summary>Lê e verifica um código de desbloqueio. Devolve os dados, ou null com o motivo em <paramref name="erro"/>.</summary>
    public static DadosDesbloqueio? LerDesbloqueio(string? texto, ECDsa chavePublica, out string? erro)
    {
        erro = null;
        var partes = Espacos().Replace(texto ?? "", "").Split('.');
        if (partes.Length != 3 || partes[0] != PrefixoDesbloqueio)
        {
            erro = "Código de desbloqueio inválido (deve começar por «LAMGESTR1.»).";
            return null;
        }
        try
        {
            var bytes = DeBase64Url(partes[1]);
            if (!chavePublica.VerifyData(bytes, DeBase64Url(partes[2]), HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
            {
                erro = "O código de desbloqueio não é válido para este sistema (assinatura errada ou texto alterado).";
                return null;
            }
            var dados = JsonSerializer.Deserialize<DadosDesbloqueio>(bytes, Json);
            if (dados is null || dados.Id == Guid.Empty || !NuitValido(dados.Nuit) || string.IsNullOrWhiteSpace(dados.Pedido))
            {
                erro = "O código de desbloqueio está incompleto.";
                return null;
            }
            return dados;
        }
        catch (Exception ex) when (ex is FormatException or JsonException or CryptographicException or ArgumentException)
        {
            erro = "Código de desbloqueio inválido (incompleto ou com caracteres a mais).";
            return null;
        }
    }

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
