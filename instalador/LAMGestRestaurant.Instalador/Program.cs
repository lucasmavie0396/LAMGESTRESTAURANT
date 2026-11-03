using LAMGestRestaurant.Instalador.Ui;

namespace LAMGestRestaurant.Instalador;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        // Verificação visual do assistente: grava imagens das páginas (não instala nem liga a nada).
        if (args is ["--captura", var pasta])
        {
            using var assistente = new Assistente { StartPosition = FormStartPosition.Manual, Location = new Point(-3000, -3000), ShowInTaskbar = false };
            assistente.Show();
            assistente.Capturar(pasta);
            return 0;
        }

        Application.Run(new Assistente());
        return 0;
    }
}
