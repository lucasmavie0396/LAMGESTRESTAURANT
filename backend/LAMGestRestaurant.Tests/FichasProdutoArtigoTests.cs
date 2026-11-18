using System.Net;
using System.Text.Json;
using LAMGestRestaurant.Application.Catalogo;
using LAMGestRestaurant.Application.Comum;
using LAMGestRestaurant.Application.Seguranca;
using LAMGestRestaurant.Application.Stock;
using LAMGestRestaurant.Domain.Comum;
using LAMGestRestaurant.Tests.Infraestrutura;
using Microsoft.EntityFrameworkCore;
using static LAMGestRestaurant.Tests.Infraestrutura.ExtensoesHttp;

namespace LAMGestRestaurant.Tests;

/// <summary>Ficha do produto (mudar a unidade com stock) e ficha do artigo (código automático, armazém do sistema, erro no campo).</summary>
[Collection(ColeccaoBaseDados.Nome)]
public class FichasProdutoArtigoTests(AplicacaoTeste app)
{
    private static GuardarProduto Produto(ProdutoDto p, int unidadeId) =>
        new(p.Codigo, p.CodigoBarras, p.Nome, null, p.CategoriaId, p.Tipo, p.PrecoVenda, p.Custo, p.TaxaImpostoId, null, p.EnviaCozinha, p.Disponivel, p.RowVersion,
            UnidadeMedidaId: unidadeId);

    /// <summary>Campo indicado pelo servidor em «errors» (o ecrã põe lá o cursor).</summary>
    private static async Task<string?> CampoDoErroAsync(HttpResponseMessage r)
    {
        using var json = JsonDocument.Parse(await r.Content.ReadAsStringAsync());
        return json.RootElement.TryGetProperty("errors", out var erros) ? erros.EnumerateObject().First().Name : null;
    }

    [Fact]
    public async Task Mudar_a_unidade_de_um_produto_com_movimentos_de_stock()
    {
        var c = await CenarioVenda.CriarAsync(app);
        var g = c.Gerente;
        var unidades = await g.Obter<List<UnidadeDto>>("/api/stock/unidades");
        int U(string codigo) => unidades.Single(u => u.Codigo == codigo).Id;

        // Produto em unidades com stock inicial (movimento): passar a kg é recusado enquanto houver stock, com o campo indicado.
        var produto = await (await g.Post("/api/produtos", new GuardarProduto(Unico("U", 10), null, Unico("Carne ", 20), null, c.Prato.CategoriaId, TipoProduto.Restaurante,
            300m, 100m, null, null, true, true, null, StockInicial: new StockInicialProduto(5, 100m, 0, null))).Esperar(HttpStatusCode.Created)).Ler<ProdutoDto>();
        var artigoAntigo = produto.ArtigoId!.Value;
        var recusa = await g.Put($"/api/produtos/{produto.Id}", Produto(produto, U("kg"))).Esperar(HttpStatusCode.UnprocessableEntity);
        Assert.Equal("UnidadeMedidaId", await CampoDoErroAsync(recusa));

        // Stock acertado para zero: passa a kg com um artigo novo (o antigo fica inactivo com o histórico em «un»).
        var principal = (await g.Obter<List<ArmazemDto>>("/api/stock/armazens")).Single(a => a.Principal).Id;
        await g.Post("/api/stock/ajustes", new AjustarStock(artigoAntigo, principal, TipoMovimentoStock.Ajuste, -5, "Acerto para mudar a unidade")).Esperar(HttpStatusCode.OK);
        produto = await g.Obter<ProdutoDto>($"/api/produtos/{produto.Id}");
        var emKg = await (await g.Put($"/api/produtos/{produto.Id}", Produto(produto, U("kg"))).Esperar(HttpStatusCode.OK)).Ler<ProdutoDto>();
        Assert.Equal("kg", emKg.Unidade);
        Assert.NotEqual(artigoAntigo, emKg.ArtigoId);
        var antigo = await g.Obter<ArtigoDto>($"/api/stock/artigos/{artigoAntigo}");
        var novo = await g.Obter<ArtigoDto>($"/api/stock/artigos/{emKg.ArtigoId}");
        Assert.Equal(("un", false), (antigo.Unidade, antigo.Activo));
        Assert.Equal(("kg", true, emKg.Nome), (novo.Unidade, novo.Activo, novo.Nome));

        // Com movimentos e da mesma natureza (kg → g): o stock continua em kg e cada venda de 1 g baixa 0,001 kg.
        await g.Post("/api/stock/ajustes", new AjustarStock(novo.Id, principal, TipoMovimentoStock.Ajuste, 2, "Entrada")).Esperar(HttpStatusCode.OK);
        var emGramas = await (await g.Put($"/api/produtos/{produto.Id}", Produto(emKg, U("g"))).Esperar(HttpStatusCode.OK)).Ler<ProdutoDto>();
        Assert.Equal(("g", novo.Id), (emGramas.Unidade, emGramas.ArtigoId));
        Assert.Equal("kg", (await g.Obter<ArtigoDto>($"/api/stock/artigos/{novo.Id}")).Unidade);
        var pedido = await c.AbrirMesaAsync(g, c.Mesa1, (emGramas, 500));
        await c.PagarAsync(g, pedido.Id, "DINHEIRO", pedido.Total).Esperar(HttpStatusCode.Created);
        Assert.Equal(1.5m, (await g.Obter<ArtigoDto>($"/api/stock/artigos/{novo.Id}")).Saldo);
    }

    [Fact]
    public async Task Artigo_com_codigo_automatico_e_armazem_do_sistema()
    {
        var (g, _) = await app.ClienteAutenticadoAsync(PerfisPadrao.Administrador);
        var kg = (await g.Obter<List<UnidadeDto>>("/api/stock/unidades")).Single(u => u.Codigo == "kg").Id;
        var camara = await (await g.Post("/api/stock/armazens", new GuardarArmazem(Unico("Câmara ", 20))).Esperar(HttpStatusCode.OK)).Ler<ArmazemDto>();

        // Sem código: o próximo automático (A + número), e o seguinte é mais um.
        var sugerido = (await g.Obter<JsonElement>("/api/stock/artigos/sugerir-codigo")).GetProperty("codigo").GetString()!;
        var a1 = await (await g.Post("/api/stock/artigos", new GuardarArtigo(null, Unico("Arroz ", 20), TipoArtigo.Ingrediente, kg, 0, false, ArmazemId: camara.Id))
            .Esperar(HttpStatusCode.OK)).Ler<ArtigoDto>();
        var a2 = await (await g.Post("/api/stock/artigos", new GuardarArtigo("", Unico("Feijão ", 20), TipoArtigo.Ingrediente, kg, 0, false))
            .Esperar(HttpStatusCode.OK)).Ler<ArtigoDto>();
        Assert.Matches("^A[0-9]{4,}$", a1.Codigo);
        Assert.True(string.CompareOrdinal(a1.Codigo, sugerido) >= 0);
        Assert.Equal(int.Parse(a1.Codigo[1..]) + 1, int.Parse(a2.Codigo[1..]));

        // Armazém do artigo: o escolhido; sem escolha, o principal.
        Assert.Equal((camara.Id, camara.Nome), (a1.ArmazemId, a1.Armazem));
        Assert.Equal(true, (await g.Obter<List<ArmazemDto>>("/api/stock/armazens")).SingleOrDefault(a => a.Id == a2.ArmazemId)?.Principal);

        // Editar sem código mantém o código; armazém inexistente é recusado no campo do armazém.
        var editado = await (await g.Put($"/api/stock/artigos/{a1.Id}", new GuardarArtigo(null, a1.Nome, a1.Tipo, kg, 1, false, ArmazemId: camara.Id))
            .Esperar(HttpStatusCode.OK)).Ler<ArtigoDto>();
        Assert.Equal(a1.Codigo, editado.Codigo);
        var recusa = await g.Put($"/api/stock/artigos/{a1.Id}", new GuardarArtigo(null, a1.Nome, a1.Tipo, kg, 1, false, ArmazemId: 999999))
            .Esperar(HttpStatusCode.UnprocessableEntity);
        Assert.Equal("ArmazemId", await CampoDoErroAsync(recusa));

        // Transferência entre armazéns do sistema.
        var principal = (await g.Obter<List<ArmazemDto>>("/api/stock/armazens")).Single(a => a.Principal).Id;
        await g.Post("/api/stock/ajustes", new AjustarStock(a1.Id, camara.Id, TipoMovimentoStock.Ajuste, 10, "Entrada")).Esperar(HttpStatusCode.OK);
        var transferido = await (await g.Post("/api/stock/transferencias", new TransferirStock(a1.Id, camara.Id, principal, 4, null)).Esperar(HttpStatusCode.OK)).Ler<ArtigoDto>();
        Assert.Equal(6m, transferido.PorArmazem.Single(s => s.ArmazemId == camara.Id).Quantidade);
        Assert.Equal(4m, transferido.PorArmazem.Single(s => s.ArmazemId == principal).Quantidade);
    }
}
