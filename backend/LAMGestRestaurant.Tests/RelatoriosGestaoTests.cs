using System.Net;
using LAMGestRestaurant.Api.Controllers;
using LAMGestRestaurant.Application.Caixa;
using LAMGestRestaurant.Application.Compras;
using LAMGestRestaurant.Application.Despesas;
using LAMGestRestaurant.Application.Relatorios;
using LAMGestRestaurant.Application.Seguranca;
using LAMGestRestaurant.Application.Stock;
using LAMGestRestaurant.Application.Vendas;
using LAMGestRestaurant.Domain.Comum;
using LAMGestRestaurant.Domain.Stock;
using LAMGestRestaurant.Tests.Infraestrutura;
using static LAMGestRestaurant.Tests.Infraestrutura.ExtensoesHttp;

namespace LAMGestRestaurant.Tests;

[Collection(ColeccaoBaseDados.Nome)]
public class RelatoriosGestaoTests(AplicacaoTeste app)
{
    private static DateOnly Hoje() =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("Africa/Maputo")));

    private static string Periodo() => $"desde={Hoje():yyyy-MM-dd}&ate={Hoje():yyyy-MM-dd}";

    [Fact]
    public async Task Financeiro_soma_vendas_despesas_e_mostra_a_diferenca_do_fecho()
    {
        var c = await CenarioVenda.CriarAsync(app);
        var url = $"/api/relatorios/financeiro?{Periodo()}";
        var antes = await c.Gerente.Obter<RelatorioFinanceiro>(url);

        var pedido = await c.AbrirMesaAsync(c.Gerente, c.Mesa1, (c.Prato, 1)); // 450,50; custo de referência 180
        await c.PagarAsync(c.Gerente, pedido.Id, "DINHEIRO", pedido.Total).Esperar(HttpStatusCode.Created);
        var categoria = (await c.Gerente.Obter<List<CategoriaDespesaDto>>("/api/despesas/categorias")).First(x => x.Activo);
        await c.Gerente.Post("/api/despesas", new NovaDespesa(categoria.Id, Hoje(), 200m, "Carvão", c.Metodos["DINHEIRO"], null, c.Sessao.Id))
            .Esperar(HttpStatusCode.OK);

        // Esperado na gaveta: 1000 + 450,50 − 200 = 1250,50; contados 1240,50 → falta 10.
        var sessao = await c.Gerente.Obter<SessaoCaixaDto>($"/api/caixa/sessoes/{c.Sessao.Id}");
        await c.Gerente.Post($"/api/caixa/sessoes/{c.Sessao.Id}/fechar",
            new FecharCaixa([new ContagemMetodo(c.Metodos["DINHEIRO"], 1240.50m)], "Falta 10", sessao.RowVersion)).Esperar(HttpStatusCode.OK);

        var depois = await c.Gerente.Obter<RelatorioFinanceiro>(url);

        Assert.Equal(450.50m, depois.TotalVendas - antes.TotalVendas);
        Assert.Equal(200m, depois.Despesas - antes.Despesas);
        Assert.Equal(180m, depois.CustoEstimadoVendas - antes.CustoEstimadoVendas);
        var semImpostos = depois.VendasSemImpostos - antes.VendasSemImpostos;
        Assert.Equal(semImpostos - 180m - 200m, depois.ResultadoEstimado - antes.ResultadoEstimado);
        Assert.Contains(depois.DespesasPorCategoria, l => l.Rotulo == categoria.Nome);

        var fecho = Assert.Single(depois.SessoesFechadas, s => s.SessaoId == c.Sessao.Id);
        Assert.Equal((1250.50m, 1240.50m, -10m), (fecho.Esperado, fecho.Contado, fecho.Diferenca));
        Assert.Equal("Falta 10", fecho.Observacoes);
        Assert.NotNull(fecho.FechadaPor);
        Assert.Equal(depois.SessoesFechadas.Sum(s => s.Diferenca), depois.DiferencasPorMetodo.Sum(m => m.Diferenca));
    }

    [Fact]
    public async Task Stock_mostra_valor_entradas_consumo_e_perdas_do_periodo()
    {
        var c = await CenarioVenda.CriarAsync(app);
        var unidades = (await c.Gerente.Obter<List<UnidadeDto>>("/api/stock/unidades")).ToDictionary(u => u.Codigo, u => u.Id);
        var armazem = (await c.Gerente.Obter<List<ArmazemDto>>("/api/stock/armazens")).Single(a => a.Principal).Id;
        var url = $"/api/relatorios/stock?{Periodo()}";
        var antes = await c.Gerente.Obter<RelatorioStock>(url);

        var artigo = await (await c.Gerente.Post("/api/stock/artigos", new GuardarArtigo(Unico("A", 12), "Frango", TipoArtigo.Ingrediente, unidades["kg"], 0, false))
            .Esperar(HttpStatusCode.OK)).Ler<ArtigoDto>();
        var fornecedor = await (await c.Gerente.Post("/api/fornecedores", new GuardarFornecedor(Unico("Forn ", 16), null, "840000000", null, null, null))
            .Esperar(HttpStatusCode.OK)).Ler<FornecedorDto>();

        // 10 kg a 100/kg com IVA 16% → total 1160; custo de stock 1000 (sem IVA).
        var compra = await (await c.Gerente.Post("/api/compras", new GuardarCompra(fornecedor.Id, Unico("FT ", 12), Hoje(), null, armazem, null,
            [new NovaLinhaCompra(artigo.Id, 10, unidades["kg"], 100, 0, 16)], null)).Esperar(HttpStatusCode.OK)).Ler<CompraDto>();
        await c.Gerente.Post($"/api/compras/{compra.Id}/confirmar", new ComVersaoSimples(compra.RowVersion)).Esperar(HttpStatusCode.OK);
        await c.Gerente.Post("/api/stock/ajustes", new AjustarStock(artigo.Id, armazem, TipoMovimentoStock.Perda, 1, "Estragado")).Esperar(HttpStatusCode.OK);

        await using (var db = app.CriarContexto())
        {
            db.Receitas.Add(new Receita { ProdutoId = c.Prato.Id, Versao = await FichaTecnicaTeste.ProximaVersaoAsync(db, c.Prato.Id), Activa = true, ValidaDesdeUtc = DateTime.UtcNow,
                Itens = [new ReceitaItem { ArtigoId = artigo.Id, Quantidade = 500m, UnidadeMedidaId = unidades["g"] }] });
            await db.SaveChangesAsync();
        }
        var pedido = await c.AbrirMesaAsync(c.Gerente, c.Mesa1, (c.Prato, 1));
        await c.PagarAsync(c.Gerente, pedido.Id, "DINHEIRO", pedido.Total).Esperar(HttpStatusCode.Created);

        var depois = await c.Gerente.Obter<RelatorioStock>(url);

        Assert.Equal(850m, depois.ValorStockActual - antes.ValorStockActual); // 8,5 kg × 100
        var entrada = Assert.Single(depois.Entradas, l => l.ArtigoId == artigo.Id);
        Assert.Equal((10m, 1000m), (entrada.Quantidade, entrada.Valor));
        var perda = Assert.Single(depois.AjustesPerdas, l => l.ArtigoId == artigo.Id);
        Assert.Equal((-1m, -100m), (perda.Quantidade, perda.Valor));
        var consumo = Assert.Single(depois.Consumo, l => l.ArtigoId == artigo.Id);
        Assert.Equal((0.5m, 50m, "kg"), (consumo.Quantidade, consumo.Valor, consumo.Unidade));
        var doFornecedor = Assert.Single(depois.ComprasPorFornecedor, l => l.Rotulo == fornecedor.Nome);
        Assert.Equal((1160m, 1), (doFornecedor.Valor, doFornecedor.Quantidade));
        Assert.Equal(1, depois.MovimentosPorTipo.Single(t => t.Tipo == TipoMovimentoStock.Perda).Movimentos
                        - (antes.MovimentosPorTipo.SingleOrDefault(t => t.Tipo == TipoMovimentoStock.Perda)?.Movimentos ?? 0));
    }

    [Fact]
    public async Task Relatorios_de_gestao_exigem_a_permissao_propria()
    {
        var (stock, _) = await app.ClienteAutenticadoAsync(PerfisPadrao.StockCompras);
        Assert.Equal(HttpStatusCode.OK, (await stock.GetAsync($"/api/relatorios/stock?{Periodo()}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await stock.GetAsync($"/api/relatorios/financeiro?{Periodo()}")).StatusCode);

        var (caixa, _) = await app.ClienteAutenticadoAsync(PerfisPadrao.Caixa);
        Assert.Equal(HttpStatusCode.Forbidden, (await caixa.GetAsync($"/api/relatorios/stock?{Periodo()}")).StatusCode);

        var (gerente, _) = await app.ClienteAutenticadoAsync(PerfisPadrao.Gerente);
        Assert.Equal(HttpStatusCode.UnprocessableEntity,
            (await gerente.GetAsync("/api/relatorios/financeiro?desde=2026-03-10&ate=2026-03-01")).StatusCode);
    }
}
