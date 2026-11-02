using LAMGestRestaurant.Domain.Vendas;

namespace LAMGestRestaurant.Tests;

public class CalculoPedidoTests
{
    private static Pedido Pedido(params (decimal preco, decimal qtd, decimal taxa)[] linhas) => new()
    {
        Itens = [.. linhas.Select(l => new PedidoItem { PrecoUnitario = l.preco, Quantidade = l.qtd, TaxaImpostoPercentagem = l.taxa })],
    };

    [Theory]
    [InlineData(116, 16, 16)]
    [InlineData(100, 16, 13.79)]
    [InlineData(100, 0, 0)]
    [InlineData(0.01, 16, 0)]
    public void Imposto_e_a_parte_incluida_no_preco(decimal total, decimal taxa, decimal esperado) =>
        Assert.Equal(esperado, CalculoPedido.ImpostoIncluido(total, taxa));

    [Fact]
    public void Totais_somam_itens_e_ignoram_cancelados()
    {
        var p = Pedido((450.50m, 2, 16), (120m, 1, 0), (999m, 1, 16));
        p.Itens[2].Cancelado = true;

        CalculoPedido.Recalcular(p);

        Assert.Equal(1021.00m, p.Subtotal);
        Assert.Equal(124.28m, p.Impostos); // 901 × 16/116
        Assert.Equal(1021.00m, p.Total);
    }

    [Fact]
    public void Desconto_reduz_imposto_proporcionalmente_e_nao_excede_subtotal()
    {
        var p = Pedido((116m, 1, 16));
        p.Desconto = 11.60m; // 10%
        CalculoPedido.Recalcular(p);
        Assert.Equal(104.40m, p.Total);
        Assert.Equal(14.40m, p.Impostos);

        p.Desconto = 500m;
        CalculoPedido.Recalcular(p);
        Assert.Equal(116m, p.Desconto);
        Assert.Equal(0m, p.Total);
    }

    [Fact]
    public void Taxa_de_entrega_soma_ao_total_sem_imposto()
    {
        var p = Pedido((116m, 1, 16));
        p.TaxaEntrega = 50m;
        CalculoPedido.Recalcular(p);
        Assert.Equal(166m, p.Total);
        Assert.Equal(16m, p.Impostos);
    }

    [Fact]
    public void Arredondamento_e_meio_para_cima_sem_erros_de_virgula_flutuante()
    {
        var p = Pedido((0.1m, 3, 0), (0.005m, 1, 0));
        CalculoPedido.Recalcular(p);
        Assert.Equal(0.31m, p.Total); // 0,30 + 0,01 (0,005 arredonda para cima)
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(0.5, true)]
    [InlineData(0.0005, false)]
    [InlineData(0, false)]
    [InlineData(-1, false)]
    [InlineData(1000, false)]
    public void Valida_quantidades(decimal quantidade, bool valida) =>
        Assert.Equal(valida, CalculoPedido.QuantidadeValida(quantidade));
}
