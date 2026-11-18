using System.Net;
using LAMGestRestaurant.Application.Catalogo;
using LAMGestRestaurant.Application.Impressao;
using LAMGestRestaurant.Application.Relatorios;
using LAMGestRestaurant.Application.Stock;
using LAMGestRestaurant.Application.Vendas;
using LAMGestRestaurant.Domain.Comum;
using LAMGestRestaurant.Tests.Infraestrutura;
using static LAMGestRestaurant.Tests.Infraestrutura.ExtensoesHttp;

namespace LAMGestRestaurant.Tests;

/// <summary>
/// Relatórios revistos depois dos acompanhamentos, das categorias só de stock e dos armazéns por artigo/ingrediente:
/// vendas (unidade), anulações (acompanhamentos), stock (stock inicial, armazém) e os novos acompanhamentos, existências e transferências.
/// </summary>
[Collection(ColeccaoBaseDados.Nome)]
public class RelatoriosArmazemTests(AplicacaoTeste app)
{
    private static string Hoje() =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("Africa/Maputo"))).ToString("yyyy-MM-dd");

    [Fact]
    public async Task Relatorios_de_vendas_stock_e_armazens_reflectem_as_novas_funcoes()
    {
        var c = await CenarioVenda.CriarAsync(app);
        var g = c.Gerente;
        var hoje = Hoje();
        var unidades = await g.Obter<List<UnidadeDto>>("/api/stock/unidades");
        var kg = unidades.Single(u => u.Codigo == "kg").Id;
        var gramas = unidades.Single(u => u.Codigo == "g").Id;
        var principal = (await g.Obter<List<ArmazemDto>>("/api/stock/armazens")).Single(a => a.Principal);
        var camara = await (await g.Post("/api/stock/armazens", new GuardarArmazem(Unico("Câmara R ", 20))).Esperar(HttpStatusCode.OK)).Ler<ArmazemDto>();

        // Artigo com stock inicial na câmara; transferência de parte para o principal.
        var arroz = await (await g.Post("/api/stock/artigos", new GuardarArtigo(null, Unico("Arroz R ", 20), TipoArtigo.Ingrediente, kg, 3, false, ArmazemId: camara.Id,
            StockInicial: new StockInicialArtigo(10, 50m))).Esperar(HttpStatusCode.OK)).Ler<ArtigoDto>();
        await g.Post("/api/stock/transferencias", new TransferirStock(arroz.Id, camara.Id, principal.Id, 4, "Para a cozinha")).Esperar(HttpStatusCode.OK);

        // Prato com acompanhamento (o arroz sai da câmara); vende-se 2 e anula-se outro já enviado.
        var grupo = await (await g.Post("/api/acompanhamentos", new GuardarGrupoAcompanhamentos(Unico("Acomp R ", 30), 1, 1,
            [new GuardarOpcaoAcompanhamento(null, "Arroz", [new NovaLinhaReceita(arroz.Id, 200, gramas, camara.Id)]), new GuardarOpcaoAcompanhamento(null, "Salada", null)],
            [c.Prato.Id])).Esperar(HttpStatusCode.Created)).Ler<GrupoAcompanhamentosDto>();
        var opcaoArroz = grupo.Opcoes.Single(o => o.Nome == "Arroz").Id;
        var pedido = await (await g.PostComChave("/api/pedidos", CenarioVenda.Chave(), new NovoPedido(TipoPedido.Mesa, c.Mesa1.Id, null, null, 2, null, null, null, false,
            [new NovoItem(c.Prato.Id, 2, null, [opcaoArroz]), new NovoItem(c.Prato.Id, 1, null, [opcaoArroz])])).Esperar(HttpStatusCode.Created)).Ler<PedidoDto>();
        pedido = await (await g.Post($"/api/pedidos/{pedido.Id}/enviar-cozinha", new ComVersao(pedido.RowVersion)).Esperar(HttpStatusCode.OK)).Ler<PedidoDto>();
        var anulado = pedido.Itens[1];
        pedido = await (await g.Post($"/api/pedidos/{pedido.Id}/itens/{anulado.Id}/cancelar", new ComMotivo("Cliente desistiu", pedido.RowVersion))
            .Esperar(HttpStatusCode.OK)).Ler<PedidoDto>();
        await c.PagarAsync(g, pedido.Id, "DINHEIRO", pedido.Total).Esperar(HttpStatusCode.Created);

        // Produto vendido a peso: a quantidade vem com a unidade.
        var categoria = c.Prato.CategoriaId;
        var camarao = await (await g.Post("/api/produtos", new GuardarProduto(Unico("KG", 10), null, Unico("Camarão ", 20), null, categoria, TipoProduto.Restaurante,
            100m, 40m, null, null, false, true, null, UnidadeMedidaId: kg, ControlaStock: false)).Esperar(HttpStatusCode.Created)).Ler<ProdutoDto>();
        var peso = await c.AbrirMesaAsync(g, c.Mesa2, (camarao, 1.485m));
        await c.PagarAsync(g, peso.Id, "DINHEIRO", peso.Total).Esperar(HttpStatusCode.Created);
        // Produto numa categoria só de stock: não aparece em «sem vendas».
        var soStock = await (await g.Post("/api/categorias", new GuardarCategoria(Unico("Só stock ", 20), null, null, null, null, 0, OcultaNoPos: true))
            .Esperar(HttpStatusCode.Created)).Ler<CategoriaDto>();
        var escondido = await (await g.Post("/api/produtos", new GuardarProduto(Unico("SS", 10), null, Unico("Oculto ", 20), null, soStock.Id, TipoProduto.Restaurante,
            1m, 1m, null, null, false, true, null)).Esperar(HttpStatusCode.Created)).Ler<ProdutoDto>();

        var vendas = await g.Obter<RelatorioVendas>($"/api/relatorios/vendas?desde={hoje}&ate={hoje}");
        Assert.Equal((1.485m, "kg"), (vendas.Produtos.Single(p => p.ProdutoId == camarao.Id).Quantidade, vendas.Produtos.Single(p => p.ProdutoId == camarao.Id).Unidade));
        Assert.Equal("un", vendas.Produtos.Single(p => p.ProdutoId == c.Prato.Id).Unidade);
        Assert.DoesNotContain(vendas.ProdutosSemVendas!, p => p.ProdutoId == escondido.Id);

        // Anulações: o prato anulado mostra o acompanhamento.
        var anulacoes = await g.Obter<RelatorioAnulacoes>($"/api/relatorios/anulacoes?desde={hoje}&ate={hoje}");
        Assert.Contains(anulacoes.ItensCancelados, i => i.Pedido == pedido.Numero && i.Produto == $"{c.Prato.Nome} (Arroz)");

        // Acompanhamentos: 2 pratos com arroz pagos (o anulado não conta); 0,4 kg de arroz estimados.
        var acomp = await g.Obter<RelatorioAcompanhamentos>($"/api/relatorios/acompanhamentos?desde={hoje}&ate={hoje}");
        var linha = acomp.PorOpcao.Single(o => o.Grupo == grupo.Nome && o.Opcao == "Arroz");
        Assert.Equal(2m, linha.Vezes);
        Assert.Equal(0.4m, acomp.Artigos.Single(a => a.ArtigoId == arroz.Id).Quantidade);

        // Stock só da câmara: stock inicial nas entradas, consumo da venda na câmara.
        var stock = await g.Obter<RelatorioStock>($"/api/relatorios/stock?desde={hoje}&ate={hoje}&armazemId={camara.Id}");
        Assert.Equal(camara.Nome, stock.Armazem);
        Assert.Contains(stock.Entradas, e => e.ArtigoId == arroz.Id && e.Quantidade == 10m && e.Armazem == camara.Nome);
        Assert.Contains(stock.Consumo, e => e.ArtigoId == arroz.Id && e.Quantidade == 0.4m && e.Armazem == camara.Nome);

        // Existências por armazém: câmara 10 − 4 − 0,4 = 5,6 (habitual, com mínimo 3); principal 4 (sem mínimo).
        var existencias = await g.Obter<RelatorioExistencias>("/api/relatorios/existencias");
        var naCamara = existencias.Linhas.Single(l => l.ArtigoId == arroz.Id && l.ArmazemId == camara.Id);
        Assert.Equal((5.6m, 3m, false, 280m), (naCamara.Quantidade, naCamara.Minimo, naCamara.AbaixoMinimo, naCamara.Valor));
        Assert.Equal((4m, 0m), (existencias.Linhas.Single(l => l.ArtigoId == arroz.Id && l.ArmazemId == principal.Id).Quantidade,
            existencias.Linhas.Single(l => l.ArtigoId == arroz.Id && l.ArmazemId == principal.Id).Minimo));
        var soCamara = await g.Obter<RelatorioExistencias>($"/api/relatorios/existencias?armazemId={camara.Id}");
        Assert.All(soCamara.Linhas, l => Assert.Equal(camara.Id, l.ArmazemId));

        // Transferências: de onde, para onde e quem.
        var transferencias = await g.Obter<RelatorioTransferencias>($"/api/relatorios/transferencias?desde={hoje}&ate={hoje}&armazemId={camara.Id}");
        var t = Assert.Single(transferencias.Linhas, l => l.ArtigoId == arroz.Id);
        Assert.Equal((4m, camara.Nome, principal.Nome, 200m), (t.Quantidade, t.Origem, t.Destino, t.Valor));
        Assert.NotNull(t.Utilizador);

        // Os três documentos novos imprimem-se.
        foreach (var tipo in new[] { "Acompanhamentos", "Existencias", "Transferencias" })
        {
            var documento = await g.Obter<DocumentoImpressao>($"/api/relatorios/documento?tipo={tipo}&desde={hoje}&ate={hoje}&armazemId={camara.Id}");
            Assert.NotEmpty(documento.Linhas);
        }
        var doc = await g.Obter<DocumentoImpressao>($"/api/relatorios/documento?tipo=Produtos&desde={hoje}&ate={hoje}");
        Assert.Contains(doc.Linhas, l => l.Celulas is { } c && c.Contains(camarao.Nome) && c.Contains("1,485 kg"));
    }
}
