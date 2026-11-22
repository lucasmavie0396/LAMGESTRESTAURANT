using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using LAMGestRestaurant.Comum;

namespace LAMGestRestaurant.Configurar.Motor;

/// <summary>Ligação de quem está a configurar (administrador do SQL Server). As credenciais nunca são gravadas.</summary>
public sealed class LigacaoAdmin
{
    public LigacaoAdmin(string instancia, bool autenticacaoWindows, string? utilizador = null, string? palavraPasse = null)
    {
        Instancia = instancia.Trim();
        AutenticacaoWindows = autenticacaoWindows;
        Utilizador = utilizador;
        PalavraPasse = palavraPasse;
    }

    public string Instancia { get; }
    public bool AutenticacaoWindows { get; }
    public string? Utilizador { get; }
    public string? PalavraPasse { get; }

    public string Ligacao(string? baseDados = null, int segundos = 8)
    {
        var b = new SqlConnectionStringBuilder
        {
            DataSource = Instancia,
            InitialCatalog = baseDados ?? "master",
            TrustServerCertificate = true,
            ConnectTimeout = segundos,
            ApplicationName = "LAMGest Configurar",
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
}

/// <summary>
/// Ligação do serviço (servidor.json), escrita pelo instalador com o Microsoft.Data.SqlClient (ex.: «Trust Server Certificate»).
/// Lê-se e altera-se como texto chave=valor, sem o SqlConnectionStringBuilder do .NET Framework (que não conhece todas as chaves):
/// muda-se só a instância, a base e, se for preciso, o login — o resto mantém-se como estava.
/// </summary>
public static class TextoLigacao
{
    private static readonly string[] ChavesServidor = { "Data Source", "Server", "Address", "Addr", "Network Address" };
    private static readonly string[] ChavesBase = { "Initial Catalog", "Database" };
    private static readonly string[] ChavesIntegrada = { "Integrated Security", "Trusted_Connection" };
    private static readonly string[] ChavesUtilizador = { "User ID", "UID", "User" };
    private static readonly string[] ChavesPalavraPasse = { "Password", "PWD" };

    public sealed class Partes
    {
        public string Servidor { get; set; } = "";
        public string BaseDados { get; set; } = "";
        public bool Integrada { get; set; }
        public string? Utilizador { get; set; }
        public string? PalavraPasse { get; set; }
    }

    public static Partes Ler(string ligacao)
    {
        var b = new System.Data.Common.DbConnectionStringBuilder { ConnectionString = ligacao };
        var integrada = Valor(b, ChavesIntegrada);
        return new Partes
        {
            Servidor = Valor(b, ChavesServidor) ?? "",
            BaseDados = Valor(b, ChavesBase) ?? "",
            Integrada = integrada is not null && (integrada.Equals("true", StringComparison.OrdinalIgnoreCase) || integrada.Equals("yes", StringComparison.OrdinalIgnoreCase) || integrada.Equals("sspi", StringComparison.OrdinalIgnoreCase)),
            Utilizador = Valor(b, ChavesUtilizador),
            PalavraPasse = Valor(b, ChavesPalavraPasse),
        };
    }

    public static string Mudar(string ligacao, string servidor, string baseDados, string? utilizador = null, string? palavraPasse = null)
    {
        var b = new System.Data.Common.DbConnectionStringBuilder { ConnectionString = ligacao };
        Definir(b, ChavesServidor, servidor);
        Definir(b, ChavesBase, baseDados);
        if (utilizador is not null)
            Definir(b, ChavesUtilizador, utilizador);
        if (palavraPasse is not null)
            Definir(b, ChavesPalavraPasse, palavraPasse);
        return b.ConnectionString;
    }

    private static string? Valor(System.Data.Common.DbConnectionStringBuilder b, string[] chaves)
    {
        foreach (var c in chaves)
            if (b.TryGetValue(c, out var v) && v is not null)
                return Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture);
        return null;
    }

    /// <summary>Altera a chave com a grafia que já existe (ou acrescenta a primeira da lista).</summary>
    private static void Definir(System.Data.Common.DbConnectionStringBuilder b, string[] chaves, string valor)
    {
        var existente = chaves.FirstOrDefault(b.ContainsKey);
        b[existente ?? chaves[0]] = valor;
    }
}

public enum SituacaoBase
{
    /// <summary>Base do LAMGest na mesma versão do programa.</summary>
    Actualizada,
    /// <summary>Base do LAMGest de uma versão anterior: é actualizada (com cópia de segurança antes) depois de confirmar.</summary>
    PrecisaActualizacao,
    /// <summary>Base actualizada por uma versão mais recente do LAMGest: não pode ser usada por este programa.</summary>
    MaisRecente,
    /// <summary>Base sem tabelas: tem de ser criada com o LAMGest-Setup.exe.</summary>
    Vazia,
    /// <summary>Tem tabelas mas não é do LAMGest.</summary>
    NaoLamgest,
    /// <summary>Não foi possível abrir (offline, sem permissão…).</summary>
    Inacessivel,
}

public sealed class InfoBase
{
    public string Nome { get; set; } = "";
    public SituacaoBase Situacao { get; set; }
    public IReadOnlyList<string> Pendentes { get; set; } = Array.Empty<string>();
    public string? UltimaMigracao { get; set; }
    public string? Erro { get; set; }

    public bool PodeSerUsada => Situacao is SituacaoBase.Actualizada or SituacaoBase.PrecisaActualizacao;

    public string Descricao => Situacao switch
    {
        SituacaoBase.Actualizada => "LAMGest",
        SituacaoBase.PrecisaActualizacao => $"versão antiga ({Pendentes.Count} actualização(ões))",
        SituacaoBase.MaisRecente => "versão mais recente que este programa",
        SituacaoBase.Vazia => "vazia (criar com o LAMGest-Setup)",
        SituacaoBase.NaoLamgest => "não é do LAMGest",
        _ => "inacessível" + (Erro is null ? "" : ": " + Erro),
    };
}

/// <summary>
/// Bases do LAMGest numa instância do SQL Server. Só bases com nome «LAMGestRestaurant…» são abertas: as outras bases
/// da instância (de outros sistemas) nunca são lidas nem alteradas. O texto SQL e as regras das bases estão em
/// <see cref="RegrasSql"/> (comum ao instalador).
/// </summary>
public static class BasesSql
{
    public const string ContaServico = RegrasSql.ContaServico;
    public const string LoginSqlServico = RegrasSql.LoginSqlServico;

    public static bool NomeValido(string nome) => RegrasSql.NomeValido(nome);

    /// <summary>Instâncias do SQL Server instaladas neste computador.</summary>
    public static IReadOnlyList<string> InstanciasLocais() => RegrasSql.InstanciasLocais();

    /// <summary>Situação de uma base a partir das migrations aplicadas e das que o programa instalado conhece.</summary>
    public static (SituacaoBase Situacao, IReadOnlyList<string> Pendentes) Classificar(bool temTabelas, IReadOnlyCollection<string> aplicadas, IReadOnlyCollection<string> doPrograma)
    {
        if (!RegrasSql.DoLamgest(aplicadas))
            return (temTabelas ? SituacaoBase.NaoLamgest : SituacaoBase.Vazia, Array.Empty<string>());
        if (doPrograma.Count > 0 && RegrasSql.Desconhecidas(doPrograma, aplicadas).Length > 0)
            return (SituacaoBase.MaisRecente, Array.Empty<string>());
        var pendentes = RegrasSql.Pendentes(doPrograma, aplicadas);
        return (pendentes.Length > 0 ? SituacaoBase.PrecisaActualizacao : SituacaoBase.Actualizada, pendentes);
    }

    /// <summary>Nomes das bases «LAMGestRestaurant…» da instância (só os nomes; nenhuma outra base é aberta).</summary>
    public static async Task<IReadOnlyList<string>> NomesAsync(LigacaoAdmin admin)
    {
        using var c = new SqlConnection(admin.Ligacao());
        await c.OpenAsync();
        using var cmd = new SqlCommand("SELECT name FROM sys.databases WHERE name LIKE N'LAMGestRestaurant%' ORDER BY name", c);
        using var r = await cmd.ExecuteReaderAsync();
        var nomes = new List<string>();
        while (await r.ReadAsync())
        {
            var nome = r.GetString(0);
            if (NomeValido(nome))
                nomes.Add(nome);
        }
        return nomes;
    }

    public static async Task<IReadOnlyList<string>> MigracoesAplicadasAsync(LigacaoAdmin admin, string baseDados)
    {
        Garantir(baseDados);
        using var c = new SqlConnection(admin.Ligacao(baseDados));
        await c.OpenAsync();
        return await MigracoesAsync(c);
    }

    public static async Task<InfoBase> AnalisarAsync(LigacaoAdmin admin, string baseDados, IReadOnlyCollection<string> doPrograma)
    {
        Garantir(baseDados);
        var info = new InfoBase { Nome = baseDados };
        try
        {
            using var c = new SqlConnection(admin.Ligacao(baseDados));
            await c.OpenAsync();
            var tabelas = await EscalarAsync(c, RegrasSql.SqlContarTabelas);
            var aplicadas = await MigracoesAsync(c);
            var (situacao, pendentes) = Classificar(tabelas > 0, aplicadas, doPrograma);
            info.Situacao = situacao;
            info.Pendentes = pendentes;
            info.UltimaMigracao = aplicadas.OrderBy(m => m, StringComparer.Ordinal).LastOrDefault();
        }
        catch (SqlException ex)
        {
            info.Situacao = SituacaoBase.Inacessivel;
            info.Erro = ex.Message;
        }
        return info;
    }

    public static async Task<IReadOnlyList<InfoBase>> ListarAsync(LigacaoAdmin admin, IReadOnlyCollection<string> doPrograma)
    {
        var lista = new List<InfoBase>();
        foreach (var nome in await NomesAsync(admin))
            lista.Add(await AnalisarAsync(admin, nome, doPrograma));
        return lista;
    }

    /// <summary>
    /// Dá à conta do serviço acesso de leitura/escrita (só dados) à base escolhida e devolve a ligação que o serviço passa a usar.
    /// Com login SQL na mesma instância mantém-se a palavra-passe; noutra instância é criado o login com uma palavra-passe nova.
    /// </summary>
    public static async Task<string> GarantirAcessoServicoAsync(LigacaoAdmin admin, string baseDados, string? ligacaoServicoActual)
    {
        Garantir(baseDados);
        var original = ligacaoServicoActual ?? "Integrated Security=True;Trust Server Certificate=True";
        var actual = TextoLigacao.Ler(original);
        if (actual.Integrada)
        {
            await ExecutarAsync(admin.Ligacao(), RegrasSql.AcessoContaWindows(ContaServico, baseDados));
            return TextoLigacao.Mudar(original, admin.Instancia, baseDados);
        }

        var mesmaInstancia = string.Equals(MesmaInstancia(actual.Servidor), MesmaInstancia(admin.Instancia), StringComparison.OrdinalIgnoreCase);
        var login = string.IsNullOrEmpty(actual.Utilizador) ? LoginSqlServico : actual.Utilizador!;
        if (!RegrasSql.LoginValido(login))
            throw new InvalidOperationException("O login SQL do serviço tem um nome inesperado.");
        var palavraPasse = actual.PalavraPasse;
        if (!mesmaInstancia || string.IsNullOrEmpty(palavraPasse))
        {
            palavraPasse = GerarPalavraPasse();
            await ExecutarAsync(admin.Ligacao(), RegrasSql.LoginSql(login, palavraPasse!, baseDados));
        }
        await ExecutarAsync(admin.Ligacao(), RegrasSql.AcessoUtilizador(login, baseDados));
        return TextoLigacao.Mudar(original, admin.Instancia, baseDados, login, palavraPasse);
    }

    /// <summary>
    /// A tarefa das cópias diárias corre como SYSTEM: precisa de copiar e verificar esta base e, ao domingo, de manter os
    /// índices e as estatísticas (as mesmas permissões que o LAMGest-Setup dá).
    /// </summary>
    public static Task GarantirPermissoesBackupAsync(LigacaoAdmin admin, string baseDados)
    {
        Garantir(baseDados);
        return ExecutarAsync(admin.Ligacao(), RegrasSql.PermissoesCopias(baseDados));
    }

    /// <summary>Cópia completa e verificada na pasta de backups da instância. Devolve o ficheiro criado.</summary>
    public static async Task<string> CopiaSegurancaAsync(LigacaoAdmin admin, string baseDados, string motivo)
    {
        Garantir(baseDados);
        using var c = new SqlConnection(admin.Ligacao());
        await c.OpenAsync();
        using var pastaCmd = new SqlCommand(RegrasSql.SqlPastaCopias, c);
        var pasta = (string)await pastaCmd.ExecuteScalarAsync();
        var ficheiro = RegrasSql.FicheiroCopia(pasta, baseDados, motivo, DateTime.Now);
        await ExecutarAsync(c, RegrasSql.Copia(baseDados, ficheiro, motivo));
        await ExecutarAsync(c, RegrasSql.VerificarCopia(ficheiro));
        return ficheiro;
    }

    /// <summary>Acrescenta um registo à auditoria da base (só inserção). Devolve false se a base não tiver a tabela.</summary>
    public static async Task<bool> AuditarAsync(LigacaoAdmin admin, string baseDados, string accao, string detalhes)
    {
        Garantir(baseDados);
        using var c = new SqlConnection(admin.Ligacao(baseDados));
        await c.OpenAsync();
        if (await EscalarAsync(c, "SELECT COUNT(*) FROM sys.tables WHERE name = 'RegistosAuditoria'") == 0)
            return false;
        using var cmd = new SqlCommand(
            "INSERT INTO RegistosAuditoria (DataUtc, UtilizadorId, NomeUtilizador, Accao, Entidade, EntidadeId, Detalhes, EnderecoIp) " +
            "VALUES (SYSUTCDATETIME(), NULL, @nome, @accao, N'Sistema', NULL, @detalhes, NULL)", c);
        var nome = $"Configurador ({Environment.UserDomainName}\\{Environment.UserName} em {Environment.MachineName})";
        cmd.Parameters.AddWithValue("@nome", nome.Length > 256 ? nome.Substring(0, 256) : nome);
        cmd.Parameters.AddWithValue("@accao", accao);
        cmd.Parameters.AddWithValue("@detalhes", detalhes);
        await cmd.ExecuteNonQueryAsync();
        return true;
    }

    private static string MesmaInstancia(string instancia)
    {
        var i = instancia.Trim();
        foreach (var local in new[] { "(local)", "localhost", "127.0.0.1", Environment.MachineName })
            if (i.StartsWith(local, StringComparison.OrdinalIgnoreCase) && (i.Length == local.Length || i[local.Length] == '\\'))
                return "." + i.Substring(local.Length);
        return i;
    }

    private static string GerarPalavraPasse()
    {
        const string maiusculas = "ABCDEFGHJKLMNPQRSTUVWXYZ", minusculas = "abcdefghijkmnopqrstuvwxyz", algarismos = "23456789";
        const string todos = maiusculas + minusculas + algarismos;
        var bytes = new byte[28];
        using (var rng = RandomNumberGenerator.Create())
            rng.GetBytes(bytes);
        var c = bytes.Select(b => todos[b % todos.Length]).ToArray();
        c[0] = maiusculas[bytes[0] % maiusculas.Length];
        c[1] = minusculas[bytes[1] % minusculas.Length];
        c[2] = algarismos[bytes[2] % algarismos.Length];
        return new string(c);
    }

    private static void Garantir(string baseDados)
    {
        if (!NomeValido(baseDados))
            throw new InvalidOperationException("Só podem ser usadas bases com nome começado por «LAMGestRestaurant».");
    }

    private static async Task<List<string>> MigracoesAsync(SqlConnection c)
    {
        var lista = new List<string>();
        if (await EscalarAsync(c, RegrasSql.SqlTemHistorico) == 0)
            return lista;
        using var cmd = new SqlCommand(RegrasSql.SqlMigracoes, c);
        using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            lista.Add(r.GetString(0));
        return lista;
    }

    private static async Task<int> EscalarAsync(SqlConnection c, string sql)
    {
        using var cmd = new SqlCommand(sql, c);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    private static async Task ExecutarAsync(string ligacao, string sql)
    {
        using var c = new SqlConnection(ligacao);
        await c.OpenAsync();
        await ExecutarAsync(c, sql);
    }

    private static async Task ExecutarAsync(SqlConnection c, string sql)
    {
        using var cmd = new SqlCommand(sql, c) { CommandTimeout = 0 };
        await cmd.ExecuteNonQueryAsync();
    }
}
