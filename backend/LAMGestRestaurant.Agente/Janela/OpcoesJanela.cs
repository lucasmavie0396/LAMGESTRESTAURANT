namespace LAMGestRestaurant.Agente.Janela;

/// <summary>
/// Programa do posto (LAMGest-Agente.exe com --app=…): abre o sistema numa janela própria (WebView2), sem navegador.
/// Aceita os mesmos argumentos que os atalhos davam ao Chrome/Edge, para o instalador e o configurador continuarem iguais:
/// --app=URL, --start-fullscreen, --kiosk-printing (impressão directa) e --user-data-dir=PASTA (dados comuns do posto).
/// Sem regras de interface: testado sem janela.
/// </summary>
public sealed record OpcoesJanela(Uri Endereco, bool EcraInteiro, bool ImpressaoDirecta, string? PastaPerfil)
{
    /// <summary>Lê os argumentos; null se não houver --app=URL válido (o executável funciona então como agente de impressão).</summary>
    public static OpcoesJanela? Ler(IReadOnlyList<string> args)
    {
        string? Valor(string nome) => args.Select(a => a.Trim()).FirstOrDefault(a => a.StartsWith(nome + "=", StringComparison.OrdinalIgnoreCase))
            ?[(nome.Length + 1)..].Trim('"');
        bool Tem(string nome) => args.Any(a => a.Trim().Equals(nome, StringComparison.OrdinalIgnoreCase));

        if (!Uri.TryCreate(Valor("--app"), UriKind.Absolute, out var endereco) || endereco.Scheme is not ("http" or "https"))
            return null;
        var perfil = Valor("--user-data-dir");
        return new OpcoesJanela(endereco, Tem("--start-fullscreen"), Tem("--kiosk-printing"), string.IsNullOrWhiteSpace(perfil) ? null : perfil);
    }

    /// <summary>
    /// Dados da janela (sessão, preferências do posto): ao lado do perfil comum do navegador, se o atalho tiver um; senão, do
    /// utilizador do Windows. Nunca dentro do perfil do Chrome (os dois motores não partilham a mesma pasta).
    /// </summary>
    public string PastaDados(string pastaUtilizador) => PastaPerfil is { } p && Path.GetDirectoryName(Path.GetFullPath(p).TrimEnd('\\', '/')) is { Length: > 0 } pai
        ? Path.Combine(pai, PastaJanela)
        : Path.Combine(pastaUtilizador, "LAMGestRestaurant", PastaJanela);

    public const string PastaJanela = "janela";

    /// <summary>Páginas do próprio servidor (mesmo esquema, anfitrião e porta) abrem-se no programa; as outras no navegador do Windows.</summary>
    public bool DoServidor(Uri destino) =>
        destino.Scheme.Equals(Endereco.Scheme, StringComparison.OrdinalIgnoreCase)
        && destino.Host.Equals(Endereco.Host, StringComparison.OrdinalIgnoreCase)
        && destino.Port == Endereco.Port;

    /// <summary>O visor do cliente abre-se em ecrã inteiro no segundo monitor, se houver.</summary>
    public static bool EVisor(Uri destino) => destino.AbsolutePath.TrimEnd('/').Equals("/visor", StringComparison.OrdinalIgnoreCase);

    /// <summary>Endereço só com a origem do servidor (para a vista escondida da impressão directa).</summary>
    public Uri Origem => new(Endereco.GetLeftPart(UriPartial.Authority) + "/");

    /// <summary>Argumentos para abrir o mesmo posto no Chrome/Edge (quando o Windows não tem o WebView2).</summary>
    public string ArgumentosNavegador() =>
        $"--app={Endereco}" + (EcraInteiro ? " --start-fullscreen" : "")
        + (ImpressaoDirecta && PastaPerfil is { } p ? $" --kiosk-printing --user-data-dir=\"{p}\"" : ImpressaoDirecta ? " --kiosk-printing" : "");

    /// <summary>Margens da impressão directa (polegadas): as mesmas do @page do documento (talão 2 mm, A4 15 mm).</summary>
    public static double Margem(int larguraPapelMm) => (larguraPapelMm >= 200 ? 15 : 2) / 25.4;
}
