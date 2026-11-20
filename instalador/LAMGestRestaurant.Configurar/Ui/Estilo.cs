using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace LAMGestRestaurant.Configurar.Ui;

/// <summary>Aspecto comum às janelas do configurador (o mesmo do LAMGest-Setup.exe).</summary>
internal static class Estilo
{
    public static readonly Color Azul = Color.FromArgb(37, 99, 235);
    public static readonly Color Verde = Color.FromArgb(21, 128, 61);
    public static readonly Color Vermelho = Color.FromArgb(185, 28, 28);
    public static readonly Color Ambar = Color.FromArgb(146, 64, 14);
    public static readonly Color FundoAmbar = Color.FromArgb(254, 243, 199);
    public static readonly Color Cinzento = Color.FromArgb(100, 116, 139);
    public static readonly Color Texto = Color.FromArgb(30, 41, 59);
    public static readonly Color Linha = Color.FromArgb(226, 232, 240);
    public static readonly Color Fundo = Color.FromArgb(248, 250, 252);

    public static void Janela(Form f, string titulo, Size tamanho)
    {
        f.Text = titulo;
        f.Font = new Font("Segoe UI", 9.75f);
        f.AutoScaleMode = AutoScaleMode.None;
        f.ClientSize = tamanho;
        f.FormBorderStyle = FormBorderStyle.FixedDialog;
        f.MaximizeBox = false;
        f.StartPosition = FormStartPosition.CenterScreen;
        f.BackColor = Fundo;
        try
        {
            f.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException)
        {
        }
    }

    /// <summary>
    /// As posições estão em píxeis a 96 ppp; o texto segue a escala do Windows (125 %, 150 %…). Chamado no fim de cada
    /// construtor, depois de criados os controlos, escala tudo na mesma proporção que o texto.
    /// </summary>
    public static void Escalar(Form f)
    {
        var fator = Fator();
        if (Math.Abs(fator - 1f) <= 0.01f)
            return;
        // Simulação para as capturas: o texto cresce o que falta da escala real do ecrã até à escala pedida.
        if (EscalaForcada is float forcada)
        {
            EscalaForcada = null;
            var real = Fator();
            EscalaForcada = forcada;
            AumentarLetra(f, forcada / real);
        }
        f.Scale(new SizeF(fator, fator));
        foreach (Control c in f.Controls)
            if (c is ListView lista)
                foreach (ColumnHeader coluna in lista.Columns)
                    coluna.Width = (int)Math.Round(coluna.Width * fator);
    }

    /// <summary>Só para as capturas: simula um ecrã com esta escala (ex.: 1,5 = 150 %).</summary>
    public static float? EscalaForcada { get; set; }

    /// <summary>
    /// Escala do ecrã principal. O programa é «dpiAware» (manifesto), por isso o ecrã devolve os ppp reais
    /// (o Control.DeviceDpi do .NET Framework devolve sempre 96 sem a configuração PerMonitor).
    /// </summary>
    public static float Fator()
    {
        if (EscalaForcada is float forcada)
            return forcada;
        try
        {
            using var g = Graphics.FromHwnd(IntPtr.Zero);
            return g.DpiX / 96f;
        }
        catch (Exception)
        {
            return 1f;
        }
    }

    private static void AumentarLetra(Control c, float fator)
    {
        // Só as letras definidas no próprio controlo (as outras herdam do pai).
        if (c.Parent is null || !c.Font.Equals(c.Parent.Font))
            c.Font = new Font(c.Font.FontFamily, c.Font.Size * fator, c.Font.Style);
        foreach (Control filho in c.Controls)
            AumentarLetra(filho, fator);
    }

    /// <summary>Cabeçalho branco com título, subtítulo e a marca LG.</summary>
    public static Panel Cabecalho(string titulo, string subtitulo, int largura)
    {
        var p = new Panel { Dock = DockStyle.Top, Height = 72, BackColor = Color.White };
        p.Controls.Add(new Label { Text = titulo, AutoSize = true, Location = new Point(24, 12), Font = new Font("Segoe UI", 13f, FontStyle.Bold), ForeColor = Color.FromArgb(15, 23, 42) });
        p.Controls.Add(new Label { Text = subtitulo, AutoSize = true, Location = new Point(26, 42), ForeColor = Color.FromArgb(71, 85, 105) });
        p.Controls.Add(new Label
        {
            Text = "LG", AutoSize = false, Size = new Size(42, 42), Location = new Point(largura - 24 - 42, 15), TextAlign = ContentAlignment.MiddleCenter,
            BackColor = Azul, ForeColor = Color.White, Font = new Font("Segoe UI", 12f, FontStyle.Bold),
        });
        return p;
    }

    public static Panel Separador(DockStyle dock) => new() { Dock = dock, Height = 1, BackColor = Linha };

    public static Label Seccao(string texto, int x, int y) =>
        new() { Text = texto, AutoSize = true, Location = new Point(x, y), Font = new Font("Segoe UI", 10.5f, FontStyle.Bold), ForeColor = Texto };

    public static Label Rotulo(string texto, int x, int y, int largura = 170) =>
        new() { Text = texto, AutoSize = false, Size = new Size(largura, 22), Location = new Point(x, y + 3), ForeColor = Cinzento };

    public static Label Valor(string texto, int x, int y, int largura = 480) =>
        new() { Text = texto, AutoSize = false, Size = new Size(largura, 22), Location = new Point(x, y + 3), ForeColor = Texto, Font = new Font("Segoe UI", 9.75f, FontStyle.Bold), UseMnemonic = false };

    public static Button Botao(string texto, int largura, bool principal = false)
    {
        var b = new Button { Text = texto, Width = largura, Height = 32, UseVisualStyleBackColor = !principal };
        if (principal)
        {
            b.BackColor = Azul;
            b.ForeColor = Color.White;
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderSize = 0;
        }
        return b;
    }

    public static TextBox Registo() => new()
    {
        Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, BackColor = Color.White, Font = new Font("Consolas", 9f), WordWrap = true,
    };

    public static void Resultado(Label l, string texto, bool? ok)
    {
        l.Text = texto;
        l.ForeColor = ok switch { true => Verde, false => Vermelho, null => Cinzento };
    }

    /// <summary>Rodapé com os botões à direita.</summary>
    public static Panel Rodape(params Button[] botoes)
    {
        var rodape = new Panel { Dock = DockStyle.Bottom, Height = 56, BackColor = Color.White };
        var fluxo = new FlowLayoutPanel { Dock = DockStyle.Right, FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Padding = new Padding(0, 11, 16, 0), WrapContents = false };
        foreach (var b in botoes)
        {
            b.Margin = new Padding(8, 0, 0, 0);
            fluxo.Controls.Add(b);
        }
        rodape.Controls.Add(fluxo);
        return rodape;
    }

    /// <summary>Grava a imagem da janela (verificação visual, sem mexer em nada).</summary>
    public static void Gravar(Form f, string ficheiro)
    {
        Application.DoEvents();
        using var imagem = new Bitmap(f.Width, f.Height);
        f.DrawToBitmap(imagem, new Rectangle(Point.Empty, f.Size));
        imagem.Save(ficheiro, System.Drawing.Imaging.ImageFormat.Png);
    }
}
