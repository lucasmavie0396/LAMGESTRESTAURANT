using System.Net;
using LAMGestRestaurant.Api.Controllers;
using LAMGestRestaurant.Application.Caixa;
using LAMGestRestaurant.Application.Impressao;
using LAMGestRestaurant.Domain.Comum;
using LAMGestRestaurant.Domain.Sistema;
using LAMGestRestaurant.Tests.Infraestrutura;
using static LAMGestRestaurant.Tests.Infraestrutura.ExtensoesHttp;

namespace LAMGestRestaurant.Tests;

/// <summary>
/// Impressão automática do fecho ao confirmar: impressora de caixa do terminal da sessão; sem ela, o documento volta para o
/// navegador; com a opção desligada não imprime.
/// </summary>
[Collection(ColeccaoBaseDados.Nome)]
public class FechoAutomaticoTests(AplicacaoTeste app)
{
    private const string Url = "/api/relatorios/fecho/imprimir-automatico";

    private static Task Definir(HttpClient c, string valor) =>
        c.Put("/api/configuracoes", new GuardarConfiguracoes(new Dictionary<string, string> { [ChavesConfiguracao.FechoAutomatico] = valor })).Esperar(HttpStatusCode.OK);

    [Fact]
    public async Task Fecho_de_caixa_sai_na_impressora_do_terminal_ou_volta_para_o_navegador()
    {
        var c = await CenarioVenda.CriarAsync(app);
        var dinheiro = c.Metodos["DINHEIRO"];
        var sessao = await c.Gerente.Obter<SessaoCaixaDto>($"/api/caixa/sessoes/{c.Sessao.Id}");
        await c.Gerente.Post($"/api/caixa/sessoes/{sessao.Id}/fechar", new FecharCaixa([new ContagemMetodo(dinheiro, 1000m)], null, sessao.RowVersion))
            .Esperar(HttpStatusCode.OK);
        await c.Gerente.Post(Url, new ImprimirFecho(null, null, null)).Esperar(HttpStatusCode.UnprocessableEntity);

        // Sem impressora de caixa no servidor: o documento volta para o posto imprimir pelo navegador.
        var semImpressora = await (await c.Gerente.Post(Url, new ImprimirFecho(sessao.Id, null, null)).Esperar(HttpStatusCode.OK)).Ler<FechoAutomatico>();
        Assert.True(semImpressora.Automatico);
        Assert.Null(semImpressora.Impressora);
        Assert.Contains(semImpressora.Documento!.Linhas, l => l.Texto == "FECHO DE CAIXA");

        var caixa = await (await c.Gerente.Post("/api/configuracoes/impressoras",
            new GuardarImpressora(Unico("Caixa "), FuncaoImpressora.Caixa, ImpressorasTeste.Caixa, 80, null, sessao.TerminalId)).Esperar(HttpStatusCode.OK)).Ler<ImpressoraDto>();
        try
        {
            var impresso = await (await c.Gerente.Post(Url, new ImprimirFecho(sessao.Id, null, null)).Esperar(HttpStatusCode.OK)).Ler<FechoAutomatico>();
            Assert.Equal((true, caixa.Nome, (DocumentoImpressao?)null), (impresso.Automatico, impresso.Impressora, impresso.Documento));
            var papel = Assert.Single(await app.Impressoras.EsperarAsync($"Sessão n.º {sessao.Id} ·"));
            Assert.Equal(ImpressorasTeste.Caixa, papel.Impressora);
            Assert.Contains("FECHO DE CAIXA", papel.Texto);

            // Opção desligada: não imprime nada.
            await Definir(c.Gerente, "false");
            var desligado = await (await c.Gerente.Post(Url, new ImprimirFecho(sessao.Id, null, null)).Esperar(HttpStatusCode.OK)).Ler<FechoAutomatico>();
            Assert.False(desligado.Automatico);
            Assert.Single(app.Impressoras.Com($"Sessão n.º {sessao.Id} ·"));
        }
        finally
        {
            await Definir(c.Gerente, "true");
            await c.Gerente.Post($"/api/configuracoes/impressoras/{caixa.Id}/activo", new { activo = false }).Esperar(HttpStatusCode.NoContent);
        }
    }
}
