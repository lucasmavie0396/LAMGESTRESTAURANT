using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using LAMGestRestaurant.Configurar.Motor;
using LAMGestRestaurant.Configurar.Ui;

namespace LAMGestRestaurant.Configurar;

/// <summary>
/// LAMGest-Configurar.exe — no servidor muda a base de dados que o sistema abre; num posto muda o servidor a que liga.
/// Entra-se com o código de acesso do LAMGest (o mesmo do LAMGest-Setup.exe).
///   --servidor / --posto  força o modo (quando o computador é as duas coisas, pergunta)
///   --captura &lt;pasta&gt;    grava imagens das janelas com dados de exemplo (não liga a nada nem altera nada)
/// </summary>
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        if (args.Length >= 2 && args[0] == "--captura")
        {
            // --captura <pasta> [escala]: a escala (ex.: 1,5) simula um ecrã a 150 %.
            if (args.Length == 3)
                Estilo.EscalaForcada = float.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture);
            Capturar(args[1], args.Length == 3 ? "-" + (int)Math.Round(Estilo.EscalaForcada!.Value * 100) : "");
            return 0;
        }

        var locais = Locais.Padrao();
        var eServidor = ServidorInstalado.Existe();
        var ePosto = PostoInstalado.Existe(locais);
        if (args.Contains("--servidor"))
            ePosto = false;
        else if (args.Contains("--posto"))
            eServidor = false;
        if (!eServidor && !ePosto)
        {
            MessageBox.Show("O LAMGest Restaurant não está instalado neste computador (nem servidor nem posto). Use o LAMGest-Setup.exe.",
                "LAMGest — Configurar base de dados", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return 1;
        }

        using (var codigo = new JanelaCodigo(locais, new BloqueioTentativas(locais.FicheiroBloqueio)))
            if (codigo.ShowDialog() != DialogResult.OK)
                return 1;
        RegistoAlteracoes.Escrever(locais, "Entrada no configurador");

        if (eServidor && ePosto)
        {
            var escolha = MessageBox.Show("Este computador é o servidor e também um posto.\n\nSim — mudar a base de dados do servidor\nNão — mudar o servidor a que este posto liga",
                "LAMGest — Configurar base de dados", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (escolha == DialogResult.Cancel)
                return 1;
            eServidor = escolha == DialogResult.Yes;
        }

        try
        {
            Form janela = eServidor
                ? new JanelaServidor(locais, ServidorInstalado.Ler(locais))
                : new JanelaPosto(locais, PostoInstalado.Ler(locais));
            Application.Run(janela);
            return 0;
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException or FormatException or ArgumentException)
        {
            MessageBox.Show("Não foi possível ler a configuração do LAMGest neste computador:\n\n" + ex.Message,
                "LAMGest — Configurar base de dados", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }

    private static void Capturar(string pasta, string sufixo)
    {
        Directory.CreateDirectory(pasta);
        var locais = new Locais(Path.Combine(Path.GetTempPath(), "lamgest-configurar-captura"));
        var fora = new Point(-3000, -3000);

        using (var codigo = new JanelaCodigo(locais, null) { StartPosition = FormStartPosition.Manual, Location = fora, ShowInTaskbar = false })
        {
            codigo.Show();
            codigo.Exemplo();
            Estilo.Gravar(codigo, Path.Combine(pasta, "1-codigo" + sufixo + ".png"));
        }

        var servidor = new ServidorInstalado
        {
            PastaInstalacao = @"C:\Program Files\LAMGestRestaurant", Porta = 8080, Instancia = @".\SYSON", BaseDados = "LAMGestRestaurantDB", EstadoServico = "a correr",
        };
        using (var janela = new JanelaServidor(locais, servidor, demonstracao: true) { StartPosition = FormStartPosition.Manual, Location = fora, ShowInTaskbar = false })
        {
            janela.Show();
            janela.Exemplo();
            Estilo.Gravar(janela, Path.Combine(pasta, "2-servidor" + sufixo + ".png"));
        }

        var posto = new PostoInstalado { Servidor = "192.168.1.10", Porta = 8080, Numero = 3, TemAgente = true, ServidorAgente = "http://192.168.1.10:8080" };
        using (var janela = new JanelaPosto(locais, posto) { StartPosition = FormStartPosition.Manual, Location = fora, ShowInTaskbar = false })
        {
            janela.Show();
            janela.Exemplo();
            Estilo.Gravar(janela, Path.Combine(pasta, "3-posto" + sufixo + ".png"));
        }
    }
}
