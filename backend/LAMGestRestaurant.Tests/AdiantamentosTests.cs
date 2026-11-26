using System.Net;
using LAMGestRestaurant.Application.Cartoes;
using LAMGestRestaurant.Application.Clientes;
using LAMGestRestaurant.Application.Comum;
using LAMGestRestaurant.Application.Impressao;
using LAMGestRestaurant.Application.Relatorios;
using LAMGestRestaurant.Application.Seguranca;
using LAMGestRestaurant.Application.Vendas;
using LAMGestRestaurant.Domain.Cartoes;
using LAMGestRestaurant.Domain.Comum;
using LAMGestRestaurant.Tests.Infraestrutura;
using static LAMGestRestaurant.Tests.Infraestrutura.ExtensoesHttp;

namespace LAMGestRestaurant.Tests;

/// <summary>
/// Adiantamentos de clientes: Cartões Evento associados a clientes, com a conta de adiantamentos (recargas, consumos,
/// transferências e mudanças de cliente), o cliente posto no pedido pago com o cartão, o relatório e a descarga.
/// </summary>
[Collection(ColeccaoBaseDados.Nome)]
public class AdiantamentosTests(AplicacaoTeste app)
{
    private const string Url = "/api/cartoes";

    private static string Numero() => "8" + Random.Shared.NextInt64(10_000_000_000, 99_999_999_999);

    private static DateOnly Hoje() => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("Africa/Maputo")));

    private static async Task<ClienteDto> ClienteAsync(HttpClient c, string nome) =>
        await (await c.Post("/api/clientes", new GuardarCliente(TipoCliente.Particular, Unico(nome), null, null, null, null, null, null, null))
            .Esperar(HttpStatusCode.OK)).Ler<ClienteDto>();

    private static async Task<CartaoDto> RegistarAsync(HttpClient c, decimal valor, int? metodo, int? clienteId = null) =>
        await (await c.PostComChave(Url, CenarioVenda.Chave(), new RegistarCartao(Numero(), null, valor, metodo, clienteId))
            .Esperar(HttpStatusCode.Created)).Ler<CartaoDto>();

    [Fact]
    public async Task Conta_de_adiantamentos_soma_recargas_consumos_transferencias_e_mudancas_de_cliente()
    {
        var c = await CenarioVenda.CriarAsync(app);
        var dinheiro = c.Metodos["DINHEIRO"];
        var ana = await ClienteAsync(c.Gerente, "Adiant Ana ");
        var rui = await ClienteAsync(c.Gerente, "Adiant Rui ");

        // Cartão registado já da Ana, com 1000 de adiantamento.
        var x = await RegistarAsync(c.Gerente, 1000m, dinheiro, ana.Id);
        Assert.Equal(ana.Id, x.ClienteId);
        Assert.Equal(ana.Nome, x.ClienteNome);
        Assert.Contains((await c.Gerente.Obter<Pagina<CartaoDto>>($"{Url}?clienteId={ana.Id}")).Itens, k => k.Id == x.Id);

        // Consumo no POS: o pedido sem cliente fica com o cliente do cartão.
        var pedido = await c.AbrirMesaAsync(c.Gerente, c.Mesa1, (c.Prato, 1)); // 450,50
        Assert.Null(pedido.ClienteId);
        pedido = await (await c.Gerente.PostComChave($"/api/pedidos/{pedido.Id}/pagamentos", CenarioVenda.Chave(),
            new RegistarPagamento(c.Sessao.Id, c.Metodos[ServicoCartoes.CodigoMetodo], 450.5m, 450.5m, x.Numero)).Esperar(HttpStatusCode.Created)).Ler<PedidoDto>();
        Assert.Equal(ana.Id, pedido.ClienteId);

        // Cartão perdido: o saldo passa para um cartão novo (sem cliente nem saldo), que fica da Ana; o antigo fica bloqueado.
        x = await c.Gerente.Obter<CartaoDto>($"{Url}/{x.Id}");
        var y = await RegistarAsync(c.Gerente, 0m, null);
        var chave = CenarioVenda.Chave();
        var corpo = new TransferirSaldo(y.Numero, x.RowVersion);
        y = await (await c.Gerente.PostComChave($"{Url}/{x.Id}/transferencia", chave, corpo).Esperar(HttpStatusCode.Created)).Ler<CartaoDto>();
        Assert.Equal((549.5m, ana.Id), (y.Saldo, y.ClienteId));
        Assert.Equal(y.Id, (await (await c.Gerente.PostComChave($"{Url}/{x.Id}/transferencia", chave, corpo).Esperar(HttpStatusCode.OK)).Ler<CartaoDto>()).Id);
        x = await c.Gerente.Obter<CartaoDto>($"{Url}/{x.Id}");
        Assert.Equal((0m, false), (x.Saldo, x.Activo));

        // Cartão com saldo e sem cliente passa para a Ana e depois para o Rui: o saldo do cartão não muda, muda a conta.
        var z = await RegistarAsync(c.Gerente, 100m, dinheiro);
        z = await (await c.Gerente.Post($"{Url}/{z.Id}/cliente", new AssociarClienteCartao(ana.Id, z.RowVersion)).Esperar(HttpStatusCode.OK)).Ler<CartaoDto>();
        z = await (await c.Gerente.Post($"{Url}/{z.Id}/cliente", new AssociarClienteCartao(rui.Id, z.RowVersion)).Esperar(HttpStatusCode.OK)).Ler<CartaoDto>();
        Assert.Equal((100m, rui.Id), (z.Saldo, z.ClienteId));
        var extratoZ = await c.Gerente.Obter<ExtratoCartaoDto>($"{Url}/{z.Id}/movimentos?tamanho=50");
        Assert.Equal((100m, 0m), (extratoZ.Entradas, extratoZ.Saidas)); // a mudança de cliente não conta como entrada nem saída do cartão

        // O saldo nunca passa para um cartão de outro cliente.
        await c.Gerente.PostComChave($"{Url}/{y.Id}/transferencia", CenarioVenda.Chave(), new TransferirSaldo(z.Numero, y.RowVersion))
            .Esperar(HttpStatusCode.UnprocessableEntity);

        var hoje = Hoje();
        var periodo = $"desde={hoje:yyyy-MM-dd}&ate={hoje:yyyy-MM-dd}";
        var eAna = await c.Gerente.Obter<ExtratoAdiantamentos>($"{Url}/clientes/{ana.Id}/extrato?{periodo}");
        Assert.Equal((0m, 1000m, 450.5m, 0m, 549.5m, 549.5m), (eAna.SaldoAnterior, eAna.Recargas, eAna.Consumos, eAna.Outros, eAna.SaldoFinal, eAna.SaldoActual));
        Assert.Equal(549.5m, eAna.Movimentos[^1].Saldo);
        Assert.Contains(eAna.Movimentos, m => m.Tipo == TipoMovimentoCartao.Pagamento && m.Pedido == pedido.Numero);
        Assert.Equal([x.Numero, y.Numero], eAna.Cartoes.Select(k => k.Numero).Order());
        var eRui = await c.Gerente.Obter<ExtratoAdiantamentos>($"{Url}/clientes/{rui.Id}/extrato?{periodo}");
        Assert.Equal((0m, 100m, 100m), (eRui.Recargas, eRui.Outros, eRui.SaldoActual));

        var rel = await c.Gerente.Obter<RelatorioAdiantamentos>($"/api/relatorios/adiantamentos?{periodo}");
        var linhaAna = Assert.Single(rel.Clientes, l => l.ClienteId == ana.Id);
        Assert.Equal((2, 1000m, 450.5m, 0m, 549.5m), (linhaAna.Cartoes, linhaAna.Recargas, linhaAna.Consumos, linhaAna.Outros, linhaAna.SaldoFinal));
        Assert.NotNull(linhaAna.UltimoConsumoUtc);
        Assert.Equal(100m, Assert.Single(rel.Clientes, l => l.ClienteId == rui.Id).Outros);
        Assert.Contains(rel.RecargasPorMetodo, m => m.Metodo == "Dinheiro");

        var doc = TextoDocumento.De(await c.Gerente.Obter<DocumentoImpressao>(
            $"/api/relatorios/documento?tipo={TipoDocumentoRelatorio.ExtratoAdiantamentos}&clienteId={ana.Id}&{periodo}"));
        Assert.Contains(ana.Nome, doc);
        Assert.Contains("549,50", doc);
        Assert.Contains("DEVIDO AOS CLIENTES", TextoDocumento.De(await c.Gerente.Obter<DocumentoImpressao>(
            $"/api/relatorios/documento?tipo={TipoDocumentoRelatorio.Adiantamentos}&{periodo}")));
    }

    [Fact]
    public async Task Descarga_de_todos_deixa_de_fora_os_cartoes_de_clientes_salvo_se_incluidos()
    {
        var c = await CenarioVenda.CriarAsync(app);
        var dinheiro = c.Metodos["DINHEIRO"];
        var cliente = await ClienteAsync(c.Gerente, "Adiant Desc ");
        var doCliente = await RegistarAsync(c.Gerente, 70m, dinheiro, cliente.Id);
        var evento = await RegistarAsync(c.Gerente, 30m, dinheiro);

        var todos = await c.Gerente.Obter<SaldosDescarga>($"{Url}/descargas/saldos");
        Assert.Contains(todos.Cartoes, k => k.Id == evento.Id);
        Assert.DoesNotContain(todos.Cartoes, k => k.Id == doCliente.Id);
        Assert.True(todos.CartoesDeClientesFora >= 1 && todos.SaldoDeClientesFora >= 70m);
        var comClientes = await c.Gerente.Obter<SaldosDescarga>($"{Url}/descargas/saldos?incluirClientes=true");
        Assert.Equal(cliente.Nome, Assert.Single(comClientes.Cartoes, k => k.Id == doCliente.Id).Cliente);
        // Um cartão indicado entra sempre.
        Assert.Single((await c.Gerente.Obter<SaldosDescarga>($"{Url}/descargas/saldos?id={doCliente.Id}")).Cartoes);
    }

    [Fact]
    public async Task So_quem_tem_a_permissao_associa_cartoes_a_clientes_ou_transfere_saldo()
    {
        var c = await CenarioVenda.CriarAsync(app);
        var cliente = await ClienteAsync(c.Gerente, "Adiant Perm ");
        var (caixa, _) = await app.ClienteAutenticadoAsync(PerfisPadrao.Caixa); // regista e recarrega, mas não associa
        var cartao = await RegistarAsync(caixa, 0m, null);
        await caixa.Post($"{Url}/{cartao.Id}/cliente", new AssociarClienteCartao(cliente.Id, cartao.RowVersion)).Esperar(HttpStatusCode.Forbidden);
        await caixa.PostComChave(Url, CenarioVenda.Chave(), new RegistarCartao(Numero(), null, 0m, null, cliente.Id)).Esperar(HttpStatusCode.Forbidden);
        await caixa.PostComChave($"{Url}/{cartao.Id}/transferencia", CenarioVenda.Chave(), new TransferirSaldo(Numero(), cartao.RowVersion))
            .Esperar(HttpStatusCode.Forbidden);

        // Cliente desactivado não recebe cartões.
        var inactivo = await ClienteAsync(c.Gerente, "Adiant Inact ");
        await c.Gerente.Post($"/api/clientes/{inactivo.Id}/activo", new { activo = false }).Esperar(HttpStatusCode.NoContent);
        await c.Gerente.Post($"{Url}/{cartao.Id}/cliente", new AssociarClienteCartao(inactivo.Id, cartao.RowVersion)).Esperar(HttpStatusCode.UnprocessableEntity);
    }
}
