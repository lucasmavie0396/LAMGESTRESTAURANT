namespace LAMGestRestaurant.Instalador.Motor;

/// <summary>
/// LAMGest-Configurar.exe (instalador\LAMGestRestaurant.Configurar): muda a base que o servidor abre ou o servidor a que
/// o posto liga, com o código de acesso do LAMGest. No servidor vem dentro do pacote da aplicação; nos postos vem embebido
/// no Setup e é copiado para a pasta do programa. Em ambos fica um atalho no menu Iniciar.
/// </summary>
public static class Configurador
{
    public const string NomeExecutavel = "LAMGest-Configurar.exe";
    public const string NomeAtalho = "LAMGest - Configurar base de dados.lnk";

    public static bool Incluido => typeof(Configurador).Assembly.GetManifestResourceInfo(NomeExecutavel) is not null;

    /// <summary>Pasta do programa nos postos (a mesma do agente de impressão).</summary>
    public static string PastaPosto() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "LAMGestRestaurant");

    /// <summary>Copia o configurador embebido para <paramref name="pasta"/> (postos). Devolve o caminho, ou null se este Setup não o incluir.</summary>
    public static string? Copiar(string pasta, IProgress<string> progresso, Stream? executavel = null)
    {
        using var origem = executavel ?? typeof(Configurador).Assembly.GetManifestResourceStream(NomeExecutavel);
        if (origem is null)
            return null;
        Directory.CreateDirectory(pasta);
        var exe = Path.Combine(pasta, NomeExecutavel);
        using (var destino = File.Create(exe))
            origem.CopyTo(destino);
        progresso.Report("• Configurador da base de dados: " + exe);
        return exe;
    }

    /// <summary>Atalho «LAMGest - Configurar base de dados» no menu Iniciar (todos os utilizadores).</summary>
    public static void CriarAtalho(string exe, string menuIniciar, IProgress<string> progresso)
    {
        if (!File.Exists(exe))
            return;
        Directory.CreateDirectory(menuIniciar);
        var atalho = Path.Combine(menuIniciar, NomeAtalho);
        InstalacaoPosto.CriarAtalho(atalho, exe, "", "LAMGest Restaurant — mudar a base de dados (código de acesso do administrador)", exe);
        progresso.Report("• Atalho: " + atalho);
    }
}
