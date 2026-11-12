using System.Net;
using LAMGestRestaurant.Application.Comum;
using LAMGestRestaurant.Application.Sala;
using LAMGestRestaurant.Application.Seguranca;
using LAMGestRestaurant.Application.Vendas;
using LAMGestRestaurant.Domain.Comum;
using LAMGestRestaurant.Tests.Infraestrutura;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static LAMGestRestaurant.Tests.Infraestrutura.ExtensoesHttp;

namespace LAMGestRestaurant.Tests;

/// <summary>
/// Responsável da mesa (quem a abriu) e permissão pos.mesas-outros: sem ela, um utilizador vê quem abriu a mesa de outro,
/// mas não o conteúdo nem pode lançar; com ela vê, lança e passa a mesa a outro responsável. Pagar não depende disto.
/// </summary>
[Collection(ColeccaoBaseDados.Nome)]
public class MesasResponsavelTests(AplicacaoTeste app)
{
    [Fact]
    public async Task Mesa_de_outro_so_com_permissao_e_passar_mesa_muda_o_responsavel()
    {
        var c = await CenarioVenda.CriarAsync(app);
        var (garcomA, _) = await app.ClienteAutenticadoAsync(PerfisPadrao.Garcom);
        var (garcomB, _) = await app.ClienteAutenticadoAsync(PerfisPadrao.Garcom);
        var (caixa, _) = await app.ClienteAutenticadoAsync(PerfisPadrao.Caixa);

        var pedido = await c.AbrirMesaAsync(garcomA, c.Mesa1, (c.Prato, 1));
        var a = pedido.ResponsavelId!.Value;
        Assert.NotNull(pedido.Responsavel);

        // A mesa mostra quem a abriu a todos; o total só a quem pode ver a mesa.
        var vistaB = (await garcomB.Obter<List<MesaDto>>("/api/mesas")).Single(m => m.Id == c.Mesa1.Id);
        Assert.Equal((a, pedido.Responsavel, 0m), (vistaB.ResponsavelId!.Value, vistaB.Responsavel, vistaB.TotalAberto));
        var vistaA = (await garcomA.Obter<List<MesaDto>>("/api/mesas")).Single(m => m.Id == c.Mesa1.Id);
        Assert.Equal(450.50m, vistaA.TotalAberto);

        // Sem pos.mesas-outros: não abre, não lança, não envia, não pede a conta, e a mesa não aparece nos pedidos abertos.
        var recusa = await garcomB.GetAsync($"/api/pedidos/{pedido.Id}").Esperar(HttpStatusCode.Forbidden);
        Assert.Contains("foi aberta por", await recusa.Content.ReadAsStringAsync());
        await garcomB.Post($"/api/pedidos/{pedido.Id}/itens", new AdicionarItens([new NovoItem(c.Bebida.Id, 1, null)], pedido.RowVersion)).Esperar(HttpStatusCode.Forbidden);
        await garcomB.Post($"/api/pedidos/{pedido.Id}/enviar-cozinha", new ComVersao(pedido.RowVersion)).Esperar(HttpStatusCode.Forbidden);
        await garcomB.Post($"/api/pedidos/{pedido.Id}/conta", new { terminalId = (int?)null, semImpressora = true }).Esperar(HttpStatusCode.Forbidden);
        Assert.Equal(0, (await garcomB.Obter<Pagina<PedidoResumo>>($"/api/pedidos?estado=Aberto&mesaId={c.Mesa1.Id}")).Total);
        await garcomB.Post($"/api/pedidos/{pedido.Id}/responsavel", new PassarResponsavel(a, pedido.RowVersion)).Esperar(HttpStatusCode.Forbidden);

        // O próprio e quem tem a permissão (perfil Caixa) lançam normalmente.
        pedido = await (await garcomA.Post($"/api/pedidos/{pedido.Id}/itens", new AdicionarItens([new NovoItem(c.Bebida.Id, 1, null)], pedido.RowVersion))
            .Esperar(HttpStatusCode.OK)).Ler<PedidoDto>();
        Assert.Equal(510.50m, (await caixa.Obter<PedidoDto>($"/api/pedidos/{pedido.Id}")).Total);
        Assert.Equal(1, (await caixa.Obter<Pagina<PedidoResumo>>($"/api/pedidos?estado=Aberto&mesaId={c.Mesa1.Id}")).Total);

        // Passar a mesa (mudança de turno): o novo responsável passa a lançar e o anterior deixa de ver.
        var bId = await app.ComServicos(async s =>
        {
            var lista = await s.GetRequiredService<IConsultaUtilizadores>().TodosAsync();
            return lista.Select(u => u.Id).Except([a]).Max();
        });
        Assert.Contains(await c.Gerente.Obter<List<Opcao>>("/api/pedidos/responsaveis"), u => u.Id == bId);
        await c.Gerente.Post($"/api/pedidos/{pedido.Id}/responsavel", new PassarResponsavel(a, pedido.RowVersion)).Esperar(HttpStatusCode.UnprocessableEntity);
        pedido = await (await c.Gerente.Post($"/api/pedidos/{pedido.Id}/responsavel", new PassarResponsavel(bId, pedido.RowVersion))
            .Esperar(HttpStatusCode.OK)).Ler<PedidoDto>();
        Assert.Equal(bId, pedido.ResponsavelId);
        Assert.NotNull(pedido.Responsavel);
        await garcomA.GetAsync($"/api/pedidos/{pedido.Id}").Esperar(HttpStatusCode.Forbidden);

        // Pagar não depende do responsável; depois de pago, o pedido volta a ser consultável (relatórios, recibo).
        await c.PagarAsync(c.Gerente, pedido.Id, "DINHEIRO", 510.50m).Esperar(HttpStatusCode.Created);
        Assert.Equal(EstadoPedido.Pago, (await garcomA.Obter<PedidoDto>($"/api/pedidos/{pedido.Id}")).Estado);

        await using var db = app.CriarContexto();
        Assert.True(await db.RegistosAuditoria.AnyAsync(r => r.Accao == "pedido.responsavel" && r.EntidadeId == pedido.Id.ToString()));
    }
}
