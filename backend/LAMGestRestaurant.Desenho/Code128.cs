namespace LAMGestRestaurant.Infrastructure.Impressao;

/// <summary>
/// Código de barras Code 128 (o que os leitores dos POS lêem sem configuração). Números com quantidade par de algarismos
/// usam o conjunto C (mais curto); o resto, o conjunto B (letras, algarismos e sinais ASCII visíveis).
/// </summary>
public static class Code128
{
    // Larguras (barra, espaço, barra…) de cada símbolo 0–105; o 106 é a paragem.
    private static readonly string[] Padroes =
    [
        "212222", "222122", "222221", "121223", "121322", "131222", "122213", "122312", "132212", "221213",
        "221312", "231212", "112232", "122132", "122231", "113222", "123122", "123221", "223211", "221132",
        "221231", "213212", "223112", "312131", "311222", "321122", "321221", "312212", "322112", "322211",
        "212123", "212321", "232121", "111323", "131123", "131321", "112313", "132113", "132311", "211313",
        "231113", "231311", "112133", "112331", "132131", "113123", "113321", "133121", "313121", "211331",
        "231131", "213113", "213311", "213131", "311123", "311321", "331121", "312113", "312311", "332111",
        "314111", "221411", "431111", "111224", "111422", "121124", "121421", "141122", "141221", "112214",
        "112412", "122114", "122411", "142112", "142211", "241211", "221114", "413111", "241112", "134111",
        "111242", "121142", "121241", "114212", "124112", "124211", "411212", "421112", "421211", "212141",
        "214121", "412121", "111143", "111341", "131141", "114113", "114311", "411113", "411311", "113141",
        "114131", "311141", "411131", "211412", "211214", "211232", "2331112",
    ];
    private const int InicioB = 104, InicioC = 105, Paragem = 106;

    internal static IReadOnlyList<string> Tabela => Padroes;

    public static bool Valido(string texto) => texto.Length is > 0 and <= 60 && texto.All(c => c is >= ' ' and <= '~');

    /// <summary>Símbolos (valores 0–106) com o início, os dados, o controlo e a paragem.</summary>
    public static IReadOnlyList<int> Simbolos(string texto)
    {
        if (!Valido(texto))
            throw new ArgumentException("O código de barras só pode ter letras, algarismos e sinais visíveis (até 60).", nameof(texto));
        var simbolos = new List<int>();
        if (texto.Length >= 4 && texto.Length % 2 == 0 && texto.All(char.IsAsciiDigit))
        {
            simbolos.Add(InicioC);
            for (var i = 0; i < texto.Length; i += 2)
                simbolos.Add((texto[i] - '0') * 10 + (texto[i + 1] - '0'));
        }
        else
        {
            simbolos.Add(InicioB);
            simbolos.AddRange(texto.Select(c => c - 32));
        }
        var controlo = simbolos[0];
        for (var i = 1; i < simbolos.Count; i++)
            controlo += simbolos[i] * i;
        simbolos.Add(controlo % 103);
        simbolos.Add(Paragem);
        return simbolos;
    }

    /// <summary>Módulos (true = barra), sem as margens brancas.</summary>
    public static IReadOnlyList<bool> Modulos(string texto)
    {
        var modulos = new List<bool>();
        foreach (var simbolo in Simbolos(texto))
        {
            var barra = true;
            foreach (var largura in Padroes[simbolo])
            {
                for (var i = 0; i < largura - '0'; i++)
                    modulos.Add(barra);
                barra = !barra;
            }
        }
        return modulos;
    }
}
