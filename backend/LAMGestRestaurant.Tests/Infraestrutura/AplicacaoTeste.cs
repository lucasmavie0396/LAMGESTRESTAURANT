using LAMGestRestaurant.Application.Comum;
using LAMGestRestaurant.Infrastructure.Dados;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

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
        await db.Database.MigrateAsync();

        // A configuração da aplicação é lida no arranque do Program; a variável de ambiente garante a base de testes.
        Environment.SetEnvironmentVariable("ConnectionStrings__Default", ConnectionString);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Default", ConnectionString);
    }

    public AppDbContext CriarContexto() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(ConnectionString).Options, new SemUtilizador(), TimeProvider.System);

    /// <summary>Executa código com os serviços da aplicação (UserManager, AppDbContext, ...).</summary>
    public async Task<T> ComServicos<T>(Func<IServiceProvider, Task<T>> accao)
    {
        using var scope = Services.CreateScope();
        return await accao(scope.ServiceProvider);
    }

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
