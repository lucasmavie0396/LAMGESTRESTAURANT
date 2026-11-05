using System.Net;
using LAMGestRestaurant.Agente;
using LAMGestRestaurant.Application.Caixa;
using LAMGestRestaurant.Application.Impressao;
using LAMGestRestaurant.Application.Seguranca;
using LAMGestRestaurant.Application.Vendas;
using LAMGestRestaurant.Domain.Comum;
using LAMGestRestaurant.Tests.Infraestrutura;
using static LAMGestRestaurant.Tests.Infraestrutura.ExtensoesHttp;

namespace LAMGestRestaurant.Tests;

/// <summary>Impressoras instaladas nos postos: o agente de cada computador recebe os trabalhos e imprime-os no seu Windows.</summary>
[Collection(ColeccaoBaseDados.Nome)]
public class AgentesImpressaoTests(AplicacaoTeste app)
{
    private const string Url = "/api/configuracoes/impressoras";
    private const string TermicaPosto = "POS-80 do balcão";

    private static async Task<TerminalDto> CriarPostoAsync(HttpClient admin)
    {
        for (var tentativa = 0; ; tentativa++)
        {
            var numero = Random.Shared.Next(100, 999);
            var r = await admin.Post("/api/terminais", new GuardarTerminal(Unico("Posto "), null, numero));
            if (r.StatusCode == HttpStatusCode.Created)
                return await r.Ler<TerminalDto>();
            if (tentativa > 20)
                throw new InvalidOperationException(await r.Content.ReadAsStringAsync());
        }
    }

    private HttpClient Agente(int numero, string chave)
    {
        var c = app.CreateClient();
        c.DefaultRequestHeaders.Add("X-Posto", numero.ToString(System.Globalization.CultureInfo.InvariantCulture));
        c.DefaultRequestHeaders.Add("X-Agente-Token", chave);
        return c;
    }

    private static async Task<(HttpClient Agente, AgenteRegistado Registo)> InstalarAgenteAsync(AgentesImpressaoTests t, HttpClient admin, TerminalDto posto)
    {
        var registo = await (await admin.Post($"/api/postos/{posto.Numero}/agente", new { }).Esperar(HttpStatusCode.OK)).Ler<AgenteRegistado>();
        var agente = t.Agente(posto.Numero!.Value, registo.Chave);
        await agente.Post("/api/agente/estado", new EstadoAgente("teste", [TermicaPosto, "Microsoft Print to PDF"])).Esperar(HttpStatusCode.NoContent);
        return (agente, registo);
    }

    private static async Task<List<TrabalhoAgenteDto>> TrabalhosAsync(HttpClient agente, int quantos = 1)
    {
        var todos = new List<TrabalhoAgenteDto>();
        for (var i = 0; i < 10 && todos.Count < quantos; i++)
            todos.AddRange(await agente.Obter<List<TrabalhoAgenteDto>>("/api/agente/trabalhos?espera=3"));
        return todos;
    }

    [Fact]
    public async Task Agente_regista_se_com_administrador_e_so_a_chave_actual_vale()
    {
        var (admin, _) = await app.ClienteAutenticadoAsync(PerfisPadrao.Administrador);
        var posto = await CriarPostoAsync(admin);

        // Registar exige configuracoes.gerir; o agente sem chave (ou com chave errada) não entra.
        var (gerente, _) = await app.ClienteAutenticadoAsync(PerfisPadrao.Gerente);
        Assert.Equal(HttpStatusCode.Forbidden, (await gerente.Post($"/api/postos/{posto.Numero}/agente", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Agente(posto.Numero!.Value, "errada").GetAsync("/api/agente/trabalhos")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.Post("/api/postos/0/agente", new { })).StatusCode);

        var (agente, primeiro) = await InstalarAgenteAsync(this, admin, posto);
        var terminal = (await admin.Obter<List<TerminalDto>>("/api/terminais")).Single(t => t.Id == posto.Id);
        Assert.True(terminal.AgenteRegistado);
        Assert.Equal("teste", terminal.AgenteVersao);
        Assert.Contains(TermicaPosto, terminal.AgenteImpressoras!);

        var locais = await admin.Obter<List<LocalImpressoras>>($"{Url}/locais");
        Assert.Contains(ImpressorasTeste.Caixa, locais.Single(l => l.PostoId is null).Impressoras);
        var local = locais.Single(l => l.PostoId == posto.Id);
        Assert.True(local is { Ligado: true, TemAgente: true });
        Assert.Contains(TermicaPosto, local.Impressoras);

        // Reinstalar o agente gera chave nova: a anterior deixa de valer. Revogar retira-a.
        var (novo, segundo) = await InstalarAgenteAsync(this, admin, posto);
        Assert.NotEqual(primeiro.Chave, segundo.Chave);
        Assert.Equal(HttpStatusCode.Unauthorized, (await agente.GetAsync("/api/agente/trabalhos")).StatusCode);
        await admin.DeleteAsync($"/api/terminais/{posto.Id}/agente").Esperar(HttpStatusCode.NoContent);
        Assert.Equal(HttpStatusCode.Unauthorized, (await novo.GetAsync("/api/agente/trabalhos")).StatusCode);
    }

    [Fact]
    public async Task Impressora_do_posto_so_aceita_impressoras_desse_computador_e_o_agente_imprime_o_teste()
    {
        var (admin, _) = await app.ClienteAutenticadoAsync(PerfisPadrao.Administrador);
        var posto = await CriarPostoAsync(admin);
        // Sem agente instalado o posto não pode ter impressoras.
        await admin.Post(Url, new GuardarImpressora(Unico("P "), FuncaoImpressora.Caixa, TermicaPosto, 80, null, null, PostoId: posto.Id))
            .Esperar(HttpStatusCode.UnprocessableEntity);
        var (agente, _) = await InstalarAgenteAsync(this, admin, posto);
        // Uma impressora do servidor não está no posto (e vice-versa).
        await admin.Post(Url, new GuardarImpressora(Unico("P "), FuncaoImpressora.Caixa, ImpressorasTeste.Caixa, 80, null, null, PostoId: posto.Id))
            .Esperar(HttpStatusCode.UnprocessableEntity);
        await admin.Post(Url, new GuardarImpressora(Unico("P "), FuncaoImpressora.Caixa, TermicaPosto, 80, null, null))
            .Esperar(HttpStatusCode.UnprocessableEntity);

        var impressora = await (await admin.Post(Url, new GuardarImpressora(Unico("P "), FuncaoImpressora.Caixa, TermicaPosto.ToUpperInvariant(), 58, null, null, PostoId: posto.Id))
            .Esperar(HttpStatusCode.OK)).Ler<ImpressoraDto>();
        try
        {
            Assert.Equal((posto.Id, posto.Nome, TermicaPosto), (impressora.PostoId, impressora.Posto, impressora.NomeWindows));

            await admin.Post($"{Url}/{impressora.Id}/teste", new { }).Esperar(HttpStatusCode.Accepted);
            var trabalho = Assert.Single(await TrabalhosAsync(agente));
            Assert.Equal((TermicaPosto, 58), (trabalho.NomeWindows, trabalho.LarguraPapelMm));
            Assert.Contains(trabalho.Documento.Linhas, l => l.Texto.Contains("Impressão de teste"));
            Assert.Empty(app.Impressoras.Com(impressora.Nome)); // não saiu no servidor
            // Entregue e sem resultado: não volta a sair logo a seguir.
            Assert.Empty(await agente.Obter<List<TrabalhoAgenteDto>>("/api/agente/trabalhos"));

            await agente.Post($"/api/agente/trabalhos/{trabalho.Id}/resultado", new ResultadoTrabalho(false, "Sem papel no posto")).Esperar(HttpStatusCode.NoContent);
            await agente.Post($"/api/agente/trabalhos/{trabalho.Id}/resultado", new ResultadoTrabalho(true, null)).Esperar(HttpStatusCode.NoContent); // repetido: ignorado
            var estado = (await admin.Obter<List<ImpressoraDto>>(Url)).Single(i => i.Id == impressora.Id);
            Assert.Equal("Sem papel no posto", estado.UltimoErro);

            // Outro posto não vê nem conclui os trabalhos deste.
            var outro = await CriarPostoAsync(admin);
            var (agenteOutro, _) = await InstalarAgenteAsync(this, admin, outro);
            await admin.Post($"{Url}/{impressora.Id}/teste", new { }).Esperar(HttpStatusCode.Accepted);
            var segundo = Assert.Single(await TrabalhosAsync(agente));
            Assert.Equal(HttpStatusCode.NotFound, (await agenteOutro.Post($"/api/agente/trabalhos/{segundo.Id}/resultado", new ResultadoTrabalho(true, null))).StatusCode);
            await agente.Post($"/api/agente/trabalhos/{segundo.Id}/resultado", new ResultadoTrabalho(true, null)).Esperar(HttpStatusCode.NoContent);
            Assert.Null((await admin.Obter<List<ImpressoraDto>>(Url)).Single(i => i.Id == impressora.Id).UltimoErro);
        }
        finally
        {
            await admin.Post($"{Url}/{impressora.Id}/activo", new { activo = false }).Esperar(HttpStatusCode.NoContent);
        }
    }

    [Fact]
    public async Task Talao_da_cozinha_sai_na_impressora_do_posto_com_o_mesmo_documento()
    {
        var c = await CenarioVenda.CriarAsync(app);
        var (admin, _) = await app.ClienteAutenticadoAsync(PerfisPadrao.Administrador);
        var posto = await CriarPostoAsync(admin);
        var (agente, _) = await InstalarAgenteAsync(this, admin, posto);
        var cozinha = await (await admin.Post(Url, new GuardarImpressora(Unico("Coz "), FuncaoImpressora.Cozinha, TermicaPosto, 80, null, null, PostoId: posto.Id))
            .Esperar(HttpStatusCode.OK)).Ler<ImpressoraDto>();
        try
        {
            var pedido = await c.AbrirMesaAsync(c.Gerente, c.Mesa1, (c.Prato, 2));
            await c.Gerente.Post($"/api/pedidos/{pedido.Id}/enviar-cozinha", new ComVersao(pedido.RowVersion)).Esperar(HttpStatusCode.OK);

            var talao = (await TrabalhosAsync(agente)).Single(t => t.Documento.Linhas.Any(l => l.Texto.Contains("2 x Frango grelhado")));
            Assert.Equal(TermicaPosto, talao.NomeWindows);
            Assert.False(talao.ComLogotipo);
            Assert.DoesNotContain(app.Impressoras.Com("2 x Frango grelhado"), i => i.Impressora == TermicaPosto);
        }
        finally
        {
            await admin.Post($"{Url}/{cozinha.Id}/activo", new { activo = false }).Esperar(HttpStatusCode.NoContent);
        }
    }

    [Fact]
    public async Task Envios_seguidos_saem_todos_no_posto_e_no_servidor_com_o_agente_sempre_a_correr()
    {
        var c = await CenarioVenda.CriarAsync(app);
        var (admin, _) = await app.ClienteAutenticadoAsync(PerfisPadrao.Administrador);
        var posto = await CriarPostoAsync(admin);
        var registo = await (await admin.Post($"/api/postos/{posto.Numero}/agente", new { }).Esperar(HttpStatusCode.OK)).Ler<AgenteRegistado>();
        var impressos = new System.Collections.Concurrent.ConcurrentQueue<TrabalhoRecebido>();
        using var soltar = new ManualResetEventSlim(false);
        using var http = MotorAgente.CriarCliente(app.Server.BaseAddress.ToString(), posto.Numero!.Value, registo.Chave, app.Server.CreateHandler());
        // O primeiro talão fica preso no «controlador»: o agente dá-o como falhado ao fim do tempo máximo e continua.
        var primeiro = true;
        var motor = new MotorAgente(http, "teste", () => [TermicaPosto], (t, _) =>
        {
            if (primeiro)
            {
                primeiro = false;
                soltar.Wait(TimeSpan.FromMinutes(1));
                return;
            }
            impressos.Enqueue(t);
        }) { TempoMaximo = TimeSpan.FromSeconds(1) };
        using var parar = new CancellationTokenSource();
        var ciclo = Task.Run(() => motor.CorrerAsync(parar.Token));
        for (var i = 0; i < 50 && motor.Estado != EstadoLigacao.Ligado; i++)
            await Task.Delay(100);

        var noPosto = await (await admin.Post(Url, new GuardarImpressora(Unico("Coz "), FuncaoImpressora.Cozinha, TermicaPosto, 80, null, null, PostoId: posto.Id))
            .Esperar(HttpStatusCode.OK)).Ler<ImpressoraDto>();
        var noServidor = await (await admin.Post(Url, new GuardarImpressora(Unico("Bar "), FuncaoImpressora.Bar, ImpressorasTeste.Bar, 80, null, null))
            .Esperar(HttpStatusCode.OK)).Ler<ImpressoraDto>();
        try
        {
            await admin.Post($"{Url}/{noPosto.Id}/teste", new { }).Esperar(HttpStatusCode.Accepted);
            ImpressoraDto? estado = null;
            for (var i = 0; i < 100 && estado?.UltimoErro is null; i++, await Task.Delay(100))
                estado = (await admin.Obter<List<ImpressoraDto>>(Url)).Single(x => x.Id == noPosto.Id);
            Assert.Contains("não respondeu", estado!.UltimoErro);

            var pedido = await c.AbrirMesaAsync(c.Gerente, c.Mesa1, (c.Prato, 1), (c.Bebida, 1));
            for (var envio = 1; envio <= 4; envio++)
            {
                if (envio > 1)
                    pedido = await (await c.Gerente.Post($"/api/pedidos/{pedido.Id}/itens", new { itens = new[] { new { produtoId = c.Prato.Id, quantidade = envio }, new { produtoId = c.Bebida.Id, quantidade = envio } }, rowVersion = pedido.RowVersion })
                        .Esperar(HttpStatusCode.OK)).Ler<PedidoDto>();
                pedido = await (await c.Gerente.Post($"/api/pedidos/{pedido.Id}/enviar-cozinha", new ComVersao(pedido.RowVersion)).Esperar(HttpStatusCode.OK)).Ler<PedidoDto>();
                var esperado = $"{envio} x Frango grelhado";
                for (var i = 0; i < 100 && !impressos.Any(t => t.Documento.Linhas.Any(l => l.Texto.Contains(esperado))); i++)
                    await Task.Delay(100);
                Assert.True(impressos.Any(t => t.Documento.Linhas.Any(l => l.Texto.Contains(esperado))), $"Envio {envio}: o talão da cozinha não saiu no posto ({impressos.Count} impressos).");
                await app.Impressoras.EsperarAsync($"{envio} x Refrigerante", quantos: 1);
            }
        }
        finally
        {
            soltar.Set();
            parar.Cancel();
            await ciclo;
            await admin.Post($"{Url}/{noPosto.Id}/activo", new { activo = false }).Esperar(HttpStatusCode.NoContent);
            await admin.Post($"{Url}/{noServidor.Id}/activo", new { activo = false }).Esperar(HttpStatusCode.NoContent);
        }
    }

    [Fact]
    public async Task Motor_do_agente_envia_as_impressoras_imprime_e_devolve_o_resultado()
    {
        var (admin, _) = await app.ClienteAutenticadoAsync(PerfisPadrao.Administrador);
        var posto = await CriarPostoAsync(admin);
        var registo = await (await admin.Post($"/api/postos/{posto.Numero}/agente", new { }).Esperar(HttpStatusCode.OK)).Ler<AgenteRegistado>();
        var impressos = new List<TrabalhoRecebido>();
        using var http = MotorAgente.CriarCliente(app.Server.BaseAddress.ToString(), posto.Numero!.Value, registo.Chave, app.Server.CreateHandler());
        var motor = new MotorAgente(http, "1.2.3", () => [TermicaPosto, "Avariada do posto"], (t, _) =>
        {
            if (t.NomeWindows == "Avariada do posto")
                throw new InvalidOperationException("Tampa aberta");
            impressos.Add(t);
        });

        Assert.Equal(0, await motor.CicloAsync(0, default)); // primeira volta: envia as impressoras
        Assert.Equal(EstadoLigacao.Ligado, motor.Estado);
        var terminal = (await admin.Obter<List<TerminalDto>>("/api/terminais")).Single(t => t.Id == posto.Id);
        Assert.Equal(("1.2.3", 2), (terminal.AgenteVersao, terminal.AgenteImpressoras!.Count));

        var boa = await (await admin.Post(Url, new GuardarImpressora(Unico("M "), FuncaoImpressora.Caixa, TermicaPosto, 80, null, null, PostoId: posto.Id))
            .Esperar(HttpStatusCode.OK)).Ler<ImpressoraDto>();
        var avariada = await (await admin.Post(Url, new GuardarImpressora(Unico("M "), FuncaoImpressora.Bar, "Avariada do posto", 58, null, null, PostoId: posto.Id))
            .Esperar(HttpStatusCode.OK)).Ler<ImpressoraDto>();
        try
        {
            await admin.Post($"{Url}/{boa.Id}/teste", new { }).Esperar(HttpStatusCode.Accepted);
            await admin.Post($"{Url}/{avariada.Id}/teste", new { }).Esperar(HttpStatusCode.Accepted);
            var tratados = 0;
            for (var i = 0; i < 10 && tratados < 2; i++)
                tratados += await motor.CicloAsync(2, default);
            Assert.Equal(2, tratados);
            var impresso = Assert.Single(impressos);
            Assert.Contains(impresso.Documento.Linhas, l => l.Texto.Contains("Impressão de teste"));
            Assert.Equal(1, motor.Impressos);

            var estados = await admin.Obter<List<ImpressoraDto>>(Url);
            Assert.True(estados.Single(i => i.Id == boa.Id) is { UltimoErro: null, UltimaImpressaoUtc: not null });
            Assert.Equal("Tampa aberta", estados.Single(i => i.Id == avariada.Id).UltimoErro);

            // Chave revogada: o motor dá-se conta e mostra o estado.
            await admin.DeleteAsync($"/api/terminais/{posto.Id}/agente").Esperar(HttpStatusCode.NoContent);
            await Assert.ThrowsAsync<AgenteNaoAutorizadoException>(() => motor.CicloAsync(0, default));
        }
        finally
        {
            await admin.Post($"{Url}/{boa.Id}/activo", new { activo = false }).Esperar(HttpStatusCode.NoContent);
            await admin.Post($"{Url}/{avariada.Id}/activo", new { activo = false }).Esperar(HttpStatusCode.NoContent);
        }
    }
}
