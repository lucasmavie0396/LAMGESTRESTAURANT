using System.Net;
using System.Text;
using System.Text.Json;
using LAMGestRestaurant.Application.Impressao;
using LAMGestRestaurant.Application.Seguranca;
using LAMGestRestaurant.Application.Vendas;
using LAMGestRestaurant.Domain.Comum;
using LAMGestRestaurant.Tests.Infraestrutura;
using static LAMGestRestaurant.Tests.Infraestrutura.ExtensoesHttp;

namespace LAMGestRestaurant.Tests;

/// <summary>Pedido de conta no POS, vários pagamentos de uma vez e impressoras em modo PDF.</summary>
[Collection(ColeccaoBaseDados.Nome)]
public class ContaPagamentosPdfTests(AplicacaoTeste app)
{
    private const string Impressoras = "/api/configuracoes/impressoras";

    private static async Task<ImpressoraDto> ImpressoraAsync(HttpClient c, FuncaoImpressora funcao, string? windows, ModoImpressora modo = ModoImpressora.Directo) =>
        await (await c.Post(Impressoras, new GuardarImpressora(Unico(funcao + " "), funcao, windows, 80, null, null, modo)).Esperar(HttpStatusCode.OK)).Ler<ImpressoraDto>();

    private static async Task DesactivarAsync(HttpClient c, params ImpressoraDto[] impressoras)
    {
        foreach (var i in impressoras)
            await c.Post($"{Impressoras}/{i.Id}/activo", new { activo = false }).Esperar(HttpStatusCode.NoContent);
    }

    private static Task<HttpResponseMessage> PagarVariosAsync(CenarioVenda c, int pedidoId, string chave, params LinhaPagamento[] linhas) =>
        c.Gerente.PostComChave($"/api/pedidos/{pedidoId}/pagamentos/multiplos", chave, new RegistarPagamentos(c.Sessao.Id, linhas));

    [Fact]
    public async Task Varios_pagamentos_de_uma_vez_fecham_o_pedido_e_um_reenvio_nao_duplica()
    {
        var c = await CenarioVenda.CriarAsync(app);
        var pedido = await c.AbrirMesaAsync(c.Gerente, c.Mesa1, (c.Prato, 2)); // 901,00
        var chave = CenarioVenda.Chave();
        LinhaPagamento[] linhas =
        [
            new(c.Metodos["DINHEIRO"], 500m, 600m, null),
            new(c.Metodos["MPESA"], 401m, 401m, "MP-ABC123"),
        ];

        pedido = await (await PagarVariosAsync(c, pedido.Id, chave, linhas).Esperar(HttpStatusCode.Created)).Ler<PedidoDto>();
        Assert.Equal(EstadoPedido.Pago, pedido.Estado);
        Assert.Equal(0m, pedido.EmDivida);
        Assert.Equal([500m, 401m], pedido.Pagamentos.Select(p => p.Valor));
        Assert.Equal(100m, pedido.Pagamentos[0].Troco);
        Assert.NotNull(pedido.ReciboNumero);

        // Reenvio (resposta perdida): devolve o mesmo pedido, sem pagamentos novos.
        pedido = await (await PagarVariosAsync(c, pedido.Id, chave, linhas).Esperar(HttpStatusCode.OK)).Ler<PedidoDto>();
        Assert.Equal(2, pedido.Pagamentos.Count);

        // O recibo mostra os dois métodos.
        var recibo = await c.Gerente.Obter<DocumentoImpressao>($"/api/pedidos/{pedido.Id}/recibo/documento");
        var texto = string.Join('\n', recibo.Linhas.Select(l => $"{l.Texto} | {l.Direita}"));
        Assert.Contains("MP-ABC123", texto);
        Assert.Contains("Troco 100,00 MT", texto);
    }

    [Fact]
    public async Task Varios_pagamentos_sao_tudo_ou_nada()
    {
        var c = await CenarioVenda.CriarAsync(app);
        var pedido = await c.AbrirMesaAsync(c.Gerente, c.Mesa1, (c.Prato, 1)); // 450,50

        // Soma acima do saldo, referência em falta, troco num método sem troco: nada fica registado.
        await PagarVariosAsync(c, pedido.Id, CenarioVenda.Chave(),
            new(c.Metodos["DINHEIRO"], 300m, 300m, null), new(c.Metodos["CARTAO"], 200m, 200m, null)).Esperar(HttpStatusCode.UnprocessableEntity);
        await PagarVariosAsync(c, pedido.Id, CenarioVenda.Chave(),
            new(c.Metodos["DINHEIRO"], 200m, 200m, null), new(c.Metodos["MPESA"], 250.50m, 250.50m, null)).Esperar(HttpStatusCode.UnprocessableEntity);
        await PagarVariosAsync(c, pedido.Id, CenarioVenda.Chave(),
            new(c.Metodos["DINHEIRO"], 200m, 200m, null), new(c.Metodos["CARTAO"], 250.50m, 300m, null)).Esperar(HttpStatusCode.UnprocessableEntity);
        pedido = await c.Gerente.Obter<PedidoDto>($"/api/pedidos/{pedido.Id}");
        Assert.Empty(pedido.Pagamentos);

        // Parte agora (dois métodos), o resto depois: o pedido só fecha no fim.
        pedido = await (await PagarVariosAsync(c, pedido.Id, CenarioVenda.Chave(),
            new(c.Metodos["DINHEIRO"], 100m, 100m, null), new(c.Metodos["CARTAO"], 150m, 150m, null)).Esperar(HttpStatusCode.Created)).Ler<PedidoDto>();
        Assert.Equal((EstadoPedido.Aberto, 200.50m), (pedido.Estado, pedido.EmDivida));
        pedido = await (await c.PagarAsync(c.Gerente, pedido.Id, "DINHEIRO", 200.50m).Esperar(HttpStatusCode.Created)).Ler<PedidoDto>();
        Assert.Equal(EstadoPedido.Pago, pedido.Estado);
    }

    [Fact]
    public async Task Pedir_conta_imprime_a_conta_na_caixa_com_o_valor_por_pessoa_e_fica_no_historico()
    {
        var c = await CenarioVenda.CriarAsync(app);
        var caixa = await ImpressoraAsync(c.Gerente, FuncaoImpressora.Caixa, ImpressorasTeste.Caixa);
        try
        {
            var pedido = await c.AbrirMesaAsync(c.Gerente, c.Mesa1, (c.Prato, 2)); // 2 pessoas, 901,00
            await c.PagarAsync(c.Gerente, pedido.Id, "DINHEIRO", 101m).Esperar(HttpStatusCode.Created);

            var resposta = await (await c.Gerente.Post($"/api/pedidos/{pedido.Id}/conta", new { terminalId = (int?)null }).Esperar(HttpStatusCode.OK)).Ler<JsonElement>();
            Assert.Equal(caixa.Nome, resposta.GetProperty("impressora").GetString());
            Assert.False(resposta.GetProperty("pdf").GetBoolean());

            var conta = (await app.Impressoras.EsperarAsync($"Pedido {pedido.Numero}")).Single(i => i.Texto.Contains("CONTA"));
            Assert.Equal(ImpressorasTeste.Caixa, conta.Impressora);
            Assert.Contains("TOTAL | 901,00 MT", conta.Texto);
            Assert.Contains("Já pago | 101,00 MT", conta.Texto);
            Assert.Contains("A PAGAR | 800,00 MT", conta.Texto);
            Assert.Contains("Por pessoa (2) | 400,00 MT", conta.Texto);
            Assert.Contains("não serve de recibo", conta.Texto);

            // A conta não fecha nem altera o pedido.
            pedido = await c.Gerente.Obter<PedidoDto>($"/api/pedidos/{pedido.Id}");
            Assert.Equal(EstadoPedido.Aberto, pedido.Estado);

            // Pedido pago: já não se pede conta.
            await c.PagarAsync(c.Gerente, pedido.Id, "DINHEIRO", 800m).Esperar(HttpStatusCode.Created);
            await c.Gerente.Post($"/api/pedidos/{pedido.Id}/conta", new { terminalId = (int?)null }).Esperar(HttpStatusCode.UnprocessableEntity);
        }
        finally
        {
            await DesactivarAsync(c.Gerente, caixa);
        }
    }

    [Fact]
    public async Task Impressora_em_modo_pdf_entrega_o_pdf_so_a_quem_o_pediu_e_nao_imprime()
    {
        var c = await CenarioVenda.CriarAsync(app);
        // Modo PDF dispensa uma impressora do Windows; em modo directo continua obrigatória.
        await c.Gerente.Post(Impressoras, new GuardarImpressora(Unico("X "), FuncaoImpressora.Bar, null, 80, null, null)).Esperar(HttpStatusCode.UnprocessableEntity);
        var bar = await ImpressoraAsync(c.Gerente, FuncaoImpressora.Bar, null, ModoImpressora.Pdf);
        Assert.Equal(ModoImpressora.Pdf, bar.Modo);
        try
        {
            var pedido = await c.AbrirMesaAsync(c.Gerente, c.Mesa1, (c.Bebida, 3));
            await c.Gerente.Post($"/api/pedidos/{pedido.Id}/enviar-cozinha", new ComVersao(pedido.RowVersion)).Esperar(HttpStatusCode.OK);

            var pdf = await app.Pdfs.EsperarAsync(pedido.Numero);
            Assert.Equal(bar.Nome, pdf.Impressora);
            Assert.Empty(app.Impressoras.Com(pedido.Numero)); // nada foi para uma impressora

            var resposta = await c.Gerente.GetAsync(pdf.Url).Esperar(HttpStatusCode.OK);
            Assert.Equal("application/pdf", resposta.Content.Headers.ContentType?.MediaType);
            Assert.Equal("SAMEORIGIN", resposta.Headers.GetValues("X-Frame-Options").Single());
            var bytes = await resposta.Content.ReadAsByteArrayAsync();
            Assert.Equal("%PDF", Encoding.ASCII.GetString(bytes, 0, 4));

            // Outro utilizador não o consegue abrir.
            var (outro, _) = await app.ClienteAutenticadoAsync(PerfisPadrao.Administrador);
            await outro.GetAsync(pdf.Url).Esperar(HttpStatusCode.NotFound);
        }
        finally
        {
            await DesactivarAsync(c.Gerente, bar);
        }
    }

    [Theory]
    [InlineData(58)]
    [InlineData(80)]
    [InlineData(210)]
    public void Gerador_pdf_produz_um_pdf_valido_em_cada_papel(int largura)
    {
        var linhas = Enumerable.Range(1, largura == 210 ? 150 : 10)
            .Select(i => new LinhaImpressao($"{i} x Produto com um nome bastante comprido para quebrar a linha", "1 234,50 MT"))
            .Prepend(new LinhaImpressao("CONTA", Alinhamento: Alinhamento.Centro, Negrito: true, Grande: true))
            .Append(LinhaImpressao.Traco)
            .ToList();
        var pdf = new Infrastructure.Impressao.GeradorPdf().Gerar(largura, new DocumentoImpressao("Teste", linhas));
        Assert.Equal("%PDF", Encoding.ASCII.GetString(pdf, 0, 4));
        // A4 com 150 linhas: mais de uma página; talão: uma só.
        var paginas = System.Text.RegularExpressions.Regex.Count(Encoding.Latin1.GetString(pdf), @"/Type\s*/Page(?!s)");
        Assert.True(largura == 210 ? paginas > 1 : paginas == 1, $"{paginas} páginas");
    }
}
