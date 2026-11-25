using System.Globalization;
using LAMGestRestaurant.Licenciamento;

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
        // A captura usa uma chave de exemplo; tudo o resto só emite licenças que a chave embutida no sistema aceita.
        var armazem = args is ["--captura", ..] ? new ArmazemLicencas(pasta)
            : new ArmazemLicencas(pasta, ChavesLicenca.ExportarPublica(ChavesLicenca.PublicaEmbutida()));
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
                    // Verificação visual: janela com dados de exemplo (use com --pasta numa pasta temporária). Usa uma base de testes
                    // (LAMGEST_TESTES_SQL ou .\SYSON) que apaga no fim.
                    ApplicationConfiguration.Initialize();
                    if (!armazem.TemChave)
                        armazem.CriarChave();
                    var hoje = DateOnly.FromDateTime(DateTime.Today);
                    var teste = new BaseLicencas(new LigacaoBase(Environment.GetEnvironmentVariable("LAMGEST_TESTES_SQL") ?? @".\SYSON", "LAMGestLicencas_Captura_Testes"));
                    try
                    {
                        teste.Preparar();
                        var sol = teste.CriarCliente("Restaurante Sol Nascente", "400123456", "84 000 0000", Plano.Mensal, 2500m, null);
                        teste.RegistarPagamento(armazem, sol, hoje.AddMonths(-1), 2500m, 1, "M-Pesa 1", hoje);
                        teste.RegistarPagamento(armazem, sol, hoje, 2500m, 1, "M-Pesa 2", hoje);
                        var mare = teste.CriarCliente("Bar Maré Alta", "400765432", null, Plano.Trimestral, 3000m, null);
                        teste.EmitirSemPagamento(armazem, mare, hoje.AddDays(-1), "Período de teste", hoje.AddDays(-30));
                        var lua = teste.CriarCliente("Pizzaria Lua", "400111222", null, Plano.Mensal, 2000m, null);
                        teste.EmitirSemPagamento(armazem, lua, hoje.AddDays(5), "Período de teste", hoje);
                        teste.MudarEstado(lua, EstadoCliente.Suspenso, "Falta de pagamento");
                        using var janela = new JanelaPrincipal(armazem, teste) { StartPosition = FormStartPosition.Manual, Location = new Point(-3000, -3000), ShowInTaskbar = false };
                        janela.Show();
                        janela.SeleccionarPrimeiro();
                        foreach (var (separador, sufixo) in new[] { (0, ""), (1, "-pagamentos"), (2, "-licencas") })
                        {
                            janela.MostrarSeparador(separador);
                            Application.DoEvents();
                            using var bmp = new Bitmap(janela.Width, janela.Height);
                            janela.DrawToBitmap(bmp, new Rectangle(Point.Empty, janela.Size));
                            bmp.Save(Path.ChangeExtension(imagem, null) + sufixo + ".png", System.Drawing.Imaging.ImageFormat.Png);
                        }
                    }
                    finally
                    {
                        teste.ApagarBaseDeTestes();
                    }
                    // Janelas pequenas: fotografadas e fechadas logo que aparecem.
                    void Fotografar(string sufixo, Action abrir)
                    {
                        using var relogio = new System.Windows.Forms.Timer { Interval = 700 };
                        relogio.Tick += (_, _) =>
                        {
                            relogio.Stop();
                            if (Application.OpenForms.Cast<Form>().LastOrDefault() is not { } f)
                                return;
                            using var bmp = new Bitmap(f.Width, f.Height);
                            f.DrawToBitmap(bmp, new Rectangle(Point.Empty, f.Size));
                            bmp.Save(Path.ChangeExtension(imagem, null) + sufixo + ".png", System.Drawing.Imaging.ImageFormat.Png);
                            f.DialogResult = DialogResult.Cancel;
                        };
                        relogio.Start();
                        abrir();
                    }
                    Fotografar("-ligacao", () => Dialogos.Ligacao(null, null));
                    var exemplo = new ClienteResumo(1, "Restaurante Sol Nascente", "400123456", null, Plano.Trimestral, 2500m, EstadoCliente.Activo, null, hoje.AddDays(5), hoje);
                    Fotografar("-pagamento", () => Dialogos.Pagamento(null!, exemplo));
                    Fotografar("-motivo", () => Dialogos.Texto(null!, "Suspender «Restaurante Sol Nascente»", "Motivo (ex.: falta de pagamento de Novembro). O gerador deixa de emitir licenças para este cliente."));
                    Fotografar("-sem-pagamento", () => Dialogos.SemPagamento(null!, exemplo));
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
        if (AbrirBase(armazem) is not { } baseLicencas)
            return 0;
        Application.Run(new JanelaPrincipal(armazem, baseLicencas));
        return 0;
    }

    /// <summary>
    /// Liga à base das licenças (pergunta a instância na primeira vez) e cria-a se for preciso. Com a base ainda vazia e o registo
    /// antigo (registo.json) com licenças, importa-as.
    /// </summary>
    private static BaseLicencas? AbrirBase(ArmazemLicencas armazem)
    {
        var ligacao = LigacaoBase.Ler(armazem.Pasta) ?? Dialogos.Ligacao(null, null);
        while (ligacao is not null)
        {
            try
            {
                var b = new BaseLicencas(ligacao);
                b.Preparar();
                ligacao.Gravar(armazem.Pasta);
                if (!b.TemLicencas() && armazem.Registo().Count > 0)
                {
                    var n = b.Importar(armazem.Registo());
                    MessageBox.Show($"{n} licenças do registo antigo foram importadas para a base {b.NomeBase}.\n\n" +
                        "Preencha em cada cliente o plano e o valor por mês.", "LAMGest — Licenças", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                return b;
            }
            catch (Exception ex) when (ex is Microsoft.Data.SqlClient.SqlException or ArgumentException or InvalidOperationException
                                           or System.Security.Cryptography.CryptographicException or FormatException)
            {
                // Volta à janela da ligação com o motivo (ex.: sem permissão para criar a base → entrar com o sa).
                ligacao = Dialogos.Ligacao(null, ligacao, "Não consegui abrir a base de dados das licenças: " + BaseLicencas.ExplicarErro(ex));
            }
        }
        return null;
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
