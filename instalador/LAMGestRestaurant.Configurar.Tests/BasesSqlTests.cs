using System;
using System.Data.SqlClient;
using System.Linq;
using System.Threading.Tasks;
using LAMGestRestaurant.Configurar.Motor;
using Xunit;

namespace LAMGestRestaurant.Configurar.Tests;

/// <summary>
/// Com o SQL Server de desenvolvimento (LAMGEST_TESTES_SQL ou .\SYSON). Cria e elimina só bases próprias, terminadas em
/// _Tests; nenhuma outra base da instância é aberta.
/// </summary>
public sealed class BasesSqlTests : IAsyncLifetime
{
    private static readonly string Instancia = Environment.GetEnvironmentVariable("LAMGEST_TESTES_SQL") ?? @".\SYSON";
    private readonly string actual = "LAMGestRestaurant_ConfActual_" + Guid.NewGuid().ToString("N").Substring(0, 6) + "_Tests";
    private readonly string antiga = "LAMGestRestaurant_ConfAntiga_" + Guid.NewGuid().ToString("N").Substring(0, 6) + "_Tests";
    private readonly LigacaoAdmin admin = new(Instancia, autenticacaoWindows: true);
    private static readonly string[] Programa = { "20260101000000_Inicial", "20260201000000_Mesas" };

    public async Task InitializeAsync()
    {
        await CriarAsync(actual, Programa);
        await CriarAsync(antiga, Programa.Take(1).ToArray());
    }

    public async Task DisposeAsync()
    {
        foreach (var b in new[] { actual, antiga })
            await ExecutarAsync(admin.Ligacao(), $"IF DB_ID(N'{b}') IS NOT NULL BEGIN ALTER DATABASE [{b}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{b}]; END");
    }

    private async Task CriarAsync(string nome, string[] migracoes)
    {
        if (!nome.EndsWith("_Tests", StringComparison.Ordinal))
            throw new InvalidOperationException("Só bases de teste.");
        await ExecutarAsync(admin.Ligacao(), $"CREATE DATABASE [{nome}]");
        await ExecutarAsync(admin.Ligacao(nome),
            "CREATE TABLE __EFMigrationsHistory (MigrationId nvarchar(150) NOT NULL PRIMARY KEY, ProductVersion nvarchar(32) NOT NULL);" +
            "CREATE TABLE RegistosAuditoria (Id bigint IDENTITY PRIMARY KEY, DataUtc datetime2 NOT NULL, UtilizadorId int NULL, NomeUtilizador nvarchar(256) NULL, " +
            "Accao nvarchar(60) NOT NULL, Entidade nvarchar(60) NULL, EntidadeId nvarchar(60) NULL, Detalhes nvarchar(max) NULL, EnderecoIp nvarchar(45) NULL);" +
            string.Concat(migracoes.Select(m => $"INSERT INTO __EFMigrationsHistory VALUES (N'{m}', N'9.0');")));
    }

    private static async Task ExecutarAsync(string ligacao, string sql)
    {
        using var c = new SqlConnection(ligacao);
        await c.OpenAsync();
        using var cmd = new SqlCommand(sql, c);
        await cmd.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task Lista_so_bases_LAMGestRestaurant_e_mostra_a_situacao_de_cada()
    {
        var lista = await BasesSql.ListarAsync(admin, Programa);

        Assert.All(lista, b => Assert.StartsWith("LAMGestRestaurant", b.Nome));
        Assert.DoesNotContain(lista, b => b.Nome.Equals("MZullo_Rest", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(SituacaoBase.Actualizada, lista.Single(b => b.Nome == actual).Situacao);
        var b2 = lista.Single(b => b.Nome == antiga);
        Assert.Equal(SituacaoBase.PrecisaActualizacao, b2.Situacao);
        Assert.Equal(new[] { "20260201000000_Mesas" }, b2.Pendentes);
        Assert.Equal("20260101000000_Inicial", b2.UltimaMigracao);
    }

    [Fact]
    public async Task Recusa_abrir_bases_fora_do_LAMGest()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => BasesSql.AnalisarAsync(admin, "MZullo_Rest", Programa));
        await Assert.ThrowsAsync<InvalidOperationException>(() => BasesSql.AuditarAsync(admin, "master", "x", "{}"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => BasesSql.GarantirAcessoServicoAsync(admin, "MZullo_Rest", null));
    }

    [Fact]
    public async Task Da_acesso_a_conta_do_servico_e_regista_a_troca_na_auditoria()
    {
        var ligacao = await BasesSql.GarantirAcessoServicoAsync(admin, actual,
            $@"Data Source={Instancia};Initial Catalog=LAMGestRestaurantDB;Integrated Security=True;Trust Server Certificate=True");
        var b = TextoLigacao.Ler(ligacao);
        Assert.Equal(actual, b.BaseDados);
        Assert.True(b.Integrada);
        Assert.Equal(Instancia, b.Servidor);
        Assert.Contains("trust server certificate=true", ligacao.ToLowerInvariant());

        using (var c = new SqlConnection(admin.Ligacao(actual)))
        {
            await c.OpenAsync();
            using var cmd = new SqlCommand($"SELECT IS_ROLEMEMBER('db_datawriter', N'{BasesSql.ContaServico}')", c);
            Assert.Equal(1, Convert.ToInt32(await cmd.ExecuteScalarAsync()));
        }

        Assert.True(await BasesSql.AuditarAsync(admin, actual, "sistema.trocar-base-dados", "{\"De\":\"a\",\"Para\":\"b\"}"));
        using (var c = new SqlConnection(admin.Ligacao(actual)))
        {
            await c.OpenAsync();
            using var cmd = new SqlCommand("SELECT Accao + '|' + Entidade + '|' + Detalhes + '|' + NomeUtilizador FROM RegistosAuditoria", c);
            var linha = (string)await cmd.ExecuteScalarAsync();
            Assert.StartsWith("sistema.trocar-base-dados|Sistema|{\"De\":\"a\",\"Para\":\"b\"}|Configurador (", linha);
        }
    }
}
