using LAMGestRestaurant.Application.Impressao;
using LAMGestRestaurant.Domain.Sistema;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;

namespace LAMGestRestaurant.Infrastructure.Impressao;

/// <summary>
/// PDF com a mesma disposição da impressão directa: talão de 58/80 mm numa única página com a altura do conteúdo,
/// ou A4 com várias páginas. Unidades em pontos (1 mm = 72/25,4 pt).
/// </summary>
public sealed class GeradorPdf : IGeradorPdf
{
    private const double Mm = 72 / 25.4;
    private sealed record Bloco(LinhaImpressao Linha, XFont Fonte, IReadOnlyList<string> Texto, bool DireitaAbaixo, double Altura);

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

    public byte[] Gerar(int larguraPapelMm, DocumentoImpressao documento, byte[]? logotipo = null)
    {
        var a4 = larguraPapelMm >= Impressora.LarguraA4;
        var tamanho = a4 ? 10 : larguraPapelMm <= Impressora.Largura58 ? 7.5 : 9;
        var normal = new XFont("Arial", tamanho, XFontStyleEx.Regular);
        var negrito = new XFont("Arial", tamanho, XFontStyleEx.Bold);
        var grande = new XFont("Arial", tamanho * 1.35, XFontStyleEx.Bold);

        var margem = (a4 ? 15 : 2) * Mm;
        var larguraPagina = (a4 ? 210 : larguraPapelMm) * Mm;
        var larguraUtil = larguraPagina - 2 * margem;

        List<Bloco> blocos;
        using (var medida = XGraphics.CreateMeasureContext(new XSize(larguraPagina, 1000), XGraphicsUnit.Point, XPageDirection.Downwards))
            blocos = Dispor(medida, documento, larguraUtil, normal, negrito, grande);

        var logo = CarregarLogotipo(logotipo);
        XSize tamanhoLogo = default;
        if (logo is not null)
        {
            // Até 3/4 da largura do talão (máx. 23 mm de altura) ou 58 × 28 mm em A4, sem deformar.
            double maxLargura = a4 ? 58 * Mm : larguraUtil * 0.75, maxAltura = (a4 ? 28 : 23) * Mm;
            var escala = Math.Min(maxLargura / logo.PointWidth, maxAltura / logo.PointHeight);
            tamanhoLogo = new XSize(logo.PointWidth * escala, logo.PointHeight * escala);
        }
        var alturaLogo = logo is null ? 0 : tamanhoLogo.Height + 3 * Mm;
        var alturaPagina = a4 ? 297 * Mm : alturaLogo + blocos.Sum(b => b.Altura) + 2 * margem + 4 * Mm;

        using var pdf = new PdfDocument();
        pdf.Info.Title = documento.Titulo;
        pdf.Info.Creator = "LAMGest Restaurant";
        var proximo = 0;
        do
        {
            var pagina = pdf.AddPage();
            pagina.Width = XUnit.FromPoint(larguraPagina);
            pagina.Height = XUnit.FromPoint(alturaPagina);
            using var g = XGraphics.FromPdfPage(pagina);
            var y = margem;
            if (logo is not null && pdf.PageCount == 1)
            {
                g.DrawImage(logo, margem + (larguraUtil - tamanhoLogo.Width) / 2, y, tamanhoLogo.Width, tamanhoLogo.Height);
                y += alturaLogo;
            }
            while (proximo < blocos.Count)
            {
                var b = blocos[proximo];
                if (y + b.Altura > alturaPagina - margem && y > margem)
                    break; // continua na página seguinte (A4)
                Desenhar(g, b, margem, y, larguraUtil);
                y += b.Altura;
                proximo++;
            }
        } while (proximo < blocos.Count);
        logo?.Dispose();

        using var saida = new MemoryStream();
        pdf.Save(saida, false);
        return saida.ToArray();
    }

    /// <summary>Logótipo legível; uma imagem inválida não impede o documento.</summary>
    private static XImage? CarregarLogotipo(byte[]? dados)
    {
        if (dados is null)
            return null;
        try
        {
            return XImage.FromStream(new MemoryStream(dados));
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or NotSupportedException or InvalidDataException)
        {
            return null;
        }
    }

    private static List<Bloco> Dispor(XGraphics g, DocumentoImpressao documento, double largura, XFont normal, XFont negrito, XFont grande)
    {
        var blocos = new List<Bloco>();
        foreach (var linha in documento.Linhas)
        {
            if (linha.Separador)
            {
                blocos.Add(new Bloco(linha, normal, [], false, normal.GetHeight() * 0.9));
                continue;
            }
            var fonte = linha.Grande ? grande : linha.Negrito ? negrito : normal;
            var alturaLinha = fonte.GetHeight();
            var larguraDireita = linha.Direita is null ? 0 : g.MeasureString(linha.Direita, fonte).Width + 2 * Mm;
            // Valor demasiado largo para a mesma linha (papel de 58 mm): passa para a linha de baixo, alinhado à direita.
            var abaixo = larguraDireita > largura * 0.55;
            var texto = Quebrar(g, linha.Texto, fonte, Math.Max(largura - (abaixo ? 0 : larguraDireita), 10));
            blocos.Add(new Bloco(linha, fonte, texto, abaixo, alturaLinha * (texto.Count + (abaixo ? 1 : 0)) + 0.6));
        }
        return blocos;
    }

    /// <summary>Quebra por palavras (mantém os espaços iniciais, usados nos recuos «   Ref.»).</summary>
    private static List<string> Quebrar(XGraphics g, string texto, XFont fonte, double largura)
    {
        if (texto.Length == 0)
            return [""];
        var linhas = new List<string>();
        var recuo = texto[..(texto.Length - texto.TrimStart().Length)];
        var actual = recuo;
        foreach (var palavra in texto.TrimStart().Split(' '))
        {
            var tentativa = actual.Trim().Length == 0 ? recuo + palavra : actual + " " + palavra;
            if (g.MeasureString(tentativa, fonte).Width <= largura || actual.Trim().Length == 0)
                actual = tentativa;
            else
            {
                linhas.Add(actual);
                actual = recuo + palavra;
            }
        }
        linhas.Add(actual);
        return linhas;
    }

    private static void Desenhar(XGraphics g, Bloco b, double x, double y, double largura)
    {
        if (b.Linha.Separador)
        {
            var caneta = new XPen(XColors.Black, 0.5) { DashStyle = XDashStyle.Dash };
            g.DrawLine(caneta, x, y + b.Altura / 2, x + largura, y + b.Altura / 2);
            return;
        }
        var alturaLinha = b.Fonte.GetHeight();
        var formato = b.Linha.Alinhamento switch
        {
            Alinhamento.Centro => XStringFormats.TopCenter,
            Alinhamento.Direita => XStringFormats.TopRight,
            _ => XStringFormats.TopLeft,
        };
        var larguraTexto = largura - (b.DireitaAbaixo || b.Linha.Direita is null ? 0 : g.MeasureString(b.Linha.Direita, b.Fonte).Width + 2 * Mm);
        for (var i = 0; i < b.Texto.Count; i++)
            g.DrawString(b.Texto[i], b.Fonte, XBrushes.Black, new XRect(x, y + i * alturaLinha, larguraTexto, alturaLinha), formato);
        if (b.Linha.Direita is not null)
        {
            var yDireita = b.DireitaAbaixo ? y + b.Texto.Count * alturaLinha : y;
            g.DrawString(b.Linha.Direita, b.Fonte, XBrushes.Black, new XRect(x, yDireita, largura, alturaLinha), XStringFormats.TopRight);
        }
    }
}
