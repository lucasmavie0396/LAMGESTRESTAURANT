using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace LAMGestRestaurant.Instalador.Motor;

public sealed record PostoServidor(int Numero, int TerminalId, string Nome);

public sealed record PaginaInicial(string Rotulo, string Caminho)
{
    public static readonly IReadOnlyList<PaginaInicial> Todas =
    [
        new("Início (painel)", ""),
        new("POS — vendas e caixa", "pos"),
        new("Cozinha (ecrã de preparação)", "cozinha"),
        new("Mesas", "mesas"),
        new("Delivery", "delivery"),
    ];

    public override string ToString() => Rotulo;
}

public sealed record OpcoesPosto(
    string Servidor,
    int Porta,
    int Numero,
    PaginaInicial Pagina,
    bool AtalhoAmbienteTrabalho,
    bool AbrirAoIniciar,
    bool EcraInteiro,
    /// <summary>Entrada com os botões dos utilizadores e o código (PIN) num teclado no ecrã.</summary>
    bool EntradaTactil = false,
    /// <summary>
    /// Recibos e talões impressos pelo posto saem directamente na impressora predefinida do Windows deste posto, sem a janela
    /// de impressão (o navegador abre com --kiosk-printing e um perfil próprio, para a opção valer mesmo com o navegador já aberto).
    /// </summary>
    bool ImpressaoDirecta = true);

/// <summary>Pastas onde os atalhos são criados (configuráveis para testes).</summary>
public sealed record PastasAtalhos(string AmbienteTrabalho, string MenuIniciar, string Arranque, string Dados)
{
    public static PastasAtalhos ParaTodosOsUtilizadores() => new(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
        Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms),
        Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup),
        InstalacaoServidor.PastaDados);
}

/// <summary>
/// Posto de trabalho: não instala programas. Cria atalhos que abrem o servidor numa janela própria do navegador,
/// já com o número do posto (?posto=N) — a aplicação associa o computador ao terminal com esse número.
/// </summary>
public static class InstalacaoPosto
{
    public const string NomeAtalho = "LAMGest Restaurant.lnk";
    public const string NomeIcone = "lam.ico";

    /// <summary>Ícone com o logótipo LAM, embebido no instalador.</summary>
    public static Stream? AbrirIcone() => typeof(InstalacaoPosto).Assembly.GetManifestResourceStream(NomeIcone);

    /// <summary><paramref name="tactil"/>: com posto, indica sempre ao navegador se usa o ecrã de entrada táctil (1) ou não (0).</summary>
    public static Uri Endereco(string servidor, int porta, string caminho = "", int? posto = null, bool? tactil = null)
    {
        var anfitriao = servidor.Trim();
        if (anfitriao.Contains("://", StringComparison.Ordinal))
            anfitriao = new Uri(anfitriao).Host;
        var b = new UriBuilder("http", anfitriao, porta, "/" + caminho);
        if (posto is int n)
            b.Query = $"posto={n}" + (tactil is bool t ? $"&tactil={(t ? 1 : 0)}" : "");
        return b.Uri;
    }

    public static string? ValidarServidor(string servidor) =>
        string.IsNullOrWhiteSpace(servidor) || Uri.CheckHostName(servidor.Trim()) == UriHostNameType.Unknown
            ? "Indique o IP ou o nome do servidor (ex.: 192.168.1.10)."
            : null;

    /// <summary>Confirma que o servidor LAMGest responde e tem a base de dados ligada.</summary>
    public static async Task<string> TestarServidorAsync(string servidor, int porta, CancellationToken ct = default)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(6) };
        JsonElement saude;
        try
        {
            saude = await http.GetFromJsonAsync<JsonElement>(Endereco(servidor, porta, "api/saude"), ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or NotSupportedException)
        {
            throw new InvalidOperationException($"O servidor {servidor}:{porta} não respondeu. Confirme o IP, a porta, a firewall do servidor e que o serviço LAMGest está a correr.", ex);
        }
        if (!saude.TryGetProperty("api", out _))
            throw new InvalidOperationException("Esse endereço responde, mas não é um servidor LAMGest.");
        return saude.TryGetProperty("baseDados", out var b) && b.GetBoolean()
            ? "Servidor LAMGest a funcionar."
            : "O servidor LAMGest responde, mas está sem ligação à base de dados.";
    }

    /// <summary>Posto registado no servidor com este número, ou null se não existir.</summary>
    public static async Task<PostoServidor?> ObterPostoAsync(string servidor, int porta, int numero, CancellationToken ct = default)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(6) };
        using var resposta = await http.GetAsync(Endereco(servidor, porta, $"api/postos/{numero}"), ct);
        if (resposta.StatusCode == HttpStatusCode.NotFound)
            return null;
        resposta.EnsureSuccessStatusCode();
        return await resposta.Content.ReadFromJsonAsync<PostoServidor>(cancellationToken: ct);
    }

    /// <summary>
    /// Google Chrome (navegador dos postos); o Edge só se o Chrome não estiver instalado. Null se não houver nenhum.
    /// </summary>
    public static string? EncontrarNavegador(Func<string, bool>? existe = null)
    {
        string[] candidatos =
        [
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Google\Chrome\Application\chrome.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"Google\Chrome\Application\chrome.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Google\Chrome\Application\chrome.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"Microsoft\Edge\Application\msedge.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Microsoft\Edge\Application\msedge.exe"),
        ];
        return candidatos.FirstOrDefault(existe ?? File.Exists);
    }

    public static bool EChrome(string? navegador) =>
        navegador is not null && Path.GetFileName(navegador).Equals("chrome.exe", StringComparison.OrdinalIgnoreCase);

    /// <summary>Perfil próprio do navegador do LAMGest neste posto (sessão e opções separadas do navegador do dia-a-dia).</summary>
    public static string PastaPerfil(PastasAtalhos pastas) => Path.Combine(pastas.Dados, "navegador");

    public static string ArgumentosNavegador(OpcoesPosto o, PastasAtalhos pastas)
    {
        var url = Endereco(o.Servidor, o.Porta, o.Pagina.Caminho, o.Numero, o.EntradaTactil);
        return $"--app={url}" + (o.EcraInteiro ? " --start-fullscreen" : "")
               + (o.ImpressaoDirecta ? $" --kiosk-printing --user-data-dir=\"{PastaPerfil(pastas)}\"" : "");
    }

    public static IReadOnlyList<string> Instalar(OpcoesPosto o, PastasAtalhos pastas, string navegador, IProgress<string> progresso)
    {
        var argumentos = ArgumentosNavegador(o, pastas);
        if (o.ImpressaoDirecta)
            PrepararPerfil(PastaPerfil(pastas), progresso);
        var descricao = $"LAMGest Restaurant — posto {o.Numero} ({o.Servidor}:{o.Porta})";
        List<string> criados = [];

        // Os atalhos mostram o logótipo LAM em vez do ícone do navegador.
        Directory.CreateDirectory(pastas.Dados);
        string? icone = null;
        if (AbrirIcone() is { } origem)
        {
            icone = Path.Combine(pastas.Dados, NomeIcone);
            using (origem)
            using (var destino = File.Create(icone))
                origem.CopyTo(destino);
        }

        void Criar(string pasta)
        {
            Directory.CreateDirectory(pasta);
            var caminho = Path.Combine(pasta, NomeAtalho);
            CriarAtalho(caminho, navegador, argumentos, descricao, icone);
            criados.Add(caminho);
            progresso.Report("• Atalho: " + caminho);
        }

        Criar(pastas.MenuIniciar);
        if (o.AtalhoAmbienteTrabalho)
            Criar(pastas.AmbienteTrabalho);
        var arranque = Path.Combine(pastas.Arranque, NomeAtalho);
        if (o.AbrirAoIniciar)
            Criar(pastas.Arranque);
        else if (File.Exists(arranque))
            File.Delete(arranque); // reinstalação sem arranque automático

        var registo =Path.Combine(pastas.Dados, "posto.json");
        File.WriteAllText(registo, JsonSerializer.Serialize(new
        {
            o.Servidor, o.Porta, o.Numero, Pagina = o.Pagina.Caminho, Navegador = navegador, o.EntradaTactil, o.ImpressaoDirecta,
            Endereco = Endereco(o.Servidor, o.Porta, o.Pagina.Caminho, o.Numero, o.EntradaTactil).ToString(),
            InstaladoEm = DateTime.Now,
        }, new JsonSerializerOptions { WriteIndented = true }));
        progresso.Report("• Registo do posto: " + registo);
        return criados;
    }

    /// <summary>Pasta do perfil do navegador, em que qualquer utilizador do Windows deste posto pode escrever (o atalho é comum a todos).</summary>
    private static void PrepararPerfil(string pasta, IProgress<string> progresso)
    {
        var info = Directory.CreateDirectory(pasta);
        if (OperatingSystem.IsWindows())
        {
            var acl = info.GetAccessControl();
            acl.AddAccessRule(new System.Security.AccessControl.FileSystemAccessRule(
                new System.Security.Principal.SecurityIdentifier(System.Security.Principal.WellKnownSidType.BuiltinUsersSid, null),
                System.Security.AccessControl.FileSystemRights.Modify,
                System.Security.AccessControl.InheritanceFlags.ContainerInherit | System.Security.AccessControl.InheritanceFlags.ObjectInherit,
                System.Security.AccessControl.PropagationFlags.None, System.Security.AccessControl.AccessControlType.Allow));
            info.SetAccessControl(acl);
        }
        progresso.Report("• Impressão directa na impressora predefinida deste posto (perfil do navegador: " + pasta + ")");
    }

    internal static void CriarAtalho(string caminho, string alvo, string argumentos, string descricao, string? icone)
    {
        var tipo = Type.GetTypeFromProgID("WScript.Shell") ?? throw new InvalidOperationException("WScript.Shell indisponível.");
        dynamic shell = Activator.CreateInstance(tipo)!;
        try
        {
            dynamic atalho = shell.CreateShortcut(caminho);
            atalho.TargetPath = alvo;
            atalho.Arguments = argumentos;
            atalho.Description = descricao;
            atalho.WorkingDirectory = Path.GetDirectoryName(alvo);
            atalho.IconLocation = (icone ?? alvo) + ",0";
            atalho.Save();
        }
        finally
        {
            System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);
        }
    }
}
