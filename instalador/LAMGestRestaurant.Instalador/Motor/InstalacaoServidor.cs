using System.IO.Compression;
using System.Net.Http.Json;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.ServiceProcess;
using System.Text.Json;
using Microsoft.Win32;

namespace LAMGestRestaurant.Instalador.Motor;

public sealed record NovoAdministrador(string Utilizador, string NomeCompleto, string PalavraPasse);

public sealed record OpcoesServidor(
    ConfigSql Sql,
    string PastaInstalacao,
    int Porta,
    bool AbrirFirewall,
    bool BackupDiario,
    TimeOnly HoraBackup,
    string? CopiaBackupPara,
    NovoAdministrador? Administrador,
    bool MigracoesConfirmadas,
    /// <summary>Porta HTTPS (null = só HTTP). O HTTP continua na porta de sempre.</summary>
    int? PortaHttps = null,
    /// <summary>Depois de todos os postos e tablets em HTTPS: o HTTP só reencaminha e o cookie deixa de passar sem cifra.</summary>
    bool SoHttps = false);

/// <summary>
/// Instalação (ou actualização) do servidor: base de dados, aplicação, serviço Windows, firewall e cópias de segurança.
/// Cada passo é idempotente: voltar a correr o instalador actualiza uma instalação existente.
/// </summary>
public sealed class InstalacaoServidor(OpcoesServidor o, IProgress<string> progresso)
{
    public const string NomeServico = "LAMGestRestaurant";
    public const string NomeTarefaBackup = "LAMGest Restaurant - Backup SQL";
    public static readonly string PastaDados = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "LAMGestRestaurant");
    private static readonly string PastaConfig = Path.Combine(PastaDados, "config");
    private static readonly string PastaChaves = Path.Combine(PastaDados, "chaves");
    private static readonly string PastaLogs = Path.Combine(PastaDados, "logs");
    private static readonly string FicheiroConfig = Path.Combine(PastaConfig, "servidor.json");
    private const string Sistema = "*S-1-5-18", Administradores = "*S-1-5-32-544";

    private string Exe => Path.Combine(o.PastaInstalacao, "LAMGestRestaurant.Api.exe");

    /// <summary>Certificado HTTPS emitido nesta instalação (null sem HTTPS).</summary>
    public CertificadoServidor? Certificado { get; private set; }

    public async Task<IReadOnlyList<string>> ExecutarAsync(CancellationToken ct = default)
    {
        Passo("A verificar o SQL Server e a base de dados…");
        var estado = await ServicoSql.AnalisarAsync(o.Sql, Pacote.Migracoes, ct);
        if (estado.Existe && !estado.Vazia && !estado.DoLamgest)
            throw new InvalidOperationException($"A base «{o.Sql.BaseDados}» já existe e não é do LAMGest. Escolha outro nome.");
        if (estado.Desconhecidas.Count > 0)
            throw new InvalidOperationException("A base foi actualizada por uma versão mais recente do LAMGest. Use o instalador dessa versão.");
        if (estado.ActualizacaoComDados && !o.MigracoesConfirmadas)
            throw new InvalidOperationException("A actualização da base de dados não foi confirmada.");
        if (!estado.TemAdministrador && o.Administrador is null)
            throw new InvalidOperationException("Indique o primeiro administrador.");

        if (estado.Existe && estado.DoLamgest)
        {
            Passo("Cópia de segurança da base existente…");
            Info("Cópia criada: " + await ServicoSql.CopiaSegurancaAsync(o.Sql, "antes-instalacao", ct));
        }

        PararServico();
        Passo("A copiar a aplicação…");
        ExtrairAplicacao();

        Passo(estado.Existe ? "A actualizar a base de dados…" : "A criar a base de dados…");
        await ServicoSql.CriarBaseAsync(o.Sql, ct);
        await ComandoAppAsync(o.Sql.LigacaoBase, null, ct, "migrar");

        if (!estado.TemAdministrador)
        {
            Passo("A criar o primeiro administrador…");
            var a = o.Administrador!;
            var saida = await ComandoAppAsync(o.Sql.LigacaoBase, $"{a.Utilizador}\n{a.NomeCompleto}\n{a.PalavraPasse}\n{a.PalavraPasse}\n", ct, "bootstrap-admin");
            Info(saida.Split('\n').LastOrDefault(l => l.Contains("criado", StringComparison.OrdinalIgnoreCase))?.Trim() ?? "Administrador criado.");
        }

        Passo("A configurar o serviço Windows…");
        await ConfigurarServicoAsync();

        Passo("A dar acesso do serviço à base de dados…");
        string? palavraPasseServico = null;
        if (o.Sql.AutenticacaoWindows)
            await ServicoSql.GarantirLoginWindowsAsync(o.Sql, ServicoSql.ContaServico, ct);
        else
        {
            palavraPasseServico = Segredos.GerarPalavraPasse();
            await ServicoSql.GarantirLoginSqlAsync(o.Sql, ServicoSql.LoginSqlServico, palavraPasseServico, ct);
            Info($"Login SQL «{ServicoSql.LoginSqlServico}» com palavra-passe aleatória (guardada só no ficheiro protegido do serviço).");
        }

        Passo("A preparar as pastas de dados e a configuração…");
        await PrepararPastasAsync(ServicoSql.LigacaoServico(o.Sql, palavraPasseServico));

        if (o.AbrirFirewall)
        {
            Passo($"A abrir a porta {o.Porta} na firewall (redes de domínio e privadas)…");
            await ConfigurarFirewallAsync(o.Porta);
            if (o.PortaHttps is int ph)
            {
                Passo($"A abrir a porta HTTPS {ph} na firewall…");
                await ConfigurarFirewallAsync(ph);
            }
        }

        if (o.BackupDiario)
        {
            Passo($"A agendar a cópia de segurança diária às {o.HoraBackup:HH\\:mm}…");
            await ServicoSql.GarantirPermissoesBackupAsync(o.Sql, ct);
            await AgendarBackupAsync();
        }

        Passo("A iniciar o serviço…");
        await IniciarEVerificarAsync(ct);

        Configurador.CriarAtalho(Path.Combine(o.PastaInstalacao, Configurador.NomeExecutavel),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), progresso);

        var enderecos = EnderecosLocais().Select(ip => o.PortaHttps is int p ? $"https://{ip}:{p}" : $"http://{ip}:{o.Porta}").ToList();
        Passo("Instalação concluída.");
        return enderecos;
    }

    private void Passo(string texto) => progresso.Report("• " + texto);
    private void Info(string texto) => progresso.Report("   " + texto);

    private static void PararServico()
    {
        using var servico = ServiceController.GetServices().FirstOrDefault(s => s.ServiceName == NomeServico);
        if (servico is null || servico.Status == ServiceControllerStatus.Stopped)
            return;
        servico.Stop();
        servico.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(60));
    }

    private void ExtrairAplicacao()
    {
        var pasta = o.PastaInstalacao;
        if (Directory.Exists(pasta) && Directory.EnumerateFileSystemEntries(pasta).Any())
        {
            // Só se substitui uma pasta vazia ou uma instalação anterior do LAMGest.
            if (!File.Exists(Exe))
                throw new InvalidOperationException($"A pasta {pasta} não está vazia e não é uma instalação do LAMGest. Escolha outra pasta.");
            foreach (var f in Directory.EnumerateFiles(pasta))
                File.Delete(f);
            foreach (var d in Directory.EnumerateDirectories(pasta))
                Directory.Delete(d, recursive: true);
        }
        Directory.CreateDirectory(pasta);
        using var zip = new ZipArchive(Pacote.AbrirAplicacao(), ZipArchiveMode.Read);
        zip.ExtractToDirectory(pasta, overwriteFiles: true);
        Info($"Aplicação em {pasta}");
    }

    /// <summary>Corre a própria aplicação (comandos migrar / bootstrap-admin) com a ligação de administrador, só nesse processo.</summary>
    private async Task<string> ComandoAppAsync(string ligacao, string? entrada, CancellationToken ct, params string[] args)
    {
        var ambiente = new Dictionary<string, string>
        {
            ["ConnectionStrings__Default"] = ligacao,
            ["ASPNETCORE_ENVIRONMENT"] = "Production",
            ["LAMGEST_CONFIG"] = "",
        };
        var r = await Processos.CorrerAsync(Exe, args, o.PastaInstalacao, ambiente, entrada, ct);
        if (r.Codigo != 0)
            throw new InvalidOperationException($"O comando «{args[0]}» falhou: {r.Saida}");
        return r.Saida;
    }

    private async Task ConfigurarServicoAsync()
    {
        var existe = ServiceController.GetServices().Any(s => s.ServiceName == NomeServico);
        var binario = $"\"{Exe}\"";
        if (!existe)
            await Processos.ExigirAsync("sc.exe", "create", NomeServico, "binPath=", binario, "DisplayName=", "LAMGest Restaurant ERP");
        // A conta virtual NT SERVICE\<serviço> só existe depois de o serviço ser criado.
        await Processos.ExigirAsync("sc.exe", "config", NomeServico, "binPath=", binario, "start=", "delayed-auto", "obj=", ServicoSql.ContaServico);
        await Processos.ExigirAsync("sc.exe", "description", NomeServico, "Servidor do LAMGest Restaurant (aplicação web, API e tempo real).");
        await Processos.ExigirAsync("sc.exe", "failure", NomeServico, "reset=", "86400", "actions=", "restart/5000/restart/30000/restart/60000");

        // Só valores sem segredos: a connection string fica no ficheiro protegido indicado por LAMGEST_CONFIG.
        using var chave = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{NomeServico}", writable: true)
            ?? throw new InvalidOperationException("Serviço criado mas sem chave no registo.");
        chave.SetValue("Environment", new[]
        {
            "ASPNETCORE_ENVIRONMENT=Production",
            "ASPNETCORE_URLS=" + Enderecos(o.Porta, o.PortaHttps),
            $"LAMGEST_CONFIG={FicheiroConfig}",
        }, RegistryValueKind.MultiString);
        Info($"Serviço «{NomeServico}» com a conta {ServicoSql.ContaServico}, arranque automático e reinício em caso de falha.");
    }

    /// <summary>Endereços em que o serviço atende: HTTP sempre (postos antigos e reencaminhamento) e HTTPS se configurado.</summary>
    public static string Enderecos(int porta, int? portaHttps) =>
        $"http://0.0.0.0:{porta}" + (portaHttps is int p ? $";https://0.0.0.0:{p}" : "");

    /// <summary>
    /// Autoridade local (reutilizada se já existir: os postos continuam a confiar nela) e certificado novo do servidor para
    /// os endereços actuais. A chave da autoridade fica numa pasta só de administradores.
    /// </summary>
    private async Task<CertificadoServidor> PrepararCertificadoAsync()
    {
        var pastaCa = Certificados.Pasta(PastaDados);
        Directory.CreateDirectory(pastaCa);
        await Processos.ExigirAsync("icacls", pastaCa, "/inheritance:r", "/grant:r", $"{Sistema}:(OI)(CI)F", $"{Administradores}:(OI)(CI)F");
        using var ca = Certificados.ObterOuCriarCa(pastaCa);
        var cert = Certificados.EmitirServidor(ca, Certificados.NomesDoServidor(EnderecosLocais()), PastaConfig);
        Certificados.ConfiarNaAutoridade(ca);
        Info($"Certificado HTTPS para {string.Join(", ", cert.Nomes)} (válido até {cert.ValidoAte.ToLocalTime():dd/MM/yyyy}).");
        Info("Impressão digital da autoridade local: " + Certificados.Legivel(cert.ImpressaoDigitalCa));
        return cert;
    }

    private async Task PrepararPastasAsync(string ligacaoServico)
    {
        foreach (var p in new[] { PastaConfig, PastaChaves, PastaLogs })
            Directory.CreateDirectory(p);
        // Configuração e chaves: sem herança; SYSTEM e Administradores com controlo total, o serviço só o necessário.
        await Processos.ExigirAsync("icacls", PastaConfig, "/inheritance:r", "/grant:r", $"{Sistema}:(OI)(CI)F", $"{Administradores}:(OI)(CI)F", $"{ServicoSql.ContaServico}:(OI)(CI)R");
        await Processos.ExigirAsync("icacls", PastaChaves, "/inheritance:r", "/grant:r", $"{Sistema}:(OI)(CI)F", $"{Administradores}:(OI)(CI)F", $"{ServicoSql.ContaServico}:(OI)(CI)M");
        await Processos.ExigirAsync("icacls", PastaLogs, "/grant", $"{ServicoSql.ContaServico}:(OI)(CI)M");

        if (o.PortaHttps is not null)
        {
            Passo("A preparar a ligação cifrada (HTTPS)…");
            Certificado = await PrepararCertificadoAsync();
        }
        var config = new Dictionary<string, object>
        {
            ["ConnectionStrings"] = new { Default = ligacaoServico },
            ["DataProtection"] = new { Pasta = PastaChaves },
            ["Serilog"] = new { WriteTo = new Dictionary<string, object> { ["1"] = new { Args = new { path = Path.Combine(PastaLogs, "lamgest-.log") } } } },
        };
        if (Certificado is { } c)
        {
            config["Kestrel"] = new { Certificates = new { Default = new { Path = c.FicheiroPfx, Password = c.Senha } } };
            config["Seguranca"] = new { PortaHttps = o.PortaHttps, CertificadoCa = c.FicheiroCa, SoHttps = o.SoHttps };
        }
        await File.WriteAllTextAsync(FicheiroConfig, JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true }));
        Info($"Configuração protegida em {FicheiroConfig}; logs em {PastaLogs}");
    }

    public const string PrefixoRegraFirewall = "LAMGest Restaurant (TCP ";

    private static async Task ConfigurarFirewallAsync(int porta)
    {
        var nome = $"{PrefixoRegraFirewall}{porta})";
        await Processos.CorrerAsync("netsh", ["advfirewall", "firewall", "delete", "rule", $"name={nome}"]);
        await Processos.ExigirAsync("netsh", "advfirewall", "firewall", "add", "rule", $"name={nome}", "dir=in", "action=allow",
            // Sem internet o Windows classifica a rede como «pública»: a regra vale em todos os perfis, mas só para a rede local
            // e para a VPN privada dos tablets fora do restaurante (Tailscale: 100.64.0.0/10). Nunca para a internet.
            "protocol=TCP", $"localport={porta}", "profile=any", "remoteip=localsubnet,100.64.0.0/10");
    }

    private async Task AgendarBackupAsync()
    {
        var script = Path.Combine(o.PastaInstalacao, "scripts", "backup-sql.ps1");
        if (!File.Exists(script))
            throw new InvalidOperationException("O pacote não inclui scripts\\backup-sql.ps1.");
        var comando = $"powershell.exe -NoProfile -ExecutionPolicy Bypass -File \"{script}\" -Servidor \"{o.Sql.Instancia}\" -BaseDados \"{o.Sql.BaseDados}\"";
        if (!string.IsNullOrWhiteSpace(o.CopiaBackupPara))
            comando += $" -CopiaPara \"{o.CopiaBackupPara}\"";
        if (comando.Length > 261)
            throw new InvalidOperationException("Caminhos demasiado longos para a tarefa agendada. Use uma pasta de instalação/cópia mais curta.");
        await Processos.ExigirAsync("schtasks.exe", "/Create", "/F", "/TN", NomeTarefaBackup, "/SC", "DAILY", "/ST", o.HoraBackup.ToString("HH\\:mm"),
            "/RU", "SYSTEM", "/RL", "HIGHEST", "/TR", comando);
        Info($"Tarefa «{NomeTarefaBackup}»; log em {Path.Combine(PastaDados, "logs")}");
    }

    private async Task IniciarEVerificarAsync(CancellationToken ct)
    {
        using (var servico = new ServiceController(NomeServico))
        {
            if (servico.Status != ServiceControllerStatus.Running)
                servico.Start();
            servico.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(60));
        }
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        for (var i = 0; i < 30; i++)
        {
            try
            {
                var saude = await http.GetFromJsonAsync<JsonElement>($"http://localhost:{o.Porta}/api/saude", ct);
                if (saude.GetProperty("baseDados").GetBoolean())
                {
                    Info("O servidor responde e está ligado à base de dados.");
                    if (o.PortaHttps is int ph)
                        await VerificarHttpsAsync(ph, ct);
                    return;
                }
            }
            catch (Exception) when (!ct.IsCancellationRequested)
            {
                // Ainda a arrancar.
            }
            await Task.Delay(2000, ct);
        }
        throw new InvalidOperationException($"O serviço arrancou mas não responde em http://localhost:{o.Porta}/api/saude. Veja os logs em {PastaLogs}.");
    }

    /// <summary>HTTPS com a confiança normal do Windows (a autoridade local já está instalada neste computador).</summary>
    private async Task VerificarHttpsAsync(int portaHttps, CancellationToken ct)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        try
        {
            using var r = await http.GetAsync($"https://localhost:{portaHttps}/api/saude", ct);
            r.EnsureSuccessStatusCode();
            Info($"Ligação cifrada (HTTPS) a funcionar na porta {portaHttps}.");
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException($"O servidor responde em HTTP mas não em https://localhost:{portaHttps} ({ex.Message}). Veja os logs em {PastaLogs}.", ex);
        }
    }

    public static IReadOnlyList<string> EnderecosLocais() =>
        [.. NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .SelectMany(n => n.GetIPProperties().UnicastAddresses)
            .Select(a => a.Address)
            .Where(a => a.AddressFamily == AddressFamily.InterNetwork && !a.ToString().StartsWith("169.254."))
            .Select(a => a.ToString())
            .Distinct()];
}
