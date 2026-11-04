using System.Diagnostics;
using System.Reflection;
using LAMGestRestaurant.Infrastructure.Impressao;
using Aplicacao = System.Windows.Forms.Application;

namespace LAMGestRestaurant.Agente;

/// <summary>
/// Agente de impressão do posto: ícone junto ao relógio e ciclo em segundo plano (<see cref="MotorAgente"/>).
/// Arranca com o Windows (atalho no arranque de todos os utilizadores, criado pelo instalador).
/// </summary>
internal static class Program
{
    private static readonly string PastaRegisto = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LAMGestRestaurant");
    private static readonly string FicheiroRegisto = Path.Combine(PastaRegisto, "agente.log");
    private static readonly object Trinco = new();

    [STAThread]
    /// <summary>--config &lt;ficheiro&gt;: outra configuração (testes); por omissão a do instalador em ProgramData.</summary>
    private static int Main(string[] args)
    {
        var ficheiro = args.Length >= 2 && args[0] == "--config" ? Path.GetFullPath(args[1]) : ConfiguracaoAgente.Ficheiro;
        // Um agente por sessão do Windows (o atalho do arranque e um arranque manual não imprimem a dobrar).
        using var unico = new Mutex(true, @"Local\LAMGestRestaurant.Agente", out var primeiro);
        if (!primeiro)
            return 0;

        ApplicationConfiguration.Initialize();
        ConfiguracaoAgente? config;
        try
        {
            config = ConfiguracaoAgente.Ler(ficheiro);
        }
        catch (Exception ex)
        {
            Registar("Configuração inválida: " + ex.Message);
            config = null;
        }
        if (config is null)
        {
            MessageBox.Show($"Este computador não está configurado como posto com impressão.\n\nFicheiro em falta ou inválido:\n{ficheiro}\n\n" +
                "Execute o LAMGest-Setup.exe como administrador, escolha «Posto» e marque «Agente de impressão».",
                "LAMGest — Agente de impressão", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return 1;
        }

        var versao = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "?";
        using var http = MotorAgente.CriarCliente(config.Servidor, config.Posto, config.Chave);
        var motor = new MotorAgente(http, versao, ImpressaoWindows.Instaladas,
            (t, logo) => ImpressaoWindows.Imprimir(t.NomeWindows, t.LarguraPapelMm, t.Documento, logo), Registar);
        using var paragem = new CancellationTokenSource();
        using var icone = CriarIcone(config, motor, paragem);
        Registar($"Agente {versao} iniciado: posto {config.Posto}, servidor {config.Servidor}");
        var ciclo = Task.Run(() => motor.CorrerAsync(paragem.Token));
        Aplicacao.Run();
        paragem.Cancel();
        try
        {
            ciclo.Wait(TimeSpan.FromSeconds(3));
        }
        catch (AggregateException)
        {
        }
        Registar("Agente terminado.");
        return 0;
    }

    private static NotifyIcon CriarIcone(ConfiguracaoAgente config, MotorAgente motor, CancellationTokenSource paragem)
    {
        using var recurso = Assembly.GetExecutingAssembly().GetManifestResourceStream("lam.ico");
        var icone = new NotifyIcon
        {
            Icon = recurso is null ? SystemIcons.Application : new Icon(recurso),
            Visible = true,
            Text = Dica(config, motor),
        };
        var estado = new ToolStripMenuItem(Texto(motor)) { Enabled = false };
        var menu = new ContextMenuStrip();
        menu.Items.Add(new ToolStripMenuItem($"LAMGest — posto {config.Posto}") { Enabled = false, Font = new Font(menu.Font, FontStyle.Bold) });
        menu.Items.Add(estado);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Impressoras deste computador…", null, (_, _) => MostrarImpressoras(config));
        menu.Items.Add("Abrir o registo", null, (_, _) => AbrirRegisto());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Sair (deixa de imprimir neste posto)", null, (_, _) =>
        {
            if (MessageBox.Show("Enquanto o agente estiver fechado, os talões e recibos das impressoras deste posto não saem.\n\nFechar o agente?",
                    "LAMGest — Agente de impressão", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) == DialogResult.Yes)
            {
                paragem.Cancel();
                Aplicacao.Exit();
            }
        });
        icone.ContextMenuStrip = menu;
        icone.DoubleClick += (_, _) => MostrarImpressoras(config);

        var anterior = motor.Estado;
        var contexto = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        motor.Mudou += () => contexto.Post(_ =>
        {
            estado.Text = Texto(motor);
            icone.Text = Dica(config, motor);
            if (motor.Estado != anterior && motor.Estado is EstadoLigacao.SemServidor or EstadoLigacao.NaoAutorizado)
                icone.ShowBalloonTip(5000, "LAMGest — impressão do posto", motor.UltimoErro ?? "", ToolTipIcon.Warning);
            anterior = motor.Estado;
        }, null);
        return icone;
    }

    private static string Texto(MotorAgente motor) => motor.Estado switch
    {
        EstadoLigacao.Ligado => $"Ligado ao servidor · {motor.Impressos} impresso(s)",
        EstadoLigacao.SemServidor => "Sem ligação ao servidor (a tentar de novo)",
        EstadoLigacao.NaoAutorizado => "Chave do posto recusada pelo servidor",
        _ => "A ligar ao servidor…",
    };

    /// <summary>Dica do ícone (o Windows corta acima de 127 caracteres).</summary>
    private static string Dica(ConfiguracaoAgente config, MotorAgente motor)
    {
        var texto = $"LAMGest — posto {config.Posto}\n{Texto(motor)}";
        return texto.Length > 127 ? texto[..127] : texto;
    }

    private static void MostrarImpressoras(ConfiguracaoAgente config)
    {
        var nomes = ImpressaoWindows.Instaladas();
        MessageBox.Show((nomes.Count == 0 ? "Não há impressoras instaladas neste Windows." : string.Join("\n", nomes.Select(n => "• " + n))) +
            $"\n\nServidor: {config.Servidor}\nEscolha onde sai cada documento no LAMGest: Configurações → Impressoras → «Ligada a: posto {config.Posto}».",
            "Impressoras deste computador", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private static void AbrirRegisto()
    {
        if (File.Exists(FicheiroRegisto))
            Process.Start(new ProcessStartInfo("notepad.exe", $"\"{FicheiroRegisto}\"") { UseShellExecute = true });
    }

    /// <summary>Registo simples (máx. ~1 MB: ao passar, começa de novo guardando o anterior em agente.log.1).</summary>
    private static void Registar(string texto)
    {
        lock (Trinco)
        {
            try
            {
                Directory.CreateDirectory(PastaRegisto);
                if (File.Exists(FicheiroRegisto) && new FileInfo(FicheiroRegisto).Length > 1_000_000)
                    File.Move(FicheiroRegisto, FicheiroRegisto + ".1", overwrite: true);
                File.AppendAllText(FicheiroRegisto, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {texto}{Environment.NewLine}");
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
