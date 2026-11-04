namespace LAMGestRestaurant.Application.Impressao;

public enum Alinhamento { Esquerda, Centro, Direita }

/// <summary>
/// Linha de um documento a imprimir. <see cref="Direita"/> é texto alinhado à direita na mesma linha (ex.: valores).
/// Com <see cref="Celulas"/> é uma linha de tabela: a 1.ª célula é a descrição (à esquerda) e as restantes são valores (à direita).
/// Em A4 as células ficam em colunas; nos talões a descrição ocupa uma linha e os valores vão em colunas por baixo.
/// Linhas de tabela seguidas formam uma tabela (as colunas alinham entre si). <see cref="Cabecalho"/> marca os títulos das colunas.
/// </summary>
public sealed record LinhaImpressao(
    string Texto,
    string? Direita = null,
    Alinhamento Alinhamento = Alinhamento.Esquerda,
    bool Negrito = false,
    bool Grande = false,
    bool Separador = false,
    IReadOnlyList<string>? Celulas = null,
    bool Cabecalho = false)
{
    public static LinhaImpressao Traco { get; } = new("", Separador: true);

    public static LinhaImpressao Tabela(IReadOnlyList<string> celulas, bool cabecalho = false, bool negrito = false) =>
        new(celulas[0], Celulas: celulas, Negrito: negrito || cabecalho, Cabecalho: cabecalho);
}

/// <summary>
/// Documento independente da impressora: o dispositivo decide a disposição conforme a largura do papel.
/// <see cref="Cabecalho"/>: identificação do estabelecimento (nome, endereço, contactos, NUIT). Em A4 fica à direita do logótipo;
/// nos talões fica centrada por baixo dele. O logótipo carregado nas configurações sai em todos os documentos e papéis.
/// </summary>
public sealed record DocumentoImpressao(string Titulo, IReadOnlyList<LinhaImpressao> Linhas, IReadOnlyList<LinhaImpressao>? Cabecalho = null);
