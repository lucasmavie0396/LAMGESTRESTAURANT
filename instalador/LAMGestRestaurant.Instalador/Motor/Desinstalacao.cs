using System.ServiceProcess;
using Microsoft.Win32;

namespace LAMGestRestaurant.Instalador.Motor;

/// <summary>Partes do LAMGest encontradas neste computador (o que a remoção pode retirar).</summary>
public sealed record PecasInstaladas(
    bool Servico,
    string? PastaServidor,
    IReadOnlyList<int> Portas,
    bool TarefaBackup,
    IReadOnlyList<string> Atalhos,
    bool Agente,
    string? PastaPosto,
    IReadOnlyList<string> FicheirosPosto,
    bool Autoridade)
{
    public bool Servidor => Servico || PastaServidor is not null || TarefaBackup;
    public bool Posto => Atalhos.Count > 0 || Agente || PastaPosto is not null || FicheirosPosto.Count > 0;
    public bool Nada => !Servidor && !Posto && !Autoridade;
}

/// <summary>Onde procurar (configurável nos testes; por omissão as pastas reais do Windows).</summary>
public sealed record LocaisRemocao(PastasAtalhos Atalhos, PastasAgente Agente, string PastaPrograma)
{
    public static LocaisRemocao Padrao() => new(PastasAtalhos.ParaTodosOsUtilizadores(), PastasAgente.Padrao(), Configurador.PastaPosto());
}

/// <summary>
/// Remove o LAMGest deste computador: serviço, tarefa de cópias, regras da firewall, atalhos, agente de impressão,
/// configurador e a autoridade local do HTTPS. <b>Nunca</b> toca na base de dados nem nas cópias de segurança: os registos
/// financeiros não se apagam. A configuração, as chaves e os logs (ProgramData\LAMGestRestaurant) também ficam, para uma
/// reinstalação continuar com a mesma base e os mesmos postos.
/// </summary>
public static class Desinstalacao
{
    /// <summary>Ficheiros só do posto na pasta de dados (o registo do posto, a chave do agente, o ícone e os perfis do navegador e do programa).</summary>
    private static readonly string[] DadosPosto = ["posto.json", AgentePosto.NomeConfiguracao, InstalacaoPosto.NomeIcone, "navegador", InstalacaoPosto.PastaJanela];

    public static PecasInstaladas Procurar(LocaisRemocao l, bool comSistema = true)
    {
        var (servico, pastaServidor, portas) = comSistema ? LerServico() : (false, null, []);
        var tarefa = comSistema && TarefaExiste();

        var a = l.Atalhos;
        var atalhos = new[]
        {
            Path.Combine(a.AmbienteTrabalho, InstalacaoPosto.NomeAtalho),
            Path.Combine(a.MenuIniciar, InstalacaoPosto.NomeAtalho),
            Path.Combine(a.Arranque, InstalacaoPosto.NomeAtalho),
            Path.Combine(l.Agente.Arranque, AgentePosto.NomeAtalho),
            Path.Combine(a.MenuIniciar, Configurador.NomeAtalho),
        }.Distinct(StringComparer.OrdinalIgnoreCase).Where(File.Exists).ToList();

        var agente = File.Exists(Path.Combine(l.Agente.Programa, AgentePosto.NomeExecutavel));
        // A pasta do programa nos postos é a mesma que a pasta padrão do servidor: só conta como do posto se não tiver o servidor.
        var pastaPosto = Directory.Exists(l.PastaPrograma) && !File.Exists(Path.Combine(l.PastaPrograma, "LAMGestRestaurant.Api.exe"))
                         && (File.Exists(Path.Combine(l.PastaPrograma, Configurador.NomeExecutavel)) || agente)
            ? l.PastaPrograma
            : null;
        var ficheiros = DadosPosto.Select(n => Path.Combine(a.Dados, n)).Where(p => File.Exists(p) || Directory.Exists(p)).ToList();
        return new PecasInstaladas(servico, pastaServidor, portas, tarefa, atalhos, agente, pastaPosto, ficheiros, comSistema && Certificados.ConfiaNoLamgest());
    }

    public static async Task RemoverAsync(PecasInstaladas p, LocaisRemocao l, IProgress<string> progresso, bool comSistema = true)
    {
        void Passo(string t) => progresso.Report("• " + t);

        if (p.Servico)
        {
            Passo("A parar e retirar o serviço «LAMGestRestaurant»…");
            using (var s = ServiceController.GetServices().FirstOrDefault(x => x.ServiceName == InstalacaoServidor.NomeServico))
            {
                if (s is not null && s.Status != ServiceControllerStatus.Stopped)
                {
                    s.Stop();
                    s.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(60));
                }
            }
            await Processos.ExigirAsync("sc.exe", "delete", InstalacaoServidor.NomeServico);
        }
        if (p.TarefaBackup)
        {
            Passo($"A retirar a tarefa «{InstalacaoServidor.NomeTarefaBackup}»…");
            await Processos.ExigirAsync("schtasks.exe", "/Delete", "/F", "/TN", InstalacaoServidor.NomeTarefaBackup);
        }
        foreach (var porta in p.Portas)
        {
            Passo($"A retirar a regra da firewall da porta {porta}…");
            await Processos.CorrerAsync("netsh", ["advfirewall", "firewall", "delete", "rule", $"name={InstalacaoServidor.PrefixoRegraFirewall}{porta})"]);
        }

        if (p.Agente && comSistema)
            AgentePosto.PararAgentes(progresso);
        foreach (var atalho in p.Atalhos)
        {
            File.Delete(atalho);
            Passo("Atalho retirado: " + atalho);
        }
        foreach (var f in p.FicheirosPosto)
        {
            if (Directory.Exists(f))
                Directory.Delete(f, recursive: true);
            else
                File.Delete(f);
            Passo("Retirado: " + f);
        }
        if (p.Agente && Directory.Exists(l.Agente.Programa))
        {
            Directory.Delete(l.Agente.Programa, recursive: true);
            Passo("Agente de impressão retirado: " + l.Agente.Programa);
        }
        if (p.PastaPosto is { } pastaPosto && Directory.Exists(pastaPosto))
        {
            Directory.Delete(pastaPosto, recursive: true);
            Passo("Programas do posto retirados: " + pastaPosto);
        }

        if (p.PastaServidor is { } pasta && File.Exists(Path.Combine(pasta, "LAMGestRestaurant.Api.exe")))
        {
            Passo("A retirar a aplicação do servidor…");
            Directory.Delete(pasta, recursive: true);
            progresso.Report("   " + pasta);
        }

        if (p.Autoridade && comSistema)
        {
            var n = Certificados.RetirarAutoridades();
            Passo($"Autoridade local do HTTPS retirada das raízes fidedignas deste computador ({n}).");
        }

        progresso.Report("");
        progresso.Report("Ficou como estava:");
        progresso.Report("   • a base de dados no SQL Server e as cópias de segurança (nunca são apagadas pelo Setup);");
        progresso.Report($"   • a configuração, as chaves e os logs em {InstalacaoServidor.PastaDados}");
        progresso.Report("     (uma reinstalação volta a usar a mesma base e a mesma autoridade local do HTTPS).");
    }

    /// <summary>Serviço instalado: pasta da aplicação (do ImagePath) e portas (de ASPNETCORE_URLS).</summary>
    private static (bool, string?, IReadOnlyList<int>) LerServico()
    {
        using var chave = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{InstalacaoServidor.NomeServico}");
        if (chave is null)
            return (false, null, []);
        var imagem = (chave.GetValue("ImagePath") as string ?? "").Trim().Trim('"');
        var pasta = imagem.EndsWith("LAMGestRestaurant.Api.exe", StringComparison.OrdinalIgnoreCase) ? Path.GetDirectoryName(imagem) : null;
        var urls = (chave.GetValue("Environment") as string[] ?? [])
            .FirstOrDefault(v => v.StartsWith("ASPNETCORE_URLS=", StringComparison.OrdinalIgnoreCase));
        return (true, pasta, PortasDe(urls));
    }

    /// <summary>Portas de «ASPNETCORE_URLS=http://0.0.0.0:8080;https://0.0.0.0:8443».</summary>
    public static IReadOnlyList<int> PortasDe(string? urls) =>
        [.. (urls?.Split('=', 2).Last() ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(u => Uri.TryCreate(u.Replace("0.0.0.0", "localhost").Replace("*", "localhost").Replace("+", "localhost"), UriKind.Absolute, out var uri) ? uri.Port : 0)
            .Where(x => x > 0)
            .Distinct()];

    private static bool TarefaExiste()
    {
        try
        {
            var r = Processos.CorrerAsync("schtasks.exe", ["/Query", "/TN", InstalacaoServidor.NomeTarefaBackup]).GetAwaiter().GetResult();
            return r.Codigo == 0;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }
}
