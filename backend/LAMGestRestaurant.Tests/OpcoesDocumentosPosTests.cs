using System.Net;
using System.Text.Json;
using LAMGestRestaurant.Api.Controllers;
using LAMGestRestaurant.Application.Caixa;
using LAMGestRestaurant.Application.Catalogo;
using LAMGestRestaurant.Infrastructure.Imagens;
using LAMGestRestaurant.Application.Impressao;
using LAMGestRestaurant.Application.Relatorios;
using LAMGestRestaurant.Application.Seguranca;
using LAMGestRestaurant.Application.Sistema;
using LAMGestRestaurant.Application.Vendas;
using LAMGestRestaurant.Domain.Comum;
using LAMGestRestaurant.Domain.Sistema;
using LAMGestRestaurant.Tests.Infraestrutura;
using static LAMGestRestaurant.Tests.Infraestrutura.ExtensoesHttp;

namespace LAMGestRestaurant.Tests;

/// <summary>Configurações → Documentos e POS: fecho, logótipo de 80 mm, cópias, botões do POS e venda ao balcão.</summary>
[Collection(ColeccaoBaseDados.Nome)]
public class OpcoesDocumentosPosTests(AplicacaoTeste app)
{
    private const string Url = "/api/configuracoes/impressoras";

    private static Task Definir(HttpClient c, params (string Chave, string Valor)[] valores) =>
        c.Put("/api/configuracoes", new GuardarConfiguracoes(valores.ToDictionary(v => v.Chave, v => v.Valor))).Esperar(HttpStatusCode.OK);

    private static async Task<ImpressoraDto> ImpressoraAsync(HttpClient c, FuncaoImpressora funcao, string windows, int largura) =>
        await (await c.Post(Url, new GuardarImpressora(Unico("O "), funcao, windows, largura, null, null)).Esperar(HttpStatusCode.OK)).Ler<ImpressoraDto>();

    private static async Task DesactivarAsync(HttpClient c, params ImpressoraDto[] impressoras)
    {
        foreach (var i in impressoras)
            await c.Post($"{Url}/{i.Id}/activo", new { activo = false }).Esperar(HttpStatusCode.NoContent);
    }

    private static Task<HttpResponseMessage> NovoBalcaoAsync(CenarioVenda c, params (Application.Catalogo.ProdutoDto Produto, decimal Qtd)[] itens) =>
        c.Gerente.PostComChave("/api/pedidos", CenarioVenda.Chave(), new NovoPedido(TipoPedido.Balcao, null, null, null, 0, null, null, null, false,
            [.. itens.Select(i => new NovoItem(i.Produto.Id, i.Qtd, null))]));

    [Fact]
    public async Task Fecho_impresso_segue_as_opcoes_de_produtos_e_contagem_cega()
    {
        var c = await CenarioVenda.CriarAsync(app);
        var pedido = await c.AbrirMesaAsync(c.Gerente, c.Mesa1, (c.Prato, 1));
        await c.PagarAsync(c.Gerente, pedido.Id, "DINHEIRO", pedido.Total).Esperar(HttpStatusCode.Created);
        var sessao = await c.Gerente.Obter<SessaoCaixaDto>($"/api/caixa/sessoes/{c.Sessao.Id}");
        await c.Gerente.Post($"/api/caixa/sessoes/{c.Sessao.Id}/fechar",
            new FecharCaixa([new ContagemMetodo(c.Metodos["DINHEIRO"], 1400m)], null, sessao.RowVersion)).Esperar(HttpStatusCode.OK);
        var url = $"/api/relatorios/documento?tipo=FechoCaixa&sessaoId={c.Sessao.Id}";

        var normal = TextoDocumento.De(await c.Gerente.Obter<DocumentoImpressao>(url));
        Assert.Contains("TOTAL ESPERADO", normal);
        Assert.Contains("DIFERENÇA", normal);
        Assert.Contains("PRODUTOS VENDIDOS POR CATEGORIA", normal);

        try
        {
            await Definir(c.Gerente, (ChavesConfiguracao.FechoContagemCega, "true"), (ChavesConfiguracao.FechoImprimeProdutos, "false"));
            var cego = TextoDocumento.De(await c.Gerente.Obter<DocumentoImpressao>(url));
            Assert.Contains("TOTAL CONTADO", cego);
            foreach (var escondido in new[] { "ESPERADO", "Esperado", "DIFERENÇA", "Diferença", "Recebido", "Pedidos pagos", "PRODUTOS VENDIDOS" })
                Assert.DoesNotContain(escondido, cego);
        }
        finally
        {
            await Definir(c.Gerente, (ChavesConfiguracao.FechoContagemCega, "false"), (ChavesConfiguracao.FechoImprimeProdutos, "true"));
        }
    }

    [Fact]
    public async Task Conta_tem_linhas_para_os_dados_da_factura_depois_do_rodape()
    {
        var c = await CenarioVenda.CriarAsync(app);
        var pedido = await c.AbrirMesaAsync(c.Gerente, c.Mesa1, (c.Prato, 1));
        var resposta = await (await c.Gerente.Post($"/api/pedidos/{pedido.Id}/conta", new { terminalId = (int?)null }).Esperar(HttpStatusCode.OK)).Ler<JsonElement>();
        var documento = resposta.GetProperty("documento").Deserialize<DocumentoImpressao>(AplicacaoTeste.Json)!;
        var texto = TextoDocumento.De(documento);

        Assert.True(texto.IndexOf("Documento de consulta", StringComparison.Ordinal) < texto.IndexOf("DADOS PARA FACTURA", StringComparison.Ordinal));
        Assert.Contains("Nome comercial:", texto);
        Assert.Contains("NUIT:", texto);
        Assert.True(documento.Linhas[^1].Separador); // a última linha é onde se escreve o NUIT
    }

    [Fact]
    public async Task Copias_por_documento_saem_no_servidor_e_no_posto()
    {
        var c = await CenarioVenda.CriarAsync(app);
        var cozinha = await ImpressoraAsync(c.Gerente, FuncaoImpressora.Cozinha, ImpressorasTeste.Cozinha, 80);
        var caixa = await ImpressoraAsync(c.Gerente, FuncaoImpressora.Caixa, ImpressorasTeste.Caixa, 80);
        try
        {
            await Definir(c.Gerente, (ChavesConfiguracao.CopiasTalao, "2"), (ChavesConfiguracao.CopiasConta, "3"), (ChavesConfiguracao.CopiasVendaDinheiro, "2"));
            var publicas = await c.Gerente.Obter<ConfiguracoesPublicas>("/api/configuracoes/publicas");
            Assert.Equal((2, 3, 1), (publicas.Copias!["talao"], publicas.Copias["conta"], publicas.Copias["factura"]));

            var pedido = await c.AbrirMesaAsync(c.Gerente, c.Mesa1, (c.Prato, 7));
            pedido = await (await c.Gerente.Post($"/api/pedidos/{pedido.Id}/enviar-cozinha", new ComVersao(pedido.RowVersion)).Esperar(HttpStatusCode.OK)).Ler<PedidoDto>();
            await app.Impressoras.EsperarAsync("7 x Frango grelhado", quantos: 2);

            await c.Gerente.Post($"/api/pedidos/{pedido.Id}/conta", new { terminalId = (int?)null }).Esperar(HttpStatusCode.OK);
            await app.Impressoras.EsperarAsync("DADOS PARA FACTURA\nPreencha", quantos: 0);
            var contas = await app.Impressoras.EsperarAsync($"Pedido {pedido.Numero} · Mesa", quantos: 3);
            Assert.Equal(3, contas.Count(i => i.Texto.Contains("CONTA", StringComparison.Ordinal)));

            pedido = await c.Gerente.Obter<PedidoDto>($"/api/pedidos/{pedido.Id}");
            pedido = await (await c.PagarAsync(c.Gerente, pedido.Id, "DINHEIRO", pedido.Total).Esperar(HttpStatusCode.Created)).Ler<PedidoDto>();
            Assert.Equal(2, (await app.Impressoras.EsperarAsync(pedido.ReciboNumero!, quantos: 2)).Count);
        }
        finally
        {
            await Definir(c.Gerente, (ChavesConfiguracao.CopiasTalao, "1"), (ChavesConfiguracao.CopiasConta, "1"), (ChavesConfiguracao.CopiasVendaDinheiro, "1"));
            await DesactivarAsync(c.Gerente, cozinha, caixa);
        }
    }

    [Fact]
    public async Task Logotipo_sai_dos_documentos_de_80_mm_quando_desligado_e_fica_nos_outros_papeis()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(6, 1))
            return;
        var c = await CenarioVenda.CriarAsync(app);
        var logo = await (await c.Gerente.PostAsync("/api/imagens/carregar", DocumentosImpressaoTests.Png()).Esperar(HttpStatusCode.OK)).Ler<ImagemGuardada>();
        var de80 = await ImpressoraAsync(c.Gerente, FuncaoImpressora.Caixa, ImpressorasTeste.Caixa, 80);
        var a4 = await ImpressoraAsync(c.Gerente, FuncaoImpressora.Producao, ImpressorasTeste.A4, 210);
        try
        {
            await Definir(c.Gerente, ("estabelecimento.logotipo", logo.Url), (ChavesConfiguracao.Logotipo80mm, "false"));
            Assert.False((await c.Gerente.Obter<ConfiguracoesPublicas>("/api/configuracoes/publicas")).Logotipo80mm);
            var antes = app.Impressoras.Com("RESUMO DE VENDAS").Count;
            foreach (var impressora in new[] { de80, a4 })
                await c.Gerente.Post("/api/relatorios/documento/imprimir", new ImprimirRelatorio(TipoDocumentoRelatorio.Vendas, DateOnly.FromDateTime(DateTime.Today), null, null, impressora.Id))
                    .Esperar(HttpStatusCode.Accepted);
            var novos = (await app.Impressoras.EsperarAsync("RESUMO DE VENDAS", quantos: antes + 2)).Skip(antes).ToList();
            Assert.Null(novos.Single(r => r.Impressora == ImpressorasTeste.Caixa).Logotipo);
            Assert.NotNull(novos.Single(r => r.Impressora == ImpressorasTeste.A4).Logotipo);
        }
        finally
        {
            await Definir(c.Gerente, ("estabelecimento.logotipo", ""), (ChavesConfiguracao.Logotipo80mm, "true"));
            await DesactivarAsync(c.Gerente, de80, a4);
        }
    }

    [Fact]
    public async Task Venda_ao_balcao_nao_tem_mesa_nem_entrega_e_envia_os_taloes_ao_pagar()
    {
        var c = await CenarioVenda.CriarAsync(app);
        var cozinha = await ImpressoraAsync(c.Gerente, FuncaoImpressora.Cozinha, ImpressorasTeste.Cozinha, 80);
        try
        {
            var pedido = await (await NovoBalcaoAsync(c, (c.Prato, 9)).Esperar(HttpStatusCode.Created)).Ler<PedidoDto>();
            Assert.Equal(TipoPedido.Balcao, pedido.Tipo);
            Assert.Null(pedido.MesaId);
            Assert.Null(pedido.EstadoEntrega);
            Assert.Empty(app.Impressoras.Com("9 x Frango grelhado"));

            pedido = await (await c.PagarAsync(c.Gerente, pedido.Id, "DINHEIRO", pedido.Total).Esperar(HttpStatusCode.Created)).Ler<PedidoDto>();
            Assert.Equal(EstadoPedido.Pago, pedido.Estado);
            Assert.All(pedido.Itens, i => Assert.Equal(EstadoItemCozinha.Pendente, i.EstadoCozinha));
            var talao = Assert.Single(await app.Impressoras.EsperarAsync("9 x Frango grelhado"));
            Assert.Contains("BALCÃO", talao.Texto.ToUpperInvariant());
        }
        finally
        {
            await DesactivarAsync(c.Gerente, cozinha);
        }
    }

    [Fact]
    public async Task Tipo_de_venda_desligado_e_recusado_e_o_pos_fica_sempre_com_um_botao()
    {
        var c = await CenarioVenda.CriarAsync(app);
        try
        {
            await Definir(c.Gerente, (ChavesConfiguracao.PosBalcao, "false"), (ChavesConfiguracao.PosCodigoBarras, "false"));
            await NovoBalcaoAsync(c, (c.Prato, 1)).Esperar(HttpStatusCode.UnprocessableEntity);
            var pos = (await c.Gerente.Obter<ConfiguracoesPublicas>("/api/configuracoes/publicas")).Pos!;
            Assert.Equal((false, false, true), (pos.Balcao, pos.CodigoBarras, pos.Mesa));

            await c.Gerente.Put("/api/configuracoes", new GuardarConfiguracoes(new Dictionary<string, string>
            {
                [ChavesConfiguracao.PosMesa] = "false", [ChavesConfiguracao.PosTakeAway] = "false", [ChavesConfiguracao.PosDelivery] = "false",
            })).Esperar(HttpStatusCode.UnprocessableEntity);
            await c.Gerente.Put("/api/configuracoes", new GuardarConfiguracoes(new Dictionary<string, string> { [ChavesConfiguracao.CopiasConta] = "11" }))
                .Esperar(HttpStatusCode.UnprocessableEntity);
        }
        finally
        {
            await Definir(c.Gerente, (ChavesConfiguracao.PosBalcao, "true"), (ChavesConfiguracao.PosCodigoBarras, "true"));
        }
    }
}
