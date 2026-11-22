using LAMGestRestaurant.Comum;
using Microsoft.Data.SqlClient;

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

/// <summary>Passos de SQL Server do instalador. O texto SQL e as regras das bases estão em <see cref="RegrasSql"/> (comum ao configurador).</summary>
public static class ServicoSql
{
    public const string ContaServico = RegrasSql.ContaServico;
    public const string LoginSqlServico = RegrasSql.LoginSqlServico;

    /// <summary>Só bases LAMGestRestaurant*: a instância pode ter bases de outros sistemas, que o instalador nunca toca.</summary>
    public static string? ValidarNomeBase(string nome) => RegrasSql.NomeValido(nome) ? null : RegrasSql.MensagemNomeBase;

    /// <summary>Instâncias do SQL Server instaladas neste computador (registo do Windows).</summary>
    public static IReadOnlyList<string> InstanciasLocais() => RegrasSql.InstanciasLocais();

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
        var tabelas = await EscalarAsync<int>(db, RegrasSql.SqlContarTabelas, ct);
        var temHistorico = await EscalarAsync<int>(db, RegrasSql.SqlTemHistorico, ct) > 0;

        List<string> aplicadas = [];
        if (temHistorico)
        {
            await using var cmd = new SqlCommand(RegrasSql.SqlMigracoes, db);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
                aplicadas.Add(r.GetString(0));
        }
        var doLamgest = RegrasSql.DoLamgest(aplicadas);
        if (tabelas > 0 && !doLamgest)
            return new EstadoBase(true, false, false, aplicadas, [], [], false, 0);

        var temAdmin = doLamgest && await EscalarAsync<int>(db,
            "SELECT COUNT(*) FROM UtilizadorPerfis up JOIN Perfis p ON p.Id = up.RoleId JOIN Utilizadores u ON u.Id = up.UserId " +
            "WHERE p.Name = N'Administrador' AND u.Activo = 1", ct) > 0;
        var pedidos = doLamgest ? await EscalarAsync<int>(db, "SELECT COUNT(*) FROM Pedidos", ct) : 0;
        return new EstadoBase(true, tabelas == 0, doLamgest, aplicadas,
            RegrasSql.Pendentes(migracoesDoPacote, aplicadas), RegrasSql.Desconhecidas(migracoesDoPacote, aplicadas), temAdmin, pedidos);
    }

    public static async Task CriarBaseAsync(ConfigSql c, CancellationToken ct = default)
    {
        Garantir(ValidarNomeBase(c.BaseDados));
        await ExecutarAsync(c.Ligacao(), RegrasSql.CriarBase(c.BaseDados), ct);
    }

    /// <summary>Cópia completa e verificada para a pasta de backups da instância. Devolve o ficheiro criado.</summary>
    public static async Task<string> CopiaSegurancaAsync(ConfigSql c, string motivo, CancellationToken ct = default)
    {
        Garantir(ValidarNomeBase(c.BaseDados));
        await using var ligacao = new SqlConnection(c.Ligacao());
        await ligacao.OpenAsync(ct);
        var pasta = await EscalarAsync<string>(ligacao, RegrasSql.SqlPastaCopias(c.BaseDados), ct);
        var ficheiro = RegrasSql.FicheiroCopia(pasta, c.BaseDados, motivo, DateTime.Now);
        await ExecutarAsync(ligacao, RegrasSql.Copia(c.BaseDados, ficheiro, motivo), ct);
        await ExecutarAsync(ligacao, RegrasSql.VerificarCopia(ficheiro), ct);
        return ficheiro;
    }

    /// <summary>Conta virtual do serviço com leitura/escrita só nesta base (sem alterar a estrutura).</summary>
    public static Task GarantirLoginWindowsAsync(ConfigSql c, string conta, CancellationToken ct = default) =>
        ExecutarAsync(c.Ligacao(), RegrasSql.AcessoContaWindows(conta, c.BaseDados), ct);

    /// <summary>Login SQL próprio do serviço (a palavra-passe do administrador do SQL Server nunca fica gravada).</summary>
    public static Task GarantirLoginSqlAsync(ConfigSql c, string login, string palavraPasse, CancellationToken ct = default) =>
        ExecutarAsync(c.Ligacao(), RegrasSql.LoginSql(login, palavraPasse, c.BaseDados) + Environment.NewLine + RegrasSql.AcessoUtilizador(login, c.BaseDados), ct);

    /// <summary>
    /// A tarefa de backup corre como SYSTEM: precisa de fazer cópias desta base, de as verificar (RESTORE VERIFYONLY) e,
    /// ao domingo, da manutenção (reorganizar índices e actualizar estatísticas; o Express não tem Agent).
    /// </summary>
    public static Task GarantirPermissoesBackupAsync(ConfigSql c, CancellationToken ct = default) =>
        ExecutarAsync(c.Ligacao(), RegrasSql.PermissoesCopias(c.BaseDados), ct);

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
