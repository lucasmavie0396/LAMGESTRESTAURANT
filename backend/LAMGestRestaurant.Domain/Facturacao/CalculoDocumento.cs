using LAMGestRestaurant.Domain.Vendas;

namespace LAMGestRestaurant.Domain.Facturacao;

/// <summary>
/// Totais dos documentos, com as mesmas regras dos pedidos: preços com IVA incluído, IVA = total × taxa / (100 + taxa),
/// arredondamento a 2 casas. O documento é coerente consigo próprio: os totais são sempre a soma das linhas.
/// </summary>
public static class CalculoDocumento
{
    public static void CalcularLinha(DocumentoLinha linha)
    {
        linha.Total = CalculoPedido.Arredondar(linha.PrecoUnitario * linha.Quantidade) - linha.Desconto;
        linha.Iva = CalculoPedido.ImpostoIncluido(linha.Total, linha.PercentagemIva);
        linha.Base = linha.Total - linha.Iva;
    }

    /// <summary>Totais do documento a partir das linhas já calculadas.</summary>
    public static void Totalizar(Documento documento)
    {
        documento.Subtotal = documento.Linhas.Sum(l => l.Total + l.Desconto);
        documento.Desconto = documento.Linhas.Sum(l => l.Desconto);
        documento.Total = documento.Linhas.Sum(l => l.Total);
        documento.Iva = documento.Linhas.Sum(l => l.Iva);
        documento.BaseTributavel = documento.Total - documento.Iva;
    }

    /// <summary>
    /// Reparte um desconto global pelas linhas, em proporção do total de cada uma (a última fica com o arredondamento),
    /// para que o IVA de cada linha seja o do valor efectivamente pago.
    /// </summary>
    public static decimal[] Repartir(decimal desconto, IReadOnlyList<decimal> totais)
    {
        var partes = new decimal[totais.Count];
        var soma = totais.Sum();
        if (desconto <= 0 || soma <= 0)
            return partes;
        var restante = desconto;
        for (var i = 0; i < totais.Count; i++)
        {
            partes[i] = i == totais.Count - 1 ? restante : Math.Min(restante, CalculoPedido.Arredondar(desconto * totais[i] / soma));
            restante -= partes[i];
        }
        return partes;
    }

    /// <summary>Resumo do IVA por taxa (base, IVA e total), como exigido na factura.</summary>
    public static IReadOnlyList<(decimal Percentagem, decimal Base, decimal Iva, decimal Total)> ResumoIva(IEnumerable<DocumentoLinha> linhas) =>
        [.. linhas.GroupBy(l => l.PercentagemIva).OrderByDescending(g => g.Key)
            .Select(g => (g.Key, g.Sum(l => l.Base), g.Sum(l => l.Iva), g.Sum(l => l.Total)))];
}
