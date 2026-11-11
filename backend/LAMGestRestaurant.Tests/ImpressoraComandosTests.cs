using System.Net;
using LAMGestRestaurant.Api.Controllers;
using LAMGestRestaurant.Application.Cartoes;
using LAMGestRestaurant.Application.Impressao;
using LAMGestRestaurant.Application.Seguranca;
using LAMGestRestaurant.Application.Vendas;
using LAMGestRestaurant.Domain.Comum;
using LAMGestRestaurant.Infrastructure.Impressao;
using LAMGestRestaurant.Tests.Infraestrutura;
using Microsoft.EntityFrameworkCore;
using static LAMGestRestaurant.Tests.Infraestrutura.ExtensoesHttp;

namespace LAMGestRestaurant.Tests;

/// <summary>Talões encostados à esquerda da área imprimível, corte do papel no fim e gaveta do dinheiro nos pagamentos.</summary>
[Collection(ColeccaoBaseDados.Nome)]
public class ImpressoraComandosTests(AplicacaoTeste app)
{
    private const string Impressoras = "/api/configuracoes/impressoras";

    private sealed class Medidor : IMedidorTexto
    {
        public double Largura(string texto, EstiloTexto estilo) => texto.Length * 1.5;
        public double Altura(EstiloTexto estilo) => 3;
    }

    [Theory]
    [InlineData(80, 72)]
    [InlineData(58, 48)]
    public void Talao_desenhado_na_area_imprimivel_comeca_encostado_a_esquerda(int papel, double imprimivel)
    {
        var documento = new DocumentoImpressao("Teste", [new LinhaImpressao("Frango", "450,00 MT"), LinhaImpressao.Traco]);
        var antes = Assert.Single(Disposicao.Dispor(documento, papel, null, new Medidor()));
        var agora = Assert.Single(Disposicao.Dispor(documento, papel, null, new Medidor(), imprimivel));
        Assert.Equal((double)papel, antes.Largura);
        Assert.Equal(imprimivel, agora.Largura);
        var texto = agora.Operacoes.OfType<OpTexto>().First(t => t.Texto == "Frango");
        Assert.Equal(Disposicao.MargemImpressora, texto.X);
        Assert.True(texto.X < antes.Operacoes.OfType<OpTexto>().First(t => t.Texto == "Frango").X);
        Assert.Equal(imprimivel - Disposicao.MargemImpressora, agora.Operacoes.OfType<OpLinha>().Single().X2, 3);
        Assert.Equal(papel <= 58 ? 48 : 72, ImpressaoWindows.ImprimivelPadrao(papel));
    }

    [Fact]
    public async Task Talao_corta_o_papel_e_cada_pagamento_abre_a_gaveta_da_impressora_de_pagamentos()
    {
        var c = await CenarioVenda.CriarAsync(app);
        var terminal = c.Sessao.TerminalId;
        var caixa = await (await c.Gerente.Post(Impressoras, new GuardarImpressora(Unico("Pagamentos "), FuncaoImpressora.Caixa, ImpressorasTeste.Caixa, 80, null, terminal,
            AbrirGaveta: true)).Esperar(HttpStatusCode.OK)).Ler<ImpressoraDto>();
        Assert.True(caixa.CortarPapel && caixa.AbrirGaveta);
        try
        {
            var gavetasAntes = app.Impressoras.Gavetas(ImpressorasTeste.Caixa);
            // Pagamento parcial e depois o resto: cada um abre a gaveta; o recibo final sai com corte do papel.
            var pedido = await c.AbrirMesaAsync(c.Gerente, c.Mesa1, (c.Prato, 1));
            await c.PagarAsync(c.Gerente, pedido.Id, "DINHEIRO", 100m).Esperar(HttpStatusCode.Created);
            pedido = await (await c.PagarAsync(c.Gerente, pedido.Id, "DINHEIRO", pedido.Total - 100m).Esperar(HttpStatusCode.Created)).Ler<PedidoDto>();
            var recibo = Assert.Single(await app.Impressoras.EsperarAsync(pedido.ReciboNumero!));
            Assert.True(recibo.Documento.Cortar);
            Assert.False(recibo.Documento.AbrirGaveta);
            await Esperar(() => app.Impressoras.Gavetas(ImpressorasTeste.Caixa) >= gavetasAntes + 2);

            // Botão «Abrir gaveta» do POS: fica na auditoria.
            await c.Gerente.Post("/api/caixa/gaveta", new AbrirGavetaTerminal(terminal)).Esperar(HttpStatusCode.Accepted);
            await Esperar(() => app.Impressoras.Gavetas(ImpressorasTeste.Caixa) >= gavetasAntes + 3);
            await using (var db = app.CriarContexto())
                Assert.True(await db.RegistosAuditoria.AnyAsync(r => r.Accao == "caixa.abrir-gaveta" && r.EntidadeId == terminal.ToString()));
            await (await app.ClienteComPermissoesAsync(Permissoes.CozinhaVer)).Post("/api/caixa/gaveta", new AbrirGavetaTerminal(terminal)).Esperar(HttpStatusCode.Forbidden);

            // Carregar um Cartão Evento é dinheiro recebido: também abre a gaveta.
            await c.Gerente.PostComChave("/api/cartoes", CenarioVenda.Chave(), new RegistarCartao(null, null, 50m, c.Metodos["DINHEIRO"])).Esperar(HttpStatusCode.Created);
            await Esperar(() => app.Impressoras.Gavetas(ImpressorasTeste.Caixa) >= gavetasAntes + 4);

            // Sem corte nem gaveta: o talão sai sem o comando de corte e o botão é recusado.
            await c.Gerente.Put($"{Impressoras}/{caixa.Id}", new GuardarImpressora(caixa.Nome, FuncaoImpressora.Caixa, ImpressorasTeste.Caixa, 80, null, terminal,
                CortarPapel: false, AbrirGaveta: false)).Esperar(HttpStatusCode.OK);
            await c.Gerente.Post("/api/caixa/gaveta", new AbrirGavetaTerminal(terminal)).Esperar(HttpStatusCode.UnprocessableEntity);
            var outro = await c.AbrirMesaAsync(c.Gerente, c.Mesa2, (c.Bebida, 1));
            outro = await (await c.PagarAsync(c.Gerente, outro.Id, "DINHEIRO", outro.Total).Esperar(HttpStatusCode.Created)).Ler<PedidoDto>();
            Assert.False(Assert.Single(await app.Impressoras.EsperarAsync(outro.ReciboNumero!)).Documento.Cortar);
        }
        finally
        {
            await c.Gerente.Post($"{Impressoras}/{caixa.Id}/activo", new { activo = false }).Esperar(HttpStatusCode.NoContent);
        }
    }

    private static async Task Esperar(Func<bool> condicao)
    {
        for (var i = 0; i < 100 && !condicao(); i++)
            await Task.Delay(100);
        Assert.True(condicao());
    }
}
