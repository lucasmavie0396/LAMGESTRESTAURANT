using System.Drawing;
using System.Drawing.Imaging;
using System.Net;
using System.Net.Http.Headers;
using LAMGestRestaurant.Api.Controllers;
using LAMGestRestaurant.Application.Caixa;
using LAMGestRestaurant.Application.Catalogo;
using LAMGestRestaurant.Application.Impressao;
using LAMGestRestaurant.Application.Relatorios;
using LAMGestRestaurant.Application.Seguranca;
using LAMGestRestaurant.Application.Vendas;
using LAMGestRestaurant.Domain.Comum;
using LAMGestRestaurant.Infrastructure.Imagens;
using LAMGestRestaurant.Tests.Infraestrutura;
using static LAMGestRestaurant.Tests.Infraestrutura.ExtensoesHttp;

namespace LAMGestRestaurant.Tests;

/// <summary>Talões de anulação e reimpressão, outros centros de produção, relatórios em documento e logótipo.</summary>
[Collection(ColeccaoBaseDados.Nome)]
public class DocumentosImpressaoTests(AplicacaoTeste app)
{
    private const string Impressoras = "/api/configuracoes/impressoras";

    private static async Task<ImpressoraDto> ImpressoraAsync(HttpClient c, FuncaoImpressora funcao, string windows, int largura = 80) =>
        await (await c.Post(Impressoras, new GuardarImpressora(Unico(funcao + " "), funcao, windows, largura, null, null)).Esperar(HttpStatusCode.OK)).Ler<ImpressoraDto>();

    private static async Task DesactivarAsync(HttpClient c, params ImpressoraDto[] impressoras)
    {
        foreach (var i in impressoras)
            await c.Post($"{Impressoras}/{i.Id}/activo", new { activo = false }).Esperar(HttpStatusCode.NoContent);
    }

    private static string Hoje() =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("Africa/Maputo"))).ToString("yyyy-MM-dd");

    private static string Texto(DocumentoImpressao d) => string.Join('\n', d.Linhas.Select(l => l.Direita is null ? l.Texto : $"{l.Texto} | {l.Direita}"));

    [Fact]
    public async Task Anular_item_ou_pedido_ja_enviado_imprime_anulacao_e_a_reimpressao_repete_o_talao()
    {
        var c = await CenarioVenda.CriarAsync(app);
        var cozinha = await ImpressoraAsync(c.Gerente, FuncaoImpressora.Cozinha, ImpressorasTeste.Cozinha);
        try
        {
            var pedido = await c.AbrirMesaAsync(c.Gerente, c.Mesa1, (c.Prato, 2));
            pedido = await (await c.Gerente.Post($"/api/pedidos/{pedido.Id}/enviar-cozinha", new ComVersao(pedido.RowVersion)).Esperar(HttpStatusCode.OK)).Ler<PedidoDto>();
            await app.Impressoras.EsperarAsync(pedido.Numero);

            // Reimpressão: o mesmo talão marcado como tal.
            await c.Gerente.Post($"/api/pedidos/{pedido.Id}/taloes/reimprimir", new { }).Esperar(HttpStatusCode.Accepted);
            var reimpresso = (await app.Impressoras.EsperarAsync("(REIMPRESSÃO)")).Last(i => i.Texto.Contains(pedido.Numero));
            Assert.Contains("2 x Frango grelhado", reimpresso.Texto);

            // Anular o item enviado: a cozinha recebe a anulação com o motivo.
            pedido = await (await c.Gerente.Post($"/api/pedidos/{pedido.Id}/itens/{pedido.Itens[0].Id}/cancelar", new ComMotivo("Cliente desistiu", pedido.RowVersion))
                .Esperar(HttpStatusCode.OK)).Ler<PedidoDto>();
            var anulacao = Assert.Single(await app.Impressoras.EsperarAsync("Cliente desistiu"));
            Assert.Equal(ImpressorasTeste.Cozinha, anulacao.Impressora);
            Assert.Contains("*** ANULAÇÃO ***", anulacao.Texto);
            Assert.Contains("NÃO FAZER 2 x Frango grelhado", anulacao.Texto);
            Assert.Null(anulacao.Logotipo); // talões nunca levam logótipo

            // Cancelar um pedido com itens enviados também anula na cozinha.
            var outro = await c.AbrirMesaAsync(c.Gerente, c.Mesa2, (c.Prato, 1));
            outro = await (await c.Gerente.Post($"/api/pedidos/{outro.Id}/enviar-cozinha", new ComVersao(outro.RowVersion)).Esperar(HttpStatusCode.OK)).Ler<PedidoDto>();
            await c.Gerente.Post($"/api/pedidos/{outro.Id}/cancelar", new ComMotivo("Mesa foi embora", outro.RowVersion)).Esperar(HttpStatusCode.OK);
            Assert.Contains("NÃO FAZER 1 x Frango grelhado", Assert.Single(await app.Impressoras.EsperarAsync("Mesa foi embora")).Texto);
        }
        finally
        {
            await DesactivarAsync(c.Gerente, cozinha);
        }
    }

    [Fact]
    public async Task Outro_centro_de_producao_so_recebe_o_que_lhe_e_atribuido()
    {
        var c = await CenarioVenda.CriarAsync(app);
        var cozinha = await ImpressoraAsync(c.Gerente, FuncaoImpressora.Cozinha, ImpressorasTeste.Cozinha);
        var pastelaria = await ImpressoraAsync(c.Gerente, FuncaoImpressora.Producao, ImpressorasTeste.A4);
        try
        {
            var sobremesas = await (await c.Gerente.Post("/api/categorias", new GuardarCategoria(Unico("Sobremesas "), null, null, null, null, 0, pastelaria.Id))
                .Esperar(HttpStatusCode.Created)).Ler<CategoriaDto>();
            var bolo = await (await c.Gerente.Post("/api/produtos", new GuardarProduto(Unico("S", 10), null, "Bolo de chocolate", null, sobremesas.Id,
                TipoProduto.Restaurante, 150, 0, null, null, true, true, null)).Esperar(HttpStatusCode.Created)).Ler<ProdutoDto>();

            var pedido = await c.AbrirMesaAsync(c.Gerente, c.Mesa1, (c.Prato, 1), (bolo, 1));
            await c.Gerente.Post($"/api/pedidos/{pedido.Id}/enviar-cozinha", new ComVersao(pedido.RowVersion)).Esperar(HttpStatusCode.OK);
            var taloes = await app.Impressoras.EsperarAsync(pedido.Numero, quantos: 2);
            Assert.Contains("Bolo de chocolate", Assert.Single(taloes, t => t.Impressora == ImpressorasTeste.A4).Texto);
            var naCozinha = Assert.Single(taloes, t => t.Impressora == ImpressorasTeste.Cozinha);
            Assert.DoesNotContain("Bolo", naCozinha.Texto); // o centro de produção não recebe pelas regras automáticas, nem a cozinha o que é dele
        }
        finally
        {
            await DesactivarAsync(c.Gerente, cozinha, pastelaria);
        }
    }

    [Fact]
    public async Task Relatorio_de_pagamentos_e_fecho_de_caixa_em_documento_com_permissoes()
    {
        var c = await CenarioVenda.CriarAsync(app);
        var pedido = await c.AbrirMesaAsync(c.Gerente, c.Mesa1, (c.Prato, 1));
        await c.PagarAsync(c.Gerente, pedido.Id, "MPESA", pedido.Total, referencia: "MP-TESTE-777").Esperar(HttpStatusCode.Created);

        var pagamentos = await c.Gerente.Obter<DocumentoImpressao>($"/api/relatorios/documento?tipo=Pagamentos&desde={Hoje()}&ate={Hoje()}");
        var texto = Texto(pagamentos);
        Assert.Contains("RELATÓRIO DE PAGAMENTOS", texto);
        Assert.Contains($"{pedido.Numero} · M-Pesa | 450,50 MT", texto);
        Assert.Contains("Ref. MP-TESTE-777", texto);
        Assert.Contains("TOTAL RECEBIDO", texto);
        Assert.Equal(UsoLogotipo.SoA4, pagamentos.Logotipo);

        foreach (var tipo in Enum.GetValues<TipoDocumentoRelatorio>().Where(t => t != TipoDocumentoRelatorio.FechoCaixa))
            Assert.NotEmpty((await c.Gerente.Obter<DocumentoImpressao>($"/api/relatorios/documento?tipo={tipo}&desde={Hoje()}&ate={Hoje()}")).Linhas);

        var fecho = Texto(await c.Gerente.Obter<DocumentoImpressao>($"/api/relatorios/documento?tipo=FechoCaixa&sessaoId={c.Sessao.Id}"));
        Assert.Contains("PONTO DE SITUAÇÃO", fecho);
        Assert.Contains("M-Pesa", fecho);
        Assert.Contains("TOTAL ESPERADO", fecho);

        var (garcom, _) = await app.ClienteAutenticadoAsync(PerfisPadrao.Garcom);
        Assert.Equal(HttpStatusCode.Forbidden, (await garcom.GetAsync($"/api/relatorios/documento?tipo=Pagamentos&desde={Hoje()}&ate={Hoje()}")).StatusCode);
        var (caixa, _) = await app.ClienteAutenticadoAsync(PerfisPadrao.Caixa);
        Assert.Equal(HttpStatusCode.Forbidden, (await caixa.GetAsync($"/api/relatorios/documento?tipo=FechoCaixa&sessaoId={c.Sessao.Id}")).StatusCode); // sessão de outro operador
        Assert.Equal(HttpStatusCode.Forbidden, (await caixa.GetAsync($"/api/relatorios/documento?tipo=Stock")).StatusCode);
    }

    [Fact]
    public async Task Logotipo_sai_no_recibo_80mm_e_nos_relatorios_A4_mas_nao_em_58mm()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(6, 1))
            return;
        var c = await CenarioVenda.CriarAsync(app);
        await c.Gerente.Put("/api/configuracoes", new GuardarConfiguracoes(new Dictionary<string, string> { ["estabelecimento.logotipo"] = "http://outro/logo.png" }))
            .Esperar(HttpStatusCode.UnprocessableEntity);

        var logo = await (await c.Gerente.PostAsync("/api/imagens/carregar", Png()).Esperar(HttpStatusCode.OK)).Ler<ImagemGuardada>();
        await c.Gerente.Put("/api/configuracoes", new GuardarConfiguracoes(new Dictionary<string, string> { ["estabelecimento.logotipo"] = logo.Url })).Esperar(HttpStatusCode.OK);
        Assert.Equal(logo.Url, (await c.Gerente.Obter<Application.Sistema.ConfiguracoesPublicas>("/api/configuracoes/publicas")).Logotipo);

        var caixa80 = await ImpressoraAsync(c.Gerente, FuncaoImpressora.Caixa, ImpressorasTeste.Caixa, 80);
        var a4 = await ImpressoraAsync(c.Gerente, FuncaoImpressora.Producao, ImpressorasTeste.A4, 210);
        var estreita = await ImpressoraAsync(c.Gerente, FuncaoImpressora.Bar, ImpressorasTeste.Bar, 58);
        try
        {
            var pedido = await c.AbrirMesaAsync(c.Gerente, c.Mesa1, (c.Bebida, 1));
            pedido = await (await c.PagarAsync(c.Gerente, pedido.Id, "DINHEIRO", pedido.Total).Esperar(HttpStatusCode.Created)).Ler<PedidoDto>();
            Assert.NotNull(Assert.Single(await app.Impressoras.EsperarAsync(pedido.ReciboNumero!)).Logotipo); // recibo 80 mm com logótipo

            // O mesmo relatório: A4 com logótipo, 80 mm e 58 mm sem.
            foreach (var impressora in new[] { a4, caixa80, estreita })
                await c.Gerente.Post("/api/relatorios/documento/imprimir", new ImprimirRelatorio(TipoDocumentoRelatorio.Vendas, DateOnly.Parse(Hoje()), null, null, impressora.Id))
                    .Esperar(HttpStatusCode.Accepted);
            var resumos = await app.Impressoras.EsperarAsync("RESUMO DE VENDAS", quantos: 3);
            Assert.NotNull(resumos.Last(r => r.Impressora == ImpressorasTeste.A4).Logotipo);
            Assert.Null(resumos.Last(r => r.Impressora == ImpressorasTeste.Caixa).Logotipo);
            Assert.Equal(58, resumos.Last(r => r.Impressora == ImpressorasTeste.Bar).LarguraPapelMm);
        }
        finally
        {
            await c.Gerente.Put("/api/configuracoes", new GuardarConfiguracoes(new Dictionary<string, string> { ["estabelecimento.logotipo"] = "" })).Esperar(HttpStatusCode.OK);
            await DesactivarAsync(c.Gerente, caixa80, a4, estreita);
        }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows6.1")]
    private static MultipartFormDataContent Png()
    {
        using var bmp = new Bitmap(300, 120);
        using (var g = Graphics.FromImage(bmp))
            g.Clear(Color.Orange);
        using var m = new MemoryStream();
        bmp.Save(m, ImageFormat.Png);
        var ficheiro = new ByteArrayContent(m.ToArray());
        ficheiro.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        return new MultipartFormDataContent { { ficheiro, "ficheiro", "logo.png" } };
    }
}
