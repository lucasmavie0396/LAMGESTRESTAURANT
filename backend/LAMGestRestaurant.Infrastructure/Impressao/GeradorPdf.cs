using LAMGestRestaurant.Application.Impressao;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;

namespace LAMGestRestaurant.Infrastructure.Impressao;

/// <summary>
/// PDF com a mesma disposição da impressão directa (<see cref="Disposicao"/>): talão de 58/80 mm numa única página
/// com a altura do conteúdo, ou A4 com várias páginas. A disposição é em mm; o PDF em pontos (1 mm = 72/25,4 pt).
/// </summary>
public sealed class GeradorPdf : IGeradorPdf
{
    private const double Mm = 72 / 25.4;

    static GeradorPdf()
    {
        GlobalFontSettings.FontResolver ??= new FontesWindows();
    }

    /// <summary>Arial (normal e negrito) da pasta de fontes do Windows do servidor; outras famílias usam a Arial.</summary>
    private sealed class FontesWindows : IFontResolver
    {
        private static readonly string Pasta = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);

        public FontResolverInfo ResolveTypeface(string familyName, bool bold, bool italic) =>
            new(bold ? "arialbd" : "arial");

        public byte[]? GetFont(string faceName) =>
            File.Exists(Path.Combine(Pasta, faceName + ".ttf")) ? File.ReadAllBytes(Path.Combine(Pasta, faceName + ".ttf")) : null;
    }

    private sealed class Fontes(int larguraPapelMm) : IMedidorTexto, IDisposable
    {
        private readonly XFont normal = new("Arial", Disposicao.TamanhoFonte(larguraPapelMm), XFontStyleEx.Regular);
        private readonly XFont negrito = new("Arial", Disposicao.TamanhoFonte(larguraPapelMm), XFontStyleEx.Bold);
        private readonly XFont grande = new("Arial", Disposicao.TamanhoFonte(larguraPapelMm) * 1.35, XFontStyleEx.Bold);
        private readonly XGraphics medida = XGraphics.CreateMeasureContext(new XSize(1000, 1000), XGraphicsUnit.Point, XPageDirection.Downwards);

        public XFont Fonte(EstiloTexto e) => e switch { EstiloTexto.Grande => grande, EstiloTexto.Negrito => negrito, _ => normal };
        public double Largura(string texto, EstiloTexto estilo) => texto.Length == 0 ? 0 : medida.MeasureString(texto, Fonte(estilo)).Width / Mm;
        public double Altura(EstiloTexto estilo) => Fonte(estilo).GetHeight() / Mm;
        public void Dispose() => medida.Dispose();
    }

    public byte[] Gerar(int larguraPapelMm, DocumentoImpressao documento, byte[]? logotipo = null)
    {
        using var fontes = new Fontes(larguraPapelMm);
        using var logo = CarregarLogotipo(logotipo);
        var paginas = Disposicao.Dispor(documento, larguraPapelMm, logo is null ? null : logo.PointWidth / logo.PointHeight, fontes);

        using var pdf = new PdfDocument();
        pdf.Info.Title = documento.Titulo;
        pdf.Info.Creator = "LAMGest Restaurant";
        var cinzento = new XSolidBrush(XColor.FromArgb(90, 90, 90));
        var fundo = new XSolidBrush(XColor.FromArgb(232, 236, 241));
        foreach (var p in paginas)
        {
            var pagina = pdf.AddPage();
            pagina.Width = XUnit.FromPoint(p.Largura * Mm);
            pagina.Height = XUnit.FromPoint(p.Altura * Mm);
            using var g = XGraphics.FromPdfPage(pagina);
            foreach (var op in p.Operacoes)
            {
                switch (op)
                {
                    case OpFundo f:
                        g.DrawRectangle(fundo, f.X * Mm, f.Y * Mm, f.Largura * Mm, f.Altura * Mm);
                        break;
                    case OpLinha l:
                        var caneta = new XPen(l.Clara ? XColor.FromArgb(190, 190, 190) : XColors.Black, l.Clara ? 0.4 : 0.6);
                        if (l.Tracejada) caneta.DashStyle = XDashStyle.Dash;
                        g.DrawLine(caneta, l.X1 * Mm, l.Y * Mm, l.X2 * Mm, l.Y * Mm);
                        break;
                    case OpLogotipo i when logo is not null:
                        g.DrawImage(logo, i.X * Mm, i.Y * Mm, i.Largura * Mm, i.Altura * Mm);
                        break;
                    case OpMatriz q:
                        for (var linha = 0; linha < q.Modulos.Count; linha++)
                            for (var col = 0; col < q.Modulos[linha].Count; col++)
                                if (q.Modulos[linha][col])
                                    g.DrawRectangle(XBrushes.Black, (q.X + col * q.LadoModulo) * Mm, (q.Y + linha * q.LadoModulo) * Mm, q.LadoModulo * Mm, q.LadoModulo * Mm);
                        break;
                    case OpBarras b:
                        for (var k = 0; k < b.Modulos.Count;)
                        {
                            if (!b.Modulos[k]) { k++; continue; }
                            var inicio = k;
                            while (k < b.Modulos.Count && b.Modulos[k]) k++;
                            g.DrawRectangle(XBrushes.Black, (b.X + inicio * b.LarguraModulo) * Mm, b.Y * Mm, (k - inicio) * b.LarguraModulo * Mm, b.Altura * Mm);
                        }
                        break;
                    case OpTexto t:
                        var fonte = fontes.Fonte(t.Estilo);
                        var formato = t.Alinhamento switch
                        {
                            Alinhamento.Centro => XStringFormats.TopCenter,
                            Alinhamento.Direita => XStringFormats.TopRight,
                            _ => XStringFormats.TopLeft,
                        };
                        g.DrawString(t.Texto, fonte, t.Cinzento ? cinzento : XBrushes.Black,
                            new XRect(t.X * Mm, t.Y * Mm, t.Largura * Mm, fonte.GetHeight()), formato);
                        break;
                }
            }
        }

        using var saida = new MemoryStream();
        pdf.Save(saida, false);
        return saida.ToArray();
    }

    /// <summary>Logótipo legível; uma imagem inválida ou num formato não suportado nunca impede o documento.</summary>
    private static XImage? CarregarLogotipo(byte[]? dados)
    {
        if (dados is null || dados.Length == 0)
            return null;
        try
        {
            // O PDFsharp lê o buffer interno do MemoryStream: tem de ser criado com publiclyVisible = true.
            return XImage.FromStream(new MemoryStream(dados, 0, dados.Length, writable: false, publiclyVisible: true));
        }
        catch (Exception)
        {
            return null; // o documento sai sem logótipo
        }
    }
}
