using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace LAMGestRestaurant.Agente.Janela;

/// <summary>
/// Janela do posto: o sistema do servidor no motor do Edge (WebView2), sem barra de endereço nem separadores. Mantém o que o
/// navegador em modo aplicação dava: ecrã inteiro (F11), impressão directa sem janela na impressora predefinida, visor do
/// cliente noutra janela (em ecrã inteiro no segundo monitor), PDF, ecrã táctil e a sessão do posto. Páginas de fora do
/// servidor abrem no navegador do Windows. Sem ligação ao servidor mostra uma página própria e tenta de novo sozinha.
/// </summary>
internal sealed class JanelaPosto : Form
{
    private readonly OpcoesJanela opcoes;
    private CoreWebView2Environment ambiente = null!;
    private readonly Func<Task<CoreWebView2Environment>> criarAmbiente;
    private readonly Uri? inicial;
    private readonly WebView2 vista = new() { Dock = DockStyle.Fill };
    private readonly System.Windows.Forms.Timer tentarDeNovo = new() { Interval = 5000 };
    private readonly TaskCompletionSource pronta = new();
    private readonly bool visor;
    private Uri? alvo;
    private bool emEcraInteiro;
    private FormWindowState estadoAnterior = FormWindowState.Maximized;

    private static readonly SemaphoreSlim FilaImpressao = new(1, 1);

    /// <param name="inicial">Endereço a abrir; null numa janela nova pedida pela página (o WebView2 é que a enche).</param>
    public JanelaPosto(OpcoesJanela opcoes, Func<Task<CoreWebView2Environment>> ambiente, Uri? inicial, bool visor = false)
    {
        this.opcoes = opcoes;
        criarAmbiente = ambiente;
        this.inicial = inicial;
        this.visor = visor;
        Text = "LAMGest Restaurant";
        using (var recurso = Assembly.GetExecutingAssembly().GetManifestResourceStream("lam.ico"))
            if (recurso is not null) Icon = new Icon(recurso);
        StartPosition = FormStartPosition.Manual;
        Bounds = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1366, 768);
        WindowState = FormWindowState.Maximized;
        BackColor = Color.White;
        Controls.Add(vista);
        tentarDeNovo.Tick += (_, _) => { tentarDeNovo.Stop(); if (alvo is not null) vista.CoreWebView2?.Navigate(alvo.ToString()); };
        vista.KeyDown += (_, e) => { if (e.KeyCode == Keys.F11) EcraInteiro(!emEcraInteiro); };
        Load += async (_, _) => await IniciarAsync();
        FormClosed += (_, _) => tentarDeNovo.Dispose();
    }

    /// <summary>Concluída quando o WebView2 está pronto (para entregar a janela nova pedida pela página).</summary>
    public Task Pronta => pronta.Task;

    public CoreWebView2? Motor => vista.CoreWebView2;

    private async Task IniciarAsync()
    {
        if (visor && Screen.AllScreens.FirstOrDefault(s => !s.Primary) is { } segundo)
        {
            WindowState = FormWindowState.Normal;
            Bounds = segundo.Bounds;
            EcraInteiro(true);
        }
        else if (opcoes.EcraInteiro && inicial is not null)
            EcraInteiro(true);

        try
        {
            ambiente = await criarAmbiente();
            await vista.EnsureCoreWebView2Async(ambiente);
        }
        catch (Exception ex)
        {
            pronta.TrySetException(ex);
            MessageBox.Show(this, "Não foi possível abrir o motor de páginas (WebView2): " + ex.Message, "LAMGest Restaurant", MessageBoxButtons.OK, MessageBoxIcon.Error);
            Close();
            return;
        }
        var motor = vista.CoreWebView2;
        motor.Settings.AreDevToolsEnabled = false;
        motor.Settings.IsStatusBarEnabled = false;
        motor.Settings.IsPasswordAutosaveEnabled = false;
        motor.Settings.IsGeneralAutofillEnabled = false;
        await motor.AddScriptToExecuteOnDocumentCreatedAsync(ScriptPagina(opcoes.ImpressaoDirecta));
        motor.DocumentTitleChanged += (_, _) => Text = string.IsNullOrWhiteSpace(motor.DocumentTitle) ? "LAMGest Restaurant" : motor.DocumentTitle;
        motor.ContainsFullScreenElementChanged += (_, _) => EcraInteiro(motor.ContainsFullScreenElement || opcoes.EcraInteiro);
        motor.NavigationStarting += AoNavegar;
        motor.NavigationCompleted += AoCarregar;
        motor.NewWindowRequested += AoPedirJanela;
        motor.WebMessageReceived += AoReceberMensagem;
        motor.PermissionRequested += (_, e) =>
        {
            // Área de transferência (copiar endereços) e câmara (códigos) só para as páginas do servidor.
            if (Uri.TryCreate(e.Uri, UriKind.Absolute, out var u) && opcoes.DoServidor(u)
                && e.PermissionKind is CoreWebView2PermissionKind.ClipboardRead or CoreWebView2PermissionKind.Camera)
                e.State = CoreWebView2PermissionState.Allow;
        };
        pronta.TrySetResult();
        if (inicial is not null)
        {
            alvo = inicial;
            motor.Navigate(inicial.ToString());
        }
    }

    /// <summary>Corre em cada página do servidor, antes dos scripts dela: anuncia a janela e, com impressão directa, imprime sem janela.</summary>
    internal static string ScriptPagina(bool impressaoDirecta) => $$"""
        (() => {
          if (window !== window.top) return;
          window.lamgestJanela = Object.freeze({ versao: 1, impressaoDirecta: {{(impressaoDirecta ? "true" : "false")}} });
          if ({{(impressaoDirecta ? "true" : "false")}}) window.print = () => window.chrome.webview.postMessage({ tipo: 'imprimir-pagina' });
        })();
        """;

    private void AoNavegar(object? remetente, CoreWebView2NavigationStartingEventArgs e)
    {
        if (!Uri.TryCreate(e.Uri, UriKind.Absolute, out var destino) || destino.Scheme is not ("http" or "https"))
            return; // about:blank, data: (páginas próprias)
        if (opcoes.DoServidor(destino))
        {
            alvo = destino;
            return;
        }
        e.Cancel = true;
        AbrirFora(destino);
    }

    private void AoCarregar(object? remetente, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (e.IsSuccess || e.WebErrorStatus == CoreWebView2WebErrorStatus.OperationCanceled || alvo is null)
            return;
        vista.CoreWebView2.NavigateToString(PaginaSemLigacao(alvo, e.WebErrorStatus.ToString()));
        tentarDeNovo.Start();
    }

    private async void AoPedirJanela(object? remetente, CoreWebView2NewWindowRequestedEventArgs e)
    {
        if (!Uri.TryCreate(e.Uri, UriKind.Absolute, out var destino) || !opcoes.DoServidor(destino))
        {
            e.Handled = true;
            if (destino is not null && destino.Scheme is "http" or "https") AbrirFora(destino);
            return;
        }
        var adiamento = e.GetDeferral();
        var nova = new JanelaPosto(opcoes with { EcraInteiro = false }, criarAmbiente, inicial: null, visor: OpcoesJanela.EVisor(destino));
        nova.Show();
        try
        {
            await nova.Pronta;
            e.NewWindow = nova.Motor;
            e.Handled = true;
        }
        catch (Exception)
        {
            nova.Close();
        }
        finally
        {
            adiamento.Complete();
        }
    }

    private async void AoReceberMensagem(object? remetente, CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (!Uri.TryCreate(e.Source, UriKind.Absolute, out var origem) || !opcoes.DoServidor(origem))
            return; // só as páginas do servidor pedem impressões
        try
        {
            using var json = JsonDocument.Parse(e.WebMessageAsJson);
            var raiz = json.RootElement;
            switch (raiz.GetProperty("tipo").GetString())
            {
                case "imprimir-pagina":
                    await ImprimirAsync(vista.CoreWebView2, 210);
                    break;
                case "imprimir":
                    var id = raiz.GetProperty("id").GetString();
                    var (ok, mensagem) = await ImprimirDocumentoAsync(raiz.GetProperty("html").GetString() ?? "", raiz.GetProperty("largura").GetInt32());
                    vista.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new { tipo = "impresso", id, ok, mensagem }));
                    break;
            }
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            Program.Registar("Mensagem da página inválida: " + ex.Message);
        }
    }

    /// <summary>
    /// Documento (recibo, talão, relatório) numa vista escondida aberta no próprio servidor (o logótipo e as imagens vêm com a
    /// sessão do posto) e impresso sem janela na impressora predefinida. Uma impressão de cada vez.
    /// </summary>
    private async Task<(bool Ok, string? Mensagem)> ImprimirDocumentoAsync(string html, int larguraMm)
    {
        await FilaImpressao.WaitAsync();
        CoreWebView2Controller? escondida = null;
        try
        {
            escondida = await ambiente.CreateCoreWebView2ControllerAsync(Handle);
            escondida.IsVisible = false;
            escondida.Bounds = new Rectangle(0, 0, 900, 1200);
            var w = escondida.CoreWebView2;
            var carregada = new TaskCompletionSource<bool>();
            w.NavigationCompleted += (_, a) => carregada.TrySetResult(a.IsSuccess);
            w.Navigate(new Uri(opcoes.Origem, "api/saude").ToString());
            if (!await carregada.Task.WaitAsync(TimeSpan.FromSeconds(20)))
                return (false, "O servidor não respondeu para preparar a impressão.");
            await w.ExecuteScriptAsync($"document.open(); document.write({JsonSerializer.Serialize(html)}); document.close();");
            for (var i = 0; i < 50; i++) // até 5 s pelas imagens (logótipo)
            {
                if (await w.ExecuteScriptAsync("document.readyState === 'complete' && Array.from(document.images).every(i => i.complete)") == "true")
                    break;
                await Task.Delay(100);
            }
            var estado = await ImprimirAsync(w, larguraMm);
            return estado == CoreWebView2PrintStatus.Succeeded ? (true, null)
                : (false, estado == CoreWebView2PrintStatus.PrinterUnavailable ? "A impressora predefinida deste posto não está disponível." : "A impressão falhou.");
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Program.Registar("Impressão directa falhou: " + ex.Message);
            return (false, ex.Message);
        }
        finally
        {
            escondida?.Close();
            FilaImpressao.Release();
        }
    }

    private async Task<CoreWebView2PrintStatus> ImprimirAsync(CoreWebView2 w, int larguraMm)
    {
        var definicoes = ambiente.CreatePrintSettings();
        definicoes.ShouldPrintHeaderAndFooter = false;
        definicoes.ShouldPrintBackgrounds = true;
        var margem = OpcoesJanela.Margem(larguraMm);
        definicoes.MarginTop = definicoes.MarginBottom = definicoes.MarginLeft = definicoes.MarginRight = margem;
        var estado = await w.PrintAsync(definicoes);
        if (estado != CoreWebView2PrintStatus.Succeeded)
            Program.Registar($"Impressão directa: {estado}");
        return estado;
    }

    private void EcraInteiro(bool activo)
    {
        if (activo == emEcraInteiro)
            return;
        emEcraInteiro = activo;
        if (activo)
        {
            estadoAnterior = WindowState == FormWindowState.Minimized ? FormWindowState.Maximized : WindowState;
            FormBorderStyle = FormBorderStyle.None;
            WindowState = FormWindowState.Normal; // para cobrir também a barra de tarefas
            WindowState = FormWindowState.Maximized;
        }
        else
        {
            FormBorderStyle = FormBorderStyle.Sizable;
            WindowState = estadoAnterior;
        }
    }

    private static void AbrirFora(Uri destino)
    {
        try
        {
            Process.Start(new ProcessStartInfo(destino.ToString()) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Program.Registar("Não foi possível abrir " + destino + ": " + ex.Message);
        }
    }

    internal static string PaginaSemLigacao(Uri alvo, string motivo) => $$"""
        <!doctype html><html lang="pt"><head><meta charset="utf-8"><title>Sem ligação ao servidor</title>
        <style>body{font-family:Segoe UI,sans-serif;display:flex;align-items:center;justify-content:center;height:100vh;margin:0;background:#f1f5f9;color:#0f172a}
        main{max-width:36rem;text-align:center;padding:2rem}h1{font-size:1.6rem}p{color:#475569}code{background:#e2e8f0;padding:.1rem .4rem;border-radius:.25rem}
        button{font-size:1.1rem;padding:.7rem 1.4rem;border:0;border-radius:.5rem;background:#1f3a68;color:#fff;cursor:pointer}</style></head>
        <body><main><h1>Sem ligação ao servidor</h1>
        <p>O servidor LAMGest em <code>{{System.Net.WebUtility.HtmlEncode(alvo.GetLeftPart(UriPartial.Authority))}}</code> não respondeu ({{System.Net.WebUtility.HtmlEncode(motivo)}}).
        Nada foi gravado. Confirme que o servidor está ligado e na rede.</p>
        <p>A tentar de novo a cada 5 segundos…</p>
        <button onclick="location.href={{JsonSerializer.Serialize(alvo.ToString())}}">Tentar agora</button></main></body></html>
        """;
}
