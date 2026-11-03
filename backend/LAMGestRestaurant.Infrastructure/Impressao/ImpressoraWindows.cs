using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Printing;
using System.Runtime.Versioning;
using LAMGestRestaurant.Application.Impressao;
using LAMGestRestaurant.Domain.Sistema;

namespace LAMGestRestaurant.Infrastructure.Impressao;

/// <summary>
/// Impressão pelo spooler do Windows (qualquer impressora com driver: térmica de talão 58/80 mm ou A4), sem diálogo.
/// As impressoras têm de estar instaladas no servidor para todos os utilizadores (locais ou partilhadas de outro computador).
/// Unidades em centésimos de polegada (GraphicsUnit.Display), as mesmas do PrintDocument.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class ImpressoraWindows : IDispositivoImpressao
{
    private sealed record Bloco(LinhaImpressao Linha, Font Fonte, float AlturaTexto, float LarguraDireita, bool DireitaAbaixo, float Altura);

    public IReadOnlyList<string> Instaladas() =>
        [.. PrinterSettings.InstalledPrinters.Cast<string>().Order(StringComparer.OrdinalIgnoreCase)];

    public void Imprimir(string nomeWindows, int larguraPapelMm, DocumentoImpressao documento) =>
        Executar(nomeWindows, larguraPapelMm, documento, null);

    /// <summary>Diagnóstico: imprime para ficheiro (ex.: «Microsoft Print to PDF» gera o PDF sem diálogo).</summary>
    public void ImprimirParaFicheiro(string nomeWindows, int larguraPapelMm, DocumentoImpressao documento, string ficheiro) =>
        Executar(nomeWindows, larguraPapelMm, documento, ficheiro);

    private static void Executar(string nomeWindows, int larguraPapelMm, DocumentoImpressao documento, string? ficheiro)
    {
        var a4 = larguraPapelMm >= Impressora.LarguraA4;
        var tamanho = a4 ? 10f : larguraPapelMm <= Impressora.Largura58 ? 7.5f : 9f;
        using var normal = new Font("Arial", tamanho);
        using var negrito = new Font("Arial", tamanho, FontStyle.Bold);
        using var grande = new Font("Arial", tamanho * 1.35f, FontStyle.Bold);

        // Talão: 2 mm de margem; A4: 15 mm.
        float margem = a4 ? 60 : 8;
        float larguraPapel = a4 ? 827 : larguraPapelMm / 25.4f * 100;
        var larguraUtil = larguraPapel - 2 * margem;
        var blocos = Dispor(documento, larguraUtil, normal, negrito, grande);

        using var pd = new PrintDocument { DocumentName = documento.Titulo, PrintController = new StandardPrintController() };
        pd.PrinterSettings.PrinterName = nomeWindows;
        if (!pd.PrinterSettings.IsValid)
            throw new InvalidOperationException($"A impressora «{nomeWindows}» não está disponível neste servidor.");
        if (ficheiro is not null)
        {
            pd.PrinterSettings.PrintToFile = true;
            pd.PrinterSettings.PrintFileName = ficheiro;
        }
        if (a4)
        {
            if (pd.PrinterSettings.PaperSizes.Cast<PaperSize>().FirstOrDefault(p => p.Kind == PaperKind.A4) is { } papelA4)
                pd.DefaultPageSettings.PaperSize = papelA4;
        }
        else
        {
            // Rolo: uma única «página» com a altura do conteúdo (o driver corta no fim).
            var altura = blocos.Sum(b => b.Altura) + 2 * margem + 40;
            pd.DefaultPageSettings.PaperSize = new PaperSize("LAMGest talão", (int)Math.Ceiling(larguraPapel), (int)Math.Ceiling(altura));
        }
        pd.DefaultPageSettings.Margins = new Margins((int)margem, (int)margem, (int)margem, (int)margem);

        var proximo = 0;
        pd.PrintPage += (_, e) =>
        {
            var g = e.Graphics!;
            g.PageUnit = GraphicsUnit.Display;
            float x = e.MarginBounds.Left, y = e.MarginBounds.Top;
            while (proximo < blocos.Count)
            {
                var b = blocos[proximo];
                if (y + b.Altura > e.MarginBounds.Bottom && y > e.MarginBounds.Top)
                    break; // continua na página seguinte (A4)
                Desenhar(g, b, x, y, larguraUtil, normal);
                y += b.Altura;
                proximo++;
            }
            e.HasMorePages = proximo < blocos.Count;
        };
        pd.Print();
    }

    private static List<Bloco> Dispor(DocumentoImpressao documento, float largura, Font normal, Font negrito, Font grande)
    {
        using var imagem = new Bitmap(1, 1);
        using var g = Graphics.FromImage(imagem);
        g.PageUnit = GraphicsUnit.Display;
        var blocos = new List<Bloco>();
        foreach (var linha in documento.Linhas)
        {
            if (linha.Separador)
            {
                blocos.Add(new Bloco(linha, normal, 0, 0, false, normal.GetHeight(g) * 0.9f));
                continue;
            }
            var fonte = linha.Grande ? grande : linha.Negrito ? negrito : normal;
            var alturaLinha = fonte.GetHeight(g);
            var larguraDireita = linha.Direita is null ? 0 : g.MeasureString(linha.Direita, fonte).Width + 6;
            // Valor demasiado largo para a mesma linha (papel de 58 mm): passa para a linha de baixo, alinhado à direita.
            var abaixo = larguraDireita > largura * 0.55f;
            var areaTexto = largura - (abaixo ? 0 : larguraDireita);
            var texto = g.MeasureString(linha.Texto.Length == 0 ? " " : linha.Texto, fonte, (int)Math.Max(areaTexto, 10), Formato(linha.Alinhamento));
            var alturaTexto = Math.Max(texto.Height, alturaLinha);
            blocos.Add(new Bloco(linha, fonte, alturaTexto, larguraDireita, abaixo, alturaTexto + (abaixo ? alturaLinha : 0) + 2));
        }
        return blocos;
    }

    private static void Desenhar(Graphics g, Bloco b, float x, float y, float largura, Font normal)
    {
        if (b.Linha.Separador)
        {
            using var caneta = new Pen(Color.Black, 1) { DashStyle = DashStyle.Dash };
            g.DrawLine(caneta, x, y + b.Altura / 2, x + largura, y + b.Altura / 2);
            return;
        }
        var areaTexto = new RectangleF(x, y, largura - (b.DireitaAbaixo ? 0 : b.LarguraDireita), b.AlturaTexto);
        g.DrawString(b.Linha.Texto, b.Fonte, Brushes.Black, areaTexto, Formato(b.Linha.Alinhamento));
        if (b.Linha.Direita is not null)
        {
            var alturaLinha = b.Fonte.GetHeight(g);
            var areaDireita = b.DireitaAbaixo
                ? new RectangleF(x, y + b.AlturaTexto, largura, alturaLinha)
                : new RectangleF(x, y, largura, alturaLinha);
            g.DrawString(b.Linha.Direita, b.Fonte, Brushes.Black, areaDireita, Formato(Alinhamento.Direita));
        }
    }

    private static readonly StringFormat Esquerda = new() { Alignment = StringAlignment.Near, Trimming = StringTrimming.Word };
    private static readonly StringFormat Centro = new() { Alignment = StringAlignment.Center, Trimming = StringTrimming.Word };
    private static readonly StringFormat Direita = new() { Alignment = StringAlignment.Far, Trimming = StringTrimming.Word };

    private static StringFormat Formato(Alinhamento a) => a switch
    {
        Alinhamento.Centro => Centro,
        Alinhamento.Direita => Direita,
        _ => Esquerda,
    };
}

/// <summary>Fora do Windows não há impressoras (a aplicação foi desenhada para servidor Windows).</summary>
public sealed class SemImpressoras : IDispositivoImpressao
{
    public IReadOnlyList<string> Instaladas() => [];

    public void Imprimir(string nomeWindows, int larguraPapelMm, DocumentoImpressao documento) =>
        throw new PlatformNotSupportedException("A impressão directa só está disponível num servidor Windows.");
}
