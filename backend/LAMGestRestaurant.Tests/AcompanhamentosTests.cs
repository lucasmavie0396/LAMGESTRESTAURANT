using System.Net;
using LAMGestRestaurant.Application.Catalogo;
using LAMGestRestaurant.Application.Impressao;
using LAMGestRestaurant.Application.Stock;
using LAMGestRestaurant.Application.Vendas;
using LAMGestRestaurant.Domain.Comum;
using LAMGestRestaurant.Domain.Stock;
using LAMGestRestaurant.Tests.Infraestrutura;
using Microsoft.EntityFrameworkCore;
using static LAMGestRestaurant.Tests.Infraestrutura.ExtensoesHttp;

namespace LAMGestRestaurant.Tests;

/// <summary>Acompanhamentos dos pratos: escolhidos no POS, sem preço, saem nos documentos e gastam stock no fecho.</summary>
[Collection(ColeccaoBaseDados.Nome)]
public class AcompanhamentosTests(AplicacaoTeste app)
{
    [Fact]
    public async Task Acompanhamentos_validam_a_escolha_nao_mudam_o_preco_e_gastam_stock()
    {
        var c = await CenarioVenda.CriarAsync(app);
        var g = c.Gerente;
        var unidades = await g.Obter<List<UnidadeDto>>("/api/stock/unidades");
        var kg = unidades.Single(u => u.Codigo == "kg").Id;
        var gramas = unidades.Single(u => u.Codigo == "g").Id;
        async Task<ArtigoDto> ArtigoAsync(string nome) =>
            await (await g.Post("/api/stock/artigos", new GuardarArtigo(Unico("AC", 12), Unico(nome + " ", 20), TipoArtigo.Ingrediente, kg, 0, false, null))
                .Esperar(HttpStatusCode.OK)).Ler<ArtigoDto>();
        var arroz = await ArtigoAsync("Arroz");
        var batata = await ArtigoAsync("Batata");
        int armazem;
        await using (var db = app.CriarContexto())
        {
            armazem = (await db.Armazens.SingleAsync(a => a.Principal)).Id;
            db.StockSaldos.AddRange(new StockSaldo { ArtigoId = arroz.Id, ArmazemId = armazem, Quantidade = 1m },
                new StockSaldo { ArtigoId = batata.Id, ArmazemId = armazem, Quantidade = 1m });
            // A ficha do prato também usa arroz: ficha e acompanhamento somam-se num só movimento.
            db.Receitas.Add(new Receita
            {
                ProdutoId = c.Prato.Id, Versao = await FichaTecnicaTeste.ProximaVersaoAsync(db, c.Prato.Id), Activa = true, ValidaDesdeUtc = DateTime.UtcNow,
                Itens = [new ReceitaItem { ArtigoId = arroz.Id, Quantidade = 100m, UnidadeMedidaId = gramas }],
            });
            await db.SaveChangesAsync();
        }

        var nome = Unico("Acompanhamento ", 30);
        var grupo = await (await g.Post("/api/acompanhamentos", new GuardarGrupoAcompanhamentos(nome, 1, 2,
        [
            new GuardarOpcaoAcompanhamento(null, "Arroz", [new NovaLinhaReceita(arroz.Id, 150m, gramas)]),
            new GuardarOpcaoAcompanhamento(null, "Batata frita", [new NovaLinhaReceita(batata.Id, 0.2m, kg)]),
            new GuardarOpcaoAcompanhamento(null, "Salada fria", null),
        ], [c.Prato.Id])).Esperar(HttpStatusCode.Created)).Ler<GrupoAcompanhamentosDto>();
        Assert.Equal(["Arroz", "Batata frita", "Salada fria"], grupo.Opcoes.Select(o => o.Nome));
        Assert.Equal([c.Prato.Id], grupo.Produtos.Select(p => p.Id));
        int Opcao(string n) => grupo.Opcoes.Single(o => o.Nome == n).Id;

        // O POS sabe que o prato pede acompanhamentos (a bebida não) e recebe as opções.
        Assert.True((await g.Obter<ProdutoDto>($"/api/produtos/{c.Prato.Id}")).TemAcompanhamentos);
        Assert.False((await g.Obter<ProdutoDto>($"/api/produtos/{c.Bebida.Id}")).TemAcompanhamentos);
        var doPrato = Assert.Single(await g.Obter<List<GrupoPos>>($"/api/acompanhamentos/produto/{c.Prato.Id}"));
        Assert.Equal(3, doPrato.Opcoes.Count);

        Task<HttpResponseMessage> Abrir(params int[] opcoes) => g.PostComChave("/api/pedidos", CenarioVenda.Chave(),
            new NovoPedido(TipoPedido.Mesa, c.Mesa1.Id, null, null, 2, null, null, null, false, [new NovoItem(c.Prato.Id, 2, null, opcoes)]));
        Assert.Contains("escolha o", await (await Abrir().Esperar(HttpStatusCode.UnprocessableEntity)).Content.ReadAsStringAsync());
        Assert.Contains("no máximo 2", await (await Abrir(Opcao("Arroz"), Opcao("Batata frita"), Opcao("Salada fria"))
            .Esperar(HttpStatusCode.UnprocessableEntity)).Content.ReadAsStringAsync());
        await Abrir(Opcao("Arroz"), 999999).Esperar(HttpStatusCode.UnprocessableEntity);

        var pedido = await (await Abrir(Opcao("Salada fria"), Opcao("Arroz")).Esperar(HttpStatusCode.Created)).Ler<PedidoDto>();
        var item = Assert.Single(pedido.Itens);
        Assert.Equal("Arroz, Salada fria", item.Acompanhamentos); // pela ordem do grupo
        Assert.Equal(901m, pedido.Total); // 2 × 450,50: os acompanhamentos não mudam o preço

        // Talão da cozinha e conta: os acompanhamentos por baixo do prato.
        var talao = ComposicaoImpressao.Talao(new DadosTalao("Cozinha", pedido.Numero, TipoPedido.Mesa, c.Mesa1.Nome, null, null, DateTime.Now, null,
            [new ItemTalao(item.Quantidade, item.NomeProduto, item.Observacoes, item.Acompanhamentos)], TipoTalao.Pedido, null));
        Assert.Contains(talao.Linhas, l => l.Texto == "   + Arroz, Salada fria");
        var conta = ComposicaoImpressao.Conta(pedido, new DadosEstabelecimento("Teste", null, null, null, "MT"), DateTime.Now);
        Assert.Contains(conta.Linhas, l => l.Texto == "   + Arroz, Salada fria");

        await c.PagarAsync(g, pedido.Id, "DINHEIRO", pedido.Total).Esperar(HttpStatusCode.Created);
        await using (var db = app.CriarContexto())
        {
            // Arroz: 2 × (100 g da ficha + 150 g do acompanhamento) = 0,5 kg, num só movimento. A batata não foi escolhida.
            Assert.Equal(0.5m, (await db.StockSaldos.SingleAsync(s => s.ArtigoId == arroz.Id && s.ArmazemId == armazem)).Quantidade);
            Assert.Equal(-0.5m, (await db.MovimentosStock.SingleAsync(m => m.ArtigoId == arroz.Id)).Quantidade);
            Assert.Equal(1m, (await db.StockSaldos.SingleAsync(s => s.ArtigoId == batata.Id && s.ArmazemId == armazem)).Quantidade);
            Assert.Equal(2, await db.PedidoItensAcompanhamentos.CountAsync(a => a.PedidoItemId == item.Id));
        }

        // Retirar uma opção do grupo: fica inactiva (está num pedido) e deixa de aparecer no POS.
        var editado = await (await g.Put($"/api/acompanhamentos/{grupo.Id}", new GuardarGrupoAcompanhamentos(nome, 1, 1,
        [
            new GuardarOpcaoAcompanhamento(Opcao("Arroz"), "Arroz branco", [new NovaLinhaReceita(arroz.Id, 150m, gramas)]),
            new GuardarOpcaoAcompanhamento(Opcao("Batata frita"), "Batata frita", null), // deixa de gastar stock
        ], [c.Prato.Id])).Esperar(HttpStatusCode.OK)).Ler<GrupoAcompanhamentosDto>();
        Assert.Equal(["Arroz branco", "Batata frita"], editado.Opcoes.Select(o => o.Nome));
        Assert.Empty(editado.Opcoes.Single(o => o.Nome == "Batata frita").Linhas);
        Assert.Equal(2, Assert.Single(await g.Obter<List<GrupoPos>>($"/api/acompanhamentos/produto/{c.Prato.Id}")).Opcoes.Count);
        // O pedido antigo mantém o nome copiado no momento da venda.
        Assert.Equal("Arroz, Salada fria", Assert.Single((await g.Obter<PedidoDto>($"/api/pedidos/{pedido.Id}")).Itens).Acompanhamentos);

        // Grupo desactivado: o prato deixa de pedir acompanhamentos.
        await g.Post($"/api/acompanhamentos/{grupo.Id}/activo", new { activo = false }).Esperar(HttpStatusCode.NoContent);
        Assert.False((await g.Obter<ProdutoDto>($"/api/produtos/{c.Prato.Id}")).TemAcompanhamentos);
    }
}
