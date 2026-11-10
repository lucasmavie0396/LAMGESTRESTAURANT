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

    private static void Executar(string nomeWindows, int larguraPapelMm, DocumentoImpressao documento, byte[]? logotipo, string? ficheiro)
    {
        using var fontes = new Fontes(larguraPapelMm);
        using var logo = CarregarLogotipo(logotipo);
        var paginas = Disposicao.Dispor(documento, larguraPapelMm, logo is null ? null : (double)logo.Width / logo.Height, fontes);
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
        static int Centesimos(double mm) => (int)Math.Ceiling(mm / 25.4 * 100);
        if (a4)
        {
            if (pd.PrinterSettings.PaperSizes.Cast<PaperSize>().FirstOrDefault(p => p.Kind == PaperKind.A4) is { } papelA4)
                pd.DefaultPageSettings.PaperSize = papelA4;
        }
        else
            // Rolo: uma única «página» com a altura do conteúdo (o driver corta no fim).
            pd.DefaultPageSettings.PaperSize = new PaperSize("LAMGest talão", Centesimos(paginas[0].Largura), Centesimos(paginas[0].Altura));
        pd.DefaultPageSettings.Margins = new Margins(0, 0, 0, 0);

        var proxima = 0;
        pd.PrintPage += (_, e) =>
        {
            var g = e.Graphics!;
            g.PageUnit = GraphicsUnit.Millimeter;
            // O GDI desenha a partir da área imprimível: desloca para que (0, 0) seja o canto do papel, como no PDF.
            g.TranslateTransform(-e.PageSettings.HardMarginX * 0.254f, -e.PageSettings.HardMarginY * 0.254f);
            Desenhar(g, paginas[proxima], fontes, logo);
            proxima++;
            e.HasMorePages = proxima < paginas.Count;
        };
        pd.Print();
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
