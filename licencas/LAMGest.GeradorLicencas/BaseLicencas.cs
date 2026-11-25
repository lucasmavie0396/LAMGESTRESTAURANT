using System.Data;
using System.Text.Json;
using System.Text.RegularExpressions;
using LAMGestRestaurant.Licenciamento;
using Microsoft.Data.SqlClient;

namespace LAMGest.GeradorLicencas;

/// <summary><see cref="ValidaAte"/>: fim do período coberto (a última licença ou o último pagamento válido, o que for mais tarde).</summary>
public sealed record ClienteResumo(int Id, string Nome, string Nuit, string? Contacto, Plano Plano, decimal ValorMensal, EstadoCliente Estado,
    string? Observacoes, DateOnly? ValidaAte, DateOnly? UltimoPagamento);

public sealed record PagamentoRegisto(int Id, DateOnly Data, decimal Valor, int Meses, string? Referencia, DateOnly PeriodoAte,
    bool Anulado, string? MotivoAnulacao, string RegistadoPor);

public sealed record LicencaRegisto(int Id, Guid LicencaId, DateOnly EmitidaEm, DateOnly ValidaAte, string Texto, int? PagamentoId,
    string? Observacoes, DateTime EmitidaEmUtc, string EmitidaPor);

public sealed record EventoHistorico(DateTime DataUtc, string Accao, string Detalhe, string Utilizador);

/// <summary>Instância do SQL Server e nome da base escolhidos neste computador (ficheiro base.json na pasta de dados).</summary>
public sealed record LigacaoBase(string Instancia, string Base)
{
    public const string BasePadrao = "LAMGestLicencas";
    private const string Ficheiro = "base.json";

    public static LigacaoBase? Ler(string pasta)
    {
        var f = Path.Combine(pasta, Ficheiro);
        return File.Exists(f) ? JsonSerializer.Deserialize<LigacaoBase>(File.ReadAllText(f)) : null;
    }

    public void Gravar(string pasta)
    {
        Directory.CreateDirectory(pasta);
        File.WriteAllText(Path.Combine(pasta, Ficheiro), JsonSerializer.Serialize(this));
    }
}

/// <summary>
/// Base de dados do fabricante (SQL Server, autenticação do Windows): clientes, pagamentos, licenças emitidas e histórico.
/// Nada se apaga: um pagamento errado é anulado; o histórico só recebe linhas novas.
/// </summary>
public sealed class BaseLicencas
{
    private const int VersaoEsquema = 1;
    private readonly string ligacao;
    private readonly string ligacaoMaster;
    private readonly string nomeBase;
    private readonly string utilizador;

    public BaseLicencas(LigacaoBase destino, string? utilizador = null)
    {
        nomeBase = ValidarNome(destino.Base);
        if (string.IsNullOrWhiteSpace(destino.Instancia))
            throw new ArgumentException("Indique a instância do SQL Server (ex.: .\\SQLEXPRESS).");
        var b = new SqlConnectionStringBuilder
        {
            DataSource = destino.Instancia.Trim(),
            IntegratedSecurity = true,
            TrustServerCertificate = true,
            ConnectTimeout = 15,
            ApplicationName = "LAMGest-Licencas",
        };
        b.InitialCatalog = "master";
        ligacaoMaster = b.ConnectionString;
        b.InitialCatalog = nomeBase;
        ligacao = b.ConnectionString;
        this.utilizador = utilizador ?? Environment.UserDomainName + "\\" + Environment.UserName;
    }

    public string NomeBase => nomeBase;

    /// <summary>Só letras, algarismos e _; nunca a base de outro sistema.</summary>
    public static string ValidarNome(string nome)
    {
        nome = nome.Trim();
        if (!Regex.IsMatch(nome, "^[A-Za-z][A-Za-z0-9_]{0,99}$"))
            throw new ArgumentException("Nome da base inválido (letras, algarismos e _).");
        if (nome.Contains("MZullo", StringComparison.OrdinalIgnoreCase) || nome.Equals("LAMGestRestaurantDB", StringComparison.OrdinalIgnoreCase)
            || nome is "master" or "model" or "msdb" or "tempdb")
            throw new ArgumentException($"A base «{nome}» pertence a outro sistema. Use uma base própria (ex.: {LigacaoBase.BasePadrao}).");
        return nome;
    }

    // ---------------------------------------------------------------- Esquema

    /// <summary>Cria a base e as tabelas (se ainda não existirem). Nunca altera nem apaga dados.</summary>
    public void Preparar()
    {
        using (var c = new SqlConnection(ligacaoMaster))
        {
            c.Open();
            using var cmd = new SqlCommand($"IF DB_ID(@nome) IS NULL CREATE DATABASE [{nomeBase}]", c);
            cmd.Parameters.AddWithValue("@nome", nomeBase);
            cmd.ExecuteNonQuery();
        }
        using var con = Abrir();
        Executar(con, null, """
            IF OBJECT_ID('dbo.Esquema') IS NULL
                CREATE TABLE dbo.Esquema (Versao int NOT NULL);
            IF OBJECT_ID('dbo.Clientes') IS NULL
                CREATE TABLE dbo.Clientes (
                    Id int IDENTITY PRIMARY KEY,
                    Nome nvarchar(150) NOT NULL,
                    Nuit char(9) NOT NULL CONSTRAINT UQ_Clientes_Nuit UNIQUE,
                    Contacto nvarchar(200) NULL,
                    Plano int NOT NULL,
                    ValorMensal decimal(18,2) NOT NULL,
                    Estado int NOT NULL,
                    Observacoes nvarchar(1000) NULL,
                    CriadoEmUtc datetime2 NOT NULL);
            IF OBJECT_ID('dbo.Pagamentos') IS NULL
                CREATE TABLE dbo.Pagamentos (
                    Id int IDENTITY PRIMARY KEY,
                    ClienteId int NOT NULL REFERENCES dbo.Clientes(Id),
                    Data date NOT NULL,
                    Valor decimal(18,2) NOT NULL,
                    Meses int NOT NULL,
                    Referencia nvarchar(100) NULL,
                    PeriodoAte date NOT NULL,
                    Anulado bit NOT NULL DEFAULT 0,
                    MotivoAnulacao nvarchar(500) NULL,
                    RegistadoEmUtc datetime2 NOT NULL,
                    RegistadoPor nvarchar(100) NOT NULL);
            IF OBJECT_ID('dbo.Licencas') IS NULL
                CREATE TABLE dbo.Licencas (
                    Id int IDENTITY PRIMARY KEY,
                    LicencaId uniqueidentifier NOT NULL CONSTRAINT UQ_Licencas_LicencaId UNIQUE,
                    ClienteId int NOT NULL REFERENCES dbo.Clientes(Id),
                    PagamentoId int NULL REFERENCES dbo.Pagamentos(Id),
                    EmitidaEm date NOT NULL,
                    ValidaAte date NOT NULL,
                    Texto nvarchar(max) NOT NULL,
                    Observacoes nvarchar(500) NULL,
                    EmitidaEmUtc datetime2 NOT NULL,
                    EmitidaPor nvarchar(100) NOT NULL);
            IF OBJECT_ID('dbo.Historico') IS NULL
                CREATE TABLE dbo.Historico (
                    Id bigint IDENTITY PRIMARY KEY,
                    ClienteId int NULL REFERENCES dbo.Clientes(Id),
                    Accao nvarchar(50) NOT NULL,
                    Detalhe nvarchar(1000) NOT NULL,
                    DataUtc datetime2 NOT NULL,
                    Utilizador nvarchar(100) NOT NULL);
            IF OBJECT_ID('dbo.TR_Historico_SoInsercao') IS NULL
                EXEC('CREATE TRIGGER dbo.TR_Historico_SoInsercao ON dbo.Historico INSTEAD OF UPDATE, DELETE AS
                      THROW 50001, ''O histórico só recebe linhas novas.'', 1;');
            IF NOT EXISTS (SELECT 1 FROM dbo.Esquema) INSERT dbo.Esquema (Versao) VALUES (1);
            """);
        var versao = (int)Escalar(con, null, "SELECT MAX(Versao) FROM dbo.Esquema")!;
        if (versao > VersaoEsquema)
            throw new InvalidOperationException("A base é de uma versão mais recente do gerador. Actualize o LAMGest-Licencas.");
    }

    private SqlConnection Abrir()
    {
        var c = new SqlConnection(ligacao);
        c.Open();
        return c;
    }

    private static void Executar(SqlConnection c, SqlTransaction? t, string sql, params (string Nome, object? Valor)[] p)
    {
        using var cmd = Comando(c, t, sql, p);
        cmd.ExecuteNonQuery();
    }

    private static object? Escalar(SqlConnection c, SqlTransaction? t, string sql, params (string Nome, object? Valor)[] p)
    {
        using var cmd = Comando(c, t, sql, p);
        var r = cmd.ExecuteScalar();
        return r is DBNull ? null : r;
    }

    private static SqlCommand Comando(SqlConnection c, SqlTransaction? t, string sql, (string Nome, object? Valor)[] p)
    {
        var cmd = new SqlCommand(sql, c, t);
        foreach (var (nome, valor) in p)
            cmd.Parameters.Add(Parametro(nome, valor));
        return cmd;
    }

    private static SqlParameter Parametro(string nome, object? valor) => valor switch
    {
        DateOnly d => new SqlParameter(nome, SqlDbType.Date) { Value = d.ToDateTime(TimeOnly.MinValue) },
        decimal m => new SqlParameter(nome, SqlDbType.Decimal) { Precision = 18, Scale = 2, Value = m },
        string s => new SqlParameter(nome, SqlDbType.NVarChar, Math.Max(s.Length, 1)) { Value = s },
        null => new SqlParameter(nome, DBNull.Value),
        _ => new SqlParameter(nome, valor),
    };

    private static List<T> Ler<T>(SqlConnection c, string sql, Func<SqlDataReader, T> linha, params (string Nome, object? Valor)[] p)
    {
        using var cmd = Comando(c, null, sql, p);
        using var r = cmd.ExecuteReader();
        var lista = new List<T>();
        while (r.Read())
            lista.Add(linha(r));
        return lista;
    }

    private static DateOnly Data(SqlDataReader r, int i) => DateOnly.FromDateTime(r.GetDateTime(i));
    private static DateOnly? DataOuNada(SqlDataReader r, int i) => r.IsDBNull(i) ? null : DateOnly.FromDateTime(r.GetDateTime(i));
    private static string? TextoOuNada(SqlDataReader r, int i) => r.IsDBNull(i) ? null : r.GetString(i);
    private static string? Limpo(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private void Registar(SqlConnection c, SqlTransaction? t, int? clienteId, string accao, string detalhe) =>
        Executar(c, t, "INSERT dbo.Historico (ClienteId, Accao, Detalhe, DataUtc, Utilizador) VALUES (@c, @a, @d, SYSUTCDATETIME(), @u)",
            ("@c", clienteId), ("@a", accao), ("@d", detalhe.Length > 1000 ? detalhe[..1000] : detalhe), ("@u", utilizador));

    // ---------------------------------------------------------------- Clientes

    public IReadOnlyList<ClienteResumo> Clientes()
    {
        using var c = Abrir();
        return Ler(c, """
            SELECT c.Id, c.Nome, c.Nuit, c.Contacto, c.Plano, c.ValorMensal, c.Estado, c.Observacoes,
                   CASE WHEN x.Licenca IS NULL OR x.Pago > x.Licenca THEN x.Pago ELSE x.Licenca END,
                   (SELECT MAX(p.Data) FROM dbo.Pagamentos p WHERE p.ClienteId = c.Id AND p.Anulado = 0)
            FROM dbo.Clientes c
            CROSS APPLY (SELECT (SELECT MAX(l.ValidaAte) FROM dbo.Licencas l WHERE l.ClienteId = c.Id) AS Licenca,
                                (SELECT MAX(p.PeriodoAte) FROM dbo.Pagamentos p WHERE p.ClienteId = c.Id AND p.Anulado = 0) AS Pago) x
            ORDER BY c.Nome
            """, r => new ClienteResumo(r.GetInt32(0), r.GetString(1), r.GetString(2), TextoOuNada(r, 3), (Plano)r.GetInt32(4), r.GetDecimal(5),
                (EstadoCliente)r.GetInt32(6), TextoOuNada(r, 7), DataOuNada(r, 8), DataOuNada(r, 9)));
    }

    public ClienteResumo Cliente(int id) =>
        Clientes().FirstOrDefault(x => x.Id == id) ?? throw new InvalidOperationException("Cliente não encontrado.");

    private static (string Nome, string Nuit) ValidarCliente(string nome, string nuit, decimal valorMensal)
    {
        nome = nome.Trim();
        nuit = nuit.Trim();
        if (nome.Length is < 2 or > 150)
            throw new ArgumentException("Indique o nome comercial (2 a 150 caracteres).");
        if (!FormatoLicenca.NuitValido(nuit))
            throw new ArgumentException("O NUIT tem 9 algarismos.");
        if (valorMensal < 0 || decimal.Round(valorMensal, 2) != valorMensal)
            throw new ArgumentException("Valor por mês inválido.");
        return (nome, nuit);
    }

    public int CriarCliente(string nome, string nuit, string? contacto, Plano plano, decimal valorMensal, string? observacoes)
    {
        (nome, nuit) = ValidarCliente(nome, nuit, valorMensal);
        using var c = Abrir();
        using var t = c.BeginTransaction();
        if (Escalar(c, t, "SELECT Nome FROM dbo.Clientes WHERE Nuit = @n", ("@n", nuit)) is string existente)
            throw new InvalidOperationException($"O NUIT {nuit} já é do cliente «{existente}».");
        var id = (int)Escalar(c, t, """
            INSERT dbo.Clientes (Nome, Nuit, Contacto, Plano, ValorMensal, Estado, Observacoes, CriadoEmUtc)
            OUTPUT INSERTED.Id VALUES (@nome, @nuit, @contacto, @plano, @valor, 0, @obs, SYSUTCDATETIME())
            """, ("@nome", nome), ("@nuit", nuit), ("@contacto", Limpo(contacto)), ("@plano", (int)plano), ("@valor", valorMensal), ("@obs", Limpo(observacoes)))!;
        Registar(c, t, id, "cliente.criar", $"{nome} (NUIT {nuit}), {plano}, {RegrasCobranca.Valor(valorMensal)}/mês");
        t.Commit();
        return id;
    }

    /// <summary>Altera os dados do cliente (o NUIT não muda: é o da licença).</summary>
    public void AlterarCliente(int id, string nome, string? contacto, Plano plano, decimal valorMensal, string? observacoes)
    {
        var actual = Cliente(id);
        (nome, _) = ValidarCliente(nome, actual.Nuit, valorMensal);
        using var c = Abrir();
        using var t = c.BeginTransaction();
        Executar(c, t, "UPDATE dbo.Clientes SET Nome = @nome, Contacto = @contacto, Plano = @plano, ValorMensal = @valor, Observacoes = @obs WHERE Id = @id",
            ("@nome", nome), ("@contacto", Limpo(contacto)), ("@plano", (int)plano), ("@valor", valorMensal), ("@obs", Limpo(observacoes)), ("@id", id));
        Registar(c, t, id, "cliente.alterar",
            $"{actual.Nome} → {nome}; {actual.Plano} → {plano}; {RegrasCobranca.Valor(actual.ValorMensal)} → {RegrasCobranca.Valor(valorMensal)}/mês");
        t.Commit();
    }

    /// <summary>Suspende (ex.: falta de pagamento), reactiva ou cancela. Exige o motivo; fica no histórico.</summary>
    public void MudarEstado(int id, EstadoCliente novo, string motivo)
    {
        motivo = motivo.Trim();
        if (motivo.Length < 3)
            throw new ArgumentException("Indique o motivo.");
        var actual = Cliente(id);
        if (actual.Estado == novo)
            throw new InvalidOperationException($"O cliente já está {novo.ToString().ToLowerInvariant()}.");
        using var c = Abrir();
        using var t = c.BeginTransaction();
        Executar(c, t, "UPDATE dbo.Clientes SET Estado = @e WHERE Id = @id", ("@e", (int)novo), ("@id", id));
        Registar(c, t, id, novo switch { EstadoCliente.Suspenso => "cliente.suspender", EstadoCliente.Cancelado => "cliente.cancelar", _ => "cliente.reactivar" },
            $"{actual.Estado} → {novo}: {motivo}");
        t.Commit();
    }

    // ---------------------------------------------------------------- Pagamentos e licenças

    private static void ExigirActivo(ClienteResumo cliente)
    {
        if (cliente.Estado != EstadoCliente.Activo)
            throw new InvalidOperationException($"O cliente «{cliente.Nome}» está {cliente.Estado.ToString().ToLowerInvariant()}: reactive-o antes de emitir licenças.");
    }

    /// <summary>
    /// Regista o pagamento e emite logo a licença até ao fim do período pago (tudo ou nada). Um pagamento antigo, cujo período já
    /// terminou, fica só registado (sem licença: já não serviria).
    /// </summary>
    public (PagamentoRegisto Pagamento, LicencaEmitida? Licenca) RegistarPagamento(ArmazemLicencas armazem, int clienteId, DateOnly data, decimal valor,
        int meses, string? referencia, DateOnly hoje)
    {
        var cliente = Cliente(clienteId);
        ExigirActivo(cliente);
        if (valor <= 0 || decimal.Round(valor, 2) != valor)
            throw new ArgumentException("Indique o valor pago.");
        if (data > hoje)
            throw new ArgumentException("A data do pagamento não pode ser depois de hoje.");
        var periodoAte = RegrasCobranca.NovaValidade(cliente.ValidaAte, data, meses);

        using var c = Abrir();
        using var t = c.BeginTransaction();
        var id = (int)Escalar(c, t, """
            INSERT dbo.Pagamentos (ClienteId, Data, Valor, Meses, Referencia, PeriodoAte, RegistadoEmUtc, RegistadoPor)
            OUTPUT INSERTED.Id VALUES (@c, @d, @v, @m, @r, @ate, SYSUTCDATETIME(), @u)
            """, ("@c", clienteId), ("@d", data), ("@v", valor), ("@m", meses), ("@r", Limpo(referencia)), ("@ate", periodoAte), ("@u", utilizador))!;
        Registar(c, t, clienteId, "pagamento.registar",
            $"{RegrasCobranca.Valor(valor)} em {data:dd/MM/yyyy}, {meses} {(meses == 1 ? "mês" : "meses")}{(Limpo(referencia) is { } r ? $", ref. {r}" : "")}; pago até {periodoAte:dd/MM/yyyy}");
        LicencaEmitida? licenca = null;
        if (periodoAte > hoje)
        {
            licenca = armazem.Emitir(cliente.Nome, cliente.Nuit, periodoAte, hoje, $"Pagamento {id}");
            GuardarLicenca(c, t, clienteId, id, licenca);
        }
        t.Commit();
        return (new PagamentoRegisto(id, data, valor, meses, Limpo(referencia), periodoAte, false, null, utilizador), licenca);
    }

    /// <summary>Licença sem pagamento (teste, oferta, contrato pago por fora). Exige o motivo.</summary>
    public LicencaEmitida EmitirSemPagamento(ArmazemLicencas armazem, int clienteId, DateOnly validaAte, string motivo, DateOnly hoje)
    {
        var cliente = Cliente(clienteId);
        ExigirActivo(cliente);
        motivo = motivo.Trim();
        if (motivo.Length < 3)
            throw new ArgumentException("Indique o motivo (ex.: período de teste).");
        using var c = Abrir();
        using var t = c.BeginTransaction();
        var licenca = armazem.Emitir(cliente.Nome, cliente.Nuit, validaAte, hoje, motivo);
        GuardarLicenca(c, t, clienteId, null, licenca);
        t.Commit();
        return licenca;
    }

    private void GuardarLicenca(SqlConnection c, SqlTransaction t, int clienteId, int? pagamentoId, LicencaEmitida l)
    {
        Executar(c, t, """
            INSERT dbo.Licencas (LicencaId, ClienteId, PagamentoId, EmitidaEm, ValidaAte, Texto, Observacoes, EmitidaEmUtc, EmitidaPor)
            VALUES (@id, @c, @p, @em, @ate, @texto, @obs, @utc, @u)
            """, ("@id", l.Dados.Id), ("@c", clienteId), ("@p", pagamentoId), ("@em", l.Dados.EmitidaEm), ("@ate", l.Dados.ValidaAte),
            ("@texto", l.Texto), ("@obs", l.Observacoes), ("@utc", l.EmitidaEmUtc), ("@u", utilizador));
        Registar(c, t, clienteId, "licenca.emitir", $"Licença {l.Dados.Id} válida até {l.Dados.ValidaAte:dd/MM/yyyy}{(l.Observacoes is { } o ? $" ({o})" : "")}");
    }

    /// <summary>
    /// Anula um pagamento registado por engano (fica na base, marcado). A licença já emitida com ele continua válida no cliente
    /// até à data dela: o sistema não tem forma de a retirar antes.
    /// </summary>
    public void AnularPagamento(int pagamentoId, string motivo)
    {
        motivo = motivo.Trim();
        if (motivo.Length < 3)
            throw new ArgumentException("Indique o motivo da anulação.");
        using var c = Abrir();
        using var t = c.BeginTransaction();
        var clienteId = Escalar(c, t, "SELECT ClienteId FROM dbo.Pagamentos WHERE Id = @id AND Anulado = 0", ("@id", pagamentoId)) as int?
            ?? throw new InvalidOperationException("Pagamento não encontrado ou já anulado.");
        Executar(c, t, "UPDATE dbo.Pagamentos SET Anulado = 1, MotivoAnulacao = @m WHERE Id = @id", ("@m", motivo), ("@id", pagamentoId));
        Registar(c, t, clienteId, "pagamento.anular", $"Pagamento {pagamentoId}: {motivo}");
        t.Commit();
    }

    public IReadOnlyList<PagamentoRegisto> Pagamentos(int clienteId)
    {
        using var c = Abrir();
        return Ler(c, """
            SELECT Id, Data, Valor, Meses, Referencia, PeriodoAte, Anulado, MotivoAnulacao, RegistadoPor
            FROM dbo.Pagamentos WHERE ClienteId = @c ORDER BY Data DESC, Id DESC
            """, r => new PagamentoRegisto(r.GetInt32(0), Data(r, 1), r.GetDecimal(2), r.GetInt32(3), TextoOuNada(r, 4), Data(r, 5), r.GetBoolean(6),
                TextoOuNada(r, 7), r.GetString(8)), ("@c", clienteId));
    }

    public IReadOnlyList<LicencaRegisto> Licencas(int clienteId)
    {
        using var c = Abrir();
        return Ler(c, """
            SELECT Id, LicencaId, EmitidaEm, ValidaAte, Texto, PagamentoId, Observacoes, EmitidaEmUtc, EmitidaPor
            FROM dbo.Licencas WHERE ClienteId = @c ORDER BY EmitidaEmUtc DESC, Id DESC
            """, r => new LicencaRegisto(r.GetInt32(0), r.GetGuid(1), Data(r, 2), Data(r, 3), r.GetString(4), r.IsDBNull(5) ? null : r.GetInt32(5),
                TextoOuNada(r, 6), DateTime.SpecifyKind(r.GetDateTime(7), DateTimeKind.Utc), r.GetString(8)), ("@c", clienteId));
    }

    public IReadOnlyList<EventoHistorico> Historico(int clienteId)
    {
        using var c = Abrir();
        return Ler(c, "SELECT DataUtc, Accao, Detalhe, Utilizador FROM dbo.Historico WHERE ClienteId = @c ORDER BY Id DESC",
            r => new EventoHistorico(DateTime.SpecifyKind(r.GetDateTime(0), DateTimeKind.Utc), r.GetString(1), r.GetString(2), r.GetString(3)), ("@c", clienteId));
    }

    // ---------------------------------------------------------------- Importação e cópia

    /// <summary>
    /// Traz as licenças do registo antigo (registo.json): um cliente por NUIT (nome da última licença, plano anual, valor 0 a
    /// preencher) e as licenças que ainda não estão na base. Pode repetir-se: o que já foi importado não se duplica.
    /// </summary>
    public int Importar(IEnumerable<LicencaEmitida> registo)
    {
        var importadas = 0;
        using var c = Abrir();
        using var t = c.BeginTransaction();
        foreach (var grupo in registo.GroupBy(l => l.Dados.Nuit))
        {
            var ultima = grupo.OrderByDescending(l => l.EmitidaEmUtc).First();
            var clienteId = Escalar(c, t, "SELECT Id FROM dbo.Clientes WHERE Nuit = @n", ("@n", grupo.Key)) as int?;
            if (clienteId is null)
            {
                clienteId = (int)Escalar(c, t, """
                    INSERT dbo.Clientes (Nome, Nuit, Contacto, Plano, ValorMensal, Estado, Observacoes, CriadoEmUtc)
                    OUTPUT INSERTED.Id VALUES (@nome, @nuit, NULL, @plano, 0, 0, 'Importado do registo antigo: preencha o valor por mês.', SYSUTCDATETIME())
                    """, ("@nome", ultima.Dados.Nome), ("@nuit", grupo.Key), ("@plano", (int)Plano.Anual))!;
                Registar(c, t, clienteId, "cliente.importar", $"{ultima.Dados.Nome} (NUIT {grupo.Key}) do registo.json");
            }
            foreach (var l in grupo.OrderBy(l => l.EmitidaEmUtc))
            {
                if (Escalar(c, t, "SELECT 1 FROM dbo.Licencas WHERE LicencaId = @id", ("@id", l.Dados.Id)) is not null)
                    continue;
                GuardarLicenca(c, t, clienteId.Value, null, l with { Observacoes = l.Observacoes ?? "Importada do registo antigo" });
                importadas++;
            }
        }
        t.Commit();
        return importadas;
    }

    public bool TemLicencas()
    {
        using var c = Abrir();
        return Escalar(c, null, "SELECT TOP 1 1 FROM dbo.Licencas") is not null;
    }

    /// <summary>
    /// Cópia de segurança (.bak, COPY_ONLY). Sem pasta, vai para a pasta de cópias do SQL Server (a que o serviço do SQL Server
    /// consegue escrever). Devolve o caminho do ficheiro.
    /// </summary>
    public string Copia(string? pasta = null)
    {
        using var c = Abrir();
        pasta ??= Escalar(c, null, "SELECT CAST(SERVERPROPERTY('InstanceDefaultBackupPath') AS nvarchar(400))") as string
            ?? throw new InvalidOperationException("Não encontrei a pasta de cópias do SQL Server: escolha uma pasta.");
        var ficheiro = Path.Combine(pasta, $"{nomeBase}-{DateTime.Now:yyyyMMdd-HHmmss}.bak");
        using var cmd = Comando(c, null, $"BACKUP DATABASE [{nomeBase}] TO DISK = @f WITH COPY_ONLY, INIT, CHECKSUM", [("@f", ficheiro)]);
        cmd.CommandTimeout = 600;
        cmd.ExecuteNonQuery();
        Registar(c, null, null, "base.copia", ficheiro);
        return ficheiro;
    }

    /// <summary>Só para os testes: apaga uma base de testes (nome terminado em _Testes).</summary>
    internal void ApagarBaseDeTestes()
    {
        if (!nomeBase.EndsWith("_Testes", StringComparison.Ordinal))
            throw new InvalidOperationException("Só se apagam bases de testes.");
        SqlConnection.ClearAllPools();
        using var c = new SqlConnection(ligacaoMaster);
        c.Open();
        using var cmd = new SqlCommand($"IF DB_ID('{nomeBase}') IS NOT NULL BEGIN ALTER DATABASE [{nomeBase}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{nomeBase}]; END", c);
        cmd.ExecuteNonQuery();
    }
}
