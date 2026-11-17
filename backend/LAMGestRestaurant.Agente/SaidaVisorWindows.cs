using System.Diagnostics;
using System.IO.Ports;
using Microsoft.Win32;

namespace LAMGestRestaurant.Agente;

/// <summary>
/// Visor do cliente neste Windows. Visor de poste: porta COM aberta enquanto o visor estiver configurado.
/// Segundo monitor: o Microsoft Edge em modo quiosque (ecrã inteiro, sem barras) no ecrã escolhido ou, sem escolha,
/// no primeiro que não é o principal; ligar o monitor mais tarde também funciona (o agente verifica a cada 20 s).
/// </summary>
public sealed class SaidaVisorWindows(Action<string> registar) : ISaidaVisor, IDisposable
{
    /// <summary>Título da página do visor (é por ele que o agente encontra a janela do Edge).</summary>
    public const string TituloPagina = "LAMGest — Visor do cliente";
    private static readonly TimeSpan IntervaloAbrir = TimeSpan.FromSeconds(30);
    private static readonly string PerfilEdge = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LAMGestRestaurant", "visor-edge");

    private SerialPort? porta;
    private string? portaChave;
    private byte[]? escrito;
    private string? monitorChave;
    private DateTime abertoEm = DateTime.MinValue;
    private string? avisoMonitor;

    public void Poste(VisorRecebido v)
    {
        FecharPagina();
        var chave = $"{v.Porta}|{v.Velocidade}";
        if (porta is null || !porta.IsOpen || chave != portaChave)
        {
            FecharPorta();
            porta = new SerialPort(v.Porta!, v.Velocidade, Parity.None, 8, StopBits.One) { WriteTimeout = 2000, Handshake = Handshake.None };
            try
            {
                porta.Open();
            }
            catch
            {
                porta.Dispose();
                porta = null;
                throw new IOException($"Não foi possível abrir a porta {v.Porta} do visor (ligado? usado por outro programa?).");
            }
            portaChave = chave;
            escrito = null;
            registar($"Visor de poste ligado em {v.Porta} ({v.Velocidade} bps, {v.Protocolo}).");
        }
        var bytes = ComandosVisor.Bytes(v.Protocolo, v.Linha1, v.Linha2, v.Colunas);
        if (escrito is not null && bytes.AsSpan().SequenceEqual(escrito))
            return;
        try
        {
            porta.Write(bytes, 0, bytes.Length);
            escrito = bytes;
        }
        catch
        {
            FecharPorta(); // na volta seguinte volta a abrir (ex.: cabo USB desligado e ligado)
            throw;
        }
    }

    public void Monitor(VisorRecebido v, Uri pagina)
    {
        FecharPorta();
        var ecra = EscolherEcra(v.Ecra);
        if (ecra is null)
        {
            Avisar(v.Ecra is { Length: > 0 } ? $"O ecrã {v.Ecra} do visor não está ligado." : "Não há segundo monitor ligado a este computador para o visor do cliente.");
            FecharPagina();
            return;
        }
        avisoMonitor = null;
        var chave = $"{ecra.DeviceName}|{pagina}";
        if (chave == monitorChave && JanelasVisor().Any())
            return;
        if (chave == monitorChave && DateTime.UtcNow - abertoEm < IntervaloAbrir)
            return; // acabou de abrir: o Edge ainda está a arrancar
        FecharPagina();
        var edge = CaminhoEdge() ?? throw new FileNotFoundException("O Microsoft Edge não está instalado: é preciso para o visor no segundo monitor.");
        var b = ecra.Bounds;
        var argumentos = $"--kiosk \"{pagina}\" --edge-kiosk-type=fullscreen --no-first-run --disable-features=Translate --user-data-dir=\"{PerfilEdge}\" " +
            $"--window-position={b.X},{b.Y} --window-size={b.Width},{b.Height}";
        Process.Start(new ProcessStartInfo(edge, argumentos) { UseShellExecute = false })?.Dispose();
        monitorChave = chave;
        abertoEm = DateTime.UtcNow;
        registar($"Visor do cliente aberto no ecrã {ecra.DeviceName} ({b.Width}x{b.Height}).");
    }

    public void Desligar()
    {
        FecharPorta();
        FecharPagina();
    }

    private void Avisar(string texto)
    {
        if (texto != avisoMonitor)
            registar(texto);
        avisoMonitor = texto;
    }

    /// <summary>O ecrã escolhido ou, sem escolha, o primeiro que não é o principal (nunca o principal: é o do operador).</summary>
    private static Screen? EscolherEcra(string? nome) =>
        nome is { Length: > 0 }
            ? Screen.AllScreens.FirstOrDefault(s => string.Equals(s.DeviceName, nome, StringComparison.OrdinalIgnoreCase))
            : Screen.AllScreens.Where(s => !s.Primary).OrderBy(s => s.Bounds.X).FirstOrDefault();

    private static IEnumerable<Process> JanelasVisor() =>
        Process.GetProcessesByName("msedge").Where(p =>
        {
            try
            {
                return p.MainWindowTitle.StartsWith(TituloPagina, StringComparison.Ordinal);
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        });

    /// <summary>Fecha as janelas do visor (também as que ficaram de um arranque anterior do agente).</summary>
    private void FecharPagina()
    {
        foreach (var p in JanelasVisor())
        {
            try
            {
                p.Kill();
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
            }
            p.Dispose();
        }
        monitorChave = null;
    }

    private void FecharPorta()
    {
        if (porta is null)
            return;
        try
        {
            porta.Dispose();
        }
        catch (IOException)
        {
        }
        porta = null;
        portaChave = null;
        escrito = null;
    }

    private static string? CaminhoEdge()
    {
        string[] candidatos =
        [
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft", "Edge", "Application", "msedge.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft", "Edge", "Application", "msedge.exe"),
        ];
        var registo = Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\msedge.exe", "", null) as string;
        return candidatos.Prepend(registo).FirstOrDefault(File.Exists);
    }

    public void Dispose() => Desligar();
}
