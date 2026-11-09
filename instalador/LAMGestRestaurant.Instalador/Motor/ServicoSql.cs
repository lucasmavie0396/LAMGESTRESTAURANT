using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.Win32;

namespace LAMGestRestaurant.Instalador.Motor;

/// <summary>Dados do SQL Server indicados no assistente. As credenciais só são usadas durante a instalação.</summary>
public sealed record ConfigSql(string Instancia, bool AutenticacaoWindows, string? Utilizador, string? PalavraPasse, string BaseDados)
{
    public string Ligacao(string? baseDados = null, int segundos = 8)
    {
        var b = new SqlConnectionStringBuilder
        {
            DataSource = Instancia,
            InitialCatalog = baseDados ?? "master",
            TrustServerCertificate = true,
            ConnectTimeout = segundos,
            ApplicationName = "LAMGest Instalador",
        };
        if (AutenticacaoWindows)
            b.IntegratedSecurity = true;
        else
        {
            b.UserID = Utilizador;
            b.Password = PalavraPasse;
        }
        return b.ConnectionString;
    }

    public string LigacaoBase => Ligacao(BaseDados);
}

public sealed record InfoServidor(string Versao, string Edicao, bool Sysadmin, bool AutenticacaoMista)
{
    /// <summary>Nome comercial da versão (ex.: «SQL Server 2019»), a partir da versão interna.</summary>
    public string Nome => Requisitos.NomeVersaoSql(Versao);
}

/// <summary>Situação da base escolhida, comparada com as migrations incluídas neste instalador.</summary>
public sealed record EstadoBase(
    bool Existe,
    bool Vazia,
    bool DoLamgest,
    IReadOnlyList<string> Aplicadas,
    IReadOnlyList<string> Pendentes,
    IReadOnlyList<string> Desconhecidas,
    bool TemAdministrador,
    int Pedidos)
{
    /// <summary>Base com dados que vai ser alterada: exige cópia de segurança e confirmação.</summary>
    public bool ActualizacaoComDados => Existe && DoLamgest && Pendentes.Count > 0;
}

public static partial class ServicoSql
{
    public const string ContaServico = @"NT SERVICE\LAMGestRestaurant";
    public const string LoginSqlServico = "lamgest_servico";

    [GeneratedRegex("^LAMGestRestaurant[A-Za-z0-9_]{0,60}$")]
    private static partial Regex RegexNomeBase();

    /// <summary>Só bases LAMGestRestaurant*: a instância pode ter bases de outros sistemas, que o instalador nunca toca.</summary>
    public static string? ValidarNomeBase(string nome) =>
        RegexNomeBase().IsMatch(nome) ? null : "O nome da base tem de começar por «LAMGestRestaurant» (letras, algarismos e _).";

    /// <summary>Instâncias do SQL Server instaladas neste computador (registo do Windows).</summary>
    public static IReadOnlyList<string> InstanciasLocais()
    {
        try
        {
            using var raiz = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var chave = raiz.OpenSubKey(@"SOFTWARE\Microsoft\Microsoft SQL Server\Instance Names\SQL");
            return chave is null ? [] : [.. chave.GetValueNames().Select(n => n == "MSSQLSERVER" ? "." : @".\" + n).Order()];
        }
        catch
        {
            return [];
        }
    }

    public static async Task<InfoServidor> TestarAsync(ConfigSql c, CancellationToken ct = default)
    {
        await using var ligacao = new SqlConnection(c.Ligacao());
        await ligacao.OpenAsync(ct);
        await using var cmd = new SqlCommand(
            "SELECT CAST(SERVERPROPERTY('ProductVersion') AS nvarchar(50)), CAST(SERVERPROPERTY('Edition') AS nvarchar(100)), " +
            "IS_SRVROLEMEMBER('sysadmin'), CAST(SERVERPROPERTY('IsIntegratedSecurityOnly') AS int)", ligacao);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        await r.ReadAsync(ct);
        return new InfoServidor(r.GetString(0), r.GetString(1), !r.IsDBNull(2) && r.GetInt32(2) == 1, r.GetInt32(3) == 0);
    }

    public static async Task<EstadoBase> AnalisarAsync(ConfigSql c, IReadOnlyList<string> migracoesDoPacote, CancellationToken ct = default)
    {
        Garantir(ValidarNomeBase(c.BaseDados));
        await using var mestre = new SqlConnection(c.Ligacao());
        await mestre.OpenAsync(ct);
        if (await EscalarAsync<int>(mestre, "SELECT COUNT(*) FROM sys.databases WHERE name = @n", ct, ("@n", c.BaseDados)) == 0)
            return new EstadoBase(false, true, false, [], migracoesDoPacote, [], false, 0);

        await using var db = new SqlConnection(c.LigacaoBase);
        await db.OpenAsync(ct);
        var tabelas = await EscalarAsync<int>(db, "SELECT COUNT(*) FROM sys.tables WHERE name <> '__EFMigrationsHistory'", ct);
        var temHistorico = await EscalarAsync<int>(db, "SELECT COUNT(*) FROM sys.tables WHERE name = '__EFMigrationsHistory'", ct) > 0;

        List<string> aplicadas = [];
        if (temHistorico)
        {
            await using var cmd = new SqlCommand("SELECT MigrationId FROM [__EFMigrationsHistory] ORDER BY MigrationId", db);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
                aplicadas.Add(r.GetString(0));
        }
        var doLamgest = aplicadas.Any(m => m.EndsWith("_Inicial", StringComparison.Ordinal));
        if (tabelas > 0 && !doLamgest)
            return new EstadoBase(true, false, false, aplicadas, [], [], false, 0);

        var temAdmin = doLamgest && await EscalarAsync<int>(db,
            "SELECT COUNT(*) FROM UtilizadorPerfis up JOIN Perfis p ON p.Id = up.RoleId JOIN Utilizadores u ON u.Id = up.UserId " +
            "WHERE p.Name = N'Administrador' AND u.Activo = 1", ct) > 0;
        var pedidos = doLamgest ? await EscalarAsync<int>(db, "SELECT COUNT(*) FROM Pedidos", ct) : 0;
        return new EstadoBase(true, tabelas == 0, doLamgest, aplicadas,
            [.. migracoesDoPacote.Except(aplicadas)], [.. aplicadas.Except(migracoesDoPacote)], temAdmin, pedidos);
    }

    public static async Task CriarBaseAsync(ConfigSql c, CancellationToken ct = default)
    {
        Garantir(ValidarNomeBase(c.BaseDados));
        await ExecutarAsync(c.Ligacao(), $"IF DB_ID(N'{c.BaseDados}') IS NULL CREATE DATABASE [{c.BaseDados}]", ct);
    }

    /// <summary>Cópia completa e verificada para a pasta de backups da instância. Devolve o ficheiro criado.</summary>
    public static async Task<string> CopiaSegurancaAsync(ConfigSql c, string motivo, CancellationToken ct = default)
    {
        Garantir(ValidarNomeBase(c.BaseDados));
        await using var ligacao = new SqlConnection(c.Ligacao());
        await ligacao.OpenAsync(ct);
        var pasta = await EscalarAsync<string>(ligacao, "SELECT CAST(SERVERPROPERTY('InstanceDefaultBackupPath') AS nvarchar(400))", ct);
        var ficheiro = Path.Combine(pasta, $"{c.BaseDados}_{motivo}_{DateTime.Now:yyyyMMdd_HHmmss}.bak");
        var sql = ficheiro.Replace("'", "''");
        await ExecutarAsync(ligacao, $"BACKUP DATABASE [{c.BaseDados}] TO DISK = N'{sql}' WITH INIT, CHECKSUM, NAME = N'LAMGest — {motivo}'", ct);
        await ExecutarAsync(ligacao, $"RESTORE VERIFYONLY FROM DISK = N'{sql}' WITH CHECKSUM", ct);
        return ficheiro;
    }

    /// <summary>Conta virtual do serviço com leitura/escrita só nesta base (sem alterar a estrutura).</summary>
    public static Task GarantirLoginWindowsAsync(ConfigSql c, string conta, CancellationToken ct = default) =>
        ExecutarAsync(c.Ligacao(), $"""
            IF SUSER_ID(N'{conta}') IS NULL CREATE LOGIN [{conta}] FROM WINDOWS;
            USE [{c.BaseDados}];
            IF USER_ID(N'{conta}') IS NULL CREATE USER [{conta}] FOR LOGIN [{conta}];
            ALTER ROLE db_datareader ADD MEMBER [{conta}];
            ALTER ROLE db_datawriter ADD MEMBER [{conta}];
            """, ct);

    /// <summary>Login SQL próprio do serviço (a palavra-passe do administrador do SQL Server nunca fica gravada).</summary>
    public static Task GarantirLoginSqlAsync(ConfigSql c, string login, string palavraPasse, CancellationToken ct = default)
    {
        if (!palavraPasse.All(char.IsAsciiLetterOrDigit))
            throw new ArgumentException("Palavra-passe gerada inválida.", nameof(palavraPasse));
        return ExecutarAsync(c.Ligacao(), $"""
            IF SUSER_ID(N'{login}') IS NULL CREATE LOGIN [{login}] WITH PASSWORD = N'{palavraPasse}', CHECK_POLICY = ON, DEFAULT_DATABASE = [{c.BaseDados}];
            ELSE ALTER LOGIN [{login}] WITH PASSWORD = N'{palavraPasse}';
            ALTER LOGIN [{login}] ENABLE;
            USE [{c.BaseDados}];
            IF USER_ID(N'{login}') IS NULL CREATE USER [{login}] FOR LOGIN [{login}];
            ALTER ROLE db_datareader ADD MEMBER [{login}];
            ALTER ROLE db_datawriter ADD MEMBER [{login}];
            """, ct);
    }

    /// <summary>A tarefa de backup corre como SYSTEM: precisa de fazer cópias desta base e de as verificar (RESTORE VERIFYONLY).</summary>
    public static Task GarantirPermissoesBackupAsync(ConfigSql c, CancellationToken ct = default) =>
        ExecutarAsync(c.Ligacao(), $"""
            IF SUSER_ID(N'NT AUTHORITY\SYSTEM') IS NULL CREATE LOGIN [NT AUTHORITY\SYSTEM] FROM WINDOWS;
            GRANT CREATE ANY DATABASE TO [NT AUTHORITY\SYSTEM];
            USE [{c.BaseDados}];
            IF USER_ID(N'NT AUTHORITY\SYSTEM') IS NULL CREATE USER [NT AUTHORITY\SYSTEM] FOR LOGIN [NT AUTHORITY\SYSTEM];
            ALTER ROLE db_backupoperator ADD MEMBER [NT AUTHORITY\SYSTEM];
            """, ct);

    public static string LigacaoServico(ConfigSql c, string? palavraPasseServico)
    {
        var b = new SqlConnectionStringBuilder
        {
            DataSource = c.Instancia,
            InitialCatalog = c.BaseDados,
            TrustServerCertificate = true,
        };
        if (c.AutenticacaoWindows)
            b.IntegratedSecurity = true;
        else
        {
            b.UserID = LoginSqlServico;
            b.Password = palavraPasseServico;
        }
        return b.ConnectionString;
    }

    private static void Garantir(string? erro)
    {
        if (erro is not null)
            throw new InvalidOperationException(erro);
    }

    private static async Task ExecutarAsync(string ligacao, string sql, CancellationToken ct)
    {
        await using var c = new SqlConnection(ligacao);
        await c.OpenAsync(ct);
        await ExecutarAsync(c, sql, ct);
    }

    private static async Task ExecutarAsync(SqlConnection c, string sql, CancellationToken ct)
    {
        await using var cmd = new SqlCommand(sql, c) { CommandTimeout = 0 };
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task<T> EscalarAsync<T>(SqlConnection c, string sql, CancellationToken ct, params (string Nome, object Valor)[] parametros)
    {
        await using var cmd = new SqlCommand(sql, c);
        foreach (var (nome, valor) in parametros)
            cmd.Parameters.AddWithValue(nome, valor);
        return (T)Convert.ChangeType((await cmd.ExecuteScalarAsync(ct))!, typeof(T));
    }
}
