using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace LAMGestRestaurant.Configurar.Motor;

/// <summary>
/// Código de acesso do configurador: o mesmo do LAMGest-Setup.exe. Só a derivação PBKDF2 (SHA-256, 200 000 iterações,
/// com sal) fica no programa — o código em si não aparece no ficheiro nem no repositório.
/// </summary>
public static class CodigoAcesso
{
    /// <summary>Iguais aos de LAMGestRestaurant.Instalador.Motor.CodigoInstalacao (um teste garante-o).</summary>
    internal const string SalBase64 = "gdGMZRGW6Sq3qCsQk0keIw==";
    internal const string DerivadoBase64 = "aZl34N6/GEWRCc58+gnzuxeFY+fQFFwp+L85VlM4GQE=";
    private const int Iteracoes = 200_000;

    public static bool Valido(string? codigo)
    {
        if (string.IsNullOrEmpty(codigo))
            return false;
        var derivado = Convert.FromBase64String(DerivadoBase64);
        using var pbkdf2 = new Rfc2898DeriveBytes(Encoding.UTF8.GetBytes(codigo), Convert.FromBase64String(SalBase64), Iteracoes, HashAlgorithmName.SHA256);
        var tentativa = pbkdf2.GetBytes(derivado.Length);
        var diferenca = 0;
        for (var i = 0; i < derivado.Length; i++)
            diferenca |= tentativa[i] ^ derivado[i];
        return diferenca == 0;
    }
}

/// <summary>
/// Bloqueio por tentativas falhadas, guardado num ficheiro (fechar e abrir o programa não o anula).
/// Ao fim de <see cref="MaximoTentativas"/> códigos errados, bloqueia durante <see cref="Duracao"/>.
/// </summary>
public sealed class BloqueioTentativas
{
    public const int MaximoTentativas = 5;
    public static readonly TimeSpan Duracao = TimeSpan.FromMinutes(5);

    private readonly string ficheiro;
    private readonly Func<DateTime> agora;

    public BloqueioTentativas(string ficheiro, Func<DateTime>? agora = null)
    {
        this.ficheiro = ficheiro;
        this.agora = agora ?? (() => DateTime.UtcNow);
    }

    /// <summary>Tempo que falta de bloqueio (null se pode tentar).</summary>
    public TimeSpan? Restante()
    {
        var (_, bloqueadoAte) = Ler();
        if (bloqueadoAte is DateTime ate && ate > agora())
            return ate - agora();
        return null;
    }

    /// <summary>Tentativas que restam antes do bloqueio.</summary>
    public int TentativasRestantes => MaximoTentativas - Ler().Falhas;

    /// <summary>Regista um código errado. Devolve true se passou a estar bloqueado.</summary>
    public bool RegistarFalha()
    {
        var (falhas, bloqueadoAte) = Ler();
        if (bloqueadoAte is DateTime ate && ate <= agora())
            falhas = 0;
        falhas++;
        if (falhas >= MaximoTentativas)
        {
            Gravar(0, agora() + Duracao);
            return true;
        }
        Gravar(falhas, null);
        return false;
    }

    public void Limpar()
    {
        try
        {
            if (File.Exists(ficheiro))
                File.Delete(ficheiro);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private (int Falhas, DateTime? BloqueadoAte) Ler()
    {
        try
        {
            if (!File.Exists(ficheiro))
                return (0, null);
            var partes = File.ReadAllText(ficheiro).Trim().Split('|');
            var falhas = int.Parse(partes[0], CultureInfo.InvariantCulture);
            DateTime? ate = partes.Length > 1 && partes[1].Length > 0
                ? DateTime.Parse(partes[1], CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal)
                : null;
            return (falhas, ate);
        }
        catch (Exception ex) when (ex is IOException or FormatException or UnauthorizedAccessException)
        {
            return (0, null);
        }
    }

    private void Gravar(int falhas, DateTime? ate)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ficheiro)!);
        File.WriteAllText(ficheiro, falhas.ToString(CultureInfo.InvariantCulture) + "|" + (ate?.ToString("o", CultureInfo.InvariantCulture) ?? ""));
    }
}
