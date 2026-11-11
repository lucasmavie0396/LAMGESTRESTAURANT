using System.Net;
using LAMGestRestaurant.Api.Controllers;
using LAMGestRestaurant.Application.Caixa;
using LAMGestRestaurant.Application.Cartoes;
using LAMGestRestaurant.Application.Comum;
using LAMGestRestaurant.Application.Impressao;
using LAMGestRestaurant.Application.Relatorios;
using LAMGestRestaurant.Application.Seguranca;
using LAMGestRestaurant.Application.Vendas;
using LAMGestRestaurant.Application.Sala;
using LAMGestRestaurant.Domain.Comum;
using LAMGestRestaurant.Domain.Cartoes;
using LAMGestRestaurant.Tests.Infraestrutura;
using Microsoft.EntityFrameworkCore;
using static LAMGestRestaurant.Tests.Infraestrutura.ExtensoesHttp;

namespace LAMGestRestaurant.Tests;

/// <summary>Configurações → Postos (online, caixa, agente, tablets) e extrato/relatório dos cartões por datas ou por caixa.</summary>
[Collection(ColeccaoBaseDados.Nome)]
public class PostosTests(AplicacaoTeste app)
{
    private const string Postos = "/api/configuracoes/postos";

    private static DateOnly Hoje() =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("Africa/Maputo")));

    [Fact]
    public async Task Posto_fica_online_com_o_sinal_do_navegador_e_mostra_caixa_e_operador()
    {
        var c = await CenarioVenda.CriarAsync(app);
        var terminal = c.Sessao.TerminalId;
        var antes = Assert.Single(await c.Gerente.Obter<List<PostoEstadoDto>>(Postos), p => p.Id == terminal);
        Assert.False(antes.Online);
        Assert.Single(antes.CaixasAbertos); // a sessão do cenário

        // Qualquer utilizador com sessão pode enviar o sinal do seu posto; só a gestão vê a lista.
        var pos = await app.ClienteComPermissoesAsync(Permissoes.PosUsar);
        await pos.PostAsync($"/api/terminais/{terminal}/presenca", null).Esperar(HttpStatusCode.NoContent);
        await pos.GetAsync(Postos).Esperar(HttpStatusCode.Forbidden);
        await pos.PostAsync("/api/terminais/999999/presenca", null).Esperar(HttpStatusCode.NotFound);

        var depois = Assert.Single(await c.Gerente.Obter<List<PostoEstadoDto>>(Postos), p => p.Id == terminal);
        Assert.True(depois.Online);
        Assert.NotNull(depois.VistoPor);
        Assert.NotNull(depois.VistoEmUtc);

        // Há mais de 2 minutos: offline. Desactivado: recusa o sinal.
        await using (var db = app.CriarContexto())
        {
            var t = await db.Terminais.SingleAsync(x => x.Id == terminal);
            t.VistoEmUtc = DateTime.UtcNow.AddMinutes(-3);
            await db.SaveChangesAsync();
        }
        Assert.False(Assert.Single(await c.Gerente.Obter<List<PostoEstadoDto>>(Postos), p => p.Id == terminal).Online);

        var outro = await (await c.Gerente.Post("/api/terminais", new GuardarTerminal(Unico("Desligado "), null)).Esperar(HttpStatusCode.Created)).Ler<TerminalDto>();
        await c.Gerente.Post($"/api/terminais/{outro.Id}/activo", new { activo = false }).Esperar(HttpStatusCode.NoContent);
        await pos.PostAsync($"/api/terminais/{outro.Id}/presenca", null).Esperar(HttpStatusCode.UnprocessableEntity);

        var rede = await c.Gerente.Obter<RedeServidor>($"{Postos}/rede");
        Assert.All(rede.Enderecos, e => Assert.StartsWith("http", e));
    }

    [Fact]
    public async Task So_um_posto_liga_os_tablets_com_codigo_de_emparelhamento_guardado_em_hash()
    {
        var c = await CenarioVenda.CriarAsync(app);
        var a = await (await c.Gerente.Post("/api/terminais", new GuardarTerminal(Unico("Tablets A "), null)).Esperar(HttpStatusCode.Created)).Ler<TerminalDto>();
        var b = await (await c.Gerente.Post("/api/terminais", new GuardarTerminal(Unico("Tablets B "), null)).Esperar(HttpStatusCode.Created)).Ler<TerminalDto>();
        try
        {
            var codigoA = await (await c.Gerente.Post($"{Postos}/{a.Id}/tablets", new DefinirTablets(true)).Esperar(HttpStatusCode.OK)).Ler<CodigoTablets>();
            var qr = await (await c.Gerente.Post($"{Postos}/qr", new TextoQr($"lamgest://emparelhar?codigo={codigoA.Codigo}")).Esperar(HttpStatusCode.OK)).Ler<RespostaQr>();
            Assert.StartsWith("<svg", qr.Svg);
            await c.Gerente.Post($"{Postos}/qr", new TextoQr(" ")).Esperar(HttpStatusCode.UnprocessableEntity);
            Assert.Matches("^[A-HJ-NP-Z2-9]{4}-[A-HJ-NP-Z2-9]{4}$", codigoA.Codigo);
            await using (var scope = app.Services.CreateAsyncScope())
            {
                var servico = scope.ServiceProvider.GetRequiredService<ServicoPostos>();
                Assert.True(await servico.CodigoValidoAsync(codigoA.Codigo));
                Assert.True(await servico.CodigoValidoAsync(codigoA.Codigo.Replace("-", "").ToLowerInvariant()));
                Assert.False(await servico.CodigoValidoAsync("AAAA-BBBB"));
            }
            // A app de venda emparelha sem sessão e recebe o posto; com um código errado é recusada.
            var anonimo = app.CreateClient();
            var emparelhado = await (await anonimo.Post("/api/tablets/emparelhar", new Emparelhar(codigoA.Codigo)).Esperar(HttpStatusCode.OK)).Ler<PostoEmparelhado>();
            Assert.Equal((a.Id, a.Nome), (emparelhado.TerminalId, emparelhado.Nome));
            await anonimo.Post("/api/tablets/emparelhar", new Emparelhar("AAAA-BBBB")).Esperar(HttpStatusCode.UnprocessableEntity);
            await using (var db = app.CriarContexto())
                Assert.DoesNotContain(codigoA.Codigo.Replace("-", ""), (await db.Terminais.SingleAsync(t => t.Id == a.Id)).TabletsCodigoHash!); // só o hash

            // Escolher B retira A; o código de A deixa de valer.
            var codigoB = await (await c.Gerente.Post($"{Postos}/{b.Id}/tablets", new DefinirTablets(true)).Esperar(HttpStatusCode.OK)).Ler<CodigoTablets>();
            var lista = await c.Gerente.Obter<List<PostoEstadoDto>>(Postos);
            Assert.Equal([b.Id], lista.Where(p => p.LigacaoTablets).Select(p => p.Id));
            await using (var scope = app.Services.CreateAsyncScope())
            {
                var servico = scope.ServiceProvider.GetRequiredService<ServicoPostos>();
                Assert.False(await servico.CodigoValidoAsync(codigoA.Codigo));
                Assert.True(await servico.CodigoValidoAsync(codigoB.Codigo));
                // Código novo: o anterior deixa de valer.
                var novo = await (await c.Gerente.PostAsync($"{Postos}/{b.Id}/tablets/codigo", null).Esperar(HttpStatusCode.OK)).Ler<CodigoTablets>();
                Assert.False(await servico.CodigoValidoAsync(codigoB.Codigo));
                Assert.True(await servico.CodigoValidoAsync(novo.Codigo));
            }
            await c.Gerente.PostAsync($"{Postos}/{a.Id}/tablets/codigo", null).Esperar(HttpStatusCode.UnprocessableEntity);
            await (await app.ClienteComPermissoesAsync(Permissoes.CaixaOperar)).Post($"{Postos}/{a.Id}/tablets", new DefinirTablets(true)).Esperar(HttpStatusCode.Forbidden);
        }
        finally
        {
            await c.Gerente.Post($"{Postos}/{b.Id}/tablets", new DefinirTablets(false)).Esperar(HttpStatusCode.NoContent);
        }
        Assert.DoesNotContain(await c.Gerente.Obter<List<PostoEstadoDto>>(Postos), p => p.LigacaoTablets && (p.Id == a.Id || p.Id == b.Id));
    }

    [Fact]
    public async Task Recibo_impresso_no_PDA_nao_sai_tambem_na_impressora_do_posto()
    {
        var c = await CenarioVenda.CriarAsync(app);
        var impressora = await (await c.Gerente.Post("/api/configuracoes/impressoras",
            new GuardarImpressora(Unico("Caixa PDA "), FuncaoImpressora.Caixa, ImpressorasTeste.Caixa, 80, null, c.Sessao.TerminalId)).Esperar(HttpStatusCode.OK)).Ler<ImpressoraDto>();
        try
        {
            async Task<PedidoDto> PagarAsync(MesaDto mesa, bool noDispositivo)
            {
                var pedido = await c.AbrirMesaAsync(c.Gerente, mesa, (c.Bebida, 1));
                return await (await c.Gerente.PostComChave($"/api/pedidos/{pedido.Id}/pagamentos/multiplos", CenarioVenda.Chave(),
                    new RegistarPagamentos(c.Sessao.Id, [new LinhaPagamento(c.Metodos["DINHEIRO"], pedido.Total, pedido.Total, null)], noDispositivo))
                    .Esperar(HttpStatusCode.Created)).Ler<PedidoDto>();
            }
            var noPda = await PagarAsync(c.Mesa1, true);
            var noPosto = await PagarAsync(c.Mesa2, false);
            await app.Impressoras.EsperarAsync(noPosto.ReciboNumero!); // a fila é sequencial: o anterior já teria saído
            Assert.Empty(app.Impressoras.Com(noPda.ReciboNumero!));
        }
        finally
        {
            await c.Gerente.Post($"/api/configuracoes/impressoras/{impressora.Id}/activo", new { activo = false }).Esperar(HttpStatusCode.NoContent);
        }
    }

    [Fact]
    public async Task Extrato_e_relatorio_dos_cartoes_por_datas_ou_por_caixa()
    {
        var c1 = await CenarioVenda.CriarAsync(app);
        var c2 = await CenarioVenda.CriarAsync(app); // outro operador, outro caixa
        var hoje = Hoje();
        var cartao = await (await c1.Gerente.PostComChave("/api/cartoes", CenarioVenda.Chave(), new RegistarCartao(null, "Caixas", 300m, c1.Metodos["DINHEIRO"]))
            .Esperar(HttpStatusCode.Created)).Ler<CartaoDto>();
        await c2.Gerente.PostComChave($"/api/cartoes/{cartao.Id}/recargas", CenarioVenda.Chave(), new RecarregarCartao(200m, c2.Metodos["DINHEIRO"]))
            .Esperar(HttpStatusCode.Created);
        var url = $"/api/cartoes/{cartao.Id}/movimentos";

        var tudo = await c1.Gerente.Obter<ExtratoCartaoDto>(url);
        Assert.Equal((2, 500m, 0m), (tudo.Total, tudo.Entradas, tudo.Saidas));
        Assert.Null(tudo.SaldoInicial);

        var porCaixa = await c1.Gerente.Obter<ExtratoCartaoDto>($"{url}?sessaoCaixaId={c2.Sessao.Id}");
        var recarga = Assert.Single(porCaixa.Itens);
        Assert.Equal((TipoMovimentoCartao.Recarga, 200m, c2.Sessao.Id), (recarga.Tipo, recarga.Valor, recarga.SessaoCaixaId));
        var sessoes = await c1.Gerente.Obter<List<SessaoCartoesDto>>($"/api/cartoes/{cartao.Id}/sessoes");
        Assert.Equal([c1.Sessao.Id, c2.Sessao.Id], sessoes.Select(s => s.Id).Order());
        Assert.All(sessoes, s => Assert.Equal(1, s.Movimentos));

        var dia = await c1.Gerente.Obter<ExtratoCartaoDto>($"{url}?desde={hoje:yyyy-MM-dd}&ate={hoje:yyyy-MM-dd}");
        Assert.Equal((2, 0m, 500m), (dia.Total, dia.SaldoInicial, dia.SaldoFinal));
        var amanha = await c1.Gerente.Obter<ExtratoCartaoDto>($"{url}?desde={hoje.AddDays(1):yyyy-MM-dd}&ate={hoje.AddDays(2):yyyy-MM-dd}");
        Assert.Equal((0, 500m, 500m), (amanha.Total, amanha.SaldoInicial, amanha.SaldoFinal));
        await c1.Gerente.GetAsync($"{url}?desde={hoje:yyyy-MM-dd}&ate={hoje.AddDays(-1):yyyy-MM-dd}").Esperar(HttpStatusCode.UnprocessableEntity);

        // Relatório de cartões só do caixa 2: só a recarga.
        var relatorio = await c1.Gerente.Obter<RelatorioCartoes>($"/api/relatorios/cartoes?sessaoCaixaId={c2.Sessao.Id}");
        Assert.Equal((0m, 200m, 1), (relatorio.Resumo.Carregado, relatorio.Resumo.Recarregado, relatorio.Movimentos.Count));
        Assert.Contains(await c1.Gerente.Obter<List<SessaoCartoesDto>>($"/api/relatorios/cartoes/sessoes?desde={hoje:yyyy-MM-dd}&ate={hoje:yyyy-MM-dd}"),
            s => s.Id == c2.Sessao.Id);
        var documento = TextoDocumento.De(await c1.Gerente.Obter<DocumentoImpressao>(
            $"/api/relatorios/documento?tipo={TipoDocumentoRelatorio.CartoesEvento}&sessaoId={c2.Sessao.Id}&desde={hoje:yyyy-MM-dd}&ate={hoje:yyyy-MM-dd}"));
        Assert.Contains($"Caixa n.º {c2.Sessao.Id}", documento);
    }

    private sealed record RespostaQr(string Svg);
}
