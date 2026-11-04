using System.Net.NetworkInformation;
using System.ServiceProcess;

namespace LAMGestRestaurant.Instalador.Motor;

/// <summary>Instância do SQL Server deste computador e o estado do seu serviço do Windows (null = não foi possível saber).</summary>
public sealed record InstanciaSql(string Nome, bool? EmExecucao);

/// <summary>
/// Pré-requisitos do modo Servidor, verificados antes de instalar para nunca falhar a meio:
/// SQL Server instalado e em execução, sqlcmd (cópias de segurança), espaço em disco e porta de rede livre.
/// As funções de avaliação são puras (recebem o que foi detectado) para poderem ser testadas.
/// </summary>
public static class Requisitos
{
    public const string EnderecoSqlExpress = "https://www.microsoft.com/pt-pt/sql-server/sql-server-downloads";
    public const string ComandoSqlcmd = "winget install Microsoft.Sqlcmd";
    public const long EspacoMinimoBytes = 1L * 1024 * 1024 * 1024;

    /// <summary>Instâncias locais com o estado do serviço (MSSQLSERVER para a instância por omissão, MSSQL$NOME para as outras).</summary>
    public static IReadOnlyList<InstanciaSql> InstanciasComEstado() =>
        [.. ServicoSql.InstanciasLocais().Select(i => new InstanciaSql(i, EstadoServico(i == "." ? "MSSQLSERVER" : "MSSQL$" + i[2..])))];

    private static bool? EstadoServico(string nome)
    {
        try
        {
            using var s = new ServiceController(nome);
            return s.Status == ServiceControllerStatus.Running;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    /// <summary>Aviso sobre o SQL Server a mostrar na página da base de dados, ou null se está tudo bem.</summary>
    public static string? AvisoSql(IReadOnlyList<InstanciaSql> instancias, string instanciaEscolhida)
    {
        if (instancias.Count == 0)
            return "Não encontrei o SQL Server neste computador. Instale o SQL Server Express (gratuito) a partir de "
                   + EnderecoSqlExpress + " — escolha a instalação «Básica» — e carregue em «Procurar de novo». "
                   + "Se a base de dados ficar noutro computador, escreva aqui essa instância (ex.: SERVIDOR\\SQLEXPRESS).";
        var escolhida = instancias.FirstOrDefault(i => string.Equals(i.Nome, instanciaEscolhida.Trim(), StringComparison.OrdinalIgnoreCase));
        return escolhida is { EmExecucao: false }
            ? $"O serviço do SQL Server da instância {escolhida.Nome} está parado. Inicie-o em Serviços (services.msc → «SQL Server ({ServicoDe(escolhida.Nome)})» → Iniciar) e teste a ligação."
            : null;
    }

    private static string ServicoDe(string instancia) => instancia == "." ? "MSSQLSERVER" : instancia[2..];

    /// <summary>
    /// Caminho do sqlcmd: no PATH do sistema (o da tarefa agendada, que corre como SYSTEM) ou nas pastas onde é instalado
    /// (SQL Server, ODBC tools, winget). Null se não existir.
    /// </summary>
    public static string? ProcurarSqlcmd(Func<string, bool>? existe = null, string? pathSistema = null, string? programFiles = null)
    {
        existe ??= File.Exists;
        pathSistema ??= Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.Machine) ?? "";
        programFiles ??= Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var candidatos = pathSistema.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(p => Path.Combine(Environment.ExpandEnvironmentVariables(p), "sqlcmd.exe"))
            .Concat(PastasConhecidas(programFiles, existe));
        return candidatos.FirstOrDefault(existe);
    }

    private static IEnumerable<string> PastasConhecidas(string programFiles, Func<string, bool> existe)
    {
        yield return Path.Combine(programFiles, "SqlCmd", "sqlcmd.exe"); // winget install Microsoft.Sqlcmd
        foreach (var versao in new[] { "180", "170", "160", "150", "130" })
        {
            yield return Path.Combine(programFiles, "Microsoft SQL Server", "Client SDK", "ODBC", versao, "Tools", "Binn", "SQLCMD.EXE");
            yield return Path.Combine(programFiles, "Microsoft SQL Server", versao, "Tools", "Binn", "SQLCMD.EXE");
        }
    }

    /// <summary>Mensagem sobre o sqlcmd quando a cópia de segurança diária está escolhida e ele não existe.</summary>
    public static string? AvisoSqlcmd(bool backupDiario, string? sqlcmd) =>
        backupDiario && sqlcmd is null
            ? "A cópia de segurança diária precisa do sqlcmd, que não está instalado neste computador. Instale-o (numa consola: "
              + ComandoSqlcmd + ", ou o «Microsoft Command Line Utilities for SQL Server») e carregue em Seguinte, ou desligue a cópia diária."
            : null;

    /// <summary>Espaço livre no disco da pasta de instalação (null se a unidade não existir).</summary>
    public static long? EspacoLivre(string pasta)
    {
        try
        {
            var raiz = Path.GetPathRoot(Path.GetFullPath(pasta));
            return raiz is null ? null : new DriveInfo(raiz).AvailableFreeSpace;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static string? AvisoEspaco(long? livre) => livre switch
    {
        null => "A unidade da pasta de instalação não existe.",
        < EspacoMinimoBytes => $"Espaço livre insuficiente no disco da instalação ({livre.Value / 1024d / 1024 / 1024:0.0} GB). São precisos pelo menos 1 GB.",
        _ => null,
    };

    /// <summary>A porta já está a ser usada por outro programa (fora uma instalação anterior do LAMGest, que é substituída).</summary>
    public static bool PortaOcupada(int porta) =>
        IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners().Any(e => e.Port == porta);

    /// <summary>O serviço do LAMGest já existe (actualização): a porta ocupada por ele não é um problema.</summary>
    public static bool LamgestInstalado() => ServiceController.GetServices().Any(s => s.ServiceName == InstalacaoServidor.NomeServico);

    public static string? AvisoPorta(int porta, bool ocupada, bool lamgestJaInstalado) =>
        ocupada && !lamgestJaInstalado
            ? $"A porta {porta} já está a ser usada por outro programa neste computador. Escolha outra porta (ex.: 8090)."
            : null;
}
