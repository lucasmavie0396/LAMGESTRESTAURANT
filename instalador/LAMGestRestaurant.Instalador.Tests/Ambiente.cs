using LAMGestRestaurant.Instalador.Motor;
using Microsoft.Data.SqlClient;

namespace LAMGestRestaurant.Instalador.Tests;

/// <summary>Bases de teste (sempre com sufixo _Tests) e a API compilada do backend, usada como o instalador a usa.</summary>
internal static class Ambiente
{
    public static string Instancia => Environment.GetEnvironmentVariable("LAMGEST_TESTES_SQL") ?? @".\SYSON";

    public static ConfigSql Config(string baseDados)
    {
        // Salvaguarda: estes testes criam e eliminam bases; nunca podem apontar para outra que não uma de testes.
        if (!baseDados.EndsWith("_Tests", StringComparison.Ordinal))
            throw new InvalidOperationException("As bases dos testes do instalador têm de terminar em _Tests.");
        return new ConfigSql(Instancia, true, null, null, baseDados);
    }

    public static string Raiz { get; } = EncontrarRaiz();

    private static string EncontrarRaiz()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !Directory.Exists(Path.Combine(d.FullName, "backend")))
            d = d.Parent;
        return d?.FullName ?? throw new InvalidOperationException("Raiz do repositório não encontrada.");
    }

    public static string ApiDll => Path.Combine(Raiz, @"backend\LAMGestRestaurant.Api\bin\Debug\net9.0\LAMGestRestaurant.Api.dll");

    /// <summary>Migrations do repositório (o instalador real recebe esta lista embebida pelo criar-setup.ps1).</summary>
    public static IReadOnlyList<string> Migracoes =>
        [.. Directory.GetFiles(Path.Combine(Raiz, @"backend\LAMGestRestaurant.Infrastructure\Dados\Migracoes"), "*.cs")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(n => n is not null && System.Text.RegularExpressions.Regex.IsMatch(n, @"^\d{14}_[A-Za-z0-9_]+$"))
            .Cast<string>()
            .Order()];

    public static Task<ResultadoProcesso> CorrerApiAsync(string ligacao, string? entrada, params string[] argumentos) =>
        Processos.CorrerAsync("dotnet", [ApiDll, .. argumentos], Path.GetDirectoryName(ApiDll),
            new Dictionary<string, string> { ["ConnectionStrings__Default"] = ligacao, ["ASPNETCORE_ENVIRONMENT"] = "Production", ["LAMGEST_CONFIG"] = "" },
            entrada);

    public static async Task EliminarBaseAsync(string baseDados)
    {
        Config(baseDados); // valida o sufixo
        await using var c = new SqlConnection(Config(baseDados).Ligacao());
        await c.OpenAsync();
        await using var cmd = new SqlCommand(
            $"IF DB_ID(N'{baseDados}') IS NOT NULL BEGIN ALTER DATABASE [{baseDados}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{baseDados}]; END", c);
        await cmd.ExecuteNonQueryAsync();
    }

    public static async Task ExecutarAsync(string ligacao, string sql)
    {
        await using var c = new SqlConnection(ligacao);
        await c.OpenAsync();
        await using var cmd = new SqlCommand(sql, c);
        await cmd.ExecuteNonQueryAsync();
    }
}
