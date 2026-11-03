using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace LAMGestRestaurant.Instalador.Motor;

/// <summary>Conteúdo embebido pelo scripts\criar-setup.ps1: aplicação publicada, lista de migrations e versão.</summary>
public static class Pacote
{
    private static readonly Assembly Montagem = typeof(Pacote).Assembly;

    public static bool TemAplicacao => Montagem.GetManifestResourceInfo("LAMGestRestaurant.zip") is not null;

    public static Stream AbrirAplicacao() =>
        Montagem.GetManifestResourceStream("LAMGestRestaurant.zip")
        ?? throw new InvalidOperationException("Este instalador não inclui a aplicação do servidor. Gere-o com scripts\\criar-setup.ps1.");

    public static IReadOnlyList<string> Migracoes => [.. LerTexto("migracoes.txt").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];

    public static string Versao => LerTexto("versao.txt").Trim() is { Length: > 0 } v ? v : "desenvolvimento";

    private static string LerTexto(string nome)
    {
        using var s = Montagem.GetManifestResourceStream(nome);
        if (s is null)
            return string.Empty;
        using var r = new StreamReader(s, Encoding.UTF8);
        return r.ReadToEnd();
    }
}

public sealed record ResultadoProcesso(int Codigo, string Saida);

public static class Processos
{
    /// <summary>Corre um programa e devolve o código e toda a saída (stdout + stderr). Nunca escreve a entrada no log.</summary>
    public static async Task<ResultadoProcesso> CorrerAsync(string ficheiro, IEnumerable<string> argumentos, string? pasta = null,
        IDictionary<string, string>? ambiente = null, string? entrada = null, CancellationToken ct = default)
    {
        var inicio = new ProcessStartInfo(ficheiro)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = entrada is not null,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = pasta ?? Environment.CurrentDirectory,
        };
        foreach (var a in argumentos)
            inicio.ArgumentList.Add(a);
        if (ambiente is not null)
            foreach (var (k, v) in ambiente)
                inicio.Environment[k] = v;

        using var p = Process.Start(inicio) ?? throw new InvalidOperationException($"Não foi possível executar {ficheiro}.");
        var saida = p.StandardOutput.ReadToEndAsync(ct);
        var erros = p.StandardError.ReadToEndAsync(ct);
        if (entrada is not null)
        {
            await p.StandardInput.WriteAsync(entrada);
            p.StandardInput.Close();
        }
        await p.WaitForExitAsync(ct);
        return new ResultadoProcesso(p.ExitCode, (await saida + await erros).Trim());
    }

    /// <summary>Como <see cref="CorrerAsync"/>, mas falha com a saída do programa se o código não for 0.</summary>
    public static async Task<string> ExigirAsync(string ficheiro, params string[] argumentos)
    {
        var r = await CorrerAsync(ficheiro, argumentos);
        if (r.Codigo != 0)
            throw new InvalidOperationException($"{Path.GetFileName(ficheiro)} {string.Join(' ', argumentos)} falhou ({r.Codigo}): {r.Saida}");
        return r.Saida;
    }
}

public static class Segredos
{
    /// <summary>Palavra-passe aleatória com maiúsculas, minúsculas e algarismos (cumpre a política do SQL Server).</summary>
    public static string GerarPalavraPasse(int tamanho = 28)
    {
        const string maiusculas = "ABCDEFGHJKLMNPQRSTUVWXYZ", minusculas = "abcdefghijkmnopqrstuvwxyz", algarismos = "23456789";
        const string todos = maiusculas + minusculas + algarismos;
        var c = new char[tamanho];
        c[0] = maiusculas[RandomNumberGenerator.GetInt32(maiusculas.Length)];
        c[1] = minusculas[RandomNumberGenerator.GetInt32(minusculas.Length)];
        c[2] = algarismos[RandomNumberGenerator.GetInt32(algarismos.Length)];
        for (var i = 3; i < tamanho; i++)
            c[i] = todos[RandomNumberGenerator.GetInt32(todos.Length)];
        RandomNumberGenerator.Shuffle(c.AsSpan());
        return new string(c);
    }

    /// <summary>Mesmas regras da aplicação (Identity): mínimo 8, com minúscula e algarismo.</summary>
    public static string? ValidarPalavraPasseAdmin(string palavraPasse) =>
        palavraPasse.Length < 8 || !palavraPasse.Any(char.IsLower) || !palavraPasse.Any(char.IsDigit)
            ? "A palavra-passe tem de ter pelo menos 8 caracteres, com letra minúscula e algarismo."
            : null;
}
