using System.Net;
using LAMGestRestaurant.Application.Caixa;
using LAMGestRestaurant.Application.Catalogo;
using LAMGestRestaurant.Application.Comum;
using LAMGestRestaurant.Application.Seguranca;
using LAMGestRestaurant.Application.Stock;
using LAMGestRestaurant.Application.Vendas;
using LAMGestRestaurant.Domain.Comum;
using LAMGestRestaurant.Tests.Infraestrutura;
using Microsoft.EntityFrameworkCore;
using static LAMGestRestaurant.Tests.Infraestrutura.ExtensoesHttp;

namespace LAMGestRestaurant.Tests;

/// <summary>Armazém de abate (centro de produção → produto → principal), gestão de armazéns e unidades, e campos novos do produto.</summary>
[Collection(ColeccaoBaseDados.Nome)]
public class ArmazensUnidadesTests(AplicacaoTeste app)
{
    private static async Task<ArmazemDto> ArmazemAsync(HttpClient c) =>
        await (await c.Post("/api/stock/armazens", new GuardarArmazem(Unico("Arm ", 20))).Esperar(HttpStatusCode.OK)).Ler<ArmazemDto>();

    private static async Task<EstacaoPreparacaoDto> CentroAsync(HttpClient c, int? armazem)
    {
        var e = await (await c.Post("/api/catalogo/estacoes", new { nome = Unico("Centro ", 20) }).Esperar(HttpStatusCode.OK)).Ler<EstacaoPreparacaoDto>();
        return armazem is null ? e : await (await c.Put($"/api/catalogo/estacoes/{e.Id}/armazem", new DefinirArmazemEstacao(armazem)).Esperar(HttpStatusCode.OK)).Ler<EstacaoPreparacaoDto>();
    }

    private static async Task<CategoriaDto> CategoriaAsync(HttpClient c, int? centro = null, int? pai = null) =>
        await (await c.Post("/api/categorias", new GuardarCategoria(Unico("Cat "), null, pai, centro, null, 0)).Esperar(HttpStatusCode.Created)).Ler<CategoriaDto>();

    private static async Task<ProdutoDto> ProdutoAsync(HttpClient c, int categoria, int? armazem = null, int? unidade = null, bool pedeQuantidade = false, StockInicialProduto? stock = null) =>
        await (await c.Post("/api/produtos", new GuardarProduto(Unico("A", 10), null, Unico("Prod ", 16), null, categoria, TipoProduto.Bar, 100m, 10m, null, null, false, true, null,
            StockInicial: stock, UnidadeMedidaId: unidade, ArmazemId: armazem, PedeQuantidade: pedeQuantidade)).Esperar(HttpStatusCode.Created)).Ler<ProdutoDto>();

    private async Task<PedidoDto> VenderAsync(CenarioVenda c, ProdutoDto produto, decimal quantidade = 1)
    {
        var p = await (await c.Gerente.PostComChave("/api/pedidos", CenarioVenda.Chave(), new NovoPedido(TipoPedido.Balcao, null, null, null, 0, null, null, null, false,
            [new NovoItem(produto.Id, quantidade, null)])).Esperar(HttpStatusCode.Created)).Ler<PedidoDto>();
        return await (await c.PagarAsync(c.Gerente, p.Id, "DINHEIRO", p.Total).Esperar(HttpStatusCode.Created)).Ler<PedidoDto>();
    }

    private async Task<int> ArmazemDaSaidaAsync(ProdutoDto produto)
    {
        await using var db = app.CriarContexto();
        return await db.MovimentosStock.Where(m => m.ArtigoId == produto.ArtigoId && m.Tipo == TipoMovimentoStock.SaidaVenda).Select(m => m.ArmazemId).SingleAsync();
    }

    private async Task<decimal> SaldoAsync(int artigo, int armazem)
    {
        await using var db = app.CriarContexto();
        return await db.StockSaldos.Where(s => s.ArtigoId == artigo && s.ArmazemId == armazem).Select(s => s.Quantidade).FirstOrDefaultAsync();
    }

    [Fact]
    public async Task Abate_sai_do_centro_de_producao_senao_do_produto_senao_do_principal()
    {
        var c = await CenarioVenda.CriarAsync(app);
        var doCentro = await ArmazemAsync(c.Gerente);
        var doProduto = await ArmazemAsync(c.Gerente);
        var principal = (await c.Gerente.Obter<List<ArmazemDto>>("/api/stock/armazens")).Single(a => a.Principal);
        var centro = await CentroAsync(c.Gerente, doCentro.Id);
        var centroSemArmazem = await CentroAsync(c.Gerente, null);
        var comCentro = await CategoriaAsync(c.Gerente, centro.Id);
        var semArmazemNoCentro = await CategoriaAsync(c.Gerente, centroSemArmazem.Id);
        var subcategoria = await CategoriaAsync(c.Gerente, pai: comCentro.Id);
        var semCentro = await CategoriaAsync(c.Gerente);

        var casos = new[]
        {
            (Produto: await ProdutoAsync(c.Gerente, comCentro.Id, doProduto.Id), Esperado: doCentro.Id),      // o centro manda
            (Produto: await ProdutoAsync(c.Gerente, subcategoria.Id, doProduto.Id), Esperado: doCentro.Id),   // herda da categoria principal
            (Produto: await ProdutoAsync(c.Gerente, semArmazemNoCentro.Id, doProduto.Id), Esperado: doProduto.Id),
            (Produto: await ProdutoAsync(c.Gerente, semCentro.Id, doProduto.Id), Esperado: doProduto.Id),
            (Produto: await ProdutoAsync(c.Gerente, semCentro.Id), Esperado: principal.Id),
        };
        foreach (var (produto, esperado) in casos)
        {
            await VenderAsync(c, produto);
            Assert.Equal(esperado, await ArmazemDaSaidaAsync(produto));
            Assert.Equal(-1m, await SaldoAsync(produto.ArtigoId!.Value, esperado));
        }
    }

    [Fact]
    public async Task Stock_inicial_entra_onde_a_venda_vai_baixar_e_o_estorno_repoe_no_mesmo_armazem()
    {
        var c = await CenarioVenda.CriarAsync(app);
        var bar = await ArmazemAsync(c.Gerente);
        var centro = await CentroAsync(c.Gerente, bar.Id);
        var categoria = await CategoriaAsync(c.Gerente, centro.Id);
        var produto = await ProdutoAsync(c.Gerente, categoria.Id, stock: new StockInicialProduto(10m, 5m, 0m, null));
        Assert.Equal(10m, await SaldoAsync(produto.ArtigoId!.Value, bar.Id));

        var pedido = await VenderAsync(c, produto, 3);
        Assert.Equal(7m, await SaldoAsync(produto.ArtigoId!.Value, bar.Id));

        var metodo = c.Metodos["DINHEIRO"];
        await c.Gerente.Post($"/api/pedidos/{pedido.Id}/estornar", new EstornarPedido(c.Sessao.Id, metodo, "Devolução", pedido.RowVersion)).Esperar(HttpStatusCode.OK);
        Assert.Equal(10m, await SaldoAsync(produto.ArtigoId!.Value, bar.Id));
    }

    [Fact]
    public async Task Transferencia_de_varios_artigos_e_tudo_ou_nada()
    {
        var c = await CenarioVenda.CriarAsync(app);
        var origem = await ArmazemAsync(c.Gerente);
        var destino = await ArmazemAsync(c.Gerente);
        var categoria = await CategoriaAsync(c.Gerente);
        var a = await ProdutoAsync(c.Gerente, categoria.Id, origem.Id, stock: new StockInicialProduto(10m, 5m, 0m, null));
        var b = await ProdutoAsync(c.Gerente, categoria.Id, origem.Id, stock: new StockInicialProduto(2m, 5m, 0m, null));
        const string Url = "/api/stock/transferencias/varios";

        await c.Gerente.Post(Url, new TransferirVarios(origem.Id, origem.Id, [new(a.ArtigoId!.Value, 1m)], null)).Esperar(HttpStatusCode.UnprocessableEntity);
        // O segundo artigo não chega: o primeiro também não sai.
        await c.Gerente.Post(Url, new TransferirVarios(origem.Id, destino.Id, [new(a.ArtigoId!.Value, 4m), new(b.ArtigoId!.Value, 3m)], null)).Esperar(HttpStatusCode.UnprocessableEntity);
        Assert.Equal((10m, 0m), (await SaldoAsync(a.ArtigoId!.Value, origem.Id), await SaldoAsync(a.ArtigoId!.Value, destino.Id)));

        await c.Gerente.Post(Url, new TransferirVarios(origem.Id, destino.Id, [new(a.ArtigoId!.Value, 4m), new(b.ArtigoId!.Value, 2m)], "Reposição")).Esperar(HttpStatusCode.NoContent);
        Assert.Equal((6m, 4m), (await SaldoAsync(a.ArtigoId!.Value, origem.Id), await SaldoAsync(a.ArtigoId!.Value, destino.Id)));
        Assert.Equal((0m, 2m), (await SaldoAsync(b.ArtigoId!.Value, origem.Id), await SaldoAsync(b.ArtigoId!.Value, destino.Id)));

        var (garcom, _) = await app.ClienteAutenticadoAsync(PerfisPadrao.Garcom);
        Assert.Equal(HttpStatusCode.Forbidden, (await garcom.Post(Url, new TransferirVarios(origem.Id, destino.Id, [new(a.ArtigoId!.Value, 1m)], null))).StatusCode);
    }

    [Fact]
    public async Task Gestao_de_armazens_protege_o_principal_o_stock_e_as_associacoes()
    {
        var c = await CenarioVenda.CriarAsync(app);
        const string Url = "/api/stock/armazens";
        var listar = () => c.Gerente.Obter<List<ArmazemDto>>(Url);
        var principal = (await listar()).Single(a => a.Principal);
        var outro = await ArmazemAsync(c.Gerente);

        var nome = Unico("Novo ", 20);
        Assert.Equal(nome, (await (await c.Gerente.Put($"{Url}/{outro.Id}", new GuardarArmazem(nome)).Esperar(HttpStatusCode.OK)).Ler<ArmazemDto>()).Nome);
        await c.Gerente.Put($"{Url}/{outro.Id}", new GuardarArmazem(principal.Nome)).Esperar(HttpStatusCode.Conflict);
        await c.Gerente.Post($"{Url}/{principal.Id}/activo", new { activo = false }).Esperar(HttpStatusCode.UnprocessableEntity);

        // Com stock não se desactiva; depois de transferido, sim.
        var categoria = await CategoriaAsync(c.Gerente);
        var produto = await ProdutoAsync(c.Gerente, categoria.Id, stock: new StockInicialProduto(5m, 1m, 0m, outro.Id));
        await c.Gerente.Post($"{Url}/{outro.Id}/activo", new { activo = false }).Esperar(HttpStatusCode.UnprocessableEntity);
        await c.Gerente.Post("/api/stock/transferencias", new TransferirStock(produto.ArtigoId!.Value, outro.Id, principal.Id, 5m, null)).Esperar(HttpStatusCode.OK);

        // Associado a um centro de produção também não.
        var centro = await CentroAsync(c.Gerente, outro.Id);
        await c.Gerente.Post($"{Url}/{outro.Id}/activo", new { activo = false }).Esperar(HttpStatusCode.UnprocessableEntity);
        await c.Gerente.Put($"/api/catalogo/estacoes/{centro.Id}/armazem", new DefinirArmazemEstacao(null)).Esperar(HttpStatusCode.OK);
        await c.Gerente.Post($"{Url}/{outro.Id}/activo", new { activo = false }).Esperar(HttpStatusCode.NoContent);
        await c.Gerente.Post($"{Url}/{outro.Id}/activo", new { activo = true }).Esperar(HttpStatusCode.NoContent);

        try
        {
            await c.Gerente.Post($"{Url}/{outro.Id}/principal", new { }).Esperar(HttpStatusCode.NoContent);
            Assert.Equal(outro.Id, (await listar()).Single(a => a.Principal).Id);
        }
        finally
        {
            await c.Gerente.Post($"{Url}/{principal.Id}/principal", new { }).Esperar(HttpStatusCode.NoContent);
        }
    }

    [Fact]
    public async Task Unidades_e_campos_do_produto()
    {
        var c = await CenarioVenda.CriarAsync(app);
        const string Url = "/api/stock/unidades";
        var unidades = await c.Gerente.Obter<List<UnidadeDto>>(Url);
        var (un, kg) = (unidades.Single(u => u.Codigo == "un"), unidades.Single(u => u.Codigo == "kg"));
        var categoria = await CategoriaAsync(c.Gerente);

        // Sem unidade indicada fica «un»; com kg, o artigo de stock fica em kg e a venda aceita decimais.
        Assert.Equal((un.Id, "un", false), (await ProdutoAsync(c.Gerente, categoria.Id)) is var p0 ? (p0.UnidadeMedidaId, p0.Unidade, p0.PedeQuantidade) : default);
        var aPeso = await ProdutoAsync(c.Gerente, categoria.Id, unidade: kg.Id, pedeQuantidade: true);
        Assert.Equal(("kg", true), (aPeso.Unidade, aPeso.PedeQuantidade));
        var pedido = await VenderAsync(c, aPeso, 1.5m);
        Assert.Equal("kg", Assert.Single(pedido.Itens).Unidade);
        await using (var db = app.CriarContexto())
            Assert.Equal(kg.Id, await db.Artigos.Where(a => a.Id == aPeso.ArtigoId).Select(a => a.UnidadeMedidaId).SingleAsync());
        Assert.Equal(-1.5m, await SaldoAsync(aPeso.ArtigoId!.Value, (await c.Gerente.Obter<List<ArmazemDto>>("/api/stock/armazens")).Single(a => a.Principal).Id));

        GuardarProduto Edicao(ProdutoDto p, int unidade) => new(p.Codigo, null, p.Nome, null, p.CategoriaId, p.Tipo, p.PrecoVenda, p.Custo, null, null, false, true, p.RowVersion, UnidadeMedidaId: unidade);
        // Com movimentos, o stock já está contado na unidade antiga; sem movimentos, o artigo acompanha.
        await c.Gerente.Put($"/api/produtos/{aPeso.Id}", Edicao(aPeso, un.Id)).Esperar(HttpStatusCode.UnprocessableEntity);
        var semMovimentos = await ProdutoAsync(c.Gerente, categoria.Id);
        await c.Gerente.Put($"/api/produtos/{semMovimentos.Id}", Edicao(semMovimentos, kg.Id)).Esperar(HttpStatusCode.OK);
        await using (var db = app.CriarContexto())
            Assert.Equal(kg.Id, await db.Artigos.Where(a => a.Id == semMovimentos.ArtigoId).Select(a => a.UnidadeMedidaId).SingleAsync());

        // Tabela de unidades: criar, não repetir, conversão presa quando em uso, desactivar só se livre.
        var caixa = await (await c.Gerente.Post(Url, new GuardarUnidade(Unico("c", 8), "Caixa", DimensaoUnidade.Unidade, 12m)).Esperar(HttpStatusCode.OK)).Ler<UnidadeDto>();
        await c.Gerente.Post(Url, new GuardarUnidade(caixa.Codigo, "Outra", DimensaoUnidade.Unidade, 6m)).Esperar(HttpStatusCode.Conflict);
        await c.Gerente.Put($"{Url}/{caixa.Id}", new GuardarUnidade(caixa.Codigo, "Caixa grande", DimensaoUnidade.Unidade, 24m)).Esperar(HttpStatusCode.OK);
        await ProdutoAsync(c.Gerente, categoria.Id, unidade: caixa.Id);
        await c.Gerente.Put($"{Url}/{caixa.Id}", new GuardarUnidade(caixa.Codigo, "Caixa grande", DimensaoUnidade.Unidade, 6m)).Esperar(HttpStatusCode.UnprocessableEntity);
        await c.Gerente.Put($"{Url}/{caixa.Id}", new GuardarUnidade(Unico("d", 8), "Só o nome", DimensaoUnidade.Unidade, 24m)).Esperar(HttpStatusCode.OK);
        await c.Gerente.Post($"{Url}/{caixa.Id}/activo", new { activo = false }).Esperar(HttpStatusCode.UnprocessableEntity);
    }
}
