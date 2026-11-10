using LAMGestRestaurant.Application.Impressao;

namespace LAMGestRestaurant.Tests.Infraestrutura;

/// <summary>Texto de um documento impresso (cabeçalho, linhas e etiquetas), para as asserções.</summary>
public static class TextoDocumento
{
    public static string De(DocumentoImpressao d) =>
        string.Join('\n', (d.Cabecalho ?? []).Concat(d.Linhas).Concat((d.Etiquetas ?? []).SelectMany(e => e.Linhas)).Select(l =>
            l.Celulas is { } c ? string.Join(" | ", c) : l.Direita is null ? l.Texto : $"{l.Texto} | {l.Direita}"));
}
