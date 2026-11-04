using LAMGestRestaurant.Application.Impressao;

namespace LAMGestRestaurant.Tests.Infraestrutura;

/// <summary>Texto de um documento composto (cabeçalho do estabelecimento + linhas; células das tabelas separadas por « | »), para os testes.</summary>
public static class TextoDocumento
{
    public static string De(DocumentoImpressao d) =>
        string.Join('\n', (d.Cabecalho ?? []).Concat(d.Linhas).Select(l =>
            l.Celulas is { } c ? string.Join(" | ", c) : l.Direita is null ? l.Texto : $"{l.Texto} | {l.Direita}"));
}
