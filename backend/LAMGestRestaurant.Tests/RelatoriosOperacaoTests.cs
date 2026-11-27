using System.Net;
using LAMGestRestaurant.Api.Controllers;
using LAMGestRestaurant.Application.Clientes;
using LAMGestRestaurant.Application.Compras;
using LAMGestRestaurant.Application.Facturacao;
using LAMGestRestaurant.Application.Relatorios;
using LAMGestRestaurant.Application.Seguranca;
using LAMGestRestaurant.Application.Stock;
using LAMGestRestaurant.Application.Vendas;
using LAMGestRestaurant.Domain.Comum;
using LAMGestRestaurant.Domain.Compras;
using LAMGestRestaurant.Domain.Facturacao;
using LAMGestRestaurant.Infrastructure.Dados;
using LAMGestRestaurant.Tests.Infraestrutura;
using Microsoft.EntityFrameworkCore;
using static LAMGestRestaurant.Tests.Infraestrutura.ExtensoesHttp;

namespace LAMGestRestaurant.Tests;

/// <summary>
/// Pagamentos a fornecedores e relatórios de operação: contas a pagar, vendas por hora, cotações, cozinha, mesas, delivery,
/// pontos e reservas. Os números contam a diferença antes/depois, porque a base de testes é partilhada.
/// </summary>
[Collection(ColeccaoBaseDados.Nome)]
public class RelatoriosOperacaoTests(AplicacaoTeste app)
{
    private static DateOnly Hoje() => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("Africa/Maputo")));
    private static string Periodo() => $"desde={Hoje():yyyy-MM-dd}&ate={Hoje():yyyy-MM-dd}";

    private static async Task<CompraDto> CompraConfirmadaAsync(CenarioVenda c, DateOnly? vencimento)
    {
        var unidades = (await c.Gerente.Obter<List<UnidadeDto>>("/api/stock/unidades")).ToDictionary(u => u.Codigo, u => u.Id);
        var armazem = (await c.Gerente.Obter<List<ArmazemDto>>("/api/stock/armazens")).Single(a => a.Principal).Id;
        var artigo = await (await c.Gerente.Post("/api/stock/artigos", new GuardarArtigo(Unico("A", 12), "Arroz", TipoArtigo.Ingrediente, unidades["kg"], 0, false))
            .Esperar(HttpStatusCode.OK)).Ler<ArtigoDto>();
        var fornecedor = await (await c.Gerente.Post("/api/fornecedores", new GuardarFornecedor(Unico("Forn ", 16), null, "840000000", null, null, null))
            .Esperar(HttpStatusCode.OK)).Ler<FornecedorDto>();
        // 10 kg a 100/kg com IVA 16% → 1160.
        var compra = await (await c.Gerente.Post("/api/compras", new GuardarCompra(fornecedor.Id, Unico("FT ", 12), vencimento?.AddDays(-30) ?? Hoje(), vencimento, armazem, null,
            [new NovaLinhaCompra(artigo.Id, 10, unidades["kg"], 100, 0, 16)], null)).Esperar(HttpStatusCode.OK)).Ler<CompraDto>();
        return await (await c.Gerente.Post($"/api/compras/{compra.Id}/confirmar", new ComVersaoSimples(compra.RowVersion)).Esperar(HttpStatusCode.OK)).Ler<CompraDto>();
    }

    [Fact]
    public async Task Pagamentos_ao_fornecedor_abatem_a_divida_saem_do_caixa_e_aparecem_nas_contas_a_pagar()
    {
        var c = await CenarioVenda.CriarAsync(app);
        var dinheiro = c.Metodos["DINHEIRO"];
        var compra = await CompraConfirmadaAsync(c, Hoje().AddDays(-10)); // já vencida
        Assert.Equal((0m, 1160m), (compra.Pago, compra.EmDivida));

        // Entra dinheiro no caixa para poder pagar pela gaveta.
        var venda = await c.AbrirMesaAsync(c.Gerente, c.Mesa1, (c.Prato, 1));
        await c.PagarAsync(c.Gerente, venda.Id, "DINHEIRO", venda.Total).Esperar(HttpStatusCode.Created);
        var gaveta = await app.EsperadoAsync(c.Sessao.Id, dinheiro);

        var url = $"/api/compras/{compra.Id}/pagamentos";
        await c.Gerente.PostComChave(url, CenarioVenda.Chave(), new NovoPagamentoCompra(2000m, dinheiro, null, null, null)).Esperar(HttpStatusCode.UnprocessableEntity); // acima da dívida
        var chave = CenarioVenda.Chave();
        var p1 = await (await c.Gerente.PostComChave(url, chave, new NovoPagamentoCompra(400m, dinheiro, null, null, c.Sessao.Id)).Esperar(HttpStatusCode.Created)).Ler<PagamentoCompraDto>();
        await c.Gerente.PostComChave(url, chave, new NovoPagamentoCompra(400m, dinheiro, null, null, c.Sessao.Id)).Esperar(HttpStatusCode.OK); // reenvio não duplica
        Assert.Equal(gaveta - 400m, await app.EsperadoAsync(c.Sessao.Id, dinheiro));
        await c.Gerente.PostComChave(url, CenarioVenda.Chave(), new NovoPagamentoCompra(100m, c.Metodos["MPESA"], Hoje(), "MP-123", null)).Esperar(HttpStatusCode.Created);
        compra = await c.Gerente.Obter<CompraDto>($"/api/compras/{compra.Id}");
        Assert.Equal((500m, 660m), (compra.Pago, compra.EmDivida));

        var rel = await c.Gerente.Obter<RelatorioContasPagar>($"/api/relatorios/contas-pagar?{Periodo()}");
        var linha = Assert.Single(rel.ComprasEmAberto, x => x.Id == compra.Id);
        Assert.Equal((660m, 10), (linha.EmDivida, linha.DiasAtraso));
        Assert.Contains(rel.Dividas, d => d.Fornecedor == compra.Fornecedor && d.Ate30 == 660m);
        Assert.Contains(rel.Pagamentos, x => x.Documento == compra.NumeroDocumento && x.Valor == 400m && !x.Anulado);

        // Anular: o dinheiro volta ao caixa aberto; uma compra com pagamentos não se anula.
        await c.Gerente.Post($"/api/compras/{compra.Id}/anular", new AnularComMotivo("Engano", compra.RowVersion)).Esperar(HttpStatusCode.UnprocessableEntity);
        await c.Gerente.Post($"/api/compras/pagamentos/{p1.Id}/anular", new AnularPagamentoCompra("Valor errado")).Esperar(HttpStatusCode.OK);
        Assert.Equal(gaveta, await app.EsperadoAsync(c.Sessao.Id, dinheiro));
        Assert.Equal(1060m, (await c.Gerente.Obter<CompraDto>($"/api/compras/{compra.Id}")).EmDivida);

        var (garcom, _) = await app.ClienteAutenticadoAsync(PerfisPadrao.Garcom);
        Assert.Equal(HttpStatusCode.Forbidden, (await garcom.GetAsync($"/api/relatorios/contas-pagar?{Periodo()}")).StatusCode);
    }

    [Fact]
    public async Task Relatorios_de_operacao_contam_vendas_cotacoes_cozinha_mesas_pontos_e_reservas()
    {
        var c = await CenarioVenda.CriarAsync(app);
        var horasAntes = await c.Gerente.Obter<RelatorioHoras>($"/api/relatorios/horas?{Periodo()}");
        var mesasAntes = await c.Gerente.Obter<RelatorioMesas>($"/api/relatorios/mesas?{Periodo()}");
        var cozinhaAntes = await c.Gerente.Obter<RelatorioCozinha>($"/api/relatorios/cozinha?{Periodo()}");

        // Uma mesa paga, com um prato «preparado» em 25 minutos.
        var pedido = await c.AbrirMesaAsync(c.Gerente, c.Mesa1, (c.Prato, 1));
        await app.ComServicos(async s =>
        {
            var db = s.GetRequiredService<AppDbContext>();
            var item = await db.PedidoItens.FirstAsync(i => i.PedidoId == pedido.Id);
            item.EnviadoCozinhaEmUtc = DateTime.UtcNow.AddMinutes(-30);
            item.ProntoEmUtc = DateTime.UtcNow.AddMinutes(-5);
            await db.SaveChangesAsync();
            return true;
        });
        await c.PagarAsync(c.Gerente, pedido.Id, "DINHEIRO", pedido.Total).Esperar(HttpStatusCode.Created);

        var horas = await c.Gerente.Obter<RelatorioHoras>($"/api/relatorios/horas?{Periodo()}");
        Assert.Equal(horasAntes.Pedidos + 1, horas.Pedidos);
        Assert.Equal(horasAntes.Valor + pedido.Total, horas.Valor);
        Assert.Equal(7, horas.PorDiaSemana.Count);
        Assert.NotNull(horas.HoraPico);

        var mesas = await c.Gerente.Obter<RelatorioMesas>($"/api/relatorios/mesas?{Periodo()}");
        Assert.Equal(mesasAntes.Pedidos + 1, mesas.Pedidos);
        Assert.Contains(mesas.PorMesa, m => m.Mesa == c.Mesa1.Nome && m.Pedidos >= 1);

        var cozinha = await c.Gerente.Obter<RelatorioCozinha>($"/api/relatorios/cozinha?{Periodo()}&limiteMinutos=20");
        Assert.Equal(cozinhaAntes.Itens + 1, cozinha.Itens);
        Assert.Equal(cozinhaAntes.Atrasados + 1, cozinha.Atrasados);
        Assert.Contains(cozinha.MaisLentos, x => x.Pedido == pedido.Numero && x.Minutos == 25m);

        // Cotação convertida em factura.
        var cotacoes = $"/api/relatorios/cotacoes?{Periodo()}";
        var cotAntes = await c.Gerente.Obter<RelatorioCotacoes>(cotacoes);
        var cliente = await (await c.Gerente.Post("/api/clientes", new GuardarCliente(TipoCliente.Empresa, Unico("Cot "), null, null, null, null, null, null, null))
            .Esperar(HttpStatusCode.OK)).Ler<ClienteDto>();
        var cot = await (await c.Gerente.PostComChave("/api/documentos/cotacoes", CenarioVenda.Chave(),
            new NovoDocumento(cliente.Id, null, null, null, null, null, [new NovaLinhaDocumento(c.Prato.Id, null, 2, null, null, null)])).Esperar(HttpStatusCode.Created)).Ler<DocumentoDto>();
        await c.Gerente.PostComChave($"/api/documentos/cotacoes/{cot.Id}/converter", CenarioVenda.Chave(), new ConverterCotacao(cliente.Id, null, cot.RowVersion))
            .Esperar(HttpStatusCode.Created);
        var cotDepois = await c.Gerente.Obter<RelatorioCotacoes>(cotacoes);
        Assert.Equal((cotAntes.Emitidas + 1, cotAntes.Convertidas + 1), (cotDepois.Emitidas, cotDepois.Convertidas));
        var linha = Assert.Single(cotDepois.Linhas, l => l.Id == cot.Id);
        Assert.Equal(SituacaoCotacao.Convertida, linha.Situacao);
        Assert.NotNull(linha.Factura);

        // Pontos, reservas e delivery respondem com o período.
        Assert.True((await c.Gerente.Obter<RelatorioPontos>($"/api/relatorios/pontos?{Periodo()}")).SaldoTotal >= 0);
        Assert.Equal(7, (await c.Gerente.Obter<RelatorioReservas>($"/api/relatorios/reservas?{Periodo()}")).PorDiaSemana.Count);
        Assert.True((await c.Gerente.Obter<RelatorioDelivery>($"/api/relatorios/delivery?{Periodo()}")).Pedidos >= 0);

        // Documentos imprimíveis com dados.
        async Task<string> Documento(TipoDocumentoRelatorio tipo) => TextoDocumento.De(await c.Gerente.Obter<Application.Impressao.DocumentoImpressao>(
            $"/api/relatorios/documento?tipo={tipo}&{Periodo()}"));
        Assert.Contains("VENDAS POR HORA", await Documento(TipoDocumentoRelatorio.VendasPorHora));
        Assert.Contains(c.Mesa1.Nome, await Documento(TipoDocumentoRelatorio.Mesas));
        Assert.Contains(pedido.Numero, await Documento(TipoDocumentoRelatorio.Cozinha));
        Assert.Contains(cot.Numero, await Documento(TipoDocumentoRelatorio.Cotacoes));

        var (garcom, _) = await app.ClienteAutenticadoAsync(PerfisPadrao.Garcom);
        Assert.Equal(HttpStatusCode.Forbidden, (await garcom.GetAsync($"/api/relatorios/horas?{Periodo()}")).StatusCode);
    }
}
