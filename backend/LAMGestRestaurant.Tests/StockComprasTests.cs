using System.Net;
using LAMGestRestaurant.Api.Controllers;
using LAMGestRestaurant.Application.Comum;
using LAMGestRestaurant.Application.Compras;
using LAMGestRestaurant.Application.Seguranca;
using LAMGestRestaurant.Application.Stock;
using LAMGestRestaurant.Domain.Comum;
using LAMGestRestaurant.Tests.Infraestrutura;
using Microsoft.EntityFrameworkCore;
using static LAMGestRestaurant.Tests.Infraestrutura.ExtensoesHttp;

namespace LAMGestRestaurant.Tests;

[Collection(ColeccaoBaseDados.Nome)]
public class StockComprasTests(AplicacaoTeste app)
{
    private record Contexto(HttpClient Gerente, Dictionary<string, int> Unidades, int Armazem);

    private async Task<Contexto> PrepararAsync()
    {
        var (gerente, _) = await app.ClienteAutenticadoAsync(PerfisPadrao.Gerente);
        var unidades = (await gerente.Obter<List<UnidadeDto>>("/api/stock/unidades")).ToDictionary(u => u.Codigo, u => u.Id);
        var armazem = (await gerente.Obter<List<ArmazemDto>>("/api/stock/armazens")).Single(a => a.Principal).Id;
        return new Contexto(gerente, unidades, armazem);
    }

    private static async Task<ArtigoDto> CriarArtigoAsync(Contexto c, string unidade = "kg", decimal minimo = 0) =>
        await (await c.Gerente.Post("/api/stock/artigos", new GuardarArtigo(Unico("A", 12), "Frango inteiro", TipoArtigo.Ingrediente, c.Unidades[unidade], minimo, false))
            .Esperar(HttpStatusCode.OK)).Ler<ArtigoDto>();

    private static async Task<FornecedorDto> CriarFornecedorAsync(Contexto c) =>
        await (await c.Gerente.Post("/api/fornecedores", new GuardarFornecedor(Unico("Forn ", 16), null, "840000000", null, null, null))
            .Esperar(HttpStatusCode.OK)).Ler<FornecedorDto>();

    private static GuardarCompra Compra(Contexto c, int fornecedor, string documento, params NovaLinhaCompra[] linhas) =>
        new(fornecedor, documento, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)), null, c.Armazem, null, linhas, null);

    [Fact]
    public async Task Compra_calcula_totais_entra_em_stock_uma_vez_e_actualiza_custo_medio()
    {
        var c = await PrepararAsync();
        var artigo = await CriarArtigoAsync(c);
        var fornecedor = await CriarFornecedorAsync(c);

        // 10 kg a 200/kg, desconto 100, IVA 16% → líquido 1900, IVA 304, total 2204.
        var rascunho = await (await c.Gerente.Post("/api/compras", Compra(c, fornecedor.Id, "FT 1/2026", new NovaLinhaCompra(artigo.Id, 10, c.Unidades["kg"], 200, 100, 16)))
            .Esperar(HttpStatusCode.OK)).Ler<CompraDto>();
        Assert.Equal((2000m, 100m, 304m, 2204m), (rascunho.Subtotal, rascunho.Desconto, rascunho.Impostos, rascunho.Total));
        Assert.Equal(0m, (await c.Gerente.Obter<ArtigoDto>($"/api/stock/artigos/{artigo.Id}")).Saldo); // rascunho não movimenta

        var confirmada = await (await c.Gerente.Post($"/api/compras/{rascunho.Id}/confirmar", new ComVersaoSimples(rascunho.RowVersion)).Esperar(HttpStatusCode.OK)).Ler<CompraDto>();
        Assert.Equal(EstadoCompra.Confirmada, confirmada.Estado);
        await c.Gerente.Post($"/api/compras/{rascunho.Id}/confirmar", new ComVersaoSimples(confirmada.RowVersion)).Esperar(HttpStatusCode.UnprocessableEntity);

        var depois1 = await c.Gerente.Obter<ArtigoDto>($"/api/stock/artigos/{artigo.Id}");
        Assert.Equal((10m, 190m, 190m), (depois1.Saldo, depois1.CustoMedio, depois1.UltimoCusto)); // custo sem IVA, com desconto

        // Segunda compra: 5000 g a 0,25/g = 5 kg a 250/kg → custo médio (10×190 + 5×250) / 15 = 210.
        var r2 = await (await c.Gerente.Post("/api/compras", Compra(c, fornecedor.Id, "FT 2/2026", new NovaLinhaCompra(artigo.Id, 5000, c.Unidades["g"], 0.25m, 0, 0)))
            .Esperar(HttpStatusCode.OK)).Ler<CompraDto>();
        await c.Gerente.Post($"/api/compras/{r2.Id}/confirmar", new ComVersaoSimples(r2.RowVersion)).Esperar(HttpStatusCode.OK);
        var depois2 = await c.Gerente.Obter<ArtigoDto>($"/api/stock/artigos/{artigo.Id}");
        Assert.Equal((15m, 210m, 250m), (depois2.Saldo, depois2.CustoMedio, depois2.UltimoCusto));

        // O mesmo documento do mesmo fornecedor não se regista duas vezes.
        await c.Gerente.Post("/api/compras", Compra(c, fornecedor.Id, "FT 1/2026", new NovaLinhaCompra(artigo.Id, 1, c.Unidades["kg"], 1, 0, 0)))
            .Esperar(HttpStatusCode.Conflict);
        await using var db = app.CriarContexto();
        Assert.Equal(2, await db.MovimentosStock.CountAsync(m => m.ArtigoId == artigo.Id && m.Tipo == TipoMovimentoStock.EntradaCompra));
    }

    [Fact]
    public async Task Anular_compra_estorna_stock_mas_nao_se_ja_foi_consumido()
    {
        var c = await PrepararAsync();
        var artigo = await CriarArtigoAsync(c);
        var fornecedor = await CriarFornecedorAsync(c);
        async Task<CompraDto> Confirmada(string doc, decimal kg)
        {
            var r = await (await c.Gerente.Post("/api/compras", Compra(c, fornecedor.Id, doc, new NovaLinhaCompra(artigo.Id, kg, c.Unidades["kg"], 100, 0, 0)))
                .Esperar(HttpStatusCode.OK)).Ler<CompraDto>();
            return await (await c.Gerente.Post($"/api/compras/{r.Id}/confirmar", new ComVersaoSimples(r.RowVersion)).Esperar(HttpStatusCode.OK)).Ler<CompraDto>();
        }
        var c1 = await Confirmada("A-1", 10);
        var c2 = await Confirmada("A-2", 10);

        var anulada = await (await c.Gerente.Post($"/api/compras/{c2.Id}/anular", new AnularComMotivo("Factura em duplicado", c2.RowVersion)).Esperar(HttpStatusCode.OK)).Ler<CompraDto>();
        Assert.Equal(EstadoCompra.Anulada, anulada.Estado);
        Assert.Equal(10m, (await c.Gerente.Obter<ArtigoDto>($"/api/stock/artigos/{artigo.Id}")).Saldo);

        // Consome 8 kg e tenta anular a primeira compra de 10 kg.
        await c.Gerente.Post("/api/stock/ajustes", new AjustarStock(artigo.Id, c.Armazem, TipoMovimentoStock.Perda, 8, "Estragado")).Esperar(HttpStatusCode.OK);
        var r = await c.Gerente.Post($"/api/compras/{c1.Id}/anular", new AnularComMotivo("Erro", c1.RowVersion)).Esperar(HttpStatusCode.UnprocessableEntity);
        Assert.Contains("já foi consumida", await r.Content.ReadAsStringAsync());
        Assert.Equal(2m, (await c.Gerente.Obter<ArtigoDto>($"/api/stock/artigos/{artigo.Id}")).Saldo);

        // Após anular, o mesmo número de documento pode ser registado de novo.
        await c.Gerente.Post("/api/compras", Compra(c, fornecedor.Id, "A-2", new NovaLinhaCompra(artigo.Id, 1, c.Unidades["kg"], 100, 0, 0))).Esperar(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Rascunho_pode_ser_editado_e_eliminado_e_valida_unidades_e_descontos()
    {
        var c = await PrepararAsync();
        var artigo = await CriarArtigoAsync(c);
        var fornecedor = await CriarFornecedorAsync(c);
        await c.Gerente.Post("/api/compras", Compra(c, fornecedor.Id, Unico("D"), new NovaLinhaCompra(artigo.Id, 1, c.Unidades["l"], 10, 0, 0)))
            .Esperar(HttpStatusCode.UnprocessableEntity); // litros num artigo em kg
        await c.Gerente.Post("/api/compras", Compra(c, fornecedor.Id, Unico("D"), new NovaLinhaCompra(artigo.Id, 1, c.Unidades["kg"], 10, 50, 0)))
            .Esperar(HttpStatusCode.UnprocessableEntity); // desconto > linha

        var r = await (await c.Gerente.Post("/api/compras", Compra(c, fornecedor.Id, Unico("D"), new NovaLinhaCompra(artigo.Id, 1, c.Unidades["kg"], 10, 0, 0))).Esperar(HttpStatusCode.OK)).Ler<CompraDto>();
        var editada = await (await c.Gerente.Put($"/api/compras/{r.Id}", Compra(c, fornecedor.Id, r.NumeroDocumento, new NovaLinhaCompra(artigo.Id, 3, c.Unidades["kg"], 10, 0, 16)) with { RowVersion = r.RowVersion })
            .Esperar(HttpStatusCode.OK)).Ler<CompraDto>();
        Assert.Equal(34.80m, editada.Total);
        Assert.Single(editada.Itens);
        await c.Gerente.Post($"/api/compras/{r.Id}/eliminar", new ComVersaoSimples(editada.RowVersion)).Esperar(HttpStatusCode.NoContent);
        Assert.Equal(HttpStatusCode.NotFound, (await c.Gerente.GetAsync($"/api/compras/{r.Id}")).StatusCode);
    }

    [Fact]
    public async Task Ficha_tecnica_e_versionada_e_a_venda_usa_a_versao_activa()
    {
        var venda = await CenarioVenda.CriarAsync(app);
        var c = await PrepararAsync();
        var artigo = await CriarArtigoAsync(c);
        await c.Gerente.Post("/api/stock/ajustes", new AjustarStock(artigo.Id, c.Armazem, TipoMovimentoStock.Ajuste, 5, "Stock inicial")).Esperar(HttpStatusCode.OK);

        var url = $"/api/receitas/produtos/{venda.Prato.Id}";
        await c.Gerente.Put(url, new GuardarReceita([new NovaLinhaReceita(artigo.Id, 250, c.Unidades["g"])], null)).Esperar(HttpStatusCode.OK);
        var v2 = await (await c.Gerente.Put(url, new GuardarReceita([new NovaLinhaReceita(artigo.Id, 300, c.Unidades["g"])], "Porção maior")).Esperar(HttpStatusCode.OK)).Ler<ReceitaDto>();
        Assert.Equal((3, true), (v2.Versao, v2.Activa)); // v1 = ficha 1:1 automática do produto; v2 e v3 = as duas guardadas

        var versoes = await c.Gerente.Obter<List<ReceitaDto>>(url);
        Assert.Equal(3, versoes.Count);
        Assert.All(versoes.Where(v => v.Versao < 3), v => Assert.False(v.Activa));
        Assert.NotNull(versoes.Single(v => v.Versao == 2).ValidaAteUtc);

        // Validações: unidade de outra dimensão e artigo repetido.
        await c.Gerente.Put(url, new GuardarReceita([new NovaLinhaReceita(artigo.Id, 1, c.Unidades["ml"])], null)).Esperar(HttpStatusCode.UnprocessableEntity);
        await c.Gerente.Put(url, new GuardarReceita([new NovaLinhaReceita(artigo.Id, 1, c.Unidades["g"]), new NovaLinhaReceita(artigo.Id, 2, c.Unidades["g"])], null))
            .Esperar(HttpStatusCode.UnprocessableEntity);

        var pedido = await venda.AbrirMesaAsync(venda.Gerente, venda.Mesa1, (venda.Prato, 2));
        await venda.PagarAsync(venda.Gerente, pedido.Id, "DINHEIRO", pedido.Total).Esperar(HttpStatusCode.Created);
        Assert.Equal(4.4m, (await c.Gerente.Obter<ArtigoDto>($"/api/stock/artigos/{artigo.Id}")).Saldo); // 5 − 2 × 0,3

        // Artigo usado numa ficha activa não se desactiva.
        await c.Gerente.Post($"/api/stock/artigos/{artigo.Id}/activo", new { activo = false }).Esperar(HttpStatusCode.UnprocessableEntity);
        await c.Gerente.Post($"{url}/retirar", new { }).Esperar(HttpStatusCode.NoContent);
        await c.Gerente.Post($"/api/stock/artigos/{artigo.Id}/activo", new { activo = false }).Esperar(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Inventario_acerta_para_a_contagem_mesmo_com_movimentos_depois_de_aberto()
    {
        var c = await PrepararAsync();
        var artigo = await CriarArtigoAsync(c);
        // Armazém próprio, para não interferir com inventários de outros testes no armazém principal.
        var armazem = await (await c.Gerente.Post("/api/stock/armazens", new GuardarArmazem(Unico("Arm ", 14))).Esperar(HttpStatusCode.OK)).Ler<ArmazemDto>();
        await c.Gerente.Post("/api/stock/ajustes", new AjustarStock(artigo.Id, armazem.Id, TipoMovimentoStock.Ajuste, 10, "Inicial")).Esperar(HttpStatusCode.OK);

        var inv = await (await c.Gerente.Post("/api/stock/inventarios", new NovoInventario(armazem.Id, null)).Esperar(HttpStatusCode.OK)).Ler<InventarioDto>();
        await c.Gerente.Post("/api/stock/inventarios", new NovoInventario(armazem.Id, null)).Esperar(HttpStatusCode.Conflict);
        var linha = inv.Linhas.Single(l => l.ArtigoId == artigo.Id);
        Assert.Equal(10m, linha.QuantidadeSistema);

        inv = await (await c.Gerente.Put($"/api/stock/inventarios/{inv.Id}/contagem", new GuardarContagem([new ContagemLinha(linha.Id, 8)], null, inv.RowVersion))
            .Esperar(HttpStatusCode.OK)).Ler<InventarioDto>();
        // Entretanto há uma perda de 1 kg: no fecho o saldo tem de ficar 8 (o contado), com um ajuste de −1.
        await c.Gerente.Post("/api/stock/ajustes", new AjustarStock(artigo.Id, armazem.Id, TipoMovimentoStock.Perda, 1, "Queda")).Esperar(HttpStatusCode.OK);
        await c.Gerente.Post($"/api/stock/inventarios/{inv.Id}/confirmar", new ComVersaoSimples(inv.RowVersion)).Esperar(HttpStatusCode.OK);

        var saldo = (await c.Gerente.Obter<ArtigoDto>($"/api/stock/artigos/{artigo.Id}")).PorArmazem.Single(a => a.ArmazemId == armazem.Id);
        Assert.Equal(8m, saldo.Quantidade);
        var movimentos = await c.Gerente.Obter<Pagina<MovimentoStockDto>>($"/api/stock/movimentos?artigoId={artigo.Id}&tipo=Inventario");
        Assert.Equal(-1m, Assert.Single(movimentos.Itens).Quantidade);
    }

    [Fact]
    public async Task Transferencia_e_ajustes_respeitam_stock_e_permissoes()
    {
        var c = await PrepararAsync();
        var artigo = await CriarArtigoAsync(c);
        var bar = await (await c.Gerente.Post("/api/stock/armazens", new GuardarArmazem(Unico("Bar ", 14))).Esperar(HttpStatusCode.OK)).Ler<ArmazemDto>();
        await c.Gerente.Post("/api/stock/ajustes", new AjustarStock(artigo.Id, c.Armazem, TipoMovimentoStock.Ajuste, 10, "Inicial")).Esperar(HttpStatusCode.OK);

        var depois = await (await c.Gerente.Post("/api/stock/transferencias", new TransferirStock(artigo.Id, c.Armazem, bar.Id, 3, null)).Esperar(HttpStatusCode.OK)).Ler<ArtigoDto>();
        Assert.Equal(10m, depois.Saldo);
        Assert.Equal(3m, depois.PorArmazem.Single(a => a.ArmazemId == bar.Id).Quantidade);
        await c.Gerente.Post("/api/stock/transferencias", new TransferirStock(artigo.Id, bar.Id, c.Armazem, 5, null)).Esperar(HttpStatusCode.UnprocessableEntity);
        await c.Gerente.Post("/api/stock/ajustes", new AjustarStock(artigo.Id, c.Armazem, TipoMovimentoStock.Perda, 50, "Demasiado")).Esperar(HttpStatusCode.UnprocessableEntity);

        var (garcom, _) = await app.ClienteAutenticadoAsync(PerfisPadrao.Garcom);
        await garcom.Post("/api/stock/ajustes", new AjustarStock(artigo.Id, c.Armazem, TipoMovimentoStock.Perda, 1, "x x x")).Esperar(HttpStatusCode.Forbidden);
        // Autorizar stock negativo exige stock.ajustar.
        var semAjuste = await app.ClienteComPermissoesAsync(Permissoes.StockGerir, Permissoes.StockVer);
        await semAjuste.Post("/api/stock/artigos", new GuardarArtigo(Unico("A", 12), "Gelo", TipoArtigo.Consumivel, c.Unidades["kg"], 0, true)).Esperar(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Artigo_abaixo_do_minimo_aparece_no_filtro_e_no_dashboard()
    {
        var c = await PrepararAsync();
        var artigo = await CriarArtigoAsync(c, minimo: 5);
        await c.Gerente.Post("/api/stock/ajustes", new AjustarStock(artigo.Id, c.Armazem, TipoMovimentoStock.Ajuste, 2, "Inicial")).Esperar(HttpStatusCode.OK);

        var abaixo = await c.Gerente.Obter<Pagina<ArtigoDto>>("/api/stock/artigos?abaixoMinimo=true&tamanho=200");
        Assert.Contains(abaixo.Itens, a => a.Id == artigo.Id && a.AbaixoMinimo);
        var dash = await c.Gerente.Obter<Application.Relatorios.Dashboard>("/api/dashboard");
        Assert.Contains(dash.StockBaixo!, s => s.ArtigoId == artigo.Id);
    }
}
