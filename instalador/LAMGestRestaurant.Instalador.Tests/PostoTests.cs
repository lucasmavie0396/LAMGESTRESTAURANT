using System.Text.Json;
using LAMGestRestaurant.Instalador.Motor;

namespace LAMGestRestaurant.Instalador.Tests;

public class PostoTests
{
    private static readonly PaginaInicial Pos = PaginaInicial.Todas.Single(p => p.Caminho == "pos");

    [Theory]
    [InlineData("192.168.1.10", 8080, "pos", 3, "http://192.168.1.10:8080/pos?posto=3")]
    [InlineData("servidor-lamgest", 8080, "", 12, "http://servidor-lamgest:8080/?posto=12")]
    [InlineData("http://192.168.1.10:9999/qualquer", 8080, "cozinha", 1, "http://192.168.1.10:8080/cozinha?posto=1")]
    public void Endereco_do_atalho_leva_o_numero_do_posto(string servidor, int porta, string caminho, int posto, string esperado) =>
        Assert.Equal(esperado, InstalacaoPosto.Endereco(servidor, porta, caminho, posto).ToString());

    [Fact]
    public void Servidor_tem_de_ser_ip_ou_nome()
    {
        Assert.Null(InstalacaoPosto.ValidarServidor("192.168.1.10"));
        Assert.Null(InstalacaoPosto.ValidarServidor("SERVIDOR"));
        Assert.NotNull(InstalacaoPosto.ValidarServidor(""));
        Assert.NotNull(InstalacaoPosto.ValidarServidor("192.168.1.10:8080"));
    }

    [Fact]
    public async Task Servidor_que_nao_responde_da_mensagem_clara()
    {
        var erro = await Assert.ThrowsAsync<InvalidOperationException>(() => InstalacaoPosto.TestarServidorAsync("127.0.0.1", 1));
        Assert.Contains("não respondeu", erro.Message);
    }

    [Fact]
    public void Instalar_cria_atalhos_com_o_endereco_do_posto_e_regista_o_posto()
    {
        var raiz = Path.Combine(Path.GetTempPath(), "lamgest-posto-" + Guid.NewGuid().ToString("N"));
        var pastas = new PastasAtalhos(Path.Combine(raiz, "desk"), Path.Combine(raiz, "menu"), Path.Combine(raiz, "arranque"), Path.Combine(raiz, "dados"));
        var navegador = Environment.ProcessPath!; // qualquer executável serve para o atalho
        var linhas = new List<string>();
        try
        {
            var opcoes = new OpcoesPosto("192.168.1.10", 8080, 3, Pos, AtalhoAmbienteTrabalho: true, AbrirAoIniciar: true, EcraInteiro: true, EntradaTactil: true);
            var criados = InstalacaoPosto.Instalar(opcoes, pastas, navegador, new Progresso(linhas));

            Assert.Equal(3, criados.Count);
            var (alvo, argumentos, icone) = LerAtalho(Path.Combine(pastas.AmbienteTrabalho, InstalacaoPosto.NomeAtalho));
            Assert.Equal(navegador, alvo, ignoreCase: true);
            // Ícone do logótipo LAM, copiado para a pasta de dados (o atalho não usa o do navegador).
            var ficheiroIcone = Path.Combine(pastas.Dados, InstalacaoPosto.NomeIcone);
            Assert.Equal(ficheiroIcone + ",0", icone, ignoreCase: true);
            Assert.True(new FileInfo(ficheiroIcone).Length > 1000);
            // Impressão directa (por omissão): perfil próprio do navegador e impressão sem janela na impressora predefinida.
            var perfil = InstalacaoPosto.PastaPerfil(pastas);
            Assert.Equal($"--app=http://192.168.1.10:8080/pos?posto=3&tactil=1 --start-fullscreen --kiosk-printing --user-data-dir=\"{perfil}\"", argumentos);
            Assert.True(Directory.Exists(perfil));
            using var registo = JsonDocument.Parse(File.ReadAllText(Path.Combine(pastas.Dados, "posto.json")));
            Assert.Equal(3, registo.RootElement.GetProperty("Numero").GetInt32());

            // Reinstalar sem arranque automático retira o atalho do arranque; sem ecrã táctil, desliga-o no navegador (tactil=0).
            InstalacaoPosto.Instalar(opcoes with { AbrirAoIniciar = false, EcraInteiro = false, EntradaTactil = false, ImpressaoDirecta = false }, pastas, navegador, new Progresso(linhas));
            Assert.False(File.Exists(Path.Combine(pastas.Arranque, InstalacaoPosto.NomeAtalho)));
            Assert.Equal("--app=http://192.168.1.10:8080/pos?posto=3&tactil=0", LerAtalho(Path.Combine(pastas.MenuIniciar, InstalacaoPosto.NomeAtalho)).Argumentos);
        }
        finally
        {
            if (Directory.Exists(raiz))
                Directory.Delete(raiz, recursive: true);
        }
    }

    [Fact]
    public void Agente_de_impressao_fica_em_programas_com_a_chave_protegida_e_arranque_automatico()
    {
        var raiz = Path.Combine(Path.GetTempPath(), "lamgest-agente-" + Guid.NewGuid().ToString("N"));
        var pastas = new PastasAgente(Path.Combine(raiz, "prog"), Path.Combine(raiz, "dados"), Path.Combine(raiz, "arranque"));
        try
        {
            var conteudo = new byte[] { 1, 2, 3, 4 };
            var exe = AgentePosto.Instalar("192.168.1.10", 8080, new RegistoAgente(3, "Balcão", "chave-secreta"), pastas,
                new Progresso([]), new MemoryStream(conteudo));

            Assert.Equal(conteudo, File.ReadAllBytes(exe));
            var ficheiro = Path.Combine(pastas.Dados, AgentePosto.NomeConfiguracao);
            using (var config = JsonDocument.Parse(File.ReadAllText(ficheiro)))
            {
                Assert.Equal("http://192.168.1.10:8080", config.RootElement.GetProperty("Servidor").GetString());
                Assert.Equal(3, config.RootElement.GetProperty("Posto").GetInt32());
                Assert.Equal("chave-secreta", config.RootElement.GetProperty("Chave").GetString());
            }
            // Sem herança: os utilizadores só lêem a chave; administradores e SYSTEM alteram-na.
            var regras = new FileInfo(ficheiro).GetAccessControl().GetAccessRules(true, true, typeof(System.Security.Principal.SecurityIdentifier))
                .Cast<System.Security.AccessControl.FileSystemAccessRule>().ToList();
            Assert.All(regras, r => Assert.False(r.IsInherited));
            var utilizadores = Assert.Single(regras, r => r.IdentityReference.Value == "S-1-5-32-545");
            Assert.Equal(System.Security.AccessControl.FileSystemRights.Read,
                utilizadores.FileSystemRights & ~System.Security.AccessControl.FileSystemRights.Synchronize);

            var (alvo, _, _) = LerAtalho(Path.Combine(pastas.Arranque, AgentePosto.NomeAtalho));
            Assert.Equal(exe, alvo, ignoreCase: true);
        }
        finally
        {
            if (Directory.Exists(raiz))
                Directory.Delete(raiz, recursive: true);
        }
    }

    [Fact]
    public async Task Registo_do_agente_sem_servidor_falha_sem_esperar_para_sempre()
    {
        await Assert.ThrowsAnyAsync<Exception>(() => AgentePosto.RegistarAsync("127.0.0.1", 1, 3, "admin", "x"));
    }

    [Fact]
    public void Navegador_dos_postos_e_o_Chrome_e_o_Edge_so_sem_Chrome()
    {
        var comAmbos = InstalacaoPosto.EncontrarNavegador(_ => true);
        Assert.True(InstalacaoPosto.EChrome(comAmbos));

        var soEdge = InstalacaoPosto.EncontrarNavegador(c => c.EndsWith("msedge.exe", StringComparison.OrdinalIgnoreCase));
        Assert.EndsWith("msedge.exe", soEdge);
        Assert.False(InstalacaoPosto.EChrome(soEdge));

        Assert.Null(InstalacaoPosto.EncontrarNavegador(_ => false));
    }

    [Fact]
    public void Palavra_passe_gerada_cumpre_a_politica()
    {
        for (var i = 0; i < 50; i++)
        {
            var p = Segredos.GerarPalavraPasse();
            Assert.Equal(28, p.Length);
            Assert.Contains(p, char.IsUpper);
            Assert.Contains(p, char.IsLower);
            Assert.Contains(p, char.IsDigit);
            Assert.All(p, ch => Assert.True(char.IsAsciiLetterOrDigit(ch)));
        }
        Assert.NotNull(Segredos.ValidarPalavraPasseAdmin("curta1"));
        Assert.NotNull(Segredos.ValidarPalavraPasseAdmin("semalgarismos"));
        Assert.Null(Segredos.ValidarPalavraPasseAdmin("restaurante1"));
    }

    private static (string Alvo, string Argumentos, string Icone) LerAtalho(string caminho)
    {
        dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
        dynamic atalho = shell.CreateShortcut(caminho);
        return ((string)atalho.TargetPath, (string)atalho.Arguments, (string)atalho.IconLocation);
    }

    /// <summary>IProgress síncrono (o Progress&lt;T&gt; publica no contexto e chegaria depois das asserções).</summary>
    private sealed class Progresso(List<string> linhas) : IProgress<string>
    {
        public void Report(string value) => linhas.Add(value);
    }
}
