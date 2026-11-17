using System.Net;
using LAMGestRestaurant.Agente;
using LAMGestRestaurant.Api.Controllers;
using LAMGestRestaurant.Application.Caixa;
using LAMGestRestaurant.Application.Catalogo;
using LAMGestRestaurant.Application.Impressao;
using LAMGestRestaurant.Application.Seguranca;
using LAMGestRestaurant.Application.Stock;
using LAMGestRestaurant.Domain.Caixa;
using LAMGestRestaurant.Domain.Comum;
using LAMGestRestaurant.Domain.Sistema;
using LAMGestRestaurant.Tests.Infraestrutura;
using static LAMGestRestaurant.Tests.Infraestrutura.ExtensoesHttp;

namespace LAMGestRestaurant.Tests;

/// <summary>Respostas das balanças pela porta série e etiquetas com peso ou preço (sem base de dados).</summary>
public class ProtocoloBalancaTests
{
    [Theory]
    [InlineData("ST,GS,+  0.485kg", 0.485, true)]
    [InlineData("US,GS,+  0.480kg", 0.480, false)]
    [InlineData("01250", 1.250, null)] // Toledo/Prix: gramas sem unidade
    [InlineData("W   485 g", 0.485, null)]
    [InlineData("  1,234 kg", 1.234, null)]
    [InlineData("N  2.000 KG  T 0.150 KG", 0.150, null)] // com unidade: conta o último número com unidade
    [InlineData("0.750 KG M", 0.750, false)] // M = em movimento
    public void Interpreta_o_peso_e_a_estabilidade(string trama, double peso, bool? estavel)
    {
        var lido = ProtocoloBalanca.Interpretar(trama)!;
        Assert.Equal(((decimal)peso, estavel, (string?)null), (lido.Peso, lido.Estavel, lido.Erro));
    }

    [Theory]
    [InlineData("SSSSS", "máximo")]
    [InlineData("OL", "máximo")]
    [InlineData("-0.100kg", "negativo")]
    [InlineData("NNNNN", "negativo")]
    [InlineData("12.5 lb", "libras")]
    public void Avisa_sobrecarga_peso_negativo_e_libras(string trama, string erro)
    {
        var lido = ProtocoloBalanca.Interpretar(trama)!;
        Assert.Null(lido.Peso);
        Assert.Contains(erro, lido.Erro);
    }

    [Fact]
    public void Tramas_completas_e_peso_instavel()
    {
        Assert.Equal(["01250", "ST 0.5kg"], ProtocoloBalanca.Tramas("\u000201250\u0003ST 0.5kg\r\n0.4", out var resto));
        Assert.Equal("0.4", resto);
        Assert.Equal(new PesoLido(null, false, null), ProtocoloBalanca.Interpretar("IIIII"));
        Assert.Null(ProtocoloBalanca.Interpretar("ST,GS"));
        Assert.Equal([ProtocoloBalanca.Enq], ProtocoloBalanca.Pedido("PedidoEnq")!);
        Assert.Null(ProtocoloBalanca.Pedido("Continua"));

        // Sem indicador da balança: só é estável com 3 leituras iguais seguidas; o indicador dela manda.
        var e = new Estabilizador();
        var p = new PesoLido(0.5m, null, null);
        Assert.False(e.Estavel(p));
        Assert.False(e.Estavel(p));
        Assert.True(e.Estavel(p));
        Assert.False(e.Estavel(p with { Peso = 0.51m }));
        Assert.True(e.Estavel(new PesoLido(0.51m, true, null)));
        Assert.False(e.Estavel(new PesoLido(0.51m, false, null)));
    }

    [Fact]
    public void Etiqueta_com_peso_ou_preco()
    {
        var formato = new FormatoEtiquetaBalanca(["2"], 5, false, 0);
        var codigo = ServicoCatalogo.Ean13("21" + "00123" + "00485");
        Assert.Equal(new EtiquetaBalanca(123, 0.485m, true), formato.Ler(codigo));
        Assert.False(formato.Ler(codigo[..12] + (char)('0' + (codigo[12] - '0' + 1) % 10))!.ControloValido);
        Assert.Null(formato.Ler("7891234567895")); // não começa pelo prefixo
        Assert.Null(formato.Ler("21001230048")); // curto
        Assert.Equal(new EtiquetaBalanca(1234, 60.6m, true),
            new FormatoEtiquetaBalanca(["28"], 4, true, 1).Ler(ServicoCatalogo.Ean13("28" + "1234" + "000606")));

        Assert.Equal(["21", "28"], FormatoEtiquetaBalanca.LerPrefixos(" 21, 28 "));
        Assert.Null(FormatoEtiquetaBalanca.LerPrefixos("3"));
        Assert.Null(FormatoEtiquetaBalanca.LerPrefixos(""));
        Assert.Null(FormatoEtiquetaBalanca.LerPrefixos("2a"));
    }
}

[Collection(ColeccaoBaseDados.Nome)]
public class BalancaTests(AplicacaoTeste app)
{
    /// <summary>Balança simulada: responde com as tramas dadas, por ordem (a última repete-se).</summary>
    private sealed class BalancaFalsa(params string[] respostas) : IPortaBalanca
    {
        private int lidas;
        public List<string> Escrito { get; } = [];
        public bool Fechada { get; private set; }
        public void Escrever(byte[] dados) => Escrito.Add(System.Text.Encoding.ASCII.GetString(dados));
        public string Ler(TimeSpan espera) => respostas[Math.Min(lidas++, respostas.Length - 1)];
        public void Dispose() => Fechada = true;
    }

    private static Task Definir(HttpClient c, params (string Chave, string Valor)[] valores) =>
        c.Put("/api/configuracoes", new GuardarConfiguracoes(valores.ToDictionary(v => v.Chave, v => v.Valor))).Esperar(HttpStatusCode.OK);

    private static async Task<ProdutoDto> ProdutoAPesoAsync(CenarioVenda c, int? plu, decimal preco = 1250m)
    {
        var kg = (await c.Gerente.Obter<List<UnidadeDto>>("/api/stock/unidades")).Single(u => u.Codigo == "kg");
        return await (await c.Gerente.Post("/api/produtos", new GuardarProduto(Unico("B", 10), null, Unico("Carne ", 14), null, c.Prato.CategoriaId, TipoProduto.Bar,
            preco, 10m, null, null, false, true, null, UnidadeMedidaId: kg.Id, PedeQuantidade: true, CodigoBalanca: plu)).Esperar(HttpStatusCode.Created)).Ler<ProdutoDto>();
    }

    [Fact]
    public async Task Etiqueta_da_balanca_lanca_o_produto_com_o_peso_ou_o_preco()
    {
        var c = await CenarioVenda.CriarAsync(app);
        var plu = Random.Shared.Next(10000, 99999);
        var carne = await ProdutoAPesoAsync(c, plu);
        Assert.Equal(plu, carne.CodigoBalanca);
        await c.Gerente.Post("/api/produtos", new GuardarProduto(Unico("B", 10), null, Unico("Outro ", 14), null, c.Prato.CategoriaId, TipoProduto.Bar,
            10m, 1m, null, null, false, true, null, CodigoBalanca: plu)).Esperar(HttpStatusCode.Conflict);
        var etiqueta = ServicoCatalogo.Ean13($"21{plu:00000}00485");

        // Desligadas: o código não é de ninguém.
        await Definir(c.Gerente, (ChavesConfiguracao.BalancaEtiquetas, "false"));
        await c.Gerente.GetAsync($"/api/balanca/etiqueta/{etiqueta}").Esperar(HttpStatusCode.NotFound);
        try
        {
            await c.Gerente.Put("/api/configuracoes", new GuardarConfiguracoes(new Dictionary<string, string> { [ChavesConfiguracao.BalancaPrefixos] = "3" }))
                .Esperar(HttpStatusCode.UnprocessableEntity);
            await Definir(c.Gerente, (ChavesConfiguracao.BalancaEtiquetas, "true"), (ChavesConfiguracao.BalancaPrefixos, "2"), (ChavesConfiguracao.BalancaDigitosCodigo, "5"),
                (ChavesConfiguracao.BalancaConteudo, "peso"));

            var lida = await c.Gerente.Obter<ProdutoEtiquetaDto>($"/api/balanca/etiqueta/{etiqueta}");
            Assert.Equal((carne.Id, 0.485m, (decimal?)null), (lida.Produto.Id, lida.Quantidade, lida.Valor));

            // Algarismo de controlo errado, código de balança sem produto, código que não é etiqueta.
            await c.Gerente.GetAsync($"/api/balanca/etiqueta/{etiqueta[..12]}{(etiqueta[12] - '0' + 1) % 10}").Esperar(HttpStatusCode.UnprocessableEntity);
            await c.Gerente.GetAsync($"/api/balanca/etiqueta/{ServicoCatalogo.Ean13("2100000" + "00485")}").Esperar(HttpStatusCode.NotFound);
            await c.Gerente.GetAsync("/api/balanca/etiqueta/7891234567895").Esperar(HttpStatusCode.NotFound);
            // Peso numa etiqueta de um produto vendido à unidade: recusado.
            var unidade = Random.Shared.Next(100000, 999999);
            await c.Gerente.Post("/api/produtos", new GuardarProduto(Unico("B", 10), null, Unico("Un ", 14), null, c.Prato.CategoriaId, TipoProduto.Bar,
                10m, 1m, null, null, false, true, null, CodigoBalanca: unidade % 100000 + 1)).Esperar(HttpStatusCode.Created);
            await c.Gerente.GetAsync($"/api/balanca/etiqueta/{ServicoCatalogo.Ean13($"21{unidade % 100000 + 1:00000}00485")}").Esperar(HttpStatusCode.UnprocessableEntity);

            // Preço na etiqueta (meticais sem casas decimais): a quantidade é o preço a dividir pelo preço por kg.
            await Definir(c.Gerente, (ChavesConfiguracao.BalancaConteudo, "preco"), (ChavesConfiguracao.BalancaDecimaisPreco, "0"));
            var porPreco = await c.Gerente.Obter<ProdutoEtiquetaDto>($"/api/balanca/etiqueta/{ServicoCatalogo.Ean13($"21{plu:00000}00625")}");
            Assert.Equal((0.5m, (decimal?)625m), (porPreco.Quantidade, porPreco.Valor));

            // O POS lança a quantidade lida; o total é calculado no servidor.
            var pedido = await (await c.Gerente.PostComChave("/api/pedidos", CenarioVenda.Chave(), new Application.Vendas.NovoPedido(TipoPedido.Balcao, null, null, null, 0, null, null, null, false,
                [new Application.Vendas.NovoItem(carne.Id, lida.Quantidade, null)])).Esperar(HttpStatusCode.Created)).Ler<Application.Vendas.PedidoDto>();
            Assert.Equal(606.25m, Assert.Single(pedido.Itens).Total);
        }
        finally
        {
            await Definir(c.Gerente, (ChavesConfiguracao.BalancaEtiquetas, "false"), (ChavesConfiguracao.BalancaConteudo, "peso"));
        }
    }

    private async Task<(TerminalDto Posto, HttpClient Agente)> PostoComAgenteAsync(HttpClient admin)
    {
        TerminalDto posto;
        for (var tentativa = 0; ; tentativa++)
        {
            var r = await admin.Post("/api/terminais", new GuardarTerminal(Unico("Balanca "), null, Random.Shared.Next(100, 999)));
            if (r.StatusCode == HttpStatusCode.Created)
            {
                posto = await r.Ler<TerminalDto>();
                break;
            }
            if (tentativa > 20)
                throw new InvalidOperationException(await r.Content.ReadAsStringAsync());
        }
        var registo = await (await admin.Post($"/api/postos/{posto.Numero}/agente", new { }).Esperar(HttpStatusCode.OK)).Ler<AgenteRegistado>();
        return (posto, MotorAgente.CriarCliente(app.Server.BaseAddress.ToString(), posto.Numero!.Value, registo.Chave, app.Server.CreateHandler()));
    }

    [Fact]
    public async Task Balanca_do_posto_o_agente_le_enquanto_o_POS_pede_o_peso()
    {
        var c = await CenarioVenda.CriarAsync(app);
        var admin = c.Gerente;
        var (posto, agente) = await PostoComAgenteAsync(admin);

        // Sem balança: o POS sabe logo que tem de escrever o peso.
        var sem = await admin.Obter<LeituraBalancaDto>($"/api/balanca/{posto.Id}?espera=5");
        Assert.False(sem.Configurada);

        await admin.Put($"/api/configuracoes/postos/{posto.Id}/balanca", new ConfigurarBalanca(ModoBalanca.PedidoW, null, 9600, FormatoSerie.Oito_N_1))
            .Esperar(HttpStatusCode.UnprocessableEntity);
        await admin.Put($"/api/configuracoes/postos/{posto.Id}/balanca", new ConfigurarBalanca(ModoBalanca.PedidoW, "COM4", 1234, FormatoSerie.Oito_N_1))
            .Esperar(HttpStatusCode.UnprocessableEntity);
        await admin.Put($"/api/configuracoes/postos/{posto.Id}/visor", new ConfigurarVisor(TipoVisor.Poste, "COM4", ProtocoloVisor.EscPos, 9600, 20, null))
            .Esperar(HttpStatusCode.NoContent);
        await admin.Put($"/api/configuracoes/postos/{posto.Id}/balanca", new ConfigurarBalanca(ModoBalanca.PedidoW, "COM4", 9600, FormatoSerie.Oito_N_1))
            .Esperar(HttpStatusCode.UnprocessableEntity); // a porta é a do visor
        await admin.Put($"/api/configuracoes/postos/{posto.Id}/visor", new ConfigurarVisor(TipoVisor.Poste, "COM3", ProtocoloVisor.EscPos, 9600, 20, null))
            .Esperar(HttpStatusCode.NoContent);
        await admin.Put($"/api/configuracoes/postos/{posto.Id}/balanca", new ConfigurarBalanca(ModoBalanca.PedidoW, "com4", 9600, FormatoSerie.Sete_E_1))
            .Esperar(HttpStatusCode.NoContent);
        var (garcom, _) = await app.ClienteAutenticadoAsync(PerfisPadrao.Garcom);
        await garcom.Put($"/api/configuracoes/postos/{posto.Id}/balanca", new ConfigurarBalanca(ModoBalanca.Nenhuma, null, 9600, FormatoSerie.Oito_N_1))
            .Esperar(HttpStatusCode.Forbidden);
        var estado = (await admin.Obter<List<PostoEstadoDto>>("/api/configuracoes/postos")).Single(p => p.Id == posto.Id);
        Assert.Equal(new BalancaPostoDto(ModoBalanca.PedidoW, "COM4", 9600, FormatoSerie.Sete_E_1), estado.Balanca);

        // O agente recebe a configuração; ainda ninguém pede o peso.
        var motor = new MotorBalanca(agente, _ => throw new InvalidOperationException("não devia abrir"));
        var inicial = (await motor.CicloAsync(0, CancellationToken.None))!;
        Assert.Equal(("PedidoW", "COM4", "Sete_E_1", false), (inicial.Modo, inicial.Porta, inicial.Formato, inicial.Activa));

        // O agente espera (pedido longo) e acorda logo que o POS pede o peso.
        var espera = motor.CicloAsync(20, CancellationToken.None);
        await Task.Delay(300);
        Assert.False(espera.IsCompleted);
        var relogio = System.Diagnostics.Stopwatch.StartNew();
        var primeira = await admin.Obter<LeituraBalancaDto>($"/api/balanca/{posto.Id}");
        Assert.True(primeira.Configurada);
        Assert.Null(primeira.Peso);
        var activa = (await espera)!;
        Assert.True(activa.Activa);
        Assert.True(relogio.Elapsed < TimeSpan.FromSeconds(10));

        // Lê a balança (pede com «W») e envia o peso; a terceira resposta é estável.
        var porLer = admin.Obter<LeituraBalancaDto>($"/api/balanca/{posto.Id}?versao={primeira.Versao}&espera=10");
        var balanca = new BalancaFalsa("US,GS,+  1.200kg\r\n", "ST,GS,+  1.250kg\r\n");
        await new MotorBalanca(agente, b => { Assert.Equal("COM4", b.Porta); return balanca; }).LerAsync(activa, CancellationToken.None, maximo: 2);
        Assert.True(balanca.Fechada);
        Assert.Equal(["W\r", "W\r"], balanca.Escrito);
        var instavel = await porLer;
        Assert.Equal((1.2m, false), (instavel.Peso, instavel.Estavel));
        var estavel = await admin.Obter<LeituraBalancaDto>($"/api/balanca/{posto.Id}?versao={instavel.Versao}&espera=1");
        Assert.Equal((1.25m, true, (string?)null), (estavel.Peso, estavel.Estavel, estavel.Erro));

        // Peso negativo ou absurdo chega ao POS como aviso (sem peso).
        await agente.Post("/api/agente/balanca/leitura", new LeituraAgente(-0.2m, true, null)).Esperar(HttpStatusCode.OK);
        var negativo = await admin.Obter<LeituraBalancaDto>($"/api/balanca/{posto.Id}?versao={estavel.Versao}&espera=1");
        Assert.Null(negativo.Peso);
        Assert.Contains("negativo", negativo.Erro);

        // Porta que não abre: o POS é avisado.
        await new MotorBalanca(agente, _ => throw new IOException("Acesso negado")).LerAsync(activa, CancellationToken.None);
        var semPorta = await admin.Obter<LeituraBalancaDto>($"/api/balanca/{posto.Id}?versao={negativo.Versao}&espera=1");
        Assert.Contains("porta COM4", semPorta.Erro);

        // Enquanto se pesa, o visor do cliente mostra o produto, o peso e o valor (calculado no servidor).
        var carne = await ProdutoAPesoAsync(c, null, 800m);
        await admin.Post("/api/visor", new ActualizarVisor(posto.Id, null, null, new PesarVisor(carne.Id, 1.25m))).Esperar(HttpStatusCode.NoContent);
        var visor = await agente.Obter<VisorAgenteDto>("/api/agente/visor");
        Assert.EndsWith("1,250 kg", visor.Linha1);
        Assert.Equal($"VALOR {ServicoVisor.Valor(1000m).PadLeft(14)}", visor.Linha2);

        // Desligar a balança: o POS volta a escrever o peso.
        await admin.Put($"/api/configuracoes/postos/{posto.Id}/balanca", new ConfigurarBalanca(ModoBalanca.Nenhuma, null, 9600, FormatoSerie.Oito_N_1))
            .Esperar(HttpStatusCode.NoContent);
        Assert.False((await admin.Obter<LeituraBalancaDto>($"/api/balanca/{posto.Id}")).Configurada);
        Assert.Equal("Nenhuma", (await motor.CicloAsync(0, CancellationToken.None))!.Modo);
    }
}
