using System.Diagnostics;
using Microsoft.Web.WebView2.Core;
using Aplicacao = System.Windows.Forms.Application;

namespace LAMGestRestaurant.Agente.Janela;

/// <summary>Arranque do programa do posto: a janela WebView2 ou, num Windows sem o WebView2, o Chrome/Edge com os mesmos argumentos.</summary>
internal static class ArranqueJanela
{
    public static int Executar(OpcoesJanela opcoes)
    {
        ApplicationConfiguration.Initialize();
        try
        {
            CoreWebView2Environment.GetAvailableBrowserVersionString();
        }
        catch (WebView2RuntimeNotFoundException)
        {
            return AbrirNoNavegador(opcoes);
        }

        var pasta = opcoes.PastaDados(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
        Task<CoreWebView2Environment>? ambiente = null;
        // Criado na primeira janela (no fio da interface, como o WebView2 exige) e partilhado pelas outras (visor, documentos).
        Task<CoreWebView2Environment> Ambiente() => ambiente ??= CoreWebView2Environment.CreateAsync(null, pasta);
        Program.Registar($"Janela do posto: {opcoes.Endereco} (dados em {pasta})");
        Aplicacao.Run(new JanelaPosto(opcoes, Ambiente, opcoes.Endereco, OpcoesJanela.EVisor(opcoes.Endereco)));
        return 0;
    }

    private static int AbrirNoNavegador(OpcoesJanela opcoes)
    {
        string[] candidatos =
        [
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Google\Chrome\Application\chrome.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"Google\Chrome\Application\chrome.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"Microsoft\Edge\Application\msedge.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Microsoft\Edge\Application\msedge.exe"),
        ];
        if (candidatos.FirstOrDefault(File.Exists) is { } navegador)
        {
            Program.Registar("WebView2 não instalado: o posto abre no " + navegador);
            Process.Start(new ProcessStartInfo(navegador, opcoes.ArgumentosNavegador()) { UseShellExecute = false });
            return 0;
        }
        MessageBox.Show("Este Windows não tem o Microsoft Edge WebView2 Runtime nem um navegador (Chrome ou Edge).\n\n" +
            "Instale o «Microsoft Edge WebView2 Runtime» (gratuito, da Microsoft) e abra de novo o LAMGest.",
            "LAMGest Restaurant", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        return 1;
    }
}
