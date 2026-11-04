using System.Net;
using LAMGestRestaurant.Application.Vendas;
using LAMGestRestaurant.Domain.Comum;
using LAMGestRestaurant.Domain.Stock;
using LAMGestRestaurant.Tests.Infraestrutura;
using Microsoft.EntityFrameworkCore;

namespace LAMGestRestaurant.Tests;

/// <summary>Baixa de stock pela ficha técnica no fecho do pedido (artigos e receitas criados directamente na BD).</summary>
[Collection(ColeccaoBaseDados.Nome)]
public class StockVendaTests(AplicacaoTeste app)
{
    /// <summary>Artigo em kg com saldo inicial e receita de 300 g por unidade do prato.</summary>
    private async Task<(int artigoId, int armazemId)> PrepararReceitaAsync(int produtoId, decimal saldoKg, bool permiteNegativo = false)
    {
        await using var db = app.CriarContexto();
        var kg = await db.UnidadesMedida.SingleAsync(u => u.Codigo == "kg");
        var g = await db.UnidadesMedida.SingleAsync(u => u.Codigo == "g");
        var armazem = await db.Armazens.SingleAsync(a => a.Principal);
        var artigo = new Artigo
        {
            Codigo = ExtensoesHttp.Unico("ART", 12), Nome = "Frango inteiro", Tipo = TipoArtigo.Ingrediente,
            UnidadeMedidaId = kg.Id, CustoMedio = 250m, PermiteStockNegativo = permiteNegativo,
        };
        db.Artigos.Add(artigo);
        await db.SaveChangesAsync();
        db.StockSaldos.Add(new StockSaldo { ArtigoId = artigo.Id, ArmazemId = armazem.Id, Quantidade = saldoKg });
        db.Receitas.Add(new Receita
        {
            ProdutoId = produtoId, Versao = await FichaTecnicaTeste.ProximaVersaoAsync(db, produtoId), Activa = true, ValidaDesdeUtc = DateTime.UtcNow,
            Itens = [new ReceitaItem { ArtigoId = artigo.Id, Quantidade = 300m, UnidadeMedidaId = g.Id }],
        });
        await db.SaveChangesAsync();
        return (artigo.Id, armazem.Id);
    }

    [Fact]
    public async Task Venda_paga_baixa_stock_uma_unica_vez_com_conversao_de_unidades()
    {
        var c = await CenarioVenda.CriarAsync(app);
        var (artigoId, armazemId) = await PrepararReceitaAsync(c.Prato.Id, 1m);
        var pedido = await c.AbrirMesaAsync(c.Gerente, c.Mesa1, (c.Prato, 2));
        var chave = CenarioVenda.Chave();

        await c.PagarAsync(c.Gerente, pedido.Id, "DINHEIRO", pedido.Total, chave: chave).Esperar(HttpStatusCode.Created);
        await c.PagarAsync(c.Gerente, pedido.Id, "DINHEIRO", pedido.Total, chave: chave).Esperar(HttpStatusCode.OK); // reenvio

        await using var db = app.CriarContexto();
        Assert.Equal(0.4m, (await db.StockSaldos.SingleAsync(s => s.ArtigoId == artigoId && s.ArmazemId == armazemId)).Quantidade); // 1 kg − 2 × 300 g
        var movimento = await db.MovimentosStock.SingleAsync(m => m.ArtigoId == artigoId);
        Assert.Equal(TipoMovimentoStock.SaidaVenda, movimento.Tipo);
        Assert.Equal(-0.6m, movimento.Quantidade);
        Assert.Equal(250m, movimento.CustoUnitario);
        Assert.True((await db.Pedidos.SingleAsync(p => p.Id == pedido.Id)).StockBaixado);
    }

    [Fact]
    public async Task Stock_insuficiente_bloqueia_o_fecho_sem_gravar_nada()
    {
        var c = await CenarioVenda.CriarAsync(app);
        var (artigoId, _) = await PrepararReceitaAsync(c.Prato.Id, 0.5m);
        var pedido = await c.AbrirMesaAsync(c.Gerente, c.Mesa1, (c.Prato, 2)); // precisa de 0,6 kg

        var r = await c.PagarAsync(c.Gerente, pedido.Id, "DINHEIRO", pedido.Total).Esperar(HttpStatusCode.UnprocessableEntity);
        Assert.Contains("Stock insuficiente de Frango inteiro", await r.Content.ReadAsStringAsync());

        await using var db = app.CriarContexto();
        var gravado = await db.Pedidos.SingleAsync(p => p.Id == pedido.Id);
        Assert.Equal(EstadoPedido.Aberto, gravado.Estado);
        Assert.Equal(0m, gravado.TotalPago);
        Assert.Equal(0, await db.Pagamentos.CountAsync(p => p.PedidoId == pedido.Id));
        Assert.Equal(0, await db.MovimentosStock.CountAsync(m => m.ArtigoId == artigoId));
    }

    [Fact]
    public async Task Artigo_autorizado_pode_ficar_negativo()
    {
        var c = await CenarioVenda.CriarAsync(app);
        var (artigoId, _) = await PrepararReceitaAsync(c.Prato.Id, 0.1m, permiteNegativo: true);
        var pedido = await c.AbrirMesaAsync(c.Gerente, c.Mesa1, (c.Prato, 1));

        await c.PagarAsync(c.Gerente, pedido.Id, "DINHEIRO", pedido.Total).Esperar(HttpStatusCode.Created);

        await using var db = app.CriarContexto();
        Assert.Equal(-0.2m, (await db.StockSaldos.SingleAsync(s => s.ArtigoId == artigoId)).Quantidade);
    }
}
