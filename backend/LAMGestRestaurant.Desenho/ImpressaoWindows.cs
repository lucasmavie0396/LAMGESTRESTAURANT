using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Printing;
using System.Runtime.Versioning;
using LAMGestRestaurant.Application.Impressao;

namespace LAMGestRestaurant.Infrastructure.Impressao;

/// <summary>
/// Impressão pelo spooler do Windows (qualquer impressora com driver: térmica de talão 58/80 mm ou A4), sem diálogo,
/// com a disposição da <see cref="Disposicao"/> (igual ao PDF). Usada pelo servidor e pelo agente de impressão dos postos.
/// </summary>
[SupportedOSPlatform("windows")]
public static class ImpressaoWindows
{
    /// <summary>Impressoras instaladas no Windows deste computador (para quem corre o processo).</summary>
    public static IReadOnlyList<string> Instaladas() =>
        [.. PrinterSettings.InstalledPrinters.Cast<string>().Order(StringComparer.OrdinalIgnoreCase)];

    /// <summary>Imprime o documento; com <paramref name="ficheiro"/>, imprime para ficheiro (ex.: «Microsoft Print to PDF», diagnóstico).</summary>
    public static void Imprimir(string nomeWindows, int larguraPapelMm, DocumentoImpressao documento, byte[]? logotipo = null, string? ficheiro = null) =>
        Executar(nomeWindows, larguraPapelMm, documento, logotipo, ficheiro);

    /// <summary>Logótipo legível; uma imagem inválida não impede a impressão do documento.</summary>
    private static Image? CarregarLogotipo(byte[]? dados)
    {
        if (dados is null || dados.Length == 0)
            return null;
        try
        {
            return Image.FromStream(new MemoryStream(dados));
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    /// <summary>Fontes Arial do tamanho do papel e medição em milímetros (tipográfica, sem as folgas do GDI+).</summary>
    private sealed class Fontes : IMedidorTexto, IDisposable
    {
        private readonly Font normal, negrito, grande;
        private readonly Bitmap imagem = new(1, 1);
        private readonly Graphics medida;

        public Fontes(int larguraPapelMm)
        {
            var tamanho = (float)Disposicao.TamanhoFonte(larguraPapelMm);
            normal = new Font("Arial", tamanho);
            negrito = new Font("Arial", tamanho, FontStyle.Bold);
            grande = new Font("Arial", tamanho * 1.35f, FontStyle.Bold);
            medida = Graphics.FromImage(imagem);
            medida.PageUnit = GraphicsUnit.Millimeter;
        }

        public Font Fonte(EstiloTexto e) => e switch { EstiloTexto.Grande => grande, EstiloTexto.Negrito => negrito, _ => normal };
        public double Largura(string texto, EstiloTexto estilo) =>
            texto.Length == 0 ? 0 : medida.MeasureString(texto, Fonte(estilo), PointF.Empty, StringFormat.GenericTypographic).Width;
        public double Altura(EstiloTexto estilo) => Fonte(estilo).GetHeight(medida);

        public void Dispose()
        {
            normal.Dispose(); negrito.Dispose(); grande.Dispose(); medida.Dispose(); imagem.Dispose();
        }
    }

    /// <summary>Largura que as térmicas imprimem quando o driver não a indica: 72 mm no rolo de 80 mm, 48 mm no de 58 mm.</summary>
    public static double ImprimivelPadrao(int larguraPapelMm) => larguraPapelMm <= Disposicao.Largura58 ? 48 : 72;

    private static void Executar(string nomeWindows, int larguraPapelMm, DocumentoImpressao documento, byte[]? logotipo, string? ficheiro)
    {
        // Só comandos (ex.: abrir a gaveta): não há nada para desenhar.
        if (documento.SoComandos)
        {
            if (ficheiro is null)
                EnviarComandos(nomeWindows, documento.Titulo, documento.AbrirGaveta ? AbrirGavetaEscPos : []);
            return;
        }

        var a4 = larguraPapelMm >= Disposicao.LarguraA4;
        using var pd = new PrintDocument { DocumentName = documento.Titulo, PrintController = new StandardPrintController() };
        pd.PrinterSettings.PrinterName = nomeWindows;
        if (!pd.PrinterSettings.IsValid)
            throw new InvalidOperationException($"A impressora «{nomeWindows}» não está disponível neste servidor.");
        if (ficheiro is not null)
        {
            pd.PrinterSettings.PrintToFile = true;
            pd.PrinterSettings.PrintFileName = ficheiro;
        }

        // Talão: desenha-se na área que a impressora imprime de facto (o driver indica-a), a partir do seu início,
        // para o texto ficar encostado à esquerda e nada ser cortado à direita.
        double? imprimivel = null;
        if (!a4 && documento.Etiquetas is null)
        {
            var indicada = pd.DefaultPageSettings.PrintableArea.Width * 0.254;
            imprimivel = indicada >= 30 && indicada <= larguraPapelMm - 2 ? indicada : ImprimivelPadrao(larguraPapelMm);
        }

        using var fontes = new Fontes(larguraPapelMm);
        using var logo = CarregarLogotipo(logotipo);
        var paginas = Disposicao.Dispor(documento, larguraPapelMm, logo is null ? null : (double)logo.Width / logo.Height, fontes, imprimivel);

        static int Centesimos(double mm) => (int)Math.Ceiling(mm / 25.4 * 100);
        if (a4)
        {
            if (pd.PrinterSettings.PaperSizes.Cast<PaperSize>().FirstOrDefault(p => p.Kind == PaperKind.A4) is { } papelA4)
                pd.DefaultPageSettings.PaperSize = papelA4;
        }
        else
            // Rolo: uma única «página» com a largura do papel e a altura do conteúdo.
            pd.DefaultPageSettings.PaperSize = new PaperSize("LAMGest talão", Centesimos(imprimivel is null ? paginas[0].Largura : larguraPapelMm), Centesimos(paginas[0].Altura));
        pd.DefaultPageSettings.Margins = new Margins(0, 0, 0, 0);

        var proxima = 0;
        pd.PrintPage += (_, e) =>
        {
            var g = e.Graphics!;
            g.PageUnit = GraphicsUnit.Millimeter;
            // O GDI desenha a partir da área imprimível. A4 e etiquetas: desloca para que (0, 0) seja o canto do papel, como no PDF;
            // talões: o desenho já foi feito para a área imprimível e começa nela.
            if (imprimivel is null)
                g.TranslateTransform(-e.PageSettings.HardMarginX * 0.254f, -e.PageSettings.HardMarginY * 0.254f);
            Desenhar(g, paginas[proxima], fontes, logo);
            proxima++;
            e.HasMorePages = proxima < paginas.Count;
        };
        pd.Print();

        // Depois do talão: cortar o papel e/ou abrir a gaveta (comandos ESC/POS directos, não passam pelo desenho).
        if (ficheiro is null && (documento.Cortar || documento.AbrirGaveta))
            EnviarComandos(nomeWindows, documento.Titulo + " (comandos)", [.. documento.AbrirGaveta ? AbrirGavetaEscPos : [], .. documento.Cortar ? CortarEscPos : []]);
    }

    /// <summary>ESC p 0 25 250: impulso no pino 2 do conector da gaveta (o padrão das gavetas ligadas à impressora).</summary>
    public static readonly byte[] AbrirGavetaEscPos = [0x1B, 0x70, 0x00, 0x19, 0xFA];

    /// <summary>Avança o papel até à lâmina e corta (GS V 66 n: corte com avanço, aceite pelas térmicas compatíveis Epson).</summary>
    public static readonly byte[] CortarEscPos = [0x1B, 0x64, 0x04, 0x1D, 0x56, 0x42, 0x00];

    /// <summary>Envia bytes tal e qual para a impressora (trabalho RAW no spooler do Windows).</summary>
    public static void EnviarComandos(string nomeWindows, string titulo, byte[] comandos)
    {
        if (comandos.Length == 0)
            return;
        if (!Spooler.OpenPrinter(nomeWindows, out var impressora, IntPtr.Zero))
            throw new InvalidOperationException($"A impressora «{nomeWindows}» não está disponível neste servidor.");
        try
        {
            var doc = new Spooler.DocInfo { NomeDocumento = titulo, Tipo = "RAW" };
            if (Spooler.StartDocPrinter(impressora, 1, doc) == 0)
                throw new InvalidOperationException($"A impressora «{nomeWindows}» recusou o comando.");
            try
            {
                Spooler.StartPagePrinter(impressora);
                var memoria = System.Runtime.InteropServices.Marshal.AllocCoTaskMem(comandos.Length);
                try
                {
                    System.Runtime.InteropServices.Marshal.Copy(comandos, 0, memoria, comandos.Length);
                    if (!Spooler.WritePrinter(impressora, memoria, comandos.Length, out _))
                        throw new InvalidOperationException($"Não foi possível enviar o comando para a impressora «{nomeWindows}».");
                }
                finally
                {
                    System.Runtime.InteropServices.Marshal.FreeCoTaskMem(memoria);
                }
                Spooler.EndPagePrinter(impressora);
            }
            finally
            {
                Spooler.EndDocPrinter(impressora);
            }
        }
        finally
        {
            Spooler.ClosePrinter(impressora);
        }
    }

    private static class Spooler
    {
        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        public sealed class DocInfo
        {
            public string? NomeDocumento;
            public string? Ficheiro;
            public string? Tipo;
        }

        [System.Runtime.InteropServices.DllImport("winspool.drv", EntryPoint = "OpenPrinterW", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        public static extern bool OpenPrinter(string nome, out IntPtr impressora, IntPtr predefinicoes);

        [System.Runtime.InteropServices.DllImport("winspool.drv", SetLastError = true)]
        public static extern bool ClosePrinter(IntPtr impressora);

        [System.Runtime.InteropServices.DllImport("winspool.drv", EntryPoint = "StartDocPrinterW", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        public static extern int StartDocPrinter(IntPtr impressora, int nivel, [System.Runtime.InteropServices.In] DocInfo doc);

        [System.Runtime.InteropServices.DllImport("winspool.drv", SetLastError = true)]
        public static extern bool EndDocPrinter(IntPtr impressora);

        [System.Runtime.InteropServices.DllImport("winspool.drv", SetLastError = true)]
        public static extern bool StartPagePrinter(IntPtr impressora);

        [System.Runtime.InteropServices.DllImport("winspool.drv", SetLastError = true)]
        public static extern bool EndPagePrinter(IntPtr impressora);

        [System.Runtime.InteropServices.DllImport("winspool.drv", SetLastError = true)]
        public static extern bool WritePrinter(IntPtr impressora, IntPtr bytes, int quantos, out int escritos);
    }

    /// <summary>
    /// Primeira página do documento em PNG, à resolução de uma impressora térmica (203 ppp), com o mesmo desenho da impressão:
    /// pré-visualização das etiquetas nas Configurações.
    /// </summary>
    public static byte[] Png(DocumentoImpressao documento, int larguraPapelMm, double pontosPorPolegada = 203)
    {
        using var fontes = new Fontes(larguraPapelMm);
        var pagina = Disposicao.Dispor(documento, larguraPapelMm, null, fontes)[0];
        var escala = pontosPorPolegada / 25.4;
        using var imagem = new Bitmap(Math.Max(1, (int)Math.Ceiling(pagina.Largura * escala)), Math.Max(1, (int)Math.Ceiling(pagina.Altura * escala)));
        imagem.SetResolution((float)pontosPorPolegada, (float)pontosPorPolegada);
        using (var g = Graphics.FromImage(imagem))
        {
            g.Clear(Color.White);
            g.PageUnit = GraphicsUnit.Millimeter;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            Desenhar(g, pagina, fontes, null);
        }
        using var saida = new MemoryStream();
        imagem.Save(saida, System.Drawing.Imaging.ImageFormat.Png);
        return saida.ToArray();
    }

    private static void Desenhar(Graphics g, PaginaDisposta pagina, Fontes fontes, Image? logo)
    {
        using var cinzento = new SolidBrush(Color.FromArgb(90, 90, 90));
        using var fundo = new SolidBrush(Color.FromArgb(232, 236, 241));
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        {
            foreach (var op in pagina.Operacoes)
            {
                switch (op)
                {
                    case OpFundo f:
                        g.FillRectangle(fundo, (float)f.X, (float)f.Y, (float)f.Largura, (float)f.Altura);
                        break;
                    case OpLinha l:
                        using (var caneta = new Pen(l.Clara ? Color.FromArgb(190, 190, 190) : Color.Black, l.Clara ? 0.15f : 0.25f))
                        {
                            if (l.Tracejada) caneta.DashStyle = DashStyle.Dash;
                            g.DrawLine(caneta, (float)l.X1, (float)l.Y, (float)l.X2, (float)l.Y);
                        }
                        break;
                    case OpLogotipo i when logo is not null:
                        g.DrawImage(logo, (float)i.X, (float)i.Y, (float)i.Largura, (float)i.Altura);
                        break;
                    case OpMatriz q:
                        var suave = g.SmoothingMode;
                        g.SmoothingMode = SmoothingMode.None;
                        for (var linha = 0; linha < q.Modulos.Count; linha++)
                            for (var col = 0; col < q.Modulos[linha].Count; col++)
                                if (q.Modulos[linha][col])
                                    g.FillRectangle(Brushes.Black, (float)(q.X + col * q.LadoModulo), (float)(q.Y + linha * q.LadoModulo), (float)q.LadoModulo, (float)q.LadoModulo);
                        g.SmoothingMode = suave;
                        break;
                    case OpBarras b:
                        // Barras sem suavização: cada módulo com as arestas nítidas que o leitor precisa.
                        var antes = g.SmoothingMode;
                        g.SmoothingMode = SmoothingMode.None;
                        for (var i = 0; i < b.Modulos.Count;)
                        {
                            if (!b.Modulos[i]) { i++; continue; }
                            var inicio = i;
                            while (i < b.Modulos.Count && b.Modulos[i]) i++;
                            g.FillRectangle(Brushes.Black, (float)(b.X + inicio * b.LarguraModulo), (float)b.Y, (float)((i - inicio) * b.LarguraModulo), (float)b.Altura);
                        }
                        g.SmoothingMode = antes;
                        break;
                    case OpTexto t:
                        var fonte = fontes.Fonte(t.Estilo);
                        var formato = new StringFormat(StringFormat.GenericTypographic)
                        {
                            Alignment = t.Alinhamento switch { Alinhamento.Centro => StringAlignment.Center, Alinhamento.Direita => StringAlignment.Far, _ => StringAlignment.Near },
                            FormatFlags = StringFormatFlags.NoWrap,
                        };
                        g.DrawString(t.Texto, fonte, t.Cinzento ? cinzento : Brushes.Black,
                            new RectangleF((float)t.X, (float)t.Y, (float)t.Largura, fonte.GetHeight(g) + 0.5f), formato);
                        break;
                }
            }
        }
    }
}
