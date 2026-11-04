using System.Net;
using LAMGestRestaurant.Application.Caixa;
using LAMGestRestaurant.Application.Cozinha;
using LAMGestRestaurant.Application.Sala;
using LAMGestRestaurant.Application.Seguranca;
using LAMGestRestaurant.Application.Vendas;
using LAMGestRestaurant.Domain.Comum;
using LAMGestRestaurant.Tests.Infraestrutura;
using Microsoft.EntityFrameworkCore;

namespace LAMGestRestaurant.Tests;

[Collection(ColeccaoBaseDados.Nome)]
public class VendasTests(AplicacaoTeste app)
{
    [Fact]
    public async Task Fluxo_completo_mesa_cozinha_pagamento_parcial_e_fecho()
    {
        var c = await CenarioVenda.CriarAsync(app);
        var pedido = await c.AbrirMesaAsync(c.Gerente, c.Mesa1, (c.Prato, 2), (c.Bebida, 1));

        Assert.Equal(EstadoPedido.Aberto, pedido.Estado);
        Assert.Equal(961.00m, pedido.Total); // 2 × 450,50 + 60
        Assert.Equal(132.56m, pedido.Impostos); // por linha: 901 × 16/116 = 124,28 + 60 × 16/116 = 8,28
        Assert.StartsWith("PED-", pedido.Numero);
        Assert.Equal(EstadoMesa.Ocupada, (await c.Gerente.Obter<MesaDto>($"/api/mesas/{c.Mesa1.Id}")).Estado);

        // Envio à cozinha: o prato vai ao KDS, a bebida (não vai à cozinha) fica logo entregue.
        pedido = await (await c.Gerente.Post($"/api/pedidos/{pedido.Id}/enviar-cozinha", new ComVersao(pedido.RowVersion)).Esperar(HttpStatusCode.OK)).Ler<PedidoDto>();
        Assert.Equal(EstadoItemCozinha.Pendente, pedido.Itens.Single(i => i.ProdutoId == c.Prato.Id).EstadoCozinha);
        Assert.Equal(EstadoItemCozinha.Entregue, pedido.Itens.Single(i => i.ProdutoId == c.Bebida.Id).EstadoCozinha);

        var (cozinha, _) = await app.ClienteAutenticadoAsync(PerfisPadrao.Cozinha);
        var ticket = (await cozinha.Obter<List<TicketCozinha>>("/api/cozinha")).Single(t => t.PedidoId == pedido.Id);
        var itemCozinha = Assert.Single(ticket.Itens);
        await cozinha.Post($"/api/cozinha/itens/{itemCozinha.Id}/estado", new AlterarEstadoCozinha(EstadoItemCozinha.Pendente, EstadoItemCozinha.EmPreparacao)).Esperar(HttpStatusCode.NoContent);
        // Outro posto com o estado antigo é recusado.
        await cozinha.Post($"/api/cozinha/itens/{itemCozinha.Id}/estado", new AlterarEstadoCozinha(EstadoItemCozinha.Pendente, EstadoItemCozinha.Pronto)).Esperar(HttpStatusCode.Conflict);
        await cozinha.Post($"/api/cozinha/itens/{itemCozinha.Id}/estado", new AlterarEstadoCozinha(EstadoItemCozinha.EmPreparacao, EstadoItemCozinha.Pronto)).Esperar(HttpStatusCode.NoContent);
        // A cozinha não regista pagamentos.
        await c.PagarAsync(cozinha, pedido.Id, "DINHEIRO", 10m).Esperar(HttpStatusCode.Forbidden);

        // Pagamento parcial em dinheiro com troco e o resto em M-Pesa com referência.
        pedido = await (await c.PagarAsync(c.Gerente, pedido.Id, "DINHEIRO", 500m, 600m).Esperar(HttpStatusCode.Created)).Ler<PedidoDto>();
        Assert.Equal(EstadoPedido.Aberto, pedido.Estado);
        Assert.Equal(461.00m, pedido.EmDivida);
        Assert.Equal(100m, pedido.Pagamentos.Single().Troco);

        pedido = await (await c.PagarAsync(c.Gerente, pedido.Id, "MPESA", 461m, referencia: "MP123ABC").Esperar(HttpStatusCode.Created)).Ler<PedidoDto>();
        Assert.Equal(EstadoPedido.Pago, pedido.Estado);
        Assert.Equal(0m, pedido.EmDivida);
        Assert.StartsWith("VD-", pedido.ReciboNumero); // o recibo do pedido é a venda a dinheiro
        Assert.Equal(EstadoMesa.Livre, (await c.Gerente.Obter<MesaDto>($"/api/mesas/{c.Mesa1.Id}")).Estado);

        var recibo = await c.Gerente.Obter<ReciboDto>($"/api/pedidos/{pedido.Id}/recibo");
        Assert.Equal(961m, recibo.Total);
        Assert.Equal(2, recibo.Linhas.Count);

        // O caixa reflecte só esta sessão: dinheiro esperado = fundo 1000 + 500.
        var sessao = await c.Gerente.Obter<SessaoCaixaDto>($"/api/caixa/sessoes/{c.Sessao.Id}");
        Assert.Equal(1, sessao.PedidosPagos);
        Assert.Equal(961m, sessao.TotalRecebido);
        Assert.Equal(1500m, sessao.PorMetodo.Single(m => m.MetodoPagamentoId == c.Metodos["DINHEIRO"]).Esperado);

        // Pedido pago não aceita mais alterações nem pagamentos.
        await c.PagarAsync(c.Gerente, pedido.Id, "DINHEIRO", 1m).Esperar(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Criacao_de_pedido_e_idempotente()
    {
        var c = await CenarioVenda.CriarAsync(app);
        var chave = CenarioVenda.Chave();
        var corpo = new NovoPedido(TipoPedido.TakeAway, null, null, null, 1, "840000000", null, null, false, [new NovoItem(c.Prato.Id, 1, null)]);

        var primeiro = await (await c.Gerente.PostComChave("/api/pedidos", chave, corpo).Esperar(HttpStatusCode.Created)).Ler<PedidoDto>();
        var segundo = await (await c.Gerente.PostComChave("/api/pedidos", chave, corpo).Esperar(HttpStatusCode.OK)).Ler<PedidoDto>();

        Assert.Equal(primeiro.Id, segundo.Id);
        await using var db = app.CriarContexto();
        Assert.Equal(1, await db.Pedidos.CountAsync(p => p.IdempotencyKey == chave));
        // Sem chave é recusado.
        await c.Gerente.Post("/api/pedidos", corpo).Esperar(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Reenvio_simultaneo_do_mesmo_pagamento_nao_duplica()
    {
        var c = await CenarioVenda.CriarAsync(app);
        var pedido = await c.AbrirMesaAsync(c.Gerente, c.Mesa1, (c.Prato, 1));
        var chave = CenarioVenda.Chave();

        var respostas = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => c.PagarAsync(c.Gerente, pedido.Id, "DINHEIRO", 100m, chave: chave)));

        Assert.All(respostas, r => Assert.True(r.IsSuccessStatusCode, $"Resposta {(int)r.StatusCode}"));
        Assert.Single(respostas, r => r.StatusCode == HttpStatusCode.Created);
        await using var db = app.CriarContexto();
        Assert.Equal(1, await db.Pagamentos.CountAsync(p => p.IdempotencyKey == chave));
        Assert.Equal(100m, (await db.Pedidos.SingleAsync(p => p.Id == pedido.Id)).TotalPago);
    }

    [Fact]
    public async Task Pagamentos_simultaneos_diferentes_nunca_excedem_o_total()
    {
        var c = await CenarioVenda.CriarAsync(app);
        var pedido = await c.AbrirMesaAsync(c.Gerente, c.Mesa1, (c.Prato, 1));

        // Dois terminais tentam pagar o total ao mesmo tempo, com chaves diferentes.
        var respostas = await Task.WhenAll(
            c.PagarAsync(c.Gerente, pedido.Id, "DINHEIRO", 450.50m),
            c.PagarAsync(c.Gerente, pedido.Id, "CARTAO", 450.50m));

        Assert.Single(respostas, r => r.StatusCode == HttpStatusCode.Created);
        await using var db = app.CriarContexto();
        var gravado = await db.Pedidos.SingleAsync(p => p.Id == pedido.Id);
        Assert.Equal(450.50m, gravado.TotalPago);
        Assert.Equal(1, await db.Pagamentos.CountAsync(p => p.PedidoId == pedido.Id));
    }

    [Theory]
    [InlineData("DINHEIRO", 100, 50, null)]        // recebido inferior
    [InlineData("MPESA", 100, 120, "REF1")]       // troco num método sem troco
    [InlineData("MPESA", 100, 100, null)]         // sem referência
    [InlineData("DINHEIRO", 5000, 5000, null)]    // acima do saldo em dívida
    public async Task Pagamentos_invalidos_sao_recusados(string metodo, decimal valor, decimal recebido, string? referencia)
    {
        var c = await CenarioVenda.CriarAsync(app);
        var pedido = await c.AbrirMesaAsync(c.Gerente, c.Mesa1, (c.Prato, 1));
        await c.PagarAsync(c.Gerente, pedido.Id, metodo, valor, recebido, referencia).Esperar(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Pagamento_exige_caixa_aberto_do_proprio_operador()
    {
        var c = await CenarioVenda.CriarAsync(app);
        var pedido = await c.AbrirMesaAsync(c.Gerente, c.Mesa1, (c.Prato, 1));
        var (caixa, _) = await app.ClienteAutenticadoAsync(PerfisPadrao.Caixa);

        // A sessão do cenário pertence a outro utilizador.
        await c.PagarAsync(caixa, pedido.Id, "DINHEIRO", 100m).Esperar(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Uma_mesa_so_tem_um_pedido_aberto_e_a_transferencia_liberta_a_origem()
    {
        var c = await CenarioVenda.CriarAsync(app);
        var pedido = await c.AbrirMesaAsync(c.Gerente, c.Mesa1, (c.Prato, 1));

        await c.Gerente.PostComChave("/api/pedidos", CenarioVenda.Chave(),
            new NovoPedido(TipoPedido.Mesa, c.Mesa1.Id, null, null, 2, null, null, null, false, null)).Esperar(HttpStatusCode.Conflict);

        pedido = await (await c.Gerente.Post($"/api/pedidos/{pedido.Id}/transferir", new TransferirMesa(c.Mesa2.Id, "Cliente mudou de mesa", pedido.RowVersion))
            .Esperar(HttpStatusCode.OK)).Ler<PedidoDto>();
        Assert.Equal(c.Mesa2.Id, pedido.MesaId);
        Assert.Equal(EstadoMesa.Livre, (await c.Gerente.Obter<MesaDto>($"/api/mesas/{c.Mesa1.Id}")).Estado);
        Assert.Equal(EstadoMesa.Ocupada, (await c.Gerente.Obter<MesaDto>($"/api/mesas/{c.Mesa2.Id}")).Estado);
        await using var db = app.CriarContexto();
        Assert.Equal(1, await db.TransferenciasMesa.CountAsync(t => t.PedidoId == pedido.Id));
    }

    [Fact]
    public async Task Clique_repetido_com_versao_antiga_nao_duplica_itens()
    {
        var c = await CenarioVenda.CriarAsync(app);
        var pedido = await c.AbrirMesaAsync(c.Gerente, c.Mesa1, (c.Prato, 1));
        var corpo = new AdicionarItens([new NovoItem(c.Bebida.Id, 1, "Sem gelo")], pedido.RowVersion);

        await c.Gerente.Post($"/api/pedidos/{pedido.Id}/itens", corpo).Esperar(HttpStatusCode.OK);
        await c.Gerente.Post($"/api/pedidos/{pedido.Id}/itens", corpo).Esperar(HttpStatusCode.Conflict);

        var actual = await c.Gerente.Obter<PedidoDto>($"/api/pedidos/{pedido.Id}");
        Assert.Equal(2, actual.Itens.Count);
        Assert.Equal(510.50m, actual.Total);
    }

    [Fact]
    public async Task Cancelar_item_enviado_e_aplicar_desconto_exigem_permissao()
    {
        var c = await CenarioVenda.CriarAsync(app);
        var (garcom, _) = await app.ClienteAutenticadoAsync(PerfisPadrao.Garcom);
        var pedido = await c.AbrirMesaAsync(garcom, c.Mesa1, (c.Prato, 2));
        pedido = await (await garcom.Post($"/api/pedidos/{pedido.Id}/enviar-cozinha", new ComVersao(pedido.RowVersion)).Esperar(HttpStatusCode.OK)).Ler<PedidoDto>();
        var item = pedido.Itens.Single();

        await garcom.Post($"/api/pedidos/{pedido.Id}/itens/{item.Id}/cancelar", new ComMotivo("Cliente desistiu", pedido.RowVersion)).Esperar(HttpStatusCode.Forbidden);
        await garcom.Post($"/api/pedidos/{pedido.Id}/desconto", new AplicarDesconto(null, 10m, pedido.RowVersion)).Esperar(HttpStatusCode.Forbidden);
        // Item enviado não se remove nem altera sem cancelar.
        await garcom.Post($"/api/pedidos/{pedido.Id}/itens/{item.Id}/remover", new ComVersao(pedido.RowVersion)).Esperar(HttpStatusCode.UnprocessableEntity);

        pedido = await (await c.Gerente.Post($"/api/pedidos/{pedido.Id}/desconto", new AplicarDesconto(null, 10m, pedido.RowVersion)).Esperar(HttpStatusCode.OK)).Ler<PedidoDto>();
        Assert.Equal(90.10m, pedido.Desconto);
        Assert.Equal(810.90m, pedido.Total);

        // Motivo obrigatório.
        await c.Gerente.Post($"/api/pedidos/{pedido.Id}/itens/{item.Id}/cancelar", new ComMotivo("", pedido.RowVersion)).Esperar(HttpStatusCode.BadRequest);
        pedido = await (await c.Gerente.Post($"/api/pedidos/{pedido.Id}/itens/{item.Id}/cancelar", new ComMotivo("Cliente desistiu", pedido.RowVersion))
            .Esperar(HttpStatusCode.OK)).Ler<PedidoDto>();
        Assert.True(pedido.Itens.Single().Cancelado);
        Assert.Equal(0m, pedido.Total);
        // Pedido sem valor não se paga.
        await c.PagarAsync(c.Gerente, pedido.Id, "DINHEIRO", 1m).Esperar(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Pedido_com_pagamento_so_se_cancela_depois_de_anular_o_pagamento()
    {
        var c = await CenarioVenda.CriarAsync(app);
        var pedido = await c.AbrirMesaAsync(c.Gerente, c.Mesa1, (c.Prato, 1));
        pedido = await (await c.PagarAsync(c.Gerente, pedido.Id, "DINHEIRO", 100m).Esperar(HttpStatusCode.Created)).Ler<PedidoDto>();

        await c.Gerente.Post($"/api/pedidos/{pedido.Id}/cancelar", new ComMotivo("Cliente saiu", pedido.RowVersion)).Esperar(HttpStatusCode.UnprocessableEntity);

        pedido = await (await c.Gerente.Post($"/api/pagamentos/{pedido.Pagamentos.Single().Id}/anular", new { motivo = "Devolvido ao cliente" })
            .Esperar(HttpStatusCode.OK)).Ler<PedidoDto>();
        Assert.Equal(0m, pedido.TotalPago);

        pedido = await (await c.Gerente.Post($"/api/pedidos/{pedido.Id}/cancelar", new ComMotivo("Cliente saiu", pedido.RowVersion)).Esperar(HttpStatusCode.OK)).Ler<PedidoDto>();
        Assert.Equal(EstadoPedido.Cancelado, pedido.Estado);
        Assert.Equal(EstadoMesa.Livre, (await c.Gerente.Obter<MesaDto>($"/api/mesas/{c.Mesa1.Id}")).Estado);
        await using var db = app.CriarContexto();
        Assert.Contains(db.RegistosAuditoria, a => a.Accao == "pedido.cancelar" && a.EntidadeId == pedido.Id.ToString());
    }

    [Fact]
    public async Task Delivery_exige_contacto_e_endereco()
    {
        var c = await CenarioVenda.CriarAsync(app);
        await c.Gerente.PostComChave("/api/pedidos", CenarioVenda.Chave(),
            new NovoPedido(TipoPedido.Delivery, null, null, null, 1, "840000000", null, null, false, [new NovoItem(c.Prato.Id, 1, null)]))
            .Esperar(HttpStatusCode.UnprocessableEntity);
        var r = await c.Gerente.PostComChave("/api/pedidos", CenarioVenda.Chave(),
            new NovoPedido(TipoPedido.Delivery, null, null, null, 1, "840000000", "Av. Julius Nyerere 100", null, false, [new NovoItem(c.Prato.Id, 1, null)]))
            .Esperar(HttpStatusCode.Created);
        Assert.Equal(EstadoEntrega.Recebido, (await r.Ler<PedidoDto>()).EstadoEntrega);
    }

    [Fact]
    public async Task Produto_esgotado_nao_entra_no_pedido()
    {
        var c = await CenarioVenda.CriarAsync(app);
        await c.Gerente.Post($"/api/produtos/{c.Prato.Id}/disponibilidade", new { disponivel = false }).Esperar(HttpStatusCode.OK);
        await c.Gerente.PostComChave("/api/pedidos", CenarioVenda.Chave(),
            new NovoPedido(TipoPedido.Mesa, c.Mesa1.Id, null, null, 2, null, null, null, false, [new NovoItem(c.Prato.Id, 1, null)]))
            .Esperar(HttpStatusCode.UnprocessableEntity);
        // A mesa não ficou ocupada pela tentativa falhada.
        Assert.Equal(EstadoMesa.Livre, (await c.Gerente.Obter<MesaDto>($"/api/mesas/{c.Mesa1.Id}")).Estado);
    }
}
