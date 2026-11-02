namespace LAMGestRestaurant.Domain.Vendas;

/// <summary>
/// Regras de cálculo dos totais do pedido. Os preços de venda incluem impostos;
/// o imposto é a parte incluída: total × taxa / (100 + taxa). Arredondamento a 2 casas, meio para cima.
/// O desconto do pedido reduz proporcionalmente o imposto incluído. A taxa de entrega não leva imposto.
/// </summary>
public static class CalculoPedido
{
    public const decimal QuantidadeMaxima = 999m;

    public static decimal Arredondar(decimal valor) => decimal.Round(valor, 2, MidpointRounding.AwayFromZero);

    public static decimal ImpostoIncluido(decimal total, decimal percentagem) =>
        percentagem <= 0 ? 0m : Arredondar(total * percentagem / (100m + percentagem));

    public static void CalcularItem(PedidoItem item)
    {
        item.Total = Arredondar(item.PrecoUnitario * item.Quantidade) - item.Desconto;
        item.ValorImposto = ImpostoIncluido(item.Total, item.TaxaImpostoPercentagem);
    }

    public static void Recalcular(Pedido pedido)
    {
        var activos = pedido.Itens.Where(i => !i.Cancelado).ToList();
        foreach (var item in activos)
            CalcularItem(item);

        pedido.Subtotal = activos.Sum(i => i.Total);
        if (pedido.Desconto > pedido.Subtotal)
            pedido.Desconto = pedido.Subtotal;
        var factor = pedido.Subtotal == 0 ? 0m : (pedido.Subtotal - pedido.Desconto) / pedido.Subtotal;
        pedido.Impostos = Arredondar(activos.Sum(i => i.ValorImposto) * factor);
        pedido.Total = pedido.Subtotal - pedido.Desconto + pedido.TaxaEntrega;
    }

    public static decimal EmDivida(Pedido pedido) => Math.Max(0m, pedido.Total - pedido.TotalPago);

    /// <summary>Quantidade válida: positiva, até 999 e no máximo 3 casas decimais.</summary>
    public static bool QuantidadeValida(decimal quantidade) =>
        quantidade > 0 && quantidade <= QuantidadeMaxima && decimal.Round(quantidade, 3) == quantidade;
}
