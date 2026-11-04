using System.Net;
using LAMGestRestaurant.Application.Caixa;
using LAMGestRestaurant.Application.Clientes;
using LAMGestRestaurant.Application.Comum;
using LAMGestRestaurant.Application.Facturacao;
using LAMGestRestaurant.Application.Impressao;
using LAMGestRestaurant.Application.Seguranca;
using LAMGestRestaurant.Application.Vendas;
using LAMGestRestaurant.Domain.Comum;
using LAMGestRestaurant.Domain.Facturacao;
using LAMGestRestaurant.Tests.Infraestrutura;
using static LAMGestRestaurant.Tests.Infraestrutura.ExtensoesHttp;

namespace LAMGestRestaurant.Tests;

[Collection(ColeccaoBaseDados.Nome)]
public class FacturacaoTests(AplicacaoTeste app)
{
    private static async Task<ClienteDto> ClienteEmpresaAsync(HttpClient c, string? nuit = "400555666") =>
        await (await c.Post("/api/clientes", new GuardarCliente(TipoCliente.Empresa, Unico("Empresa "), null, null, "Av. 24 de Julho, 100, Maputo",
            nuit, null, null, null)).Esperar(HttpStatusCode.OK)).Ler<ClienteDto>();

    private static async Task<string> TextoAsync(HttpClient c, int documentoId) =>
        string.Join("\n", (await c.Obter<DocumentoImpressao>($"/api/documentos/{documentoId}/impressao")).Linhas.Select(l => l.Texto + " " + l.Direita));

    [Fact]
    public async Task Pedido_pago_emite_VD_com_cliente_IVA_por_taxa_e_desconto_repartido()
    {
        var c = await CenarioVenda.CriarAsync(app);
        var cliente = await ClienteEmpresaAsync(c.Gerente);
        var pedido = await c.AbrirMesaAsync(c.Gerente, c.Mesa1, (c.Prato, 2), (c.Bebida, 1)); // 901 + 60 = 961
        pedido = await (await c.Gerente.Post($"/api/pedidos/{pedido.Id}/cliente", new AssociarCliente(cliente.Id, pedido.RowVersion)).Esperar(HttpStatusCode.OK)).Ler<PedidoDto>();
        pedido = await (await c.Gerente.Post($"/api/pedidos/{pedido.Id}/desconto", new AplicarDesconto(61m, null, pedido.RowVersion)).Esperar(HttpStatusCode.OK)).Ler<PedidoDto>();
        Assert.Equal(900m, pedido.Total);
        pedido = await (await c.PagarAsync(c.Gerente, pedido.Id, "DINHEIRO", 900m, 1000m).Esperar(HttpStatusCode.Created)).Ler<PedidoDto>();
        Assert.StartsWith("VD-", pedido.ReciboNumero);

        var lista = await c.Gerente.Obter<Pagina<DocumentoResumo>>($"/api/documentos?tipo=VendaDinheiro&pesquisa={pedido.Numero}");
        var vd = await c.Gerente.Obter<DocumentoDto>($"/api/documentos/{Assert.Single(lista.Itens).Id}");
        Assert.Equal((pedido.ReciboNumero, pedido.Numero, 900m, 61m), (vd.Numero, vd.PedidoNumero, vd.Total, vd.Desconto));
        Assert.Equal((cliente.Nome, "400555666"), (vd.ClienteNome, vd.ClienteNuit));
        // Desconto repartido em proporção: 61 × 901/961 = 57,19 no prato e 3,81 na bebida; IVA de cada linha sobre o valor pago.
        Assert.Equal([57.19m, 3.81m], vd.Linhas.Select(l => l.Desconto));
        Assert.Equal([843.81m, 56.19m], vd.Linhas.Select(l => l.Total));
        Assert.Equal(vd.Linhas.Sum(l => l.Iva), vd.Iva);
        Assert.Equal(116.39m + 7.75m, vd.Iva);
        Assert.Equal(vd.Total - vd.Iva, vd.BaseTributavel);
        var resumo = Assert.Single(vd.ResumoIva);
        Assert.Equal((16m, vd.BaseTributavel, vd.Iva), (resumo.Percentagem, resumo.Base, resumo.Iva));

        var texto = await TextoAsync(c.Gerente, vd.Id);
        foreach (var esperado in new[] { "VENDA A DINHEIRO", vd.Numero, "NUIT: 400555666", "IVA 16 %", "Processado por computador", "Troco" })
            Assert.Contains(esperado, texto);
        // O recibo do pedido (reimpressão pelo navegador) é a própria VD.
        var recibo = await c.Gerente.Obter<DocumentoImpressao>($"/api/pedidos/{pedido.Id}/recibo/documento");
        Assert.Equal($"Venda a Dinheiro {vd.Numero}", recibo.Titulo);
    }

    [Fact]
    public async Task Estorno_do_pedido_emite_nota_de_credito_total_da_VD()
    {
        var c = await CenarioVenda.CriarAsync(app);
        var pedido = await c.AbrirMesaAsync(c.Gerente, c.Mesa1, (c.Prato, 1));
        pedido = await (await c.PagarAsync(c.Gerente, pedido.Id, "DINHEIRO", 450.50m).Esperar(HttpStatusCode.Created)).Ler<PedidoDto>();
        await c.Gerente.Post($"/api/pedidos/{pedido.Id}/estornar", new EstornarPedido(c.Sessao.Id, c.Metodos["DINHEIRO"], "Cliente devolveu", pedido.RowVersion))
            .Esperar(HttpStatusCode.OK);

        var vdId = Assert.Single((await c.Gerente.Obter<Pagina<DocumentoResumo>>($"/api/documentos?tipo=VendaDinheiro&pesquisa={pedido.Numero}")).Itens).Id;
        var vd = await c.Gerente.Obter<DocumentoDto>($"/api/documentos/{vdId}");
        Assert.Equal(450.50m, vd.ValorCreditado);
        var relacionado = Assert.Single(vd.Relacionados);
        Assert.Equal(TipoDocumento.NotaCredito, relacionado.Tipo);
        var nc = await c.Gerente.Obter<DocumentoDto>($"/api/documentos/{relacionado.Id}");
        Assert.Equal((450.50m, vd.Iva, "Cliente devolveu", vd.Numero), (nc.Total, nc.Iva, nc.Motivo, nc.DocumentoOrigemNumero));
        Assert.StartsWith("NC-", nc.Numero);
        Assert.Contains($"Referente à Venda a Dinheiro {vd.Numero}", await TextoAsync(c.Gerente, nc.Id));
        // Uma VD não se anula nem leva nota de crédito manual: corrige-se pelo estorno.
        await c.Gerente.Post($"/api/documentos/{vd.Id}/anular", new ComMotivo("Engano", vd.RowVersion)).Esperar(HttpStatusCode.UnprocessableEntity);
        await c.Gerente.PostComChave("/api/documentos/notas-credito", CenarioVenda.Chave(), new NovaNotaCredito(vd.Id, "Engano", null))
            .Esperar(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Factura_a_credito_recibos_parciais_nota_de_credito_e_conta_corrente()
    {
        var c = await CenarioVenda.CriarAsync(app);
        var cliente = await ClienteEmpresaAsync(c.Gerente);
        var taxas = await c.Gerente.Obter<List<Application.Catalogo.TaxaImpostoDto>>("/api/catalogo/taxas");

        // Sem cliente da ficha não há factura a crédito.
        await c.Gerente.PostComChave("/api/documentos/facturas", CenarioVenda.Chave(),
            new NovoDocumento(null, "Fulano", null, null, null, null, [new NovaLinhaDocumento(c.Prato.Id, null, 1, null, null, null)])).Esperar(HttpStatusCode.UnprocessableEntity);

        var chave = CenarioVenda.Chave();
        var pedidoFactura = new NovoDocumento(cliente.Id, null, null, null, null, "Evento de 20 pessoas",
        [
            new NovaLinhaDocumento(c.Prato.Id, null, 10, null, 4.50m, null),                  // 4 505 − 4,50 = 4 500,50 a 16 %
            new NovaLinhaDocumento(null, "Aluguer da sala", 1, 1000m, null, null),              // isento
            new NovaLinhaDocumento(null, "Decoração", 1, 580m, null, taxas.Single(t => t.Percentagem == 16m).Id),
        ]);
        var factura = await (await c.Gerente.PostComChave("/api/documentos/facturas", chave, pedidoFactura).Esperar(HttpStatusCode.Created)).Ler<DocumentoDto>();
        var repetida = await (await c.Gerente.PostComChave("/api/documentos/facturas", chave, pedidoFactura).Esperar(HttpStatusCode.OK)).Ler<DocumentoDto>();
        Assert.Equal(factura.Id, repetida.Id);
        Assert.StartsWith("FT-", factura.Numero);
        Assert.Equal(6080.50m, factura.Total);
        Assert.Equal(factura.Total, factura.EmDivida);
        Assert.NotNull(factura.Vencimento);
        Assert.Equal([16m, 0m], factura.ResumoIva.Select(r => r.Percentagem));
        Assert.Equal(1000m, factura.ResumoIva.Single(r => r.Percentagem == 0).Base);
        Assert.Contains("Isento nos termos", factura.Linhas[1].MotivoIsencao);
        Assert.Contains("* Isento nos termos", await TextoAsync(c.Gerente, factura.Id));

        // Recibo parcial em dinheiro: entra na sessão de caixa; não pode exceder a dívida.
        var dinheiro = c.Metodos["DINHEIRO"];
        await c.Gerente.PostComChave("/api/documentos/recibos", CenarioVenda.Chave(),
            new NovoRecibo(cliente.Id, dinheiro, null, null, null, [new LiquidacaoNova(factura.Id, 100m)])).Esperar(HttpStatusCode.UnprocessableEntity); // sem caixa
        await c.Gerente.PostComChave("/api/documentos/recibos", CenarioVenda.Chave(),
            new NovoRecibo(cliente.Id, dinheiro, null, c.Sessao.Id, null, [new LiquidacaoNova(factura.Id, 7000m)])).Esperar(HttpStatusCode.UnprocessableEntity);
        var esperadoAntes = await app.EsperadoAsync(c.Sessao.Id, dinheiro);
        var recibo = await (await c.Gerente.PostComChave("/api/documentos/recibos", CenarioVenda.Chave(),
            new NovoRecibo(cliente.Id, dinheiro, null, c.Sessao.Id, null, [new LiquidacaoNova(factura.Id, 2000m)])).Esperar(HttpStatusCode.Created)).Ler<DocumentoDto>();
        Assert.StartsWith("RC-", recibo.Numero);
        Assert.Equal(factura.Numero, Assert.Single(recibo.Liquidacoes).FacturaNumero);
        Assert.Equal(esperadoAntes + 2000m,
            await app.EsperadoAsync(c.Sessao.Id, dinheiro));

        var conta = await c.Gerente.Obter<ContaCorrente>($"/api/documentos/conta-corrente/{cliente.Id}");
        Assert.Equal(4080.50m, Assert.Single(conta.Facturas).EmDivida);

        // Nota de crédito de 4 pratos (1 802 − desconto proporcional 1,80 = 1 800,20); acima da dívida é recusada.
        factura = await c.Gerente.Obter<DocumentoDto>($"/api/documentos/{factura.Id}");
        var prato = factura.Linhas[0];
        var nc = await (await c.Gerente.PostComChave("/api/documentos/notas-credito", CenarioVenda.Chave(),
            new NovaNotaCredito(factura.Id, "Vieram 6 pessoas a menos", [new LinhaCredito(prato.Id, 4)])).Esperar(HttpStatusCode.Created)).Ler<DocumentoDto>();
        Assert.Equal(1800.20m, nc.Total);
        Assert.Equal(1.80m, nc.Desconto);
        await c.Gerente.PostComChave("/api/documentos/notas-credito", CenarioVenda.Chave(),
            new NovaNotaCredito(factura.Id, "Demasiado", [new LinhaCredito(prato.Id, 7)])).Esperar(HttpStatusCode.UnprocessableEntity); // só restam 6
        factura = await c.Gerente.Obter<DocumentoDto>($"/api/documentos/{factura.Id}");
        Assert.Equal((2000m, 1800.20m, 2280.30m), (factura.ValorPago, factura.ValorCreditado, factura.EmDivida));
        Assert.Equal(6m, factura.Linhas[0].QuantidadeCreditavel);
        // Creditar o resto da factura excede o que falta pagar (2 000 já pagos): é preciso anular o recibo primeiro.
        await c.Gerente.PostComChave("/api/documentos/notas-credito", CenarioVenda.Chave(), new NovaNotaCredito(factura.Id, "Evento cancelado", null))
            .Esperar(HttpStatusCode.UnprocessableEntity);

        // Anular o recibo devolve a dívida e retira o dinheiro da gaveta.
        recibo = await (await c.Gerente.Post($"/api/documentos/{recibo.Id}/anular", new ComMotivo("Cheque devolvido", recibo.RowVersion))
            .Esperar(HttpStatusCode.OK)).Ler<DocumentoDto>();
        Assert.Equal(EstadoDocumento.Anulado, recibo.Estado);
        Assert.Equal(esperadoAntes,
            await app.EsperadoAsync(c.Sessao.Id, dinheiro));
        var total = await (await c.Gerente.PostComChave("/api/documentos/notas-credito", CenarioVenda.Chave(), new NovaNotaCredito(factura.Id, "Evento cancelado", null))
            .Esperar(HttpStatusCode.Created)).Ler<DocumentoDto>();
        Assert.Equal(6080.50m - 1800.20m, total.Total);
        factura = await c.Gerente.Obter<DocumentoDto>($"/api/documentos/{factura.Id}");
        Assert.Equal((0m, factura.Total), (factura.EmDivida, factura.ValorCreditado));
        Assert.Empty((await c.Gerente.Obter<ContaCorrente>($"/api/documentos/conta-corrente/{cliente.Id}")).Facturas);
        // Uma factura nunca se anula.
        await c.Gerente.Post($"/api/documentos/{factura.Id}/anular", new ComMotivo("Engano", factura.RowVersion)).Esperar(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Cotacao_sem_valor_fiscal_converte_se_numa_factura_uma_unica_vez()
    {
        var c = await CenarioVenda.CriarAsync(app);
        var cliente = await ClienteEmpresaAsync(c.Gerente, nuit: null);
        var cotacao = await (await c.Gerente.PostComChave("/api/documentos/cotacoes", CenarioVenda.Chave(),
            new NovoDocumento(null, "Casamento Silva", "400111222", null, null, null, [new NovaLinhaDocumento(c.Prato.Id, "Prato do dia", 50, 400m, null, null)]))
            .Esperar(HttpStatusCode.Created)).Ler<DocumentoDto>();
        Assert.StartsWith("CT-", cotacao.Numero);
        Assert.Equal((20000m, "Casamento Silva", "Prato do dia"), (cotacao.Total, cotacao.ClienteNome, cotacao.Linhas[0].Descricao));
        Assert.NotNull(cotacao.ValidaAte);
        Assert.Contains("não serve de factura", await TextoAsync(c.Gerente, cotacao.Id));

        // Sem cliente da ficha não converte; com cliente, a factura copia as linhas e a cotação fica convertida.
        await c.Gerente.PostComChave($"/api/documentos/cotacoes/{cotacao.Id}/converter", CenarioVenda.Chave(), new ConverterCotacao(null, null, cotacao.RowVersion))
            .Esperar(HttpStatusCode.UnprocessableEntity);
        var factura = await (await c.Gerente.PostComChave($"/api/documentos/cotacoes/{cotacao.Id}/converter", CenarioVenda.Chave(),
            new ConverterCotacao(cliente.Id, null, cotacao.RowVersion)).Esperar(HttpStatusCode.Created)).Ler<DocumentoDto>();
        Assert.Equal((TipoDocumento.Factura, 20000m, cotacao.Numero, cliente.Nome), (factura.Tipo, factura.Total, factura.DocumentoOrigemNumero, factura.ClienteNome));
        cotacao = await c.Gerente.Obter<DocumentoDto>($"/api/documentos/{cotacao.Id}");
        Assert.Equal(EstadoDocumento.Convertido, cotacao.Estado);
        await c.Gerente.PostComChave($"/api/documentos/cotacoes/{cotacao.Id}/converter", CenarioVenda.Chave(), new ConverterCotacao(cliente.Id, null, cotacao.RowVersion))
            .Esperar(HttpStatusCode.UnprocessableEntity);
        await c.Gerente.Post($"/api/documentos/{cotacao.Id}/anular", new ComMotivo("Engano", cotacao.RowVersion)).Esperar(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Permissoes_e_numeracao_sequencial_sem_saltos()
    {
        var c = await CenarioVenda.CriarAsync(app);
        var (caixa, _) = await app.ClienteAutenticadoAsync(PerfisPadrao.Caixa);
        var (garcom, _) = await app.ClienteAutenticadoAsync(PerfisPadrao.Garcom);
        var corpo = new NovoDocumento(null, null, null, null, null, null, [new NovaLinhaDocumento(c.Prato.Id, null, 1, null, null, null)]);

        await garcom.GetAsync("/api/documentos").Esperar(HttpStatusCode.Forbidden);
        await caixa.GetAsync("/api/documentos").Esperar(HttpStatusCode.OK); // consulta
        await caixa.PostComChave("/api/documentos/cotacoes", CenarioVenda.Chave(), corpo).Esperar(HttpStatusCode.Forbidden);
        var emissor = await app.ClienteComPermissoesAsync(Permissoes.FacturacaoVer, Permissoes.FacturacaoEmitir);
        var cotacao = await (await emissor.PostComChave("/api/documentos/cotacoes", CenarioVenda.Chave(), corpo).Esperar(HttpStatusCode.Created)).Ler<DocumentoDto>();
        await emissor.Post($"/api/documentos/{cotacao.Id}/anular", new ComMotivo("Engano", cotacao.RowVersion)).Esperar(HttpStatusCode.Forbidden);

        // Emissões simultâneas: números consecutivos, todos diferentes.
        var tarefas = Enumerable.Range(0, 6).Select(async _ =>
            (await (await c.Gerente.PostComChave("/api/documentos/cotacoes", CenarioVenda.Chave(), corpo).Esperar(HttpStatusCode.Created)).Ler<DocumentoDto>()).Numero);
        var numeros = (await Task.WhenAll(tarefas)).Select(n => int.Parse(n[^6..])).Order().ToList();
        Assert.Equal(Enumerable.Range(numeros[0], 6), numeros);
    }
}
