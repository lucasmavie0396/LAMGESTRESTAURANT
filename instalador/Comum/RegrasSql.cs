using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace LAMGestRestaurant.Comum;

/// <summary>
/// Regras SQL comuns ao LAMGest-Setup (instalador, .NET 9) e ao LAMGest-Configurar (configurador, .NET Framework 4.7.2).
/// Este ficheiro é compilado nos dois projectos: só texto SQL e regras puras, sem tipos do cliente SQL (cada programa usa o
/// seu: Microsoft.Data.SqlClient no instalador, System.Data.SqlClient no configurador).
/// </summary>
public static class RegrasSql
{
    /// <summary>Conta virtual do serviço Windows (autenticação Windows).</summary>
    public const string ContaServico = @"NT SERVICE\LAMGestRestaurant";

    /// <summary>Login SQL próprio do serviço (autenticação SQL Server).</summary>
    public const string LoginSqlServico = "lamgest_servico";

    public const string MensagemNomeBase = "O nome da base tem de começar por «LAMGestRestaurant» (letras, algarismos e _).";

    private static readonly Regex NomeBase = new("^LAMGestRestaurant[A-Za-z0-9_]{0,60}$", RegexOptions.CultureInvariant);
    private static readonly Regex NomeLogin = new("^[A-Za-z0-9_]{1,60}$", RegexOptions.CultureInvariant);

    /// <summary>Só bases LAMGestRestaurant*: a instância pode ter bases de outros sistemas, que nunca são abertas nem alteradas.</summary>
    public static bool NomeValido(string nome) => NomeBase.IsMatch(nome);

    public static bool LoginValido(string login) => NomeLogin.IsMatch(login);

    /// <summary>Uma base é do LAMGest quando tem a migration inicial aplicada.</summary>
    public static bool DoLamgest(IEnumerable<string> aplicadas) => aplicadas.Any(m => m.EndsWith("_Inicial", StringComparison.Ordinal));

    /// <summary>Migrations do programa ainda por aplicar na base (por ordem).</summary>
    public static string[] Pendentes(IEnumerable<string> doPrograma, IEnumerable<string> aplicadas) =>
        doPrograma.Except(aplicadas, StringComparer.Ordinal).OrderBy(m => m, StringComparer.Ordinal).ToArray();

    /// <summary>Migrations da base que o programa não conhece (base actualizada por uma versão mais recente).</summary>
    public static string[] Desconhecidas(IEnumerable<string> doPrograma, IEnumerable<string> aplicadas) =>
        aplicadas.Except(doPrograma, StringComparer.Ordinal).OrderBy(m => m, StringComparer.Ordinal).ToArray();

    public const string SqlContarTabelas = "SELECT COUNT(*) FROM sys.tables WHERE name <> '__EFMigrationsHistory'";
    public const string SqlTemHistorico = "SELECT COUNT(*) FROM sys.tables WHERE name = '__EFMigrationsHistory'";
    public const string SqlMigracoes = "SELECT MigrationId FROM [__EFMigrationsHistory] ORDER BY MigrationId";
    /// <summary>
    /// Pasta das cópias de segurança: a da instância; sem ela (ex.: LocalDB, ou instância sem pasta de cópias configurada), a
    /// pasta de dados da instância; em último caso, a pasta do ficheiro de dados da própria base.
    /// </summary>
    public static string SqlPastaCopias(string baseDados)
    {
        Garantir(baseDados);
        return $"""
            SELECT COALESCE(
                NULLIF(CAST(SERVERPROPERTY('InstanceDefaultBackupPath') AS nvarchar(400)), N''),
                NULLIF(CAST(SERVERPROPERTY('InstanceDefaultDataPath') AS nvarchar(400)), N''),
                (SELECT TOP 1 LEFT(physical_name, LEN(physical_name) - CHARINDEX(N'\', REVERSE(physical_name)) + 1)
                 FROM sys.master_files WHERE database_id = DB_ID(N'{baseDados}') AND type = 0))
            """;
    }

    /// <summary>Conta Windows com leitura/escrita só nesta base (sem alterar a estrutura).</summary>
    public static string AcessoContaWindows(string conta, string baseDados) =>
        $"""
        IF SUSER_ID(N'{conta}') IS NULL CREATE LOGIN [{conta}] FROM WINDOWS;
        {AcessoUtilizador(conta, baseDados)}
        """;

    /// <summary>Cria (ou renova a palavra-passe do) login SQL do serviço. A palavra-passe é gerada (só letras e algarismos).</summary>
    public static string LoginSql(string login, string palavraPasse, string baseDados)
    {
        if (!LoginValido(login))
            throw new InvalidOperationException("O login SQL do serviço tem um nome inesperado.");
        if (palavraPasse.Length == 0 || !palavraPasse.All(c => c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9'))
            throw new ArgumentException("Palavra-passe gerada inválida.", nameof(palavraPasse));
        Garantir(baseDados);
        return $"""
            IF SUSER_ID(N'{login}') IS NULL CREATE LOGIN [{login}] WITH PASSWORD = N'{palavraPasse}', CHECK_POLICY = ON, DEFAULT_DATABASE = [{baseDados}];
            ELSE ALTER LOGIN [{login}] WITH PASSWORD = N'{palavraPasse}';
            ALTER LOGIN [{login}] ENABLE;
            """;
    }

    /// <summary>Utilizador da base (para um login que já existe) com leitura/escrita de dados.</summary>
    public static string AcessoUtilizador(string login, string baseDados)
    {
        Garantir(baseDados);
        return $"""
            USE [{baseDados}];
            IF USER_ID(N'{login}') IS NULL CREATE USER [{login}] FOR LOGIN [{login}];
            ALTER ROLE db_datareader ADD MEMBER [{login}];
            ALTER ROLE db_datawriter ADD MEMBER [{login}];
            """;
    }

    /// <summary>
    /// A tarefa das cópias diárias corre como SYSTEM: precisa de copiar e verificar esta base (RESTORE VERIFYONLY) e, ao
    /// domingo, de manter os índices e as estatísticas (o SQL Server Express não tem Agent).
    /// </summary>
    public static string PermissoesCopias(string baseDados)
    {
        Garantir(baseDados);
        return $"""
            IF SUSER_ID(N'NT AUTHORITY\SYSTEM') IS NULL CREATE LOGIN [NT AUTHORITY\SYSTEM] FROM WINDOWS;
            GRANT CREATE ANY DATABASE TO [NT AUTHORITY\SYSTEM];
            USE [{baseDados}];
            IF USER_ID(N'NT AUTHORITY\SYSTEM') IS NULL CREATE USER [NT AUTHORITY\SYSTEM] FOR LOGIN [NT AUTHORITY\SYSTEM];
            ALTER ROLE db_backupoperator ADD MEMBER [NT AUTHORITY\SYSTEM];
            ALTER ROLE db_ddladmin ADD MEMBER [NT AUTHORITY\SYSTEM];
            GRANT VIEW DATABASE STATE TO [NT AUTHORITY\SYSTEM];
            """;
    }

    /// <summary>Ficheiro da cópia de segurança na pasta de cópias da instância.</summary>
    public static string FicheiroCopia(string pasta, string baseDados, string motivo, DateTime agora) =>
        Path.Combine(pasta, $"{baseDados}_{motivo}_{agora:yyyyMMdd_HHmmss}.bak");

    /// <summary>Cópia completa com verificação dos blocos (CHECKSUM).</summary>
    public static string Copia(string baseDados, string ficheiro, string motivo)
    {
        Garantir(baseDados);
        return $"BACKUP DATABASE [{baseDados}] TO DISK = N'{Texto(ficheiro)}' WITH INIT, CHECKSUM, NAME = N'LAMGest — {Texto(motivo)}'";
    }

    /// <summary>Confirma que a cópia se pode restaurar.</summary>
    public static string VerificarCopia(string ficheiro) => $"RESTORE VERIFYONLY FROM DISK = N'{Texto(ficheiro)}' WITH CHECKSUM";

    public static string CriarBase(string baseDados)
    {
        Garantir(baseDados);
        return $"IF DB_ID(N'{baseDados}') IS NULL CREATE DATABASE [{baseDados}]";
    }

    /// <summary>Instâncias do SQL Server instaladas neste computador (registo do Windows): «.» para a predefinida, «.\NOME» para as outras.</summary>
    public static IReadOnlyList<string> InstanciasLocais()
    {
        try
        {
            using var raiz = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var chave = raiz.OpenSubKey(@"SOFTWARE\Microsoft\Microsoft SQL Server\Instance Names\SQL");
            return chave is null
                ? Array.Empty<string>()
                : chave.GetValueNames().Select(n => n == "MSSQLSERVER" ? "." : @".\" + n).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToArray();
        }
        catch (Exception)
        {
            return Array.Empty<string>();
        }
    }

    private static string Texto(string valor) => valor.Replace("'", "''");

    private static void Garantir(string baseDados)
    {
        if (!NomeValido(baseDados))
            throw new InvalidOperationException(MensagemNomeBase);
    }
}
