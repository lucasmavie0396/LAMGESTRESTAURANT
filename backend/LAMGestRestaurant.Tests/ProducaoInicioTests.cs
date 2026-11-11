using System.Net;
using LAMGestRestaurant.Api.Controllers;
using LAMGestRestaurant.Application.Cartoes;
using LAMGestRestaurant.Application.Clientes;
using LAMGestRestaurant.Application.Seguranca;
using LAMGestRestaurant.Application.Sistema;
using LAMGestRestaurant.Application.Vendas;
using LAMGestRestaurant.Domain.Comum;
using LAMGestRestaurant.Domain.Sistema;
using LAMGestRestaurant.Tests.Infraestrutura;
using Microsoft.EntityFrameworkCore;
using static LAMGestRestaurant.Tests.Infraestrutura.ExtensoesHttp;

namespace LAMGestRestaurant.Tests;

/// <summary>
/// Modo de testes e início da produção: apaga vendas, documentos, caixa, clientes, cartões e stock, mantém configurações e produtos,
/// e recomeça a numeração. Corre uma vez por execução (a base de testes é recriada no arranque).
/// </summary>
[Collection(ColeccaoBaseDados.Nome)]
public class ProducaoInicioTests(AplicacaoTeste app)
{
    private const string Url = "/api/sistema/producao";

    [Fact]
    public async Task Iniciar_producao_apaga_os_dados_de_teste_mantem_o_resto_e_so_se_faz_uma_vez()
    {
        // Dados de teste: venda paga, cliente, factura a crédito, recibo, cartão, mesa ocupada.
        var c = await CenarioVenda.CriarAsync(app);
        var cliente = await (await c.Gerente.Post("/api/clientes", new GuardarCliente(TipoCliente.Particular, Unico("Teste "), null, null, null, null, null, null, null))
            .Esperar(HttpStatusCode.OK)).Ler<ClienteDto>();
        var pedido = await c.AbrirMesaAsync(c.Gerente, c.Mesa1, (c.Prato, 1));
        await c.PagarAsync(c.Gerente, pedido.Id, "DINHEIRO", pedido.Total).Esperar(HttpStatusCode.Created);
        var fiado = await c.AbrirMesaAsync(c.Gerente, c.Mesa1, (c.Bebida, 1));
        await c.Gerente.PostComChave($"/api/pedidos/{fiado.Id}/pagamentos/multiplos", CenarioVenda.Chave(),
            new RegistarPagamentos(c.Sessao.Id, [new LinhaPagamento(c.Metodos["FACTURA_CREDITO"], fiado.Total, fiado.Total, null)], false, cliente.Id)).Esperar(HttpStatusCode.Created);
        await c.Gerente.PostComChave("/api/cartoes", CenarioVenda.Chave(), new RegistarCartao(null, null, 100m, c.Metodos["DINHEIRO"])).Esperar(HttpStatusCode.Created);
        await c.AbrirMesaAsync(c.Gerente, c.Mesa2, (c.Bebida, 2)); // fica aberto: a mesa ocupada

        var estado = await c.Gerente.Obter<EstadoProducao>(Url);
        Assert.False(estado.EmProducao);
        Assert.True(estado.Dados!.Pedidos >= 3 && estado.Dados.Documentos >= 2 && estado.Dados.Clientes >= 1 && estado.Dados.CartoesEvento >= 1);
        var pos = await app.ClienteComPermissoesAsync(Permissoes.PosUsar);
        var estadoPos = await pos.Obter<EstadoProducao>(Url);
        Assert.Null(estadoPos.Dados); // a faixa vê o modo, não os números
        await pos.Post(Url, new PedidoInicioProducao(AplicacaoTeste.PalavraPasseTeste, true, ServicoProducao.FraseConfirmacao)).Esperar(HttpStatusCode.Forbidden);
        await c.Gerente.Post(Url, new PedidoInicioProducao("errada", true, ServicoProducao.FraseConfirmacao)).Esperar(HttpStatusCode.UnprocessableEntity);
        await c.Gerente.Post(Url, new PedidoInicioProducao(AplicacaoTeste.PalavraPasseTeste, true, "apagar")).Esperar(HttpStatusCode.UnprocessableEntity);

        int produtos, configuracoes, impressoras, utilizadores, mesas, saldos;
        await using (var db = app.CriarContexto())
        {
            produtos = await db.Produtos.CountAsync();
            configuracoes = await db.Configuracoes.CountAsync();
            impressoras = await db.Impressoras.CountAsync();
            utilizadores = await db.Users.CountAsync();
            mesas = await db.Mesas.CountAsync();
            saldos = await db.StockSaldos.CountAsync();
        }

        var resultado = await (await c.Gerente.Post(Url, new PedidoInicioProducao(AplicacaoTeste.PalavraPasseTeste, true, ServicoProducao.FraseConfirmacao))
            .Esperar(HttpStatusCode.OK)).Ler<ResultadoProducao>();
        Assert.True(resultado.Apagou);
        Assert.NotNull(resultado.CopiaSeguranca);
        Assert.True(File.Exists(resultado.CopiaSeguranca), "A cópia de segurança devia existir.");
        File.Delete(resultado.CopiaSeguranca!);

        await using (var db = app.CriarContexto())
        {
            Assert.Equal(0, await db.Pedidos.CountAsync());
            Assert.Equal(0, await db.Pagamentos.CountAsync());
            Assert.Equal(0, await db.Documentos.CountAsync());
            Assert.Equal(0, await db.Clientes.CountAsync());
            Assert.Equal(0, await db.CartoesEvento.CountAsync());
            Assert.Equal(0, await db.MovimentosCartao.CountAsync());
            Assert.Equal(0, await db.SessoesCaixa.CountAsync());
            Assert.Equal(0, await db.MovimentosCaixa.CountAsync());
            Assert.Equal(0, await db.MovimentosStock.CountAsync());
            Assert.Equal(0, await db.SeriesDocumento.CountAsync());
            Assert.All(await db.StockSaldos.ToListAsync(), s => Assert.Equal(0m, s.Quantidade));
            Assert.All(await db.Mesas.ToListAsync(), m => Assert.Equal(EstadoMesa.Livre, m.Estado));
            // Fica: produtos, configurações (mais a data de produção), impressoras, utilizadores, mesas, saldos (a zero).
            Assert.Equal((produtos, configuracoes + 1, impressoras, utilizadores, mesas, saldos),
                (await db.Produtos.CountAsync(), await db.Configuracoes.CountAsync(), await db.Impressoras.CountAsync(), await db.Users.CountAsync(),
                    await db.Mesas.CountAsync(), await db.StockSaldos.CountAsync()));
            Assert.True(await db.RegistosAuditoria.AnyAsync(r => r.Accao == "sistema.iniciar-producao"));
        }

        // Em produção: não há segunda limpeza; a data não se muda pelas Configurações; a numeração recomeça no 1.
        Assert.True((await c.Gerente.Obter<EstadoProducao>(Url)).EmProducao);
        await c.Gerente.Post(Url, new PedidoInicioProducao(AplicacaoTeste.PalavraPasseTeste, true, ServicoProducao.FraseConfirmacao)).Esperar(HttpStatusCode.Conflict);
        await c.Gerente.Put("/api/configuracoes", new GuardarConfiguracoes(new Dictionary<string, string> { [ChavesConfiguracao.ProducaoDesde] = "" }))
            .Esperar(HttpStatusCode.UnprocessableEntity);
        var real = await CenarioVenda.CriarAsync(app);
        var venda = await real.AbrirMesaAsync(real.Gerente, real.Mesa1, (real.Bebida, 1));
        venda = await (await real.PagarAsync(real.Gerente, venda.Id, "DINHEIRO", venda.Total).Esperar(HttpStatusCode.Created)).Ler<PedidoDto>();
        Assert.EndsWith("-000001", venda.ReciboNumero);
        Assert.EndsWith("-000001", venda.Numero);
    }
}
