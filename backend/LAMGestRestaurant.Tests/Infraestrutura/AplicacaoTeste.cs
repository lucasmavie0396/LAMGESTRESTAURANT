using System.Net.Http.Json;
using LAMGestRestaurant.Application.Comum;
using LAMGestRestaurant.Application.Impressao;
using LAMGestRestaurant.Infrastructure.Dados;
using LAMGestRestaurant.Infrastructure.Identidade;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace LAMGestRestaurant.Tests.Infraestrutura;

/// <summary>
/// Arranca a API real contra uma base de dados de testes própria (LAMGestRestaurantDB_Tests),
/// recriada a partir das migrations no início de cada execução.
/// Servidor configurável com a variável LAMGEST_TESTES_SQL (por omissão .\SYSON).
/// </summary>
public class AplicacaoTeste : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string NomeBaseDados = "LAMGestRestaurantDB_Tests";

    public string ConnectionString { get; } = new SqlConnectionStringBuilder
    {
        DataSource = Environment.GetEnvironmentVariable("LAMGEST_TESTES_SQL") ?? @".\SYSON",
        InitialCatalog = NomeBaseDados,
        IntegratedSecurity = true,
        TrustServerCertificate = true,
    }.ConnectionString;

    public async Task InitializeAsync()
    {
        // Salvaguarda: este código elimina a base de dados; nunca pode apontar para outra que não a de testes.
        if (!new SqlConnectionStringBuilder(ConnectionString).InitialCatalog.EndsWith("_Tests", StringComparison.Ordinal))
            throw new InvalidOperationException("A base de dados de testes tem de terminar em _Tests.");

        await using var db = CriarContexto();
        await db.Database.EnsureDeletedAsync();
        // Os testes correm no nível da versão mais antiga suportada (SQL Server 2016): migrations e consultas têm de servir nela.
        var mestre = new SqlConnectionStringBuilder(ConnectionString) { InitialCatalog = "master" }.ConnectionString;
        await using (var ligacao = new SqlConnection(mestre))
        {
            await ligacao.OpenAsync();
            await using var comando = new SqlCommand($"CREATE DATABASE [{NomeBaseDados}]; ALTER DATABASE [{NomeBaseDados}] SET COMPATIBILITY_LEVEL = 130;", ligacao);
            await comando.ExecuteNonQueryAsync();
        }
        SqlConnection.ClearAllPools();
        await db.Database.MigrateAsync();

        // Licença de teste válida (assinada com a chave de teste) para os restantes testes trabalharem normalmente.
        await InstalarLicencaAsync(db, DateOnly.FromDateTime(DateTime.UtcNow).AddYears(1));
        // Os testes abrem caixas sem «Abrir Dia» (os testes do dia de operação ligam a exigência e voltam a desligá-la).
        db.Configuracoes.Add(new Domain.Sistema.Configuracao { Chave = Domain.Sistema.ChavesConfiguracao.ExigirDiaAberto, Valor = "false" });
        await db.SaveChangesAsync();

        // A configuração da aplicação é lida no arranque do Program; a variável de ambiente garante a base de testes.
        Environment.SetEnvironmentVariable("ConnectionStrings__Default", ConnectionString);
    }

    /// <summary>NUIT do estabelecimento de teste (o da licença instalada no início).</summary>
    public const string NuitTeste = "400000001";

    /// <summary>Chave de teste: faz o papel do gerador do fabricante (a chave real nunca entra nos testes).</summary>
    public static readonly System.Security.Cryptography.ECDsa ChaveLicencasTeste = Licenciamento.ChavesLicenca.Nova();

    public static string LicencaTeste(DateOnly validaAte, string nuit = NuitTeste, string nome = "Restaurante de Teste", DateOnly? emitidaEm = null) =>
        Licenciamento.FormatoLicenca.Codificar(new Licenciamento.DadosLicenca(Guid.NewGuid(), nome, nuit,
            emitidaEm ?? DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1), validaAte), ChaveLicencasTeste);

    /// <summary>
    /// Instala uma licença directamente na base (também já expirada, o que a API recusaria) e apaga a cache do estado.
    /// Devolve o texto instalado.
    /// </summary>
    public async Task<string> InstalarLicencaAsync(DateOnly validaAte, DateOnly? emitidaEm = null)
    {
        await using var db = CriarContexto();
        var texto = await InstalarLicencaAsync(db, validaAte, emitidaEm);
        Services.GetRequiredService<Application.Licencas.CacheEstadoLicenca>().Invalidar();
        return texto;
    }

    private static async Task<string> InstalarLicencaAsync(AppDbContext db, DateOnly validaAte, DateOnly? emitidaEm = null)
    {
        var texto = LicencaTeste(validaAte, emitidaEm: emitidaEm ?? (validaAte < DateOnly.FromDateTime(DateTime.UtcNow) ? validaAte.AddYears(-1) : null));
        var dados = Licenciamento.FormatoLicenca.Ler(texto, ChaveLicencasTeste, out _)!;
        db.Licencas.Add(new Domain.Sistema.LicencaSistema
        {
            LicencaId = dados.Id, NomeComercial = dados.Nome, Nuit = dados.Nuit, EmitidaEm = dados.EmitidaEm, ValidaAte = dados.ValidaAte,
            Texto = texto, ActivadaEmUtc = DateTime.UtcNow, UltimaVerificacaoUtc = DateTime.UtcNow,
        });
        var nuit = await db.Configuracoes.FirstOrDefaultAsync(c => c.Chave == Domain.Sistema.ChavesConfiguracao.Nuit);
        if (nuit is null)
            db.Configuracoes.Add(new Domain.Sistema.Configuracao { Chave = Domain.Sistema.ChavesConfiguracao.Nuit, Valor = NuitTeste });
        else
            nuit.Valor = NuitTeste;
        await db.SaveChangesAsync();
        return texto;
    }

    /// <summary>Impressoras simuladas: registam o que seria impresso (nenhum papel sai durante os testes).</summary>
    public ImpressorasTeste Impressoras { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Default", ConnectionString);
        builder.UseSetting("Impressao:TempoMaximoSegundos", "2"); // a impressora «Pendurada» dá erro depressa
        builder.UseSetting("Licenciamento:ChavePublicaTeste", Licenciamento.ChavesLicenca.ExportarPublica(ChaveLicencasTeste));
        builder.ConfigureServices(s =>
        {
            s.RemoveAll<IDispositivoImpressao>();
            s.AddSingleton<IDispositivoImpressao>(Impressoras);
            // Notificador real, mais o registo dos PDF entregues (o teste não tem cliente SignalR).
            s.RemoveAll<INotificador>();
            s.AddSingleton<Api.TempoReal.NotificadorSignalR>();
            s.AddSingleton<INotificador>(sp => new NotificadorComRegisto(sp.GetRequiredService<Api.TempoReal.NotificadorSignalR>(), Pdfs));
        });
    }

    /// <summary>PDF das impressoras em modo PDF entregues a cada utilizador.</summary>
    public PdfsTeste Pdfs { get; } = new();

    public AppDbContext CriarContexto() => CriarContexto(ConnectionString);

    public static AppDbContext CriarContexto(string ligacao) =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(ligacao).Options, new SemUtilizador(), TimeProvider.System);

    /// <summary>Executa código com os serviços da aplicação (UserManager, AppDbContext, ...).</summary>
    public async Task<T> ComServicos<T>(Func<IServiceProvider, Task<T>> accao)
    {
        using var scope = Services.CreateScope();
        return await accao(scope.ServiceProvider);
    }

    public const string PalavraPasseTeste = "segredo123";

    /// <summary>Cria um utilizador com o perfil indicado e devolve o nome de utilizador.</summary>
    /// <summary>Valor esperado de um método numa sessão (calculado no servidor; a API só o mostra depois do fecho).</summary>
    public Task<decimal> EsperadoAsync(int sessaoId, int metodoId) => ComServicos(async s =>
    {
        var sessao = await s.GetRequiredService<AppDbContext>().SessoesCaixa.Include(x => x.LinhasFecho).FirstAsync(x => x.Id == sessaoId);
        return await s.GetRequiredService<Application.Caixa.ServicoCaixa>().EsperadoNoMetodoAsync(sessao, metodoId);
    });

    public Task<string> CriarUtilizadorAsync(string perfil, bool activo = true, bool deveAlterar = false) => ComServicos(async s =>
    {
        await s.GetRequiredService<SemeadorDados>().ExecutarAsync();
        var um = s.GetRequiredService<UserManager<Utilizador>>();
        var u = new Utilizador
        {
            UserName = $"t{Guid.NewGuid():N}"[..15],
            NomeCompleto = "Teste",
            Activo = activo,
            DeveAlterarPalavraPasse = deveAlterar,
            CriadoEmUtc = DateTime.UtcNow,
        };
        var criado = await um.CreateAsync(u, PalavraPasseTeste);
        if (!criado.Succeeded || !(await um.AddToRoleAsync(u, perfil)).Succeeded)
            throw new InvalidOperationException("Falha ao criar utilizador de teste.");
        return u.UserName!;
    });

    /// <summary>Cliente HTTP com sessão iniciada por um novo utilizador do perfil indicado.</summary>
    public async Task<(HttpClient Cliente, string NomeUtilizador)> ClienteAutenticadoAsync(string perfil)
    {
        var nome = await CriarUtilizadorAsync(perfil);
        var cliente = CreateClient();
        var resposta = await cliente.PostAsJsonAsync("/api/auth/login", new { nomeUtilizador = nome, palavraPasse = PalavraPasseTeste });
        resposta.EnsureSuccessStatusCode();
        return (cliente, nome);
    }

    /// <summary>Cliente com sessão de um utilizador cujo perfil (criado para o teste) tem exactamente estas permissões.</summary>
    public async Task<HttpClient> ClienteComPermissoesAsync(params string[] permissoes)
    {
        var nomePerfil = $"Teste {Guid.NewGuid():N}"[..20];
        await ComServicos(async s =>
        {
            var rm = s.GetRequiredService<RoleManager<Perfil>>();
            var perfil = new Perfil(nomePerfil);
            await rm.CreateAsync(perfil);
            foreach (var p in permissoes)
                await rm.AddClaimAsync(perfil, new System.Security.Claims.Claim(Application.Seguranca.Permissoes.ClaimType, p));
            return true;
        });
        return (await ClienteAutenticadoAsync(nomePerfil)).Cliente;
    }

    /// <summary>Opções JSON iguais às da API (enums como texto).</summary>
    public static readonly System.Text.Json.JsonSerializerOptions Json = new(System.Text.Json.JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    Task IAsyncLifetime.DisposeAsync() => Task.CompletedTask;

    private sealed class SemUtilizador : IUtilizadorActual
    {
        public int? Id => null;
        public string? Nome => null;
        public string? EnderecoIp => null;
        public bool TemPermissao(string permissao) => false;
    }
}

[CollectionDefinition(Nome)]
public class ColeccaoBaseDados : ICollectionFixture<AplicacaoTeste>
{
    public const string Nome = "BaseDados";
}
