using System.Net;
using LAMGestRestaurant.Api.Controllers;
using LAMGestRestaurant.Application.Catalogo;
using LAMGestRestaurant.Application.Clientes;
using LAMGestRestaurant.Application.Impressao;
using LAMGestRestaurant.Application.Relatorios;
using LAMGestRestaurant.Application.Seguranca;
using LAMGestRestaurant.Application.Vendas;
using LAMGestRestaurant.Domain.Comum;
using LAMGestRestaurant.Tests.Infraestrutura;
using static LAMGestRestaurant.Tests.Infraestrutura.ExtensoesHttp;

namespace LAMGestRestaurant.Tests;

/// <summary>Relatórios de detalhe: lista de vendas, clientes, IVA e descontos, anulações; acrescentos ao resumo e ao painel.</summary>
[Collection(ColeccaoBaseDados.Nome)]
public class RelatoriosDetalheTests(AplicacaoTeste app)
{
    private static readonly TimeZoneInfo Maputo = TimeZoneInfo.FindSystemTimeZoneById("Africa/Maputo");
    private static string Hoje() => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Maputo)).ToString("yyyy-MM-dd");

    private static async Task<PedidoDto> PagarAsync(CenarioVenda c, PedidoDto pedido, params LinhaPagamento[] linhas) =>
        await (await c.Gerente.PostComChave($"/api/pedidos/{pedido.Id}/pagamentos/multiplos", CenarioVenda.Chave(), new RegistarPagamentos(c.Sessao.Id, linhas))
            .Esperar(HttpStatusCode.Created)).Ler<PedidoDto>();

    [Fact]
    public async Task Lista_de_vendas_mostra_cada_venda_e_os_totais_batem_com_o_resumo()
    {
        var c = await CenarioVenda.CriarAsync(app);
        var pedido = await c.AbrirMesaAsync(c.Gerente, c.Mesa1, (c.Prato, 2)); // 901,00
        pedido = await (await c.Gerente.Post($"/api/pedidos/{pedido.Id}/desconto", new AplicarDesconto(101m, null, pedido.RowVersion)).Esperar(HttpStatusCode.OK)).Ler<PedidoDto>();
        pedido = await PagarAsync(c, pedido, new(c.Metodos["DINHEIRO"], 500m, 600m, null), new(c.Metodos["CARTAO"], 300m, 300m, null));

        var lista = await c.Gerente.Obter<ListaVendas>($"/api/relatorios/lista-vendas?desde={Hoje()}&ate={Hoje()}&pesquisa={pedido.Numero}");
        var linha = Assert.Single(lista.Vendas.Itens);
        Assert.Equal((901m, 101m, 800m, 900m, 100m), (linha.Subtotal, linha.Desconto, linha.Total, linha.Recebido, linha.Troco));
        Assert.Equal("Dinheiro, Cartão", linha.Metodos);
        Assert.Equal(pedido.ReciboNumero, linha.Recibo);
        Assert.Equal(EstadoPedido.Pago, linha.Estado);
        Assert.Contains(lista.Metodos, m => m.Nome == "M-Pesa");

        // Sem filtros: os totais de todas as linhas (não só da página) são os do resumo de vendas do mesmo dia.
        var todas = await c.Gerente.Obter<ListaVendas>($"/api/relatorios/lista-vendas?desde={Hoje()}&ate={Hoje()}&tamanho=1");
        var resumo = await c.Gerente.Obter<RelatorioVendas>($"/api/relatorios/vendas?desde={Hoje()}&ate={Hoje()}");
        Assert.Single(todas.Vendas.Itens);
        Assert.Equal((resumo.NumeroPedidos, resumo.TotalVendas, resumo.Descontos, resumo.Impostos), (todas.Totais.Vendas, todas.Totais.Total, todas.Totais.Desconto, todas.Totais.Impostos));

        // Filtro por método: só aparece com Cartão, não com M-Pesa.
        Assert.Contains((await c.Gerente.Obter<ListaVendas>($"/api/relatorios/lista-vendas?desde={Hoje()}&ate={Hoje()}&metodoPagamentoId={c.Metodos["CARTAO"]}")).Vendas.Itens, v => v.Id == pedido.Id);
        Assert.DoesNotContain((await c.Gerente.Obter<ListaVendas>($"/api/relatorios/lista-vendas?desde={Hoje()}&ate={Hoje()}&metodoPagamentoId={c.Metodos["MPESA"]}&tamanho=200")).Vendas.Itens, v => v.Id == pedido.Id);
    }

    [Fact]
    public async Task Lista_de_vendas_filtra_por_operador_estado_e_hora()
    {
        var c = await CenarioVenda.CriarAsync(app);
        var (outro, _) = await app.ClienteAutenticadoAsync(PerfisPadrao.Administrador);
        var outroId = (await outro.Obter<SessaoUtilizador>("/api/auth/me")).Id;
        var meu = await c.AbrirMesaAsync(c.Gerente, c.Mesa1, (c.Bebida, 1));
        var dele = await c.AbrirMesaAsync(outro, c.Mesa2, (c.Bebida, 2));
        await c.PagarAsync(c.Gerente, meu.Id, "DINHEIRO", meu.Total).Esperar(HttpStatusCode.Created);
        await c.PagarAsync(outro, dele.Id, "DINHEIRO", dele.Total).Esperar(HttpStatusCode.Created);

        var doOutro = await c.Gerente.Obter<ListaVendas>($"/api/relatorios/lista-vendas?desde={Hoje()}&ate={Hoje()}&operadorId={outroId}");
        Assert.Equal([dele.Id], doOutro.Vendas.Itens.Select(v => v.Id));
        Assert.Contains(doOutro.Operadores, o => o.Id == outroId);

        // Cancelado: não é venda; aparece só com o estado Cancelado, com o motivo.
        var cancelado = await c.AbrirMesaAsync(c.Gerente, c.Mesa1, (c.Prato, 1));
        await c.Gerente.Post($"/api/pedidos/{cancelado.Id}/cancelar", new ComMotivo("Cliente saiu", cancelado.RowVersion)).Esperar(HttpStatusCode.OK);
        var pagos = await c.Gerente.Obter<ListaVendas>($"/api/relatorios/lista-vendas?desde={Hoje()}&ate={Hoje()}&todas=true");
        Assert.DoesNotContain(pagos.Vendas.Itens, v => v.Id == cancelado.Id);
        var cancelados = await c.Gerente.Obter<ListaVendas>($"/api/relatorios/lista-vendas?desde={Hoje()}&ate={Hoje()}&estado=Cancelado&pesquisa={cancelado.Numero}");
        Assert.Equal("Cliente saiu", Assert.Single(cancelados.Vendas.Itens).Motivo);

        // Janela de horas (locais): com a hora actual aparece; numa janela de outras horas, não.
        var agora = TimeOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Maputo));
        string Hora(TimeOnly h) => h.ToString("HH\\:mm");
        var dentro = await c.Gerente.Obter<ListaVendas>($"/api/relatorios/lista-vendas?desde={Hoje()}&ate={Hoje()}&pesquisa={meu.Numero}&horaDe={Hora(agora.AddHours(-1))}&horaAte={Hora(agora.AddHours(1))}");
        Assert.Single(dentro.Vendas.Itens);
        var fora = await c.Gerente.Obter<ListaVendas>($"/api/relatorios/lista-vendas?desde={Hoje()}&ate={Hoje()}&pesquisa={meu.Numero}&horaDe={Hora(agora.AddHours(2))}&horaAte={Hora(agora.AddHours(3))}");
        Assert.Empty(fora.Vendas.Itens);
    }

    [Fact]
    public async Task Iva_por_taxa_bate_com_o_imposto_dos_pedidos_e_separa_o_isento()
    {
        var c = await CenarioVenda.CriarAsync(app);
        var isento = await (await c.Gerente.Post("/api/produtos", new GuardarProduto(Unico("I", 10), null, "Água isenta", null, c.Prato.CategoriaId,
            TipoProduto.Bar, 50m, 20m, null, null, false, true, null)).Esperar(HttpStatusCode.Created)).Ler<ProdutoDto>();
        var pedido = await c.AbrirMesaAsync(c.Gerente, c.Mesa1, (c.Prato, 1), (isento, 2)); // 450,50 (16%) + 100 (0%)
        pedido = await (await c.Gerente.Post($"/api/pedidos/{pedido.Id}/desconto", new AplicarDesconto(null, 10m, pedido.RowVersion)).Esperar(HttpStatusCode.OK)).Ler<PedidoDto>();
        await c.PagarAsync(c.Gerente, pedido.Id, "DINHEIRO", pedido.Total).Esperar(HttpStatusCode.Created);

        var r = await c.Gerente.Obter<RelatorioImpostos>($"/api/relatorios/impostos?desde={Hoje()}&ate={Hoje()}");
        var resumo = await c.Gerente.Obter<RelatorioVendas>($"/api/relatorios/vendas?desde={Hoje()}&ate={Hoje()}");
        Assert.Equal(resumo.Impostos, r.Impostos);
        Assert.Equal(resumo.TotalVendas, r.Total);
        Assert.Equal(r.Impostos, r.PorTaxa.Sum(t => t.Imposto));
        Assert.Equal(r.Total - r.TaxasEntrega, r.PorTaxa.Sum(t => t.ComImposto));
        Assert.Equal(r.Total, r.BaseSemImposto + r.Impostos + r.TaxasEntrega);
        var zero = Assert.Single(r.PorTaxa, t => t.Percentagem == 0);
        Assert.Equal(("Isento (0%)", 0m), (zero.Taxa, zero.Imposto));
        Assert.Contains(r.PorTaxa, t => t.Taxa == "IVA 16%");
        Assert.Equal("dia", r.Agrupamento);
        Assert.Equal(r.Total, r.PorPeriodo.Sum(p => p.Total));
    }

    [Fact]
    public async Task Vendas_por_cliente_so_conta_vendas_com_cliente_associado()
    {
        var c = await CenarioVenda.CriarAsync(app);
        var nome = Unico("Cliente Rel ");
        var cliente = await (await c.Gerente.Post("/api/clientes", new GuardarCliente(TipoCliente.Particular, nome, "84" + Random.Shared.Next(1000000, 9999999),
            null, null, null, null, null, null)).Esperar(HttpStatusCode.OK)).Ler<ClienteDto>();
        foreach (var produto in new[] { c.Prato, c.Bebida })
        {
            var pedido = await c.AbrirMesaAsync(c.Gerente, c.Mesa1, (produto, 1));
            pedido = await (await c.Gerente.Post($"/api/pedidos/{pedido.Id}/cliente", new AssociarCliente(cliente.Id, pedido.RowVersion)).Esperar(HttpStatusCode.OK)).Ler<PedidoDto>();
            await c.PagarAsync(c.Gerente, pedido.Id, "DINHEIRO", pedido.Total).Esperar(HttpStatusCode.Created);
        }

        var r = await c.Gerente.Obter<RelatorioClientes>($"/api/relatorios/clientes?desde={Hoje()}&ate={Hoje()}&pesquisa={Uri.EscapeDataString(nome)}");
        var linha = Assert.Single(r.Clientes);
        Assert.Equal((cliente.Id, 2, 510.50m), (linha.ClienteId, linha.Compras, linha.Total));
        Assert.NotNull(linha.Telefone); // o administrador vê clientes

        // Um perfil só com relatórios de vendas não vê o telefone.
        var semClientes = await app.ClienteComPermissoesAsync(Permissoes.RelatoriosVendas);
        var r2 = await semClientes.Obter<RelatorioClientes>($"/api/relatorios/clientes?desde={Hoje()}&ate={Hoje()}&pesquisa={Uri.EscapeDataString(nome)}");
        Assert.Null(Assert.Single(r2.Clientes).Telefone);
    }

    [Fact]
    public async Task Anulacoes_lista_cancelados_estornos_itens_e_pagamentos_anulados_com_o_motivo()
    {
        var c = await CenarioVenda.CriarAsync(app);
        // Item cancelado depois de enviado, num pedido que continua.
        var p1 = await c.AbrirMesaAsync(c.Gerente, c.Mesa1, (c.Prato, 1), (c.Bebida, 1));
        p1 = await (await c.Gerente.Post($"/api/pedidos/{p1.Id}/enviar-cozinha", new ComVersao(p1.RowVersion)).Esperar(HttpStatusCode.OK)).Ler<PedidoDto>();
        var item = p1.Itens.Single(i => i.ProdutoId == c.Prato.Id);
        p1 = await (await c.Gerente.Post($"/api/pedidos/{p1.Id}/itens/{item.Id}/cancelar", new ComMotivo("Queimado", p1.RowVersion)).Esperar(HttpStatusCode.OK)).Ler<PedidoDto>();
        // Pagamento parcial anulado.
        p1 = await (await c.PagarAsync(c.Gerente, p1.Id, "DINHEIRO", 10m).Esperar(HttpStatusCode.Created)).Ler<PedidoDto>();
        await c.Gerente.Post($"/api/pagamentos/{p1.Pagamentos.Single().Id}/anular", new MotivoSimples("Engano no valor")).Esperar(HttpStatusCode.OK);
        // Estorno de um pedido pago.
        var p2 = await c.AbrirMesaAsync(c.Gerente, c.Mesa2, (c.Bebida, 2));
        p2 = await (await c.PagarAsync(c.Gerente, p2.Id, "DINHEIRO", p2.Total).Esperar(HttpStatusCode.Created)).Ler<PedidoDto>();
        await c.Gerente.Post($"/api/pedidos/{p2.Id}/estornar", new EstornarPedido(c.Sessao.Id, c.Metodos["DINHEIRO"], "Bebida estragada", p2.RowVersion)).Esperar(HttpStatusCode.OK);

        var r = await c.Gerente.Obter<RelatorioAnulacoes>($"/api/relatorios/anulacoes?desde={Hoje()}&ate={Hoje()}");
        var itemAnulado = Assert.Single(r.ItensCancelados, i => i.PedidoId == p1.Id);
        Assert.Equal(("Queimado", 450.50m), (itemAnulado.Motivo, itemAnulado.Valor));
        Assert.NotNull(itemAnulado.Por);
        Assert.Equal("Engano no valor", Assert.Single(r.PagamentosAnulados, p => p.PedidoId == p1.Id).Motivo);
        var estorno = Assert.Single(r.Estornos, e => e.Id == p2.Id);
        Assert.Equal(("Bebida estragada", 120m, p2.ReciboNumero), (estorno.Motivo, estorno.Valor, estorno.Recibo));

        // O estorno não é venda.
        var vendas = await c.Gerente.Obter<ListaVendas>($"/api/relatorios/lista-vendas?desde={Hoje()}&ate={Hoje()}&todas=true");
        Assert.DoesNotContain(vendas.Vendas.Itens, v => v.Id == p2.Id);
    }

    [Fact]
    public async Task Resumo_de_vendas_traz_produtos_sem_vendas_e_operadores_e_o_painel_o_mes()
    {
        var c = await CenarioVenda.CriarAsync(app);
        var pedido = await c.AbrirMesaAsync(c.Gerente, c.Mesa1, (c.Prato, 1));
        pedido = await (await c.Gerente.Post($"/api/pedidos/{pedido.Id}/desconto", new AplicarDesconto(50.50m, null, pedido.RowVersion)).Esperar(HttpStatusCode.OK)).Ler<PedidoDto>();
        await c.PagarAsync(c.Gerente, pedido.Id, "DINHEIRO", pedido.Total).Esperar(HttpStatusCode.Created);

        var r = await c.Gerente.Obter<RelatorioVendas>($"/api/relatorios/vendas?desde={Hoje()}&ate={Hoje()}");
        Assert.Contains(r.ProdutosSemVendas!, p => p.ProdutoId == c.Bebida.Id); // a bebida deste cenário não foi vendida
        Assert.DoesNotContain(r.ProdutosSemVendas!, p => p.ProdutoId == c.Prato.Id);
        Assert.Equal(c.Prato.Codigo, r.Produtos.Single(p => p.ProdutoId == c.Prato.Id).Codigo);
        Assert.Equal(r.TotalVendas, r.Operadores!.Sum(o => o.Total));
        Assert.Equal(r.Descontos, r.Operadores!.Sum(o => o.Descontos));
        Assert.All(r.Operadores!, o => Assert.Equal(o.Total, o.Metodos.Sum(m => m.Valor)));

        var painel = await c.Gerente.Obter<Dashboard>("/api/dashboard");
        Assert.True(painel.VendasMes >= painel.VendasHoje && painel.PedidosMes >= painel.PedidosHoje);
    }

    [Theory]
    [InlineData(TipoDocumentoRelatorio.ListaVendas, "LISTA DE VENDAS")]
    [InlineData(TipoDocumentoRelatorio.Clientes, "VENDAS POR CLIENTE")]
    [InlineData(TipoDocumentoRelatorio.Impostos, "IVA E DESCONTOS")]
    [InlineData(TipoDocumentoRelatorio.Anulacoes, "ANULAÇÕES E DEVOLUÇÕES")]
    public async Task Novos_relatorios_tambem_saem_em_documento_imprimivel(TipoDocumentoRelatorio tipo, string titulo)
    {
        var (gerente, _) = await app.ClienteAutenticadoAsync(PerfisPadrao.Gerente);
        var doc = await gerente.Obter<DocumentoImpressao>($"/api/relatorios/documento?tipo={tipo}&desde={Hoje()}&ate={Hoje()}");
        Assert.Contains(doc.Linhas, l => l.Texto == titulo);
        Assert.Contains(doc.Linhas, l => l.Texto.StartsWith("Emitido em", StringComparison.Ordinal));
        // Só com a permissão de relatórios de vendas.
        var cozinha = (await app.ClienteAutenticadoAsync(PerfisPadrao.Cozinha)).Cliente;
        await cozinha.GetAsync($"/api/relatorios/documento?tipo={tipo}&desde={Hoje()}&ate={Hoje()}").Esperar(HttpStatusCode.Forbidden);
    }
}
