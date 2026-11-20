using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using LAMGestRestaurant.Configurar.Motor;
using Xunit;

namespace LAMGestRestaurant.Configurar.Tests;

public sealed class ConfiguradorTests : IDisposable
{
    private readonly string pasta = Path.Combine(Path.GetTempPath(), "lamgest-configurar-testes-" + Guid.NewGuid().ToString("N"));

    public ConfiguradorTests() => Directory.CreateDirectory(pasta);

    public void Dispose()
    {
        try
        {
            Directory.Delete(pasta, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    internal static string Raiz()
    {
        var d = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
        while (d is not null && !Directory.Exists(Path.Combine(d.FullName, "instalador")))
            d = d.Parent;
        return d?.FullName ?? throw new InvalidOperationException("Raiz do repositório não encontrada.");
    }

    // ---------------- Código de acesso ----------------

    [Fact]
    public void Codigo_usa_a_mesma_derivacao_do_instalador_e_recusa_codigos_errados()
    {
        // O código nunca está no repositório: confirma-se que o sal e a derivação são os do LAMGest-Setup.exe.
        var instalador = File.ReadAllText(Path.Combine(Raiz(), @"instalador\LAMGestRestaurant.Instalador\Motor\CodigoInstalacao.cs"));
        Assert.Contains($"\"{CodigoAcesso.SalBase64}\"", instalador);
        Assert.Contains($"\"{CodigoAcesso.DerivadoBase64}\"", instalador);
        Assert.Contains("200_000", instalador);

        Assert.False(CodigoAcesso.Valido(null));
        Assert.False(CodigoAcesso.Valido(""));
        Assert.False(CodigoAcesso.Valido("admin"));
        Assert.False(CodigoAcesso.Valido("codigo-errado-123#"));
    }

    [Fact]
    public void Bloqueio_ao_fim_de_cinco_codigos_errados_durante_cinco_minutos_mesmo_reabrindo()
    {
        var agora = new DateTime(2026, 11, 20, 10, 0, 0, DateTimeKind.Utc);
        var ficheiro = Path.Combine(pasta, "config", "configurar.bloqueio");
        BloqueioTentativas Novo() => new(ficheiro, () => agora);

        var b = Novo();
        Assert.Null(b.Restante());
        for (var i = 0; i < 4; i++)
            Assert.False(b.RegistarFalha());
        Assert.Equal(1, b.TentativasRestantes);
        Assert.True(Novo().RegistarFalha()); // outra «abertura» do programa continua a contar

        Assert.Equal(TimeSpan.FromMinutes(5), Novo().Restante());
        agora = agora.AddMinutes(4);
        Assert.NotNull(Novo().Restante());
        agora = agora.AddMinutes(1).AddSeconds(1);
        Assert.Null(Novo().Restante());
        Assert.Equal(5, Novo().TentativasRestantes);

        Assert.False(Novo().RegistarFalha());
        Novo().Limpar();
        Assert.Equal(5, Novo().TentativasRestantes);
    }

    // ---------------- Bases ----------------

    [Theory]
    [InlineData("LAMGestRestaurantDB", true)]
    [InlineData("LAMGestRestaurant_Filial2", true)]
    [InlineData("MZullo_Rest", false)]
    [InlineData("master", false)]
    [InlineData("LAMGestRestaurant]; DROP DATABASE x--", false)]
    public void So_bases_LAMGestRestaurant_podem_ser_abertas(string nome, bool valido) => Assert.Equal(valido, BasesSql.NomeValido(nome));

    [Fact]
    public void Classifica_a_base_pelas_migrations()
    {
        string[] programa = { "20260101000000_Inicial", "20260201000000_Mesas", "20260301000000_Stock" };

        Assert.Equal(SituacaoBase.Vazia, BasesSql.Classificar(false, new string[0], programa).Situacao);
        Assert.Equal(SituacaoBase.NaoLamgest, BasesSql.Classificar(true, new[] { "20250101_OutroSistema" }, programa).Situacao);
        Assert.Equal(SituacaoBase.Actualizada, BasesSql.Classificar(true, programa, programa).Situacao);

        var (situacao, pendentes) = BasesSql.Classificar(true, programa.Take(1).ToArray(), programa);
        Assert.Equal(SituacaoBase.PrecisaActualizacao, situacao);
        Assert.Equal(programa.Skip(1), pendentes);

        Assert.Equal(SituacaoBase.MaisRecente, BasesSql.Classificar(true, programa.Concat(new[] { "20270101000000_Futura" }).ToArray(), programa).Situacao);
        // Instalação antiga sem a lista do programa: sem comparação de versões.
        Assert.Equal(SituacaoBase.Actualizada, BasesSql.Classificar(true, programa, new string[0]).Situacao);

        Assert.True(new InfoBase { Situacao = SituacaoBase.PrecisaActualizacao }.PodeSerUsada);
        Assert.False(new InfoBase { Situacao = SituacaoBase.MaisRecente }.PodeSerUsada);
        Assert.False(new InfoBase { Situacao = SituacaoBase.Vazia }.PodeSerUsada);
    }

    // ---------------- Servidor ----------------

    [Fact]
    public void Ligacao_do_instalador_muda_so_a_instancia_e_a_base()
    {
        // Como o instalador a escreve (Microsoft.Data.SqlClient), com chaves que o .NET Framework não conhece.
        const string windows = @"Data Source=.\SYSON;Initial Catalog=LAMGestRestaurantDB;Integrated Security=True;Trust Server Certificate=True";
        var p = TextoLigacao.Ler(windows);
        Assert.Equal(@".\SYSON", p.Servidor);
        Assert.Equal("LAMGestRestaurantDB", p.BaseDados);
        Assert.True(p.Integrada);

        var nova = TextoLigacao.Ler(TextoLigacao.Mudar(windows, @".\SQLEXPRESS", "LAMGestRestaurant_Filial2"));
        Assert.Equal(@".\SQLEXPRESS", nova.Servidor);
        Assert.Equal("LAMGestRestaurant_Filial2", nova.BaseDados);
        Assert.True(nova.Integrada);
        Assert.Contains("trust server certificate=true", TextoLigacao.Mudar(windows, ".", "LAMGestRestaurantX").ToLowerInvariant());

        const string sql = "Server=SRV\\SQL;Database=LAMGestRestaurantDB;User ID=lamgest_servico;Password=Ab3;Trust Server Certificate=True";
        var s = TextoLigacao.Ler(sql);
        Assert.False(s.Integrada);
        Assert.Equal("lamgest_servico", s.Utilizador);
        Assert.Equal("Ab3", s.PalavraPasse);
        var mudada = TextoLigacao.Ler(TextoLigacao.Mudar(sql, "SRV\\SQL", "LAMGestRestaurant_B", "lamgest_servico", "Nova9x"));
        Assert.Equal("LAMGestRestaurant_B", mudada.BaseDados);
        Assert.Equal("Nova9x", mudada.PalavraPasse);
    }

    [Fact]
    public void Le_a_porta_e_o_ficheiro_de_configuracao_do_ambiente_do_servico()
    {
        var s = new ServidorInstalado { FicheiroConfig = "padrao.json" };
        ServidorInstalado.AplicarAmbiente(s, new[] { "ASPNETCORE_ENVIRONMENT=Production", "ASPNETCORE_URLS=http://0.0.0.0:8091", @"LAMGEST_CONFIG=D:\Dados\servidor.json" });
        Assert.Equal(8091, s.Porta);
        Assert.Equal(@"D:\Dados\servidor.json", s.FicheiroConfig);
    }

    [Fact]
    public void Muda_so_a_ligacao_no_servidor_json_e_mantem_o_resto()
    {
        var ficheiro = Path.Combine(pasta, "servidor.json");
        File.WriteAllText(ficheiro, "{\r\n  \"ConnectionStrings\": { \"Default\": \"Data Source=.\\\\SYSON;Initial Catalog=LAMGestRestaurantDB;Integrated Security=True\" },\r\n" +
                                    "  \"DataProtection\": { \"Pasta\": \"C:\\\\ProgramData\\\\LAMGestRestaurant\\\\chaves\" },\r\n" +
                                    "  \"Serilog\": { \"WriteTo\": { \"1\": { \"Args\": { \"path\": \"C:\\\\logs\\\\lamgest-.log\" } } } }\r\n}");
        TrocaBase.GravarLigacao(ficheiro, @"Data Source=.\SYSON;Initial Catalog=LAMGestRestaurant_Filial2;Integrated Security=True;Trust Server Certificate=True");

        var lido = Json.LerFicheiro(ficheiro);
        var ligacoes = (IDictionary<string, object?>)Json.Obter(lido, "ConnectionStrings")!;
        Assert.Equal(@"Data Source=.\SYSON;Initial Catalog=LAMGestRestaurant_Filial2;Integrated Security=True;Trust Server Certificate=True", Json.Texto(ligacoes, "Default"));
        Assert.Equal(@"C:\ProgramData\LAMGestRestaurant\chaves", Json.Texto((IDictionary<string, object?>)Json.Obter(lido, "DataProtection")!, "Pasta"));
        Assert.Contains("lamgest-.log", File.ReadAllText(ficheiro));
        Assert.Contains("\n  \"DataProtection\": {", File.ReadAllText(ficheiro)); // indentado e legível
    }

    [Fact]
    public void Tarefa_das_copias_passa_a_copiar_a_base_nova_mantendo_o_resto()
    {
        const string xml = "<?xml version=\"1.0\" encoding=\"UTF-16\"?>\r\n<Task><Triggers><CalendarTrigger><StartBoundary>2026-01-01T23:30:00</StartBoundary></CalendarTrigger></Triggers>" +
                           "<Actions><Exec><Command>powershell.exe</Command><Arguments>-NoProfile -ExecutionPolicy Bypass -File \"C:\\Program Files\\LAMGestRestaurant\\scripts\\backup-sql.ps1\" " +
                           "-Servidor \".\\SYSON\" -BaseDados \"LAMGestRestaurantDB\" -CopiaPara \"E:\\Backups\"</Arguments></Exec></Actions></Task>";
        var novo = TarefaBackup.MudarBase(xml, @".\SQLEXPRESS", "LAMGestRestaurant_Filial2")!;
        Assert.Contains("-Servidor \".\\SQLEXPRESS\" -BaseDados \"LAMGestRestaurant_Filial2\" -CopiaPara \"E:\\Backups\"", novo);
        Assert.Contains("23:30:00", novo);
        Assert.Contains("backup-sql.ps1", novo);

        Assert.Null(TarefaBackup.MudarBase("<Task><Actions><Exec><Command>x.exe</Command></Exec></Actions></Task>", ".", "LAMGestRestaurantDB"));
    }

    // ---------------- Posto ----------------

    [Fact]
    public void Novo_endereco_do_posto_mantem_a_pagina_e_as_opcoes()
    {
        Assert.Equal("http://192.168.1.20:8090/pos?posto=7&tactil=1",
            ConfiguracaoPosto.NovoEndereco("http://192.168.1.10:8080/pos?posto=3&tactil=1", "192.168.1.20", 8090, 7));
        Assert.Equal("http://servidor-lam:8080/?posto=2", ConfiguracaoPosto.NovoEndereco("http://10.0.0.5:8080/?posto=1", " servidor-lam ", 8080, 2));

        var args = "--app=http://192.168.1.10:8080/cozinha?posto=3&tactil=0 --start-fullscreen --kiosk-printing --user-data-dir=\"C:\\ProgramData\\LAMGestRestaurant\\navegador\"";
        Assert.Equal("--app=http://192.168.1.20:8080/cozinha?posto=4&tactil=0 --start-fullscreen --kiosk-printing --user-data-dir=\"C:\\ProgramData\\LAMGestRestaurant\\navegador\"",
            ConfiguracaoPosto.NovosArgumentos(args, "192.168.1.20", 8080, 4));
        Assert.Null(ConfiguracaoPosto.NovosArgumentos("", "192.168.1.20", 8080, 4));

        Assert.Null(ConfiguracaoPosto.ValidarServidor("192.168.1.20"));
        Assert.Null(ConfiguracaoPosto.ValidarServidor("servidor-lam"));
        Assert.NotNull(ConfiguracaoPosto.ValidarServidor(""));
        Assert.NotNull(ConfiguracaoPosto.ValidarServidor("http://x y"));
    }

    [Fact]
    public void Atalho_do_posto_muda_so_os_argumentos_e_a_descricao()
    {
        var atalho = Path.Combine(pasta, "LAMGest Restaurant.lnk");
        Atalhos.Alterar(atalho, "--app=http://192.168.1.10:8080/?posto=1 --kiosk-printing", "antigo");
        var antes = Atalhos.Ler(atalho);
        var novos = ConfiguracaoPosto.NovosArgumentos(antes.Argumentos, "192.168.1.20", 8080, 5)!;
        Atalhos.Alterar(atalho, novos, "LAMGest Restaurant — posto 5 (192.168.1.20:8080)");

        var depois = Atalhos.Ler(atalho);
        Assert.Equal("--app=http://192.168.1.20:8080/?posto=5 --kiosk-printing", depois.Argumentos);
        Assert.Equal("LAMGest Restaurant — posto 5 (192.168.1.20:8080)", depois.Descricao);
    }

    [Fact]
    public void Configuracao_do_agente_fica_so_para_administradores_alterarem()
    {
        var ficheiro = Path.Combine(pasta, "agente.json");
        ConfiguracaoPosto.GravarAgente(ficheiro, "http://192.168.1.20:8080", 5, "chave-exemplo");
        var lido = Json.LerFicheiro(ficheiro);
        Assert.Equal("http://192.168.1.20:8080", Json.Texto(lido, "Servidor"));
        Assert.Equal(5, Json.Inteiro(lido, "Posto"));
        Assert.Equal("chave-exemplo", Json.Texto(lido, "Chave"));

        var regras = File.GetAccessControl(ficheiro).GetAccessRules(true, true, typeof(System.Security.Principal.SecurityIdentifier))
            .Cast<System.Security.AccessControl.FileSystemAccessRule>().ToList();
        var utilizadores = regras.Single(r => r.IdentityReference.Value == "S-1-5-32-545");
        const System.Security.AccessControl.FileSystemRights escrita = System.Security.AccessControl.FileSystemRights.Write
            | System.Security.AccessControl.FileSystemRights.Delete | System.Security.AccessControl.FileSystemRights.ChangePermissions;
        Assert.Equal((System.Security.AccessControl.FileSystemRights)0, utilizadores.FileSystemRights & escrita);
        Assert.DoesNotContain(regras, r => r.IsInherited);
    }

    [Fact]
    public void Json_mantem_chaves_desconhecidas_e_compara_sem_maiusculas()
    {
        var d = Json.LerObjecto("{\"Servidor\":\"10.0.0.1\",\"Porta\":8080,\"Numero\":3,\"EntradaTactil\":true,\"Extra\":[1,2]}");
        Json.Definir(d, "servidor", "10.0.0.2");
        Json.Definir(d, "Porta", 8090);
        var texto = Json.Escrever(d);
        var lido = Json.LerObjecto(texto);
        Assert.Equal("10.0.0.2", Json.Texto(lido, "Servidor"));
        Assert.Equal(8090, Json.Inteiro(lido, "porta"));
        Assert.Equal(true, Json.Obter(lido, "EntradaTactil"));
        Assert.Matches(new Regex("\"Extra\": \\[\\s*1,\\s*2\\s*\\]"), texto);
        Assert.DoesNotContain("servidor", lido.Keys); // a grafia original mantém-se
    }
}
