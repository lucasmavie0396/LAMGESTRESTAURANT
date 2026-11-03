using System.Diagnostics;
using LAMGestRestaurant.Instalador.Motor;
using Microsoft.Data.SqlClient;

namespace LAMGestRestaurant.Instalador.Tests;

/// <summary>
/// Passos de base de dados do instalador do servidor contra o SQL Server de desenvolvimento, em bases próprias *_Tests.
/// (O serviço Windows, firewall e tarefa agendada alteram o sistema e não são executados nos testes.)
/// </summary>
public class ServidorTests : IAsyncLifetime
{
    private const string Base = "LAMGestRestaurantDB_Instalador_Tests";
    private const string Outra = "LAMGestRestaurantDB_Outra_Tests";

    public async Task InitializeAsync()
    {
        await Ambiente.EliminarBaseAsync(Base);
        await Ambiente.EliminarBaseAsync(Outra);
    }

    public async Task DisposeAsync()
    {
        await Ambiente.EliminarBaseAsync(Base);
        await Ambiente.EliminarBaseAsync(Outra);
    }

    [Fact]
    public async Task Base_nova_e_criada_migrada_e_recebe_o_administrador_e_o_posto_fica_consultavel()
    {
        var c = Ambiente.Config(Base);
        var info = await ServicoSql.TestarAsync(c);
        Assert.True(info.Sysadmin);

        var inicial = await ServicoSql.AnalisarAsync(c, Ambiente.Migracoes);
        Assert.False(inicial.Existe);
        Assert.Equal(Ambiente.Migracoes, inicial.Pendentes);

        await ServicoSql.CriarBaseAsync(c);
        var vazia = await ServicoSql.AnalisarAsync(c, Ambiente.Migracoes);
        Assert.True(vazia is { Existe: true, Vazia: true, DoLamgest: false });

        var migrar = await Ambiente.CorrerApiAsync(c.LigacaoBase, null, "migrar");
        Assert.True(migrar.Codigo == 0, migrar.Saida);
        var migrada = await ServicoSql.AnalisarAsync(c, Ambiente.Migracoes);
        Assert.True(migrada is { DoLamgest: true, TemAdministrador: false, Pedidos: 0, ActualizacaoComDados: false });
        Assert.Empty(migrada.Pendentes);

        // Instalador mais antigo do que a base: a migration mais recente aparece como desconhecida.
        var antigo = await ServicoSql.AnalisarAsync(c, Ambiente.Migracoes.SkipLast(1).ToList());
        Assert.Equal([Ambiente.Migracoes[^1]], antigo.Desconhecidas);

        var palavraPasse = Segredos.GerarPalavraPasse(12).ToLowerInvariant() + "1";
        var admin = await Ambiente.CorrerApiAsync(c.LigacaoBase, $"admin.teste\nAdministrador Teste\n{palavraPasse}\n{palavraPasse}\n", "bootstrap-admin");
        Assert.True(admin.Codigo == 0, admin.Saida);
        Assert.True((await ServicoSql.AnalisarAsync(c, Ambiente.Migracoes)).TemAdministrador);

        var copia = await ServicoSql.CopiaSegurancaAsync(c, "teste");
        try
        {
            Assert.True(File.Exists(copia));
        }
        finally
        {
            File.Delete(copia);
        }

        // O posto: a API (como o serviço a corre) responde ao instalador dos postos.
        await Ambiente.ExecutarAsync(c.LigacaoBase, "INSERT INTO Terminais (Nome, Numero, Activo) VALUES (N'Caixa Bar', 7, 1)");
        const int porta = 5093;
        var inicio = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        inicio.ArgumentList.Add(Ambiente.ApiDll);
        inicio.WorkingDirectory = Path.GetDirectoryName(Ambiente.ApiDll);
        inicio.Environment["ConnectionStrings__Default"] = c.LigacaoBase;
        inicio.Environment["ASPNETCORE_ENVIRONMENT"] = "Production";
        inicio.Environment["ASPNETCORE_URLS"] = $"http://localhost:{porta}";
        using var api = Process.Start(inicio)!;
        api.OutputDataReceived += (_, _) => { };
        api.ErrorDataReceived += (_, _) => { };
        api.BeginOutputReadLine();
        api.BeginErrorReadLine();
        try
        {
            string? estado = null;
            for (var i = 0; i < 30 && estado is null; i++)
            {
                try { estado = await InstalacaoPosto.TestarServidorAsync("localhost", porta); }
                catch (InvalidOperationException) { await Task.Delay(1000); }
            }
            Assert.Equal("Servidor LAMGest a funcionar.", estado);
            Assert.Equal("Caixa Bar", (await InstalacaoPosto.ObterPostoAsync("localhost", porta, 7))?.Nome);
            Assert.Null(await InstalacaoPosto.ObterPostoAsync("localhost", porta, 8));
        }
        finally
        {
            api.Kill(entireProcessTree: true);
        }
    }

    [Fact]
    public async Task Base_de_outro_sistema_nunca_e_tratada_como_do_lamgest()
    {
        var c = Ambiente.Config(Outra);
        await ServicoSql.CriarBaseAsync(c);
        await Ambiente.ExecutarAsync(c.LigacaoBase, "CREATE TABLE Clientes (Id int)");

        var estado = await ServicoSql.AnalisarAsync(c, Ambiente.Migracoes);

        Assert.True(estado is { Existe: true, Vazia: false, DoLamgest: false });
        Assert.Empty(estado.Pendentes);
    }

    [Fact]
    public async Task Nomes_fora_do_padrao_sao_recusados_sem_ligar_a_base()
    {
        Assert.NotNull(ServicoSql.ValidarNomeBase("MZullo_Rest"));
        Assert.NotNull(ServicoSql.ValidarNomeBase("master"));
        Assert.NotNull(ServicoSql.ValidarNomeBase("LAMGestRestaurantDB]; DROP DATABASE x; --"));
        Assert.Null(ServicoSql.ValidarNomeBase("LAMGestRestaurantDB"));

        var c = new ConfigSql(Ambiente.Instancia, true, null, null, "MZullo_Rest");
        await Assert.ThrowsAsync<InvalidOperationException>(() => ServicoSql.AnalisarAsync(c, Ambiente.Migracoes));
        await Assert.ThrowsAsync<InvalidOperationException>(() => ServicoSql.CriarBaseAsync(c));
    }

    [Fact]
    public async Task Login_sql_do_servico_le_e_escreve_dados_mas_nao_altera_a_estrutura()
    {
        var c = Ambiente.Config(Base);
        var info = await ServicoSql.TestarAsync(c);
        await ServicoSql.CriarBaseAsync(c);
        await Ambiente.ExecutarAsync(c.LigacaoBase, "CREATE TABLE Dados (Id int)");
        var login = "lamgest_teste_" + Guid.NewGuid().ToString("N")[..8];
        var palavraPasse = Segredos.GerarPalavraPasse();
        try
        {
            await ServicoSql.GarantirLoginSqlAsync(c, login, palavraPasse);
            await ServicoSql.GarantirLoginSqlAsync(c, login, palavraPasse); // idempotente (reinstalação)
            if (!info.AutenticacaoMista)
                return; // instância só com autenticação Windows: o login existe mas não pode ser usado

            var ligacao = new SqlConnectionStringBuilder(c.LigacaoBase) { IntegratedSecurity = false, UserID = login, Password = palavraPasse }.ConnectionString;
            await Ambiente.ExecutarAsync(ligacao, "INSERT INTO Dados VALUES (1); SELECT COUNT(*) FROM Dados;");
            await Assert.ThrowsAsync<SqlException>(() => Ambiente.ExecutarAsync(ligacao, "CREATE TABLE Outra (Id int)"));
        }
        finally
        {
            await Ambiente.EliminarBaseAsync(Base);
            await Ambiente.ExecutarAsync(c.Ligacao(), $"IF SUSER_ID(N'{login}') IS NOT NULL DROP LOGIN [{login}]");
        }
    }
}
