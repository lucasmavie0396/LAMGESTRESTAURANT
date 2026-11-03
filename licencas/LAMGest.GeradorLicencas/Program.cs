using System.Globalization;

namespace LAMGest.GeradorLicencas;

internal static class Program
{
    /// <summary>
    /// Sem argumentos: abre o gerador. Linha de comandos (automatização e verificação):
    /// --criar-chaves &lt;ficheiro da chave pública&gt;  cria a chave deste computador e grava a pública;
    /// --chave-publica &lt;ficheiro&gt;  grava a chave pública actual (para confirmar que é a embutida no sistema);
    /// --emitir &lt;nome&gt; &lt;nuit&gt; &lt;aaaa-mm-dd&gt; &lt;ficheiro&gt;  emite uma licença e grava-a no ficheiro.
    /// --pasta &lt;dir&gt; (primeiro argumento) usa outra pasta de dados.
    /// </summary>
    [STAThread]
    private static int Main(string[] args)
    {
        var pasta = ArmazemLicencas.PastaPadrao;
        if (args is ["--pasta", var p, .. var resto])
        {
            pasta = p;
            args = resto;
        }
        var armazem = new ArmazemLicencas(pasta);
        try
        {
            switch (args)
            {
                case ["--criar-chaves", var ficheiro]:
                    File.WriteAllText(ficheiro, armazem.CriarChave());
                    return 0;
                case ["--chave-publica", var ficheiro]:
                    File.WriteAllText(ficheiro, armazem.ChavePublica());
                    return 0;
                case ["--emitir", var nome, var nuit, var ate, var ficheiro]:
                    var l = armazem.Emitir(nome, nuit, DateOnly.ParseExact(ate, "yyyy-MM-dd", CultureInfo.InvariantCulture), DateOnly.FromDateTime(DateTime.Today));
                    File.WriteAllText(ficheiro, l.Texto);
                    return 0;
                case ["--captura", var imagem]:
                    // Verificação visual: janela com dados de exemplo (use com --pasta numa pasta temporária).
                    ApplicationConfiguration.Initialize();
                    if (!armazem.TemChave)
                        armazem.CriarChave();
                    var hoje = DateOnly.FromDateTime(DateTime.Today);
                    armazem.Emitir("Restaurante Sol Nascente", "400123456", hoje.AddDays(20), hoje);
                    armazem.Emitir("Bar Maré Alta", "400765432", hoje.AddYears(1), hoje);
                    using (var janela = new JanelaPrincipal(armazem) { StartPosition = FormStartPosition.Manual, Location = new Point(-3000, -3000), ShowInTaskbar = false })
                    {
                        janela.Show();
                        janela.SeleccionarPrimeiro();
                        Application.DoEvents();
                        using var bmp = new Bitmap(janela.Width, janela.Height);
                        janela.DrawToBitmap(bmp, new Rectangle(Point.Empty, janela.Size));
                        bmp.Save(imagem, System.Drawing.Imaging.ImageFormat.Png);
                    }
                    return 0;
                case []:
                    break;
                default:
                    return 2;
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or IOException)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }

        ApplicationConfiguration.Initialize();
        if (!armazem.TemChave && !PrimeiraUtilizacao(armazem))
            return 0;
        Application.Run(new JanelaPrincipal(armazem));
        return 0;
    }

    /// <summary>Sem chave neste computador: repor uma cópia (normal) ou criar uma chave nova (só na primeira instalação do fabricante).</summary>
    private static bool PrimeiraUtilizacao(ArmazemLicencas armazem)
    {
        var resposta = MessageBox.Show(
            "Este computador ainda não tem a chave de assinatura das licenças.\n\n" +
            "Sim — repor a partir de uma cópia de segurança (.pem).\n" +
            "Não — criar uma chave NOVA (só se for a primeira vez: o sistema LAMGest tem de ser compilado com a nova chave pública, " +
            "e as licenças assinadas com outra chave deixam de servir).",
            "LAMGest — Gerador de licenças", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
        try
        {
            if (resposta == DialogResult.Yes)
            {
                using var dialogo = new OpenFileDialog { Filter = "Cópia da chave (*.pem)|*.pem" };
                if (dialogo.ShowDialog() != DialogResult.OK || Palavra.Pedir(null!, "Palavra-passe da cópia:", false) is not { } senha)
                    return false;
                armazem.ImportarCopia(dialogo.FileName, senha);
                return true;
            }
            if (resposta == DialogResult.No)
            {
                var publica = armazem.CriarChave();
                Clipboard.SetText(publica);
                MessageBox.Show("Chave criada. A chave pública foi copiada: grave-a em backend\\LAMGestRestaurant.Licenciamento\\chave-publica.txt e volte a gerar o Setup.\n\n" +
                    "Faça já uma cópia de segurança (menu Chave).", "LAMGest — Gerador de licenças", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return true;
            }
        }
        catch (InvalidOperationException ex)
        {
            MessageBox.Show(ex.Message, "LAMGest — Gerador de licenças", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        return false;
    }
}
