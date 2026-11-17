using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace LAMGestRestaurant.Agente;

/// <summary>Peso lido de uma resposta da balança: kg (null sem peso), estável (null = a balança não diz) e erro.</summary>
public sealed record PesoLido(decimal? Peso, bool? Estavel, string? Erro);

/// <summary>
/// Respostas das balanças por porta série, sem depender da marca: o número com a unidade (kg ou g; sem unidade e sem
/// casas decimais = gramas, como nas Toledo/Prix), os indicadores de estabilidade (ST/US, M de movimento, «I» das Prix)
/// e os avisos de sobrecarga e peso negativo.
/// </summary>
public static partial class ProtocoloBalanca
{
    public const byte Enq = 0x05;
    public const byte Stx = 0x02;
    public const byte Etx = 0x03;

    /// <summary>O que o agente envia para pedir o peso (nada no modo contínuo).</summary>
    public static byte[]? Pedido(string modo) => modo switch
    {
        "PedidoEnq" => [Enq],
        "PedidoW" => "W\r"u8.ToArray(),
        _ => null,
    };

    /// <summary>Separa as tramas completas (terminadas em CR, LF ou ETX); o resto fica em <paramref name="resto"/> à espera do fim.</summary>
    public static IReadOnlyList<string> Tramas(string recebido, out string resto)
    {
        var tramas = new List<string>();
        var inicio = 0;
        for (var i = 0; i < recebido.Length; i++)
        {
            if (recebido[i] is not ('\r' or '\n' or (char)Etx))
                continue;
            var trama = Limpar(recebido[inicio..i]);
            if (trama.Length > 0)
                tramas.Add(trama);
            inicio = i + 1;
        }
        resto = recebido[inicio..];
        return tramas;
    }

    /// <summary>Tira os caracteres de controlo (STX, ACK, etc.) e os espaços das pontas.</summary>
    public static string Limpar(string texto)
    {
        var s = new StringBuilder(texto.Length);
        foreach (var c in texto)
            if (c >= ' ' && c < 127)
                s.Append(c);
        return s.ToString().Trim();
    }

    public static PesoLido? Interpretar(string trama)
    {
        var t = Limpar(trama).ToUpperInvariant();
        if (t.Length == 0)
            return null;
        if (t.Contains("OL") || t.Contains("OVER") || Regex.IsMatch(t, "^S{3,}$") || t.Contains("HHHH"))
            return new PesoLido(null, false, "Peso acima do máximo da balança.");
        if (Regex.IsMatch(t, "^N{3,}$") || t.Contains("LLLL"))
            return new PesoLido(null, false, "Peso negativo: verifique a tara da balança.");
        if (Regex.IsMatch(t, "^I{3,}$"))
            return new PesoLido(null, false, null); // Toledo/Prix: peso instável
        bool? estavel = Regex.IsMatch(t, @"\b(US|MOTION)\b|\bM\b|\?") ? false : Regex.IsMatch(t, @"\bST\b") ? true : null;

        var numeros = Numero().Matches(t);
        if (numeros.Count == 0)
            return null;
        // Com unidade, conta o número que a tem (há balanças que também enviam a tara ou o preço); sem unidade, o último.
        var m = numeros.LastOrDefault(x => x.Groups["u"].Success) ?? numeros[^1];
        var texto = m.Groups["n"].Value.Replace(',', '.');
        if (!decimal.TryParse(texto, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var valor))
            return null;
        if (m.Groups["s"].Value == "-")
            return new PesoLido(null, false, "Peso negativo: verifique a tara da balança.");
        var unidade = m.Groups["u"].Success ? m.Groups["u"].Value : null;
        if (unidade is "LB" or "OZ")
            return new PesoLido(null, false, "A balança está em libras: mude-a para quilogramas.");
        var kg = unidade switch
        {
            "KG" => valor,
            "G" => valor / 1000m,
            _ => texto.Contains('.') ? valor : valor / 1000m,
        };
        return new PesoLido(decimal.Round(kg, 3, MidpointRounding.AwayFromZero), estavel, null);
    }

    [GeneratedRegex(@"(?<s>[-+]?)\s*(?<n>\d+(?:[.,]\d+)?)(?![\d.,])\s*(?:(?<u>KG|G|LB|OZ)(?![A-Z]))?")]
    private static partial Regex Numero();
}

/// <summary>
/// Estabilidade do peso: com o indicador da balança, conta o dela (instável nunca passa); sem indicador, o peso só
/// é estável depois de <see cref="Leituras"/> leituras iguais seguidas.
/// </summary>
public sealed class Estabilizador
{
    public const int Leituras = 3;
    private readonly Queue<decimal> ultimas = new();

    public bool Estavel(PesoLido p)
    {
        if (p.Peso is not decimal peso || p.Estavel == false)
        {
            ultimas.Clear();
            return false;
        }
        ultimas.Enqueue(peso);
        while (ultimas.Count > Leituras)
            ultimas.Dequeue();
        return p.Estavel == true || (ultimas.Count == Leituras && ultimas.All(x => x == peso));
    }

    public void Limpar() => ultimas.Clear();
}
