using System.Net;
using LAMGestRestaurant.Api.Controllers;
using LAMGestRestaurant.Application.Caixa;
using LAMGestRestaurant.Application.Impressao;
using LAMGestRestaurant.Application.Seguranca;
using LAMGestRestaurant.Application.Sistema;
using LAMGestRestaurant.Application.Vendas;
using LAMGestRestaurant.Domain.Comum;
using LAMGestRestaurant.Tests.Infraestrutura;
using static LAMGestRestaurant.Tests.Infraestrutura.ExtensoesHttp;

namespace LAMGestRestaurant.Tests;

[Collection(ColeccaoBaseDados.Nome)]
public class ImpressaoTests(AplicacaoTeste app)
{
    private const string Url = "/api/configuracoes/impressoras";

    private static async Task<ImpressoraDto> CriarAsync(HttpClient c, FuncaoImpressora funcao, string windows, int largura, int? terminal = null, int? estacao = null) =>
        await (await c.Post(Url, new GuardarImpressora(Unico(funcao + " "), funcao, windows, largura, estacao, terminal)).Esperar(HttpStatusCode.OK)).Ler<ImpressoraDto>();

    /// <summary>As impressoras são globais: desactivam-se no fim para não imprimirem nos outros testes.</summary>
    private static async Task DesactivarAsync(HttpClient c, params ImpressoraDto[] impressoras)
    {
        foreach (var i in impressoras)
            await c.Post($"{Url}/{i.Id}/activo", new { activo = false }).Esperar(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task So_aceita_impressoras_instaladas_no_servidor_e_papel_de_80_58_ou_A4()
    {
        var (admin, _) = await app.ClienteAutenticadoAsync(PerfisPadrao.Administrador);
        Assert.Contains(ImpressorasTeste.Cozinha, await admin.Obter<List<string>>($"{Url}/instaladas"));

        await admin.Post(Url, new GuardarImpressora(Unico("X "), FuncaoImpressora.Caixa, "Não existe", 80, null, null)).Esperar(HttpStatusCode.UnprocessableEntity);
        await admin.Post(Url, new GuardarImpressora(Unico("X "), FuncaoImpressora.Caixa, ImpressorasTeste.Caixa, 59, null, null)).Esperar(HttpStatusCode.UnprocessableEntity);

        // Estação só faz sentido para cozinha/bar e terminal só para caixa: o que não se aplica é ignorado.
        var terminal = await (await admin.Post("/api/terminais", new GuardarTerminal(Unico("T "), null)).Esperar(HttpStatusCode.Created)).Ler<TerminalDto>();
        var cozinha = await CriarAsync(admin, FuncaoImpressora.Cozinha, ImpressorasTeste.Cozinha.ToUpperInvariant(), 80, terminal: terminal.Id);
        Assert.Null(cozinha.TerminalId);
        Assert.Equal(ImpressorasTeste.Cozinha, cozinha.NomeWindows); // nome normalizado para o do Windows
        var a4 = await CriarAsync(admin, FuncaoImpressora.Caixa, ImpressorasTeste.A4, 210, terminal: terminal.Id);
        Assert.Equal((terminal.Id, 210), (a4.TerminalId, a4.LarguraPapelMm));
        await admin.Post(Url, new GuardarImpressora(cozinha.Nome, FuncaoImpressora.Bar, ImpressorasTeste.Bar, 58, null, null)).Esperar(HttpStatusCode.Conflict);

        var editada = await (await admin.Put($"{Url}/{cozinha.Id}", new GuardarImpressora(cozinha.Nome, FuncaoImpressora.Bar, ImpressorasTeste.Bar, 58, null, null))
            .Esperar(HttpStatusCode.OK)).Ler<ImpressoraDto>();
        Assert.Equal((FuncaoImpressora.Bar, 58), (editada.Funcao, editada.LarguraPapelMm));
        await DesactivarAsync(admin, cozinha, a4);

        var (gerente, _) = await app.ClienteAutenticadoAsync(PerfisPadrao.Gerente); // sem configuracoes.gerir
        Assert.Equal(HttpStatusCode.Forbidden, (await gerente.GetAsync(Url)).StatusCode);
    }

    [Fact]
    public async Task Envio_imprime_cozinha_e_bar_separados_e_o_recibo_sai_no_caixa_do_terminal()
    {
        var c = await CenarioVenda.CriarAsync(app);
        var cozinha = await CriarAsync(c.Gerente, FuncaoImpressora.Cozinha, ImpressorasTeste.Cozinha, 80);
        var bar = await CriarAsync(c.Gerente, FuncaoImpressora.Bar, ImpressorasTeste.Bar, 58);
        var caixaGeral = await CriarAsync(c.Gerente, FuncaoImpressora.Caixa, ImpressorasTeste.A4, 210);
        var caixaTerminal = await CriarAsync(c.Gerente, FuncaoImpressora.Caixa, ImpressorasTeste.Caixa, 80, terminal: c.Sessao.TerminalId);
        try
        {
            var pedido = await c.AbrirMesaAsync(c.Gerente, c.Mesa1, (c.Prato, 2), (c.Bebida, 1));
            pedido = await (await c.Gerente.Post($"/api/pedidos/{pedido.Id}/enviar-cozinha", new ComVersao(pedido.RowVersion)).Esperar(HttpStatusCode.OK)).Ler<PedidoDto>();

            var taloes = await app.Impressoras.EsperarAsync(pedido.Numero, quantos: 2);
            var naCozinha = Assert.Single(taloes, t => t.Impressora == ImpressorasTeste.Cozinha);
            Assert.Contains("2 x Frango grelhado", naCozinha.Texto);
            Assert.DoesNotContain("Refrigerante", naCozinha.Texto);
            Assert.Contains($"Mesa {c.Mesa1.Nome}", naCozinha.Texto);
            var noBar = Assert.Single(taloes, t => t.Impressora == ImpressorasTeste.Bar);
            Assert.Equal(58, noBar.LarguraPapelMm);
            Assert.Contains("1 x Refrigerante", noBar.Texto);
            Assert.DoesNotContain("Frango", noBar.Texto);

            pedido = await (await c.PagarAsync(c.Gerente, pedido.Id, "DINHEIRO", pedido.Total, recebido: 1000m).Esperar(HttpStatusCode.Created)).Ler<PedidoDto>();
            var recibo = Assert.Single(await app.Impressoras.EsperarAsync(pedido.ReciboNumero!));
            Assert.Equal(ImpressorasTeste.Caixa, recibo.Impressora); // a do terminal, não a geral
            Assert.Contains("TOTAL | 961,00 MT", recibo.Texto);
            Assert.Contains("Troco 39,00 MT", recibo.Texto);

            // Reimpressão a partir de um posto sem impressora própria → impressora de caixa geral (A4).
            await c.Gerente.Post($"/api/pedidos/{pedido.Id}/recibo/imprimir", new ImprimirReciboNoTerminal(int.MaxValue)).Esperar(HttpStatusCode.Accepted);
            var reimpressoes = await app.Impressoras.EsperarAsync(pedido.ReciboNumero!, quantos: 2);
            Assert.Contains(reimpressoes, r => r.Impressora == ImpressorasTeste.A4 && r.LarguraPapelMm == 210);
        }
        finally
        {
            await DesactivarAsync(c.Gerente, cozinha, bar, caixaGeral, caixaTerminal);
        }
    }

    [Fact]
    public async Task Recibo_automatico_pode_ser_desligado_e_sem_impressora_de_caixa_a_reimpressao_e_recusada()
    {
        var c = await CenarioVenda.CriarAsync(app);
        var pedido = await c.AbrirMesaAsync(c.Gerente, c.Mesa1, (c.Bebida, 1));
        pedido = await (await c.PagarAsync(c.Gerente, pedido.Id, "DINHEIRO", pedido.Total).Esperar(HttpStatusCode.Created)).Ler<PedidoDto>();
        // Sem impressora de caixa activa: o posto deve imprimir pelo navegador.
        await c.Gerente.Post($"/api/pedidos/{pedido.Id}/recibo/imprimir", new ImprimirReciboNoTerminal(null)).Esperar(HttpStatusCode.UnprocessableEntity);

        var caixa = await CriarAsync(c.Gerente, FuncaoImpressora.Caixa, ImpressorasTeste.Caixa, 80);
        await c.Gerente.Put("/api/configuracoes", new Api.Controllers.GuardarConfiguracoes(new Dictionary<string, string> { ["impressao.recibo-automatico"] = "false" }))
            .Esperar(HttpStatusCode.OK);
        try
        {
            var outro = await c.AbrirMesaAsync(c.Gerente, c.Mesa2, (c.Bebida, 1));
            outro = await (await c.PagarAsync(c.Gerente, outro.Id, "DINHEIRO", outro.Total).Esperar(HttpStatusCode.Created)).Ler<PedidoDto>();
            await Task.Delay(1500);
            Assert.Empty(app.Impressoras.Com(outro.ReciboNumero!));
        }
        finally
        {
            await c.Gerente.Put("/api/configuracoes", new Api.Controllers.GuardarConfiguracoes(new Dictionary<string, string> { ["impressao.recibo-automatico"] = "true" }))
                .Esperar(HttpStatusCode.OK);
            await DesactivarAsync(c.Gerente, caixa);
        }
    }

    [Fact]
    public async Task Impressora_presa_da_erro_e_nao_para_as_outras_nem_os_envios_seguintes()
    {
        var c = await CenarioVenda.CriarAsync(app);
        var presa = await CriarAsync(c.Gerente, FuncaoImpressora.Cozinha, ImpressorasTeste.Pendurada, 80);
        var bar = await CriarAsync(c.Gerente, FuncaoImpressora.Bar, ImpressorasTeste.Bar, 80);
        try
        {
            var pedido = await c.AbrirMesaAsync(c.Gerente, c.Mesa1, (c.Prato, 1), (c.Bebida, 1));
            for (var envio = 1; envio <= 3; envio++)
            {
                if (envio > 1)
                    pedido = await (await c.Gerente.Post($"/api/pedidos/{pedido.Id}/itens",
                            new { itens = new[] { new { produtoId = c.Prato.Id, quantidade = envio }, new { produtoId = c.Bebida.Id, quantidade = envio + 10 } }, rowVersion = pedido.RowVersion })
                        .Esperar(HttpStatusCode.OK)).Ler<PedidoDto>();
                pedido = await (await c.Gerente.Post($"/api/pedidos/{pedido.Id}/enviar-cozinha", new ComVersao(pedido.RowVersion)).Esperar(HttpStatusCode.OK)).Ler<PedidoDto>();
                // O bar imprime logo, mesmo com a cozinha presa no controlador.
                await app.Impressoras.EsperarAsync($"{(envio == 1 ? 1 : envio + 10)} x Refrigerante");
            }

            // A impressora presa fica com o erro de tempo esgotado (2 s nos testes) e a fila dela continua.
            ImpressoraDto? estado = null;
            for (var i = 0; i < 100 && estado?.UltimoErro is null; i++, await Task.Delay(100))
                estado = (await c.Gerente.Obter<List<ImpressoraDto>>(Url)).Single(x => x.Id == presa.Id);
            Assert.Contains("não respondeu", estado!.UltimoErro);
        }
        finally
        {
            app.Impressoras.SoltarPendurada.Set();
            await DesactivarAsync(c.Gerente, presa, bar);
        }
    }

    [Fact]
    public async Task Falha_da_impressora_fica_registada_no_estado()
    {
        var (admin, _) = await app.ClienteAutenticadoAsync(PerfisPadrao.Administrador);
        var avariada = await CriarAsync(admin, FuncaoImpressora.Cozinha, ImpressorasTeste.Avariada, 80);
        var boa = await CriarAsync(admin, FuncaoImpressora.Caixa, ImpressorasTeste.Caixa, 80);
        try
        {
            await admin.Post($"{Url}/{avariada.Id}/teste", new { }).Esperar(HttpStatusCode.Accepted);
            await admin.Post($"{Url}/{boa.Id}/teste", new { }).Esperar(HttpStatusCode.Accepted);
            Assert.Contains(await app.Impressoras.EsperarAsync(boa.Nome), i => i.Texto.Contains("Impressão de teste"));

            ImpressoraDto? estado = null;
            for (var i = 0; i < 50 && estado?.UltimoErro is null; i++, await Task.Delay(100))
                estado = (await admin.Obter<List<ImpressoraDto>>(Url)).Single(x => x.Id == avariada.Id);
            Assert.Equal("Sem papel", estado!.UltimoErro);
            var ok = (await admin.Obter<List<ImpressoraDto>>(Url)).Single(x => x.Id == boa.Id);
            Assert.True(ok is { UltimoErro: null, UltimaImpressaoUtc: not null });
        }
        finally
        {
            await DesactivarAsync(admin, avariada, boa);
        }
    }

    [Fact]
    public async Task Modos_de_pagamento_criam_se_editam_se_e_desactivam_se()
    {
        var (admin, _) = await app.ClienteAutenticadoAsync(PerfisPadrao.Administrador);
        var nome = Unico("Cartão Débito ", 24);
        var novo = await (await admin.Post("/api/configuracoes/metodos-pagamento", new GuardarMetodoPagamento(nome, false, true, false, 50)).Esperar(HttpStatusCode.OK))
            .Ler<MetodoPagamentoDto>();
        Assert.StartsWith("CARTAODEBITO", novo.Codigo);
        Assert.True(novo is { ExigeReferencia: true, PermiteTroco: false, Activo: true, Ordem: 50 });

        await admin.Post("/api/configuracoes/metodos-pagamento", new GuardarMetodoPagamento(nome, false, false, false, 1)).Esperar(HttpStatusCode.Conflict);
        await admin.Post("/api/configuracoes/metodos-pagamento", new GuardarMetodoPagamento(Unico("Vale "), true, false, false, 1)).Esperar(HttpStatusCode.UnprocessableEntity);

        var editado = await (await admin.Put($"/api/configuracoes/metodos-pagamento/{novo.Id}", new GuardarMetodoPagamento(nome + " POS", false, false, false, 2))
            .Esperar(HttpStatusCode.OK)).Ler<MetodoPagamentoDto>();
        Assert.Equal((novo.Codigo, nome + " POS", false), (editado.Codigo, editado.Nome, editado.ExigeReferencia)); // o código não muda

        await admin.Post($"/api/configuracoes/metodos-pagamento/{novo.Id}/activo", new { activo = false }).Esperar(HttpStatusCode.NoContent);
        Assert.False((await admin.Obter<List<MetodoPagamentoDto>>("/api/caixa/metodos-pagamento")).Single(m => m.Id == novo.Id).Activo);

        var (caixa, _) = await app.ClienteAutenticadoAsync(PerfisPadrao.Caixa);
        await caixa.Post("/api/configuracoes/metodos-pagamento", new GuardarMetodoPagamento(Unico("M "), false, false, false, 1)).Esperar(HttpStatusCode.Forbidden);
    }

    [Fact]
    public void Recibo_formata_valores_com_virgula_e_espaco_nos_milhares()
    {
        var recibo = new ReciboDto("REC-2026-000001", "PED-2026-000001", new DateTime(2026, 11, 3, 10, 0, 0, DateTimeKind.Utc), "Restaurante Teste",
            null, null, "123456789", "5", TipoPedido.Mesa, "Ana",
            [new ReciboLinha("Bife", 2, 617.25m, 1234.50m)], 1234.50m, 0, 170.28m, 0, 1234.50m,
            [new PagamentoDto(1, "Dinheiro", 1234.50m, 1300m, 65.50m, null, EstadoPagamento.Valido, DateTime.UtcNow, "Ana", null)], "MT", "Obrigado");

        var documento = ComposicaoImpressao.Recibo(recibo, TimeZoneInfo.FindSystemTimeZoneById("Africa/Maputo"));
        var linhas = documento.Linhas;

        // Tabela de artigos: títulos e linha com os valores sem moeda (a moeda vai no título da coluna).
        Assert.Contains(linhas, l => l.Cabecalho && l.Celulas!.SequenceEqual(["Descrição", "Qtd.", "Preço", "Total (MT)"]));
        Assert.Contains(linhas, l => l.Celulas is { } c && c.SequenceEqual(["Bife", "2", "617,25", "1 234,50"]));
        Assert.Contains(linhas, l => l.Texto == "TOTAL" && l.Direita == "1 234,50 MT" && l.Negrito);
        Assert.Contains(linhas, l => l.Texto == "03/11/2026 12:00"); // hora de Maputo
        Assert.Contains(linhas, l => l.Direita == "Troco 65,50 MT");
        Assert.Contains(documento.Cabecalho!, l => l.Texto == "NUIT 123456789"); // identificação do estabelecimento no cabeçalho
        Assert.Equal("Restaurante Teste", documento.Cabecalho![0].Texto);
    }
}
