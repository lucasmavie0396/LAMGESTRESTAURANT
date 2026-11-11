using System.Net;
using LAMGestRestaurant.Api.Controllers;
using LAMGestRestaurant.Application.Clientes;
using LAMGestRestaurant.Application.Facturacao;
using LAMGestRestaurant.Application.Impressao;
using LAMGestRestaurant.Application.Relatorios;
using LAMGestRestaurant.Application.Seguranca;
using LAMGestRestaurant.Application.Vendas;
using LAMGestRestaurant.Domain.Comum;
using LAMGestRestaurant.Domain.Facturacao;
using LAMGestRestaurant.Tests.Infraestrutura;
using static LAMGestRestaurant.Tests.Infraestrutura.ExtensoesHttp;

namespace LAMGestRestaurant.Tests;

/// <summary>Conta corrente do cliente: extrato com saldo, histórico de documentos, facturas a crédito e impressão em 80 mm / A4.</summary>
[Collection(ColeccaoBaseDados.Nome)]
public class ContaCorrenteTests(AplicacaoTeste app)
{
    private static DateOnly Hoje() =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("Africa/Maputo")));

    private static string Texto(DocumentoImpressao d) => TextoDocumento.De(d).Replace(" | ", " ");

    [Fact]
    public async Task Extrato_historico_e_facturas_a_credito_somam_facturas_recibos_e_notas_de_credito()
    {
        var c = await CenarioVenda.CriarAsync(app);
        var hoje = Hoje();
        var cliente = await (await c.Gerente.Post("/api/clientes", new GuardarCliente(TipoCliente.Empresa, Unico("Conta "), null, null, null,
            "400777888", null, null, null)).Esperar(HttpStatusCode.OK)).Ler<ClienteDto>();
        async Task<DocumentoDto> FacturaAsync(decimal valor) =>
            await (await c.Gerente.PostComChave("/api/documentos/facturas", CenarioVenda.Chave(), new NovoDocumento(cliente.Id, null, null, null, hoje.AddDays(30), null,
                [new NovaLinhaDocumento(null, "Serviço de catering", 1, valor, null, null)])).Esperar(HttpStatusCode.Created)).Ler<DocumentoDto>();
        async Task<DocumentoDto> ReciboAsync(int facturaId, decimal valor) =>
            await (await c.Gerente.PostComChave("/api/documentos/recibos", CenarioVenda.Chave(),
                new NovoRecibo(cliente.Id, c.Metodos["DINHEIRO"], null, c.Sessao.Id, null, [new LiquidacaoNova(facturaId, valor)])).Esperar(HttpStatusCode.Created)).Ler<DocumentoDto>();

        var ft1 = await FacturaAsync(1000m);
        var ft2 = await FacturaAsync(500m);
        var rc = await ReciboAsync(ft1.Id, 300m);
        var anulado = await ReciboAsync(ft1.Id, 200m);
        await c.Gerente.Post($"/api/documentos/{anulado.Id}/anular", new ComMotivo("Cheque devolvido", anulado.RowVersion)).Esperar(HttpStatusCode.OK);
        var nc = await (await c.Gerente.PostComChave("/api/documentos/notas-credito", CenarioVenda.Chave(), new NovaNotaCredito(ft2.Id, "Serviço não prestado", null))
            .Esperar(HttpStatusCode.Created)).Ler<DocumentoDto>();

        // Extrato de hoje: 1 500 de facturas − 300 do recibo − 500 da nota de crédito = 700; o recibo anulado não conta.
        var dia = $"desde={hoje:yyyy-MM-dd}&ate={hoje:yyyy-MM-dd}";
        var extrato = await c.Gerente.Obter<ExtratoContaCorrente>($"/api/documentos/conta-corrente/{cliente.Id}/extrato?{dia}");
        Assert.Equal((0m, 1500m, 800m, 700m), (extrato.SaldoAnterior, extrato.Debitos, extrato.Creditos, extrato.SaldoFinal));
        Assert.Equal([ft1.Numero, ft2.Numero, rc.Numero, nc.Numero], extrato.Movimentos.Select(m => m.Numero));
        Assert.Equal([1000m, 1500m, 1200m, 700m], extrato.Movimentos.Select(m => m.Saldo));
        Assert.Contains(ft1.Numero, extrato.Movimentos[2].Descricao);
        Assert.Equal((700m, 0m), (extrato.EmDividaHoje, extrato.VencidoHoje));
        Assert.Equal(ft1.Id, Assert.Single(extrato.FacturasEmAberto).Id);
        // A partir de amanhã, tudo é saldo anterior.
        var seguinte = await c.Gerente.Obter<ExtratoContaCorrente>(
            $"/api/documentos/conta-corrente/{cliente.Id}/extrato?desde={hoje.AddDays(1):yyyy-MM-dd}&ate={hoje.AddDays(5):yyyy-MM-dd}");
        Assert.Equal((700m, 700m), (seguinte.SaldoAnterior, seguinte.SaldoFinal));
        Assert.Empty(seguinte.Movimentos);

        // Histórico: todos os documentos (o recibo anulado aparece, mas não soma).
        var historico = await c.Gerente.Obter<HistoricoCliente>($"/api/documentos/historico-cliente/{cliente.Id}?{dia}");
        Assert.Equal(5, historico.Documentos.Count);
        Assert.Equal(EstadoDocumento.Anulado, historico.Documentos.Single(d => d.Id == anulado.Id).Estado);
        Assert.Equal((4, 1500m, 300m, 500m, 700m), (historico.Totais.Documentos, historico.Totais.Facturado, historico.Totais.Recebido, historico.Totais.Creditado, historico.Totais.EmDivida));
        var soFacturas = await c.Gerente.Obter<HistoricoCliente>($"/api/documentos/historico-cliente/{cliente.Id}?{dia}&tipo=Factura");
        Assert.Equal(2, soFacturas.Documentos.Count);

        // Facturas a crédito do cliente: situação de cada uma e dívida de hoje (ainda não vencida).
        var relatorio = await c.Gerente.Obter<RelatorioFacturasCredito>($"/api/documentos/facturas-credito?{dia}&clienteId={cliente.Id}");
        Assert.Equal((2, 1500m, 300m, 500m, 700m, 0m), (relatorio.Resumo.Facturas, relatorio.Resumo.Facturado, relatorio.Resumo.Pago, relatorio.Resumo.Creditado,
            relatorio.Resumo.EmDivida, relatorio.Resumo.Vencido));
        Assert.Equal([SituacaoFactura.Parcial, SituacaoFactura.Creditada], relatorio.Facturas.Select(f => f.Situacao));
        var divida = Assert.Single(relatorio.Dividas);
        Assert.Equal((cliente.Id, 1, 700m, 700m), (divida.ClienteId, divida.Facturas, divida.EmDivida, divida.NaoVencido));
        var emDivida = await c.Gerente.Obter<RelatorioFacturasCredito>($"/api/documentos/facturas-credito?{dia}&clienteId={cliente.Id}&emDivida=true");
        Assert.Equal(ft1.Id, Assert.Single(emDivida.Facturas).Id);
        // Todos os clientes: inclui este.
        Assert.Contains((await c.Gerente.Obter<RelatorioFacturasCredito>($"/api/documentos/facturas-credito?{dia}")).Dividas, d => d.ClienteId == cliente.Id);

        // Documentos imprimíveis (mesmos números).
        string Url(TipoDocumentoRelatorio tipo) => $"/api/relatorios/documento?tipo={tipo}&{dia}&clienteId={cliente.Id}";
        var textoExtrato = Texto(await c.Gerente.Obter<DocumentoImpressao>(Url(TipoDocumentoRelatorio.ExtratoCliente)));
        foreach (var esperado in new[] { "EXTRATO DE CONTA CORRENTE", cliente.Nome, "NUIT 400777888", ft1.Numero, nc.Numero, $"SALDO EM {hoje:dd/MM/yyyy}", "700,00" })
            Assert.Contains(esperado, textoExtrato);
        Assert.DoesNotContain(anulado.Numero, textoExtrato);
        var textoHistorico = Texto(await c.Gerente.Obter<DocumentoImpressao>(Url(TipoDocumentoRelatorio.HistoricoCliente)));
        Assert.Contains($"{anulado.Numero} · Recibo (ANULADO)", textoHistorico);
        var textoFacturas = Texto(await c.Gerente.Obter<DocumentoImpressao>(Url(TipoDocumentoRelatorio.FacturasCredito)));
        foreach (var esperado in new[] { "FACTURAS A CRÉDITO", ft1.Numero, "Parcial", "Creditada", "TOTAL EM DÍVIDA" })
            Assert.Contains(esperado, textoFacturas);
        // Histórico e extrato sem cliente: recusado.
        await c.Gerente.GetAsync($"/api/relatorios/documento?tipo={TipoDocumentoRelatorio.ExtratoCliente}&{dia}").Esperar(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Fatura_a_Credito_no_POS_gera_factura_do_cliente_e_o_Pagamento_de_Fatura_gera_recibos_parciais()
    {
        var c = await CenarioVenda.CriarAsync(app);
        var credito = c.Metodos[ServicoDocumentos.CodigoFacturaCredito];
        var dinheiro = c.Metodos["DINHEIRO"];
        var cliente = await (await c.Gerente.Post("/api/clientes", new GuardarCliente(TipoCliente.Particular, Unico("Fiado "), null, null, null, null, null, null, null))
            .Esperar(HttpStatusCode.OK)).Ler<ClienteDto>();
        var pedido = await c.AbrirMesaAsync(c.Gerente, c.Mesa1, (c.Prato, 1)); // 450,50
        RegistarPagamentos Corpo(decimal valor, int? clienteId, int? outro = null) => new(c.Sessao.Id,
            outro is int o ? [new LinhaPagamento(credito, valor, valor, null), new LinhaPagamento(o, 1m, 1m, null)] : [new LinhaPagamento(credito, valor, valor, null)], false, clienteId);
        Task<HttpResponseMessage> Pagar(RegistarPagamentos corpo) => c.Gerente.PostComChave($"/api/pedidos/{pedido.Id}/pagamentos/multiplos", CenarioVenda.Chave(), corpo);

        await Pagar(Corpo(pedido.Total, null)).Esperar(HttpStatusCode.UnprocessableEntity);              // sem cliente
        await Pagar(Corpo(100m, cliente.Id)).Esperar(HttpStatusCode.UnprocessableEntity);                 // só uma parte
        await Pagar(Corpo(pedido.Total - 1m, cliente.Id, dinheiro)).Esperar(HttpStatusCode.UnprocessableEntity); // misturado
        var antes = await app.EsperadoAsync(c.Sessao.Id, dinheiro);
        var pago = await (await Pagar(Corpo(pedido.Total, cliente.Id)).Esperar(HttpStatusCode.Created)).Ler<PedidoDto>();
        Assert.Equal((EstadoPedido.Pago, cliente.Id), (pago.Estado, pago.ClienteId));
        Assert.StartsWith("FT-", pago.ReciboNumero);
        Assert.Equal(antes, await app.EsperadoAsync(c.Sessao.Id, dinheiro)); // não entrou dinheiro

        // Entra na conta corrente e no histórico do cliente; o recibo do pedido é a própria factura.
        var conta = await c.Gerente.Obter<ContaCorrente>($"/api/documentos/conta-corrente/{cliente.Id}");
        var factura = Assert.Single(conta.Facturas);
        Assert.Equal((pago.ReciboNumero, 450.50m), (factura.Numero, factura.EmDivida));
        Assert.NotNull(factura.Vencimento);
        Assert.Contains(factura.Numero, TextoDocumento.De(await c.Gerente.Obter<DocumentoImpressao>($"/api/pedidos/{pedido.Id}/recibo/documento")));
        var hoje = Hoje();
        var historico = await c.Gerente.Obter<HistoricoCliente>($"/api/documentos/historico-cliente/{cliente.Id}?desde={hoje:yyyy-MM-dd}&ate={hoje:yyyy-MM-dd}");
        Assert.Equal(TipoDocumento.Factura, Assert.Single(historico.Documentos).Tipo);
        // Corrige-se com nota de crédito, não com estorno; o método não aparece no fecho do caixa.
        await c.Gerente.Post($"/api/pedidos/{pedido.Id}/estornar", new EstornarPedido(c.Sessao.Id, dinheiro, "Engano", pago.RowVersion)).Esperar(HttpStatusCode.UnprocessableEntity);
        Assert.DoesNotContain((await c.Gerente.Obter<Application.Caixa.SessaoCaixaDto>($"/api/caixa/sessoes/{c.Sessao.Id}")).PorMetodo, m => m.MetodoPagamentoId == credito);

        // Pagamento de Fatura pelo operador de caixa: parcial e depois o resto, com recibo que imprime.
        await c.Gerente.PostComChave("/api/documentos/recibos", CenarioVenda.Chave(),
            new NovoRecibo(cliente.Id, credito, null, c.Sessao.Id, null, [new LiquidacaoNova(factura.Id, 10m)])).Esperar(HttpStatusCode.UnprocessableEntity);
        var parcial = await (await c.Gerente.PostComChave("/api/documentos/recibos", CenarioVenda.Chave(),
            new NovoRecibo(cliente.Id, dinheiro, null, c.Sessao.Id, null, [new LiquidacaoNova(factura.Id, 150.50m)])).Esperar(HttpStatusCode.Created)).Ler<DocumentoDto>();
        Assert.StartsWith("RC-", parcial.Numero);
        Assert.Equal(300m, Assert.Single((await c.Gerente.Obter<ContaCorrente>($"/api/documentos/conta-corrente/{cliente.Id}")).Facturas).EmDivida);
        await c.Gerente.PostComChave("/api/documentos/recibos", CenarioVenda.Chave(),
            new NovoRecibo(cliente.Id, dinheiro, null, c.Sessao.Id, null, [new LiquidacaoNova(factura.Id, 300m)])).Esperar(HttpStatusCode.Created);
        Assert.Empty((await c.Gerente.Obter<ContaCorrente>($"/api/documentos/conta-corrente/{cliente.Id}")).Facturas);
        Assert.Equal(antes + 450.50m, await app.EsperadoAsync(c.Sessao.Id, dinheiro));

        var caixa = await app.ClienteComPermissoesAsync(Permissoes.PagamentosRegistar);
        await caixa.GetAsync($"/api/documentos/conta-corrente/{cliente.Id}").Esperar(HttpStatusCode.OK);
        await caixa.GetAsync($"/api/documentos/{parcial.Id}/impressao").Esperar(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Contas_correntes_exigem_facturacao_ou_relatorios_e_o_periodo_e_validado()
    {
        var c = await CenarioVenda.CriarAsync(app);
        var hoje = Hoje();
        var cliente = await (await c.Gerente.Post("/api/clientes", new GuardarCliente(TipoCliente.Particular, Unico("Sem conta "), null, null, null,
            null, null, null, null)).Esperar(HttpStatusCode.OK)).Ler<ClienteDto>();
        var dia = $"desde={hoje:yyyy-MM-dd}&ate={hoje:yyyy-MM-dd}";

        var pos = await app.ClienteComPermissoesAsync(Permissoes.PosUsar);
        await pos.GetAsync($"/api/documentos/conta-corrente/{cliente.Id}/extrato?{dia}").Esperar(HttpStatusCode.Forbidden);
        await pos.GetAsync($"/api/documentos/facturas-credito?{dia}").Esperar(HttpStatusCode.Forbidden);
        await pos.GetAsync($"/api/relatorios/documento?tipo={TipoDocumentoRelatorio.FacturasCredito}&{dia}").Esperar(HttpStatusCode.Forbidden);

        var relatorios = await app.ClienteComPermissoesAsync(Permissoes.RelatoriosVendas);
        var vazio = await relatorios.Obter<ExtratoContaCorrente>($"/api/documentos/conta-corrente/{cliente.Id}/extrato?{dia}");
        Assert.Equal((0m, 0m), (vazio.SaldoAnterior, vazio.SaldoFinal));
        Assert.Contains("Sem movimentos no período.", Texto(await relatorios.Obter<DocumentoImpressao>(
            $"/api/relatorios/documento?tipo={TipoDocumentoRelatorio.ExtratoCliente}&{dia}&clienteId={cliente.Id}")));

        await c.Gerente.GetAsync($"/api/documentos/conta-corrente/{cliente.Id}/extrato?desde={hoje:yyyy-MM-dd}&ate={hoje.AddDays(-1):yyyy-MM-dd}")
            .Esperar(HttpStatusCode.UnprocessableEntity);
        await c.Gerente.GetAsync($"/api/documentos/conta-corrente/999999/extrato?{dia}").Esperar(HttpStatusCode.NotFound);
        // O extrato pode abranger vários anos; o relatório das facturas, no máximo um.
        await c.Gerente.GetAsync($"/api/documentos/conta-corrente/{cliente.Id}/extrato?desde={hoje.AddYears(-3):yyyy-MM-dd}&ate={hoje:yyyy-MM-dd}").Esperar(HttpStatusCode.OK);
        await c.Gerente.GetAsync($"/api/documentos/facturas-credito?desde={hoje.AddYears(-3):yyyy-MM-dd}&ate={hoje:yyyy-MM-dd}").Esperar(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Relatorio_numa_impressora_do_servidor_sai_no_papel_escolhido_80mm_ou_A4()
    {
        var c = await CenarioVenda.CriarAsync(app);
        var hoje = Hoje();
        var impressora = await (await c.Gerente.Post("/api/configuracoes/impressoras",
            new GuardarImpressora(Unico("Caixa "), FuncaoImpressora.Caixa, ImpressorasTeste.Caixa, 80, null, null)).Esperar(HttpStatusCode.OK)).Ler<ImpressoraDto>();
        try
        {
            await c.Gerente.Post("/api/relatorios/documento/imprimir",
                new ImprimirRelatorio(TipoDocumentoRelatorio.FacturasCredito, hoje, hoje, null, impressora.Id, null, 300)).Esperar(HttpStatusCode.UnprocessableEntity);

            var a4 = await (await c.Gerente.Post("/api/relatorios/documento/imprimir",
                new ImprimirRelatorio(TipoDocumentoRelatorio.FacturasCredito, hoje, hoje, null, impressora.Id, null, 210)).Esperar(HttpStatusCode.Accepted)).Ler<RespostaImpressao>();
            Assert.Equal(210, a4.LarguraPapelMm);
            Assert.Equal(210, Assert.Single(await app.Impressoras.EsperarAsync("FACTURAS A CRÉDITO")).LarguraPapelMm);

            var talao = await (await c.Gerente.Post("/api/relatorios/documento/imprimir",
                new ImprimirRelatorio(TipoDocumentoRelatorio.Despesas, hoje, hoje, null, impressora.Id)).Esperar(HttpStatusCode.Accepted)).Ler<RespostaImpressao>();
            Assert.Equal(80, talao.LarguraPapelMm); // sem escolha: o papel da impressora
            Assert.Equal(80, (await app.Impressoras.EsperarAsync("RELATÓRIO DE DESPESAS"))[^1].LarguraPapelMm);
        }
        finally
        {
            await c.Gerente.Post($"/api/configuracoes/impressoras/{impressora.Id}/activo", new { activo = false }).Esperar(HttpStatusCode.NoContent);
        }
    }

    private sealed record RespostaImpressao(string Impressora, bool Pdf, int LarguraPapelMm);
}
