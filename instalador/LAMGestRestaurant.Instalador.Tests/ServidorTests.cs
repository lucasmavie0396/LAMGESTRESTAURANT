using System.Diagnostics;
using LAMGestRestaurant.Comum;
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

        // O posto: a API (como o serviço a corre, com HTTP e HTTPS) responde ao instalador dos postos.
        await Ambiente.ExecutarAsync(c.LigacaoBase, "INSERT INTO Terminais (Nome, Numero, Activo) VALUES (N'Caixa Bar', 7, 1)");
        const int porta = 5093, portaHttps = 5443;
        var pastaCert = Path.Combine(Path.GetTempPath(), "lamgest-cert-" + Guid.NewGuid().ToString("N"));
        using var ca = Certificados.ObterOuCriarCa(Path.Combine(pastaCert, "ca"));
        var cert = Certificados.EmitirServidor(ca, Certificados.NomesDoServidor(InstalacaoServidor.EnderecosLocais()), Path.Combine(pastaCert, "config"));
        var inicio = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        inicio.ArgumentList.Add(Ambiente.ApiDll);
        inicio.WorkingDirectory = Path.GetDirectoryName(Ambiente.ApiDll);
        inicio.Environment["ConnectionStrings__Default"] = c.LigacaoBase;
        inicio.Environment["ASPNETCORE_ENVIRONMENT"] = "Production";
        inicio.Environment["ASPNETCORE_URLS"] = InstalacaoServidor.Enderecos(porta, portaHttps).Replace("0.0.0.0", "localhost");
        inicio.Environment["Kestrel__Certificates__Default__Path"] = cert.FicheiroPfx;
        inicio.Environment["Kestrel__Certificates__Default__Password"] = cert.Senha;
        inicio.Environment["Seguranca__PortaHttps"] = portaHttps.ToString(System.Globalization.CultureInfo.InvariantCulture);
        inicio.Environment["Seguranca__CertificadoCa"] = cert.FicheiroCa;
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

            // HTTPS: o posto lê a autoridade em HTTP, confia só nela e entra já com a palavra-passe cifrada.
            var https = await Certificados.LerDoServidorAsync("localhost", porta);
            Assert.NotNull(https);
            Assert.Equal(portaHttps, https.Porta);
            Assert.Equal(cert.ImpressaoDigitalCa, https.ImpressaoDigital);
            using (var cliente = Certificados.Cliente(https, TimeSpan.FromSeconds(10)))
                Assert.True((await cliente.GetAsync($"https://localhost:{portaHttps}/api/saude")).IsSuccessStatusCode);
            using (var cliente = Certificados.Cliente(https, TimeSpan.FromSeconds(10), new System.Net.CookieContainer()))
            {
                var login = await cliente.PostAsync($"https://localhost:{portaHttps}/api/auth/login", new StringContent(
                    System.Text.Json.JsonSerializer.Serialize(new { nomeUtilizador = "admin.teste", palavraPasse }), System.Text.Encoding.UTF8, "application/json"));
                Assert.True(login.IsSuccessStatusCode, await login.Content.ReadAsStringAsync());
            }

            // Outra autoridade (um servidor falso na rede) é recusada.
            using var falsa = Certificados.ObterOuCriarCa(Path.Combine(pastaCert, "falsa"));
            using (var cliente = Certificados.Cliente(https with { Ca = falsa }, TimeSpan.FromSeconds(10)))
                await Assert.ThrowsAsync<HttpRequestException>(() => cliente.GetAsync($"https://localhost:{portaHttps}/api/saude"));
        }
        finally
        {
            api.Kill(entireProcessTree: true);
            Directory.Delete(pastaCert, recursive: true);
        }
    }

    [Fact]
    public async Task Servidor_e_tambem_posto_criado_pela_base_sem_ip_e_sem_duplicar_ao_repetir()
    {
        var c = Ambiente.Config(Base);
        await ServicoSql.CriarBaseAsync(c);
        var migrar = await Ambiente.CorrerApiAsync(c.LigacaoBase, null, "migrar");
        Assert.True(migrar.Codigo == 0, migrar.Saida);

        var opcoes = new OpcoesPostoServidor(3, PaginaInicial.Todas[1]);
        var primeira = await Ambiente.CorrerApiAsync(c.LigacaoBase, null, InstalacaoServidor.ArgumentosPostoServidor(opcoes, comAgente: true));
        Assert.True(primeira.Codigo == 0, primeira.Saida);
        var criado = InstalacaoServidor.LerPostoServidor(primeira.Saida);
        Assert.Equal((3, true, "Posto 3 — Servidor"), (criado.Numero, criado.Criado, criado.Nome));
        Assert.False(string.IsNullOrEmpty(criado.Chave));

        // O instalador pode correr de novo: o mesmo posto, chave nova do agente.
        var segunda = await Ambiente.CorrerApiAsync(c.LigacaoBase, null, InstalacaoServidor.ArgumentosPostoServidor(opcoes, comAgente: true));
        Assert.True(segunda.Codigo == 0, segunda.Saida);
        var repetido = InstalacaoServidor.LerPostoServidor(segunda.Saida);
        Assert.Equal((criado.TerminalId, false), (repetido.TerminalId, repetido.Criado));
        Assert.NotEqual(criado.Chave, repetido.Chave);

        // Sem agente não sai chave.
        var semAgente = InstalacaoServidor.LerPostoServidor((await Ambiente.CorrerApiAsync(c.LigacaoBase, null, InstalacaoServidor.ArgumentosPostoServidor(opcoes, comAgente: false))).Saida);
        Assert.Null(semAgente.Chave);

        await using var ligacao = new SqlConnection(c.Ligacao(Base));
        await ligacao.OpenAsync();
        await using var cmd = new SqlCommand(
            "SELECT COUNT(*) FROM Terminais WHERE Numero = 3; SELECT Valor FROM Configuracoes WHERE Chave = 'postos.servidor'", ligacao);
        await using var leitor = await cmd.ExecuteReaderAsync();
        Assert.True(await leitor.ReadAsync());
        Assert.Equal(1, leitor.GetInt32(0));
        Assert.True(await leitor.NextResultAsync() && await leitor.ReadAsync());
        Assert.Equal(criado.TerminalId.ToString(System.Globalization.CultureInfo.InvariantCulture), leitor.GetString(0));
    }

    [Fact]
    public void Posto_do_servidor_abre_por_localhost_em_http_ou_https()
    {
        var p = new OpcoesPostoServidor(2, PaginaInicial.Todas[1], EntradaTactil: true);
        var semHttps = new OpcoesServidor(Ambiente.Config(Base), @"C:\LAMGest", 8080, true, false, new TimeOnly(23, 30), null, null, false, Posto: p);
        Assert.Equal("http://localhost:8080/pos?posto=2&tactil=1", InstalacaoServidor.EnderecoPostoLocal(semHttps, p).ToString());
        Assert.Equal("https://localhost:8443/pos?posto=2&tactil=1", InstalacaoServidor.EnderecoPostoLocal(semHttps with { PortaHttps = 8443 }, p).ToString());
        var local = InstalacaoServidor.OpcoesPostoLocal(8080, p, null);
        Assert.Equal(("localhost", 8080, 2, true), (local.Servidor, local.Porta, local.Numero, local.EntradaTactil));

        Assert.Equal(["posto-servidor", "--numero", "2", "--agente"], InstalacaoServidor.ArgumentosPostoServidor(p, comAgente: true));
        Assert.Equal(new PostoCriado(2, 15, false, "Caixa do balcão", null), InstalacaoServidor.LerPostoServidor("info: a ligar\r\nPOSTO 2 15 existente Caixa do balcão\r\n"));
        Assert.Throws<InvalidOperationException>(() => InstalacaoServidor.LerPostoServidor("Existem migrations por aplicar."));
    }

    [Fact]
    public async Task Permissoes_das_copias_encontram_a_conta_SYSTEM_pelo_SID_em_qualquer_idioma_do_Windows()
    {
        // Erro num Windows em Português: «Windows NT user or group 'NT AUTHORITY\SYSTEM' not found» (lá chama-se
        // «AUTORIDADE NT\SISTEMA»). A conta procura-se pelo SID; o nome só se usa para a criar, se ainda não houver login.
        var c = Ambiente.Config(Base);
        await ServicoSql.CriarBaseAsync(c);
        var script = RegrasSql.PermissoesCopias(Base, @"AUTORIDADE NT\SISTEMA");
        Assert.DoesNotContain(@"NT AUTHORITY\SYSTEM", script);
        Assert.Contains(RegrasSql.SidSistema, script);

        // Duas vezes: o instalador pode correr de novo.
        await Ambiente.ExecutarAsync(c.Ligacao(), RegrasSql.PermissoesCopias(Base));
        await Ambiente.ExecutarAsync(c.Ligacao(), script);

        await using var ligacao = new SqlConnection(c.Ligacao(Base));
        await ligacao.OpenAsync();
        await using var papeis = new SqlCommand(
            $"""
            SELECT r.name FROM sys.database_role_members m
            JOIN sys.database_principals u ON u.principal_id = m.member_principal_id
            JOIN sys.database_principals r ON r.principal_id = m.role_principal_id
            WHERE u.sid = {RegrasSql.SidSistema} ORDER BY r.name
            """, ligacao);
        var nomes = new List<string>();
        await using (var leitor = await papeis.ExecuteReaderAsync())
            while (await leitor.ReadAsync())
                nomes.Add(leitor.GetString(0));
        Assert.Equal(["db_backupoperator", "db_ddladmin"], nomes);

        // O nome dado pelo Windows deste computador é o do login que o SQL Server tem para o SID S-1-5-18.
        await using var login = new SqlCommand($"SELECT name FROM sys.server_principals WHERE sid = {RegrasSql.SidSistema}", ligacao);
        Assert.Equal(RegrasSql.NomeContaSistema(), (string?)await login.ExecuteScalarAsync(), ignoreCase: true);
        Assert.Contains("SET @conta = N'O''Brien';", RegrasSql.PermissoesCopias(Base, "O'Brien"));
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
