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
    bool MigracoesConfirmadas);

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
            await ConfigurarFirewallAsync();
        }

        if (o.BackupDiario)
        {
            Passo($"A agendar a cópia de segurança diária às {o.HoraBackup:HH\\:mm}…");
            await ServicoSql.GarantirPermissoesBackupAsync(o.Sql, ct);
            await AgendarBackupAsync();
        }

        Passo("A iniciar o serviço…");
        await IniciarEVerificarAsync(ct);

        var enderecos = EnderecosLocais().Select(ip => $"http://{ip}:{o.Porta}").ToList();
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
            $"ASPNETCORE_URLS=http://0.0.0.0:{o.Porta}",
            $"LAMGEST_CONFIG={FicheiroConfig}",
        }, RegistryValueKind.MultiString);
        Info($"Serviço «{NomeServico}» com a conta {ServicoSql.ContaServico}, arranque automático e reinício em caso de falha.");
    }

    private async Task PrepararPastasAsync(string ligacaoServico)
    {
        foreach (var p in new[] { PastaConfig, PastaChaves, PastaLogs })
            Directory.CreateDirectory(p);
        // Configuração e chaves: sem herança; SYSTEM e Administradores com controlo total, o serviço só o necessário.
        await Processos.ExigirAsync("icacls", PastaConfig, "/inheritance:r", "/grant:r", $"{Sistema}:(OI)(CI)F", $"{Administradores}:(OI)(CI)F", $"{ServicoSql.ContaServico}:(OI)(CI)R");
        await Processos.ExigirAsync("icacls", PastaChaves, "/inheritance:r", "/grant:r", $"{Sistema}:(OI)(CI)F", $"{Administradores}:(OI)(CI)F", $"{ServicoSql.ContaServico}:(OI)(CI)M");
        await Processos.ExigirAsync("icacls", PastaLogs, "/grant", $"{ServicoSql.ContaServico}:(OI)(CI)M");

        var config = new
        {
            ConnectionStrings = new { Default = ligacaoServico },
            DataProtection = new { Pasta = PastaChaves },
            Serilog = new { WriteTo = new Dictionary<string, object> { ["1"] = new { Args = new { path = Path.Combine(PastaLogs, "lamgest-.log") } } } },
        };
        await File.WriteAllTextAsync(FicheiroConfig, JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true }));
        Info($"Configuração protegida em {FicheiroConfig}; logs em {PastaLogs}");
    }

    private async Task ConfigurarFirewallAsync()
    {
        var nome = $"LAMGest Restaurant (TCP {o.Porta})";
        await Processos.CorrerAsync("netsh", ["advfirewall", "firewall", "delete", "rule", $"name={nome}"]);
        await Processos.ExigirAsync("netsh", "advfirewall", "firewall", "add", "rule", $"name={nome}", "dir=in", "action=allow",
            // Sem internet o Windows classifica a rede como «pública»: a regra vale em todos os perfis, mas só para a rede local.
            "protocol=TCP", $"localport={o.Porta}", "profile=any", "remoteip=localsubnet");
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

    public static IReadOnlyList<string> EnderecosLocais() =>
        [.. NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .SelectMany(n => n.GetIPProperties().UnicastAddresses)
            .Select(a => a.Address)
            .Where(a => a.AddressFamily == AddressFamily.InterNetwork && !a.ToString().StartsWith("169.254."))
            .Select(a => a.ToString())
            .Distinct()];
}
