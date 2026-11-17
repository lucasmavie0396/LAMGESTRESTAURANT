using System.Net;
using System.Text;
using LAMGestRestaurant.Agente;
using LAMGestRestaurant.Application.Caixa;
using LAMGestRestaurant.Application.Impressao;
using LAMGestRestaurant.Application.Seguranca;
using LAMGestRestaurant.Domain.Caixa;
using LAMGestRestaurant.Tests.Infraestrutura;
using static LAMGestRestaurant.Tests.Infraestrutura.ExtensoesHttp;

namespace LAMGestRestaurant.Tests;

/// <summary>Linhas do visor de poste e comandos da porta COM (sem base de dados).</summary>
public class LinhasVisorTests
{
    [Fact]
    public void Linhas_do_visor_de_poste_tem_a_largura_do_visor_sem_acentos()
    {
        var venda = new ConteudoVisor(FaseVisor.Venda, "", [new("Água", 1, 50m), new("Galinha à zambeziana", 2, 1250m)], 1300m, null, null, "P1");
        var (l1, l2) = ServicoVisor.LinhasPoste(venda, 20);
        Assert.Equal("2x Galinha  1 250,00", l1);
        Assert.Equal("TOTAL" + "1 300,00".PadLeft(15), l2);

        var pagamento = venda with { Fase = FaseVisor.Pagamento, Recebido = 1500m, Troco = 200m };
        Assert.Equal(("TOTAL" + "1 300,00".PadLeft(15), "TROCO" + "200,00".PadLeft(15)), ServicoVisor.LinhasPoste(pagamento, 20));
        var semTroco = pagamento with { Recebido = 500m, Troco = 0m };
        Assert.Equal("RECEBIDO" + "500,00".PadLeft(12), ServicoVisor.LinhasPoste(semTroco, 20).Linha2);

        var pago = venda with { Fase = FaseVisor.Pago, Mensagem = "Obrigado, volte sempre!", Troco = 200m };
        var (p1, p2) = ServicoVisor.LinhasPoste(pago, 20);
        Assert.Equal("TROCO" + "200,00".PadLeft(15), p1);
        Assert.Equal("Obrigado, volte semp", p2);

        var repouso = ServicoVisor.LinhasPoste(ConteudoVisor.Repouso("Bem-vindo ao Restaurante Maré Alta!"), 20);
        Assert.Equal(("Bem-vindo ao", "Restaurante Mare Alt"), (repouso.Linha1.Trim(), repouso.Linha2));
        Assert.All(new[] { l1, l2, p1, p2, repouso.Linha1, repouso.Linha2 }, l => Assert.Equal(20, l.Length));
        Assert.Equal(40, ServicoVisor.LinhasPoste(venda, 40).Linha1.Length);
    }

    [Fact]
    public void Comandos_da_porta_COM_conforme_o_protocolo()
    {
        var escpos = ComandosVisor.Bytes("EscPos", "Ola", "Mundo", 20);
        Assert.Equal(new byte[] { 0x1B, (byte)'@', 0x0C, 0x1F, (byte)'$', 1, 1 }, escpos[..7]);
        Assert.Equal("Ola".PadRight(20), Encoding.ASCII.GetString(escpos, 7, 20));
        Assert.Equal(new byte[] { 0x1F, (byte)'$', 1, 2 }, escpos[27..31]);
        Assert.Equal(7 + 20 + 4 + 20, escpos.Length);

        var cd = Encoding.ASCII.GetString(ComandosVisor.Bytes("Cd5220", "A", "B", 20));
        Assert.Equal("\u001b@\u000c\u001bQA" + "A".PadRight(20) + "\r\u001bQB" + "B".PadRight(20) + "\r", cd);
        Assert.Equal("\u000c" + "Linha um".PadRight(20) + "dois".PadRight(20), Encoding.ASCII.GetString(ComandosVisor.Bytes("Simples", "Linha um", "dois", 20)));
    }
}

/// <summary>Visor do cliente dos postos: configuração, conteúdo vindo do pedido, agente (visor de poste) e página do visor.</summary>
[Collection(ColeccaoBaseDados.Nome)]
public class VisorClienteTests(AplicacaoTeste app)
{
    private sealed class SaidaFalsa : ISaidaVisor
    {
        public List<string> Chamadas { get; } = [];
        public void Poste(VisorRecebido visor) => Chamadas.Add($"poste {visor.Porta} {visor.Linha1}|{visor.Linha2}");
        public void Monitor(VisorRecebido visor, Uri pagina) => Chamadas.Add($"monitor {pagina.PathAndQuery}");
        public void Desligar() => Chamadas.Add("desligar");
    }

    private async Task<(TerminalDto Posto, HttpClient Agente)> PostoComAgenteAsync(HttpClient admin)
    {
        TerminalDto posto;
        for (var tentativa = 0; ; tentativa++)
        {
            var r = await admin.Post("/api/terminais", new GuardarTerminal(Unico("Visor "), null, Random.Shared.Next(100, 999)));
            if (r.StatusCode == HttpStatusCode.Created)
            {
                posto = await r.Ler<TerminalDto>();
                break;
            }
            if (tentativa > 20)
                throw new InvalidOperationException(await r.Content.ReadAsStringAsync());
        }
        var registo = await (await admin.Post($"/api/postos/{posto.Numero}/agente", new { }).Esperar(HttpStatusCode.OK)).Ler<AgenteRegistado>();
        var agente = MotorAgente.CriarCliente(app.Server.BaseAddress.ToString(), posto.Numero!.Value, registo.Chave, app.Server.CreateHandler());
        return (posto, agente);
    }

    private static Task<VisorAgenteDto> VisorAsync(HttpClient agente, long versao = 0, int espera = 0) =>
        agente.Obter<VisorAgenteDto>($"/api/agente/visor?versao={versao}&espera={espera}");

    [Fact]
    public async Task Visor_de_poste_mostra_o_pedido_o_pagamento_e_o_troco_e_o_agente_recebe_logo()
    {
        var c = await CenarioVenda.CriarAsync(app);
        var admin = c.Gerente;
        var (posto, agente) = await PostoComAgenteAsync(admin);

        // O agente envia as portas COM e os ecrãs do computador: aparecem em Configurações → Postos.
        await agente.Post("/api/agente/estado", new EstadoAgente("teste", [], [new("COM3", "USB-SERIAL CH340 (COM3)")],
            [new("\\\\.\\DISPLAY1", 1920, 1080, true), new("\\\\.\\DISPLAY2", 800, 600, false)])).Esperar(HttpStatusCode.NoContent);
        var estado = (await admin.Obter<List<PostoEstadoDto>>("/api/configuracoes/postos")).Single(p => p.Id == posto.Id);
        Assert.Equal(TipoVisor.Nenhum, estado.Visor!.Tipo);
        Assert.Equal("USB-SERIAL CH340 (COM3)", Assert.Single(estado.Visor.PortasDetectadas).Descricao);
        Assert.Equal(2, estado.Visor.EcrasDetectados.Count);

        // Sem visor configurado: o POS pode avisar à vontade (não faz nada) e o teste é recusado.
        await admin.Post("/api/visor", new ActualizarVisor(posto.Id, null, null)).Esperar(HttpStatusCode.NoContent);
        await admin.Post($"/api/configuracoes/postos/{posto.Id}/visor/testar", new { }).Esperar(HttpStatusCode.UnprocessableEntity);
        await admin.Post("/api/visor", new ActualizarVisor(int.MaxValue, null, null)).Esperar(HttpStatusCode.NotFound);

        await admin.Put($"/api/configuracoes/postos/{posto.Id}/visor", new ConfigurarVisor(TipoVisor.Poste, null, ProtocoloVisor.EscPos, 9600, 20, null))
            .Esperar(HttpStatusCode.UnprocessableEntity);
        await admin.Put($"/api/configuracoes/postos/{posto.Id}/visor", new ConfigurarVisor(TipoVisor.Poste, "com3", ProtocoloVisor.Cd5220, 9600, 20, null))
            .Esperar(HttpStatusCode.NoContent);
        var (garcom, _) = await app.ClienteAutenticadoAsync(PerfisPadrao.Garcom);
        await garcom.Put($"/api/configuracoes/postos/{posto.Id}/visor", new ConfigurarVisor(TipoVisor.Nenhum, null, ProtocoloVisor.EscPos, 9600, 20, null))
            .Esperar(HttpStatusCode.Forbidden);

        var inicial = await VisorAsync(agente);
        Assert.Equal((TipoVisor.Poste, "COM3", ProtocoloVisor.Cd5220), (inicial.Tipo, inicial.Porta, inicial.Protocolo));
        Assert.Equal("Bem-vindo!", inicial.Linha1.Trim());

        // O agente espera (pedido longo) e acorda logo que o POS põe o pedido no ecrã.
        var pedido = await c.AbrirMesaAsync(admin, c.Mesa1, (c.Bebida, 2));
        var espera = VisorAsync(agente, inicial.Versao, 20);
        await Task.Delay(300);
        Assert.False(espera.IsCompleted);
        var relogio = System.Diagnostics.Stopwatch.StartNew();
        await admin.Post("/api/visor", new ActualizarVisor(posto.Id, pedido.Id, null)).Esperar(HttpStatusCode.NoContent);
        var venda = await espera;
        Assert.True(relogio.Elapsed < TimeSpan.FromSeconds(10));
        Assert.StartsWith("2x Refriger", venda.Linha1); // nome cortado para caber o valor
        Assert.EndsWith(ServicoVisor.Valor(pedido.Itens.Sum(i => i.Total)), venda.Linha1);
        Assert.Equal($"TOTAL {ServicoVisor.Valor(pedido.Total).PadLeft(14)}", venda.Linha2);

        // Pagamento: recebido e troco calculados no servidor a partir do pedido.
        await admin.Post("/api/visor", new ActualizarVisor(posto.Id, pedido.Id, pedido.Total + 100m)).Esperar(HttpStatusCode.NoContent);
        var pagamento = await VisorAsync(agente);
        Assert.Equal($"TROCO {ServicoVisor.Valor(100m).PadLeft(14)}", pagamento.Linha2);

        await c.PagarAsync(admin, pedido.Id, "DINHEIRO", pedido.Total, pedido.Total + 50m).Esperar(HttpStatusCode.Created);
        await admin.Post("/api/visor", new ActualizarVisor(posto.Id, pedido.Id, null)).Esperar(HttpStatusCode.NoContent);
        var pago = await VisorAsync(agente);
        Assert.Equal($"TROCO {ServicoVisor.Valor(50m).PadLeft(14)}", pago.Linha1);
        Assert.Equal("Obrigado, volte semp", pago.Linha2);

        // O ciclo do agente mostra no visor de poste o que recebe.
        var saida = new SaidaFalsa();
        var motor = new MotorVisor(agente, saida);
        await motor.CicloAsync(0, CancellationToken.None);
        Assert.Equal($"poste COM3 {pago.Linha1}|{pago.Linha2}", Assert.Single(saida.Chamadas));

        await admin.Post($"/api/configuracoes/postos/{posto.Id}/visor/testar", new { }).Esperar(HttpStatusCode.NoContent);
        await motor.CicloAsync(5, CancellationToken.None);
        Assert.Contains("Teste do visor", saida.Chamadas[^1]);

        // Desligar o visor: o agente fecha a porta.
        await admin.Put($"/api/configuracoes/postos/{posto.Id}/visor", new ConfigurarVisor(TipoVisor.Nenhum, null, ProtocoloVisor.EscPos, 9600, 20, null))
            .Esperar(HttpStatusCode.NoContent);
        await motor.CicloAsync(5, CancellationToken.None);
        Assert.Equal("desligar", saida.Chamadas[^1]);
    }

    [Fact]
    public async Task Pagina_do_visor_no_segundo_monitor_so_abre_com_a_chave_do_endereco()
    {
        var c = await CenarioVenda.CriarAsync(app);
        var admin = c.Gerente;
        var (posto, agente) = await PostoComAgenteAsync(admin);
        await admin.Put($"/api/configuracoes/postos/{posto.Id}/visor", new ConfigurarVisor(TipoVisor.Monitor, null, ProtocoloVisor.EscPos, 9600, 20, "\\\\.\\DISPLAY2"))
            .Esperar(HttpStatusCode.NoContent);
        var visor = (await admin.Obter<List<PostoEstadoDto>>("/api/configuracoes/postos")).Single(p => p.Id == posto.Id).Visor!;
        Assert.Equal((TipoVisor.Monitor, "\\\\.\\DISPLAY2"), (visor.Tipo, visor.Ecra));
        Assert.StartsWith($"/visor?terminal={posto.Id}&chave=", visor.Endereco);
        var chave = visor.Endereco![(visor.Endereco.IndexOf("chave=", StringComparison.Ordinal) + 6)..];

        // O agente abre a página no segundo monitor.
        var saida = new SaidaFalsa();
        await new MotorVisor(agente, saida).CicloAsync(0, CancellationToken.None);
        Assert.Equal($"monitor {visor.Endereco}", Assert.Single(saida.Chamadas));

        // Sem sessão, só com a chave certa.
        var anonimo = app.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonimo.GetAsync($"/api/visor/ecra?terminal={posto.Id}&chave=errada")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonimo.GetAsync($"/api/visor/ecra?terminal={posto.Id}")).StatusCode);
        var repouso = await anonimo.Obter<EcraVisorDto>($"/api/visor/ecra?terminal={posto.Id}&chave={chave}");
        Assert.Equal((FaseVisor.Repouso, "Bem-vindo!"), (repouso.Conteudo.Fase, repouso.Conteudo.Mensagem));
        Assert.True(repouso.Versao < 1L << 53, "A versão tem de caber nos números do JavaScript.");

        var pedido = await c.AbrirMesaAsync(admin, c.Mesa1, (c.Prato, 1), (c.Bebida, 3));
        var espera = anonimo.Obter<EcraVisorDto>($"/api/visor/ecra?terminal={posto.Id}&chave={chave}&versao={repouso.Versao}&espera=20");
        await admin.Post("/api/visor", new ActualizarVisor(posto.Id, pedido.Id, null)).Esperar(HttpStatusCode.NoContent);
        var venda = await espera;
        Assert.Equal(FaseVisor.Venda, venda.Conteudo.Fase);
        Assert.Equal(pedido.Total, venda.Conteudo.Total);
        Assert.Equal(["Frango grelhado", "Refrigerante"], venda.Conteudo.Linhas.Select(l => l.Descricao));
        Assert.Equal(3, venda.Conteudo.Linhas[1].Quantidade);

        // Endereço novo: o antigo deixa de abrir.
        await admin.Post($"/api/configuracoes/postos/{posto.Id}/visor/endereco", new { }).Esperar(HttpStatusCode.NoContent);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonimo.GetAsync($"/api/visor/ecra?terminal={posto.Id}&chave={chave}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonimo.GetAsync($"/api/visor/logotipo?terminal={posto.Id}&chave={chave}")).StatusCode);
    }
}
