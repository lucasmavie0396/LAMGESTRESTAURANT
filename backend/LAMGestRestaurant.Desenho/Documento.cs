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
    bool Cabecalho = false,
    /// <summary>Em A4, começa uma página nova antes desta linha (nos talões não tem efeito).</summary>
    bool QuebraPagina = false,
    /// <summary>Só nos talões de 58 e 80 mm (em A4 não sai): ex.: a linha do provedor do sistema.</summary>
    bool SoTalao = false,
    /// <summary>Faixa no topo de uma grelha: título (<see cref="Texto"/>) e resumo (<see cref="Direita"/>). Em A4 é uma faixa escura; nos talões, negrito sublinhado.</summary>
    bool Faixa = false,
    /// <summary>Linha de tabela a destacar (ex.: abaixo do mínimo): a vermelho em A4; nos talões, negrito com «(!)».</summary>
    bool Alerta = false,
    /// <summary>Linha de tabela com o total da grelha (fundo e traço por cima em A4).</summary>
    bool Subtotal = false,
    /// <summary>Linhas de tabela: quantas células iniciais são texto (à esquerda); as restantes são valores (à direita).</summary>
    int ColunasTexto = 1,
    /// <summary>Quadro de resumo (<see cref="Texto"/> = rótulo, <see cref="Direita"/> = valor). Seguidos ficam lado a lado em A4; nos talões são linhas.</summary>
    bool Cartao = false,
    /// <summary>Título do relatório (<see cref="Direita"/> = período). Em A4 à esquerda com o período à direita; nos talões centrados em duas linhas.</summary>
    bool Titulo = false,
    /// <summary>Total do documento: em A4 num quadro com o valor em destaque.</summary>
    bool Total = false,
    /// <summary>Só em A4 (nos talões não sai): ex.: a legenda das cores.</summary>
    bool SoA4 = false)
{
    public static LinhaImpressao Traco { get; } = new("", Separador: true);

    public static LinhaImpressao Tabela(IReadOnlyList<string> celulas, bool cabecalho = false, bool negrito = false, bool alerta = false,
        bool subtotal = false, int colunasTexto = 1) =>
        new(celulas[0], Celulas: celulas, Negrito: negrito || cabecalho || alerta || subtotal, Cabecalho: cabecalho, Alerta: alerta, Subtotal: subtotal,
            ColunasTexto: Math.Clamp(colunasTexto, 1, celulas.Count));

    public static LinhaImpressao NovaFaixa(string titulo, string? resumo = null) => new(titulo, resumo, Negrito: true, Faixa: true);

    public static LinhaImpressao NovoCartao(string rotulo, string valor, bool alerta = false) => new(rotulo, valor, Negrito: alerta, Cartao: true, Alerta: alerta);
}

/// <summary>
/// Documento independente da impressora: o dispositivo decide a disposição conforme a largura do papel.
/// <see cref="Cabecalho"/>: identificação do estabelecimento (nome, endereço, contactos, NUIT). Em A4 fica à direita do logótipo;
/// nos talões fica centrada por baixo dele. O logótipo carregado nas configurações sai em todos os documentos e papéis.
/// <see cref="Cortar"/>: cortar o papel no fim (comando ESC/POS das térmicas). <see cref="AbrirGaveta"/>: abrir a gaveta do dinheiro ligada à impressora.
/// Sem linhas nem etiquetas só se envia o comando (ex.: abrir a gaveta).
/// </summary>
public sealed record DocumentoImpressao(string Titulo, IReadOnlyList<LinhaImpressao> Linhas, IReadOnlyList<LinhaImpressao>? Cabecalho = null,
    IReadOnlyList<EtiquetaImpressao>? Etiquetas = null, FormatoEtiqueta? Formato = null, bool Cortar = false, bool AbrirGaveta = false)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public bool SoComandos => Linhas.Count == 0 && Etiquetas is null && Cabecalho is null;
}

/// <summary>Tamanho de cada etiqueta, em mm (uma etiqueta por página do rolo).</summary>
public sealed record FormatoEtiqueta(double LarguraMm, double AlturaMm);

/// <summary>Uma etiqueta: linhas de texto (centradas) e, opcionalmente, o código de barras em baixo com o número legível.</summary>
/// <summary>Com <see cref="QrCode"/>, o QR code fica à esquerda e o texto e o código de barras à direita.</summary>
public sealed record EtiquetaImpressao(IReadOnlyList<LinhaImpressao> Linhas, string? CodigoBarras = null, string? QrCode = null);
