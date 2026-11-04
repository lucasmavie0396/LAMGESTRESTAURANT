using System.Net;
using LAMGestRestaurant.Api.Controllers;
using LAMGestRestaurant.Application.Caixa;
using LAMGestRestaurant.Application.Clientes;
using LAMGestRestaurant.Application.Comum;
using LAMGestRestaurant.Application.Despesas;
using LAMGestRestaurant.Application.Pessoal;
using LAMGestRestaurant.Application.Reservas;
using LAMGestRestaurant.Application.Seguranca;
using LAMGestRestaurant.Application.Sistema;
using LAMGestRestaurant.Application.Vendas;
using LAMGestRestaurant.Domain.Comum;
using LAMGestRestaurant.Tests.Infraestrutura;
using Microsoft.EntityFrameworkCore;
using static LAMGestRestaurant.Tests.Infraestrutura.ExtensoesHttp;

namespace LAMGestRestaurant.Tests;

[Collection(ColeccaoBaseDados.Nome)]
public class GestaoTests(AplicacaoTeste app)
{
    private static string Telefone() => "84" + Random.Shared.Next(1000000, 9999999);

    private static GuardarCliente Cliente(string telefone) =>
        new(TipoCliente.Particular, "Ana Teste", telefone, null, null, null, null, null, null);

    [Fact]
    public async Task Cliente_nao_se_duplica_pelo_telefone_e_o_pos_pesquisa_mas_nao_cria()
    {
        var (gerente, _) = await app.ClienteAutenticadoAsync(PerfisPadrao.Gerente);
        var tel = Telefone();
        var cliente = await (await gerente.Post("/api/clientes", Cliente(tel)).Esperar(HttpStatusCode.OK)).Ler<ClienteDto>();

        // O mesmo número escrito com espaços é o mesmo telefone.
        await gerente.Post("/api/clientes", Cliente($"{tel[..2]} {tel[2..5]} {tel[5..]}")).Esperar(HttpStatusCode.Conflict);

        var (garcom, _) = await app.ClienteAutenticadoAsync(PerfisPadrao.Garcom);
        var encontrados = await garcom.Obter<Pagina<ClienteDto>>($"/api/clientes?pesquisa={tel}");
        Assert.Contains(encontrados.Itens, c => c.Id == cliente.Id);
        await garcom.Post("/api/clientes", Cliente(Telefone())).Esperar(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Fidelizacao_acumula_pontos_no_fecho_e_retira_no_estorno()
    {
        var c = await CenarioVenda.CriarAsync(app);
        await c.Gerente.Put("/api/configuracoes", new GuardarConfiguracoes(new Dictionary<string, string>
        {
            ["fidelizacao.activa"] = "true", ["fidelizacao.valor-por-ponto"] = "100",
        })).Esperar(HttpStatusCode.OK);
        var cliente = await (await c.Gerente.Post("/api/clientes", Cliente(Telefone())).Esperar(HttpStatusCode.OK)).Ler<ClienteDto>();

        var pedido = await c.AbrirMesaAsync(c.Gerente, c.Mesa1, (c.Prato, 1)); // 450,50 → 4 pontos
        pedido = await (await c.Gerente.Post($"/api/pedidos/{pedido.Id}/cliente", new AssociarCliente(cliente.Id, pedido.RowVersion)).Esperar(HttpStatusCode.OK)).Ler<PedidoDto>();
        Assert.Equal(cliente.Id, pedido.ClienteId);
        pedido = await (await c.PagarAsync(c.Gerente, pedido.Id, "DINHEIRO", pedido.Total).Esperar(HttpStatusCode.Created)).Ler<PedidoDto>();
        Assert.Equal(4, (await c.Gerente.Obter<ClienteDto>($"/api/clientes/{cliente.Id}")).SaldoPontos);

        await c.Gerente.Post($"/api/pedidos/{pedido.Id}/estornar", new EstornarPedido(c.Sessao.Id, c.Metodos["DINHEIRO"], "Devolvido", pedido.RowVersion))
            .Esperar(HttpStatusCode.OK);
        var depois = await c.Gerente.Obter<ClienteDto>($"/api/clientes/{cliente.Id}");
        Assert.Equal(0, depois.SaldoPontos);
        var movimentos = await c.Gerente.Obter<List<MovimentoPontosDto>>($"/api/clientes/{cliente.Id}/pontos");
        Assert.Equal([TipoMovimentoPontos.Estorno, TipoMovimentoPontos.Acumulacao], movimentos.Select(m => m.Tipo));

        // Ajuste manual não deixa o saldo negativo.
        await c.Gerente.Post($"/api/clientes/{cliente.Id}/pontos", new AjustarPontos(-1, "Correcção", depois.RowVersion)).Esperar(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Reservas_detectam_conflitos_na_mesma_mesa_e_exigem_motivo_para_cancelar()
    {
        var c = await CenarioVenda.CriarAsync(app);
        var amanha = DateTime.UtcNow.Date.AddDays(1).AddHours(17); // 19:00 em Maputo
        GuardarReserva Reserva(DateTime inicio, int pessoas = 2) => new(null, "Carlos", "841112222", inicio, 120, pessoas, c.Mesa1.Id, null, null);

        var r1 = await (await c.Gerente.Post("/api/reservas", Reserva(amanha)).Esperar(HttpStatusCode.OK)).Ler<ReservaDto>();
        await c.Gerente.Post("/api/reservas", Reserva(amanha.AddHours(1))).Esperar(HttpStatusCode.Conflict); // sobrepõe-se
        await c.Gerente.Post("/api/reservas", Reserva(amanha.AddHours(2))).Esperar(HttpStatusCode.OK);       // começa quando a 1.ª acaba
        await c.Gerente.Post("/api/reservas", Reserva(amanha.AddHours(5), pessoas: 10)).Esperar(HttpStatusCode.UnprocessableEntity); // capacidade 4
        await c.Gerente.Post("/api/reservas", Reserva(DateTime.UtcNow.AddDays(-1))).Esperar(HttpStatusCode.UnprocessableEntity);   // no passado

        await c.Gerente.Post($"/api/reservas/{r1.Id}/estado", new AlterarEstadoReserva(EstadoReserva.Cancelada, null, r1.RowVersion)).Esperar(HttpStatusCode.UnprocessableEntity);
        var cancelada = await (await c.Gerente.Post($"/api/reservas/{r1.Id}/estado", new AlterarEstadoReserva(EstadoReserva.Cancelada, "Cliente ligou", r1.RowVersion))
            .Esperar(HttpStatusCode.OK)).Ler<ReservaDto>();
        Assert.Equal(EstadoReserva.Cancelada, cancelada.Estado);
        // Cancelada liberta o horário (18:30–20:30 só se sobrepunha à reserva cancelada).
        await c.Gerente.Post("/api/reservas", Reserva(amanha.AddMinutes(-30))).Esperar(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Despesa_paga_pela_caixa_sai_da_gaveta_e_a_anulacao_devolve()
    {
        var c = await CenarioVenda.CriarAsync(app);
        var categorias = await c.Gerente.Obter<List<CategoriaDespesaDto>>("/api/despesas/categorias");
        var categoria = categorias.First(x => x.Activo).Id;
        var hoje = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("Africa/Maputo")));
        NovaDespesa Despesa(decimal valor, int? sessao) => new(categoria, hoje, valor, "Gás para a cozinha", c.Metodos["DINHEIRO"], null, sessao);

        await c.Gerente.Post("/api/despesas", Despesa(5000, c.Sessao.Id)).Esperar(HttpStatusCode.UnprocessableEntity); // gaveta tem 1000
        var despesa = await (await c.Gerente.Post("/api/despesas", Despesa(200, c.Sessao.Id)).Esperar(HttpStatusCode.OK)).Ler<DespesaDto>();
        Assert.Equal(800m, await app.EsperadoAsync(c.Sessao.Id, c.Metodos["DINHEIRO"]));

        var (caixa, _) = await app.ClienteAutenticadoAsync(PerfisPadrao.Caixa);
        await caixa.Post($"/api/despesas/{despesa.Id}/anular", new AnularComMotivo("Engano", despesa.RowVersion)).Esperar(HttpStatusCode.Forbidden);
        var anulada = await (await c.Gerente.Post($"/api/despesas/{despesa.Id}/anular", new AnularComMotivo("Valor errado", despesa.RowVersion)).Esperar(HttpStatusCode.OK)).Ler<DespesaDto>();
        Assert.Equal(EstadoDespesa.Anulada, anulada.Estado);
        Assert.Equal(1000m, await app.EsperadoAsync(c.Sessao.Id, c.Metodos["DINHEIRO"]));

        // Despesa não paga pela caixa (ex.: transferência) não mexe na sessão.
        await c.Gerente.Post("/api/despesas", Despesa(300, null) with { MetodoPagamentoId = c.Metodos["TRANSF"], Referencia = "TRF-1" }).Esperar(HttpStatusCode.OK);
        var resumo = await c.Gerente.Obter<ResumoDespesas>($"/api/despesas/resumo?desde={hoje:yyyy-MM-dd}&ate={hoje:yyyy-MM-dd}");
        Assert.True(resumo.Total >= 300m);
    }

    [Fact]
    public async Task Dados_pessoais_de_funcionarios_so_com_permissao_e_garcom_associado_ao_utilizador()
    {
        var (gerente, _) = await app.ClienteAutenticadoAsync(PerfisPadrao.Gerente);
        var (garcomCli, garcomNome) = await app.ClienteAutenticadoAsync(PerfisPadrao.Garcom);
        var garcomId = (await garcomCli.Obter<Api.Controllers.SessaoUtilizador>("/api/auth/me")).Id;

        var f = await (await gerente.Post("/api/funcionarios", new GuardarFuncionario("João Garçom", FuncaoFuncionario.Garcom, null, null, "BI 110100123456A", null, garcomId))
            .Esperar(HttpStatusCode.OK)).Ler<FuncionarioDto>();
        Assert.Equal("BI 110100123456A", f.DocumentoIdentificacao);
        await gerente.Post("/api/funcionarios", new GuardarFuncionario("Outro", FuncaoFuncionario.Garcom, null, null, null, null, garcomId)).Esperar(HttpStatusCode.Conflict);

        var semDados = await app.ClienteComPermissoesAsync(Permissoes.FuncionariosVer, Permissoes.FuncionariosGerir);
        var vistos = await semDados.Obter<List<FuncionarioDto>>("/api/funcionarios");
        Assert.Null(vistos.Single(x => x.Id == f.Id).DocumentoIdentificacao);
        // Editar sem a permissão não apaga o documento.
        await semDados.Put($"/api/funcionarios/{f.Id}", new GuardarFuncionario("João Garçom", FuncaoFuncionario.Garcom, "840000000", null, null, null, garcomId)).Esperar(HttpStatusCode.OK);
        Assert.Equal("BI 110100123456A", (await gerente.Obter<List<FuncionarioDto>>("/api/funcionarios")).Single(x => x.Id == f.Id).DocumentoIdentificacao);

        // Pedido criado pelo utilizador do garçom fica associado a ele.
        var c = await CenarioVenda.CriarAsync(app);
        var pedido = await c.AbrirMesaAsync(garcomCli, c.Mesa1, (c.Prato, 1));
        Assert.Equal(f.Id, pedido.GarcomId);
        Assert.Contains(await garcomCli.Obter<List<Opcao>>("/api/funcionarios/por-funcao/Garcom"), o => o.Id == f.Id);
    }

    [Fact]
    public async Task Configuracoes_validam_chaves_e_valores_e_ficam_auditadas()
    {
        var (admin, _) = await app.ClienteAutenticadoAsync(PerfisPadrao.Administrador);
        GuardarConfiguracoes Um(string k, string v) => new(new Dictionary<string, string> { [k] = v });

        await admin.Put("/api/configuracoes", Um("geral.fuso-horario", "Lua/Base")).Esperar(HttpStatusCode.UnprocessableEntity);
        await admin.Put("/api/configuracoes", Um("inventada", "1")).Esperar(HttpStatusCode.UnprocessableEntity);
        await admin.Put("/api/configuracoes", Um("delivery.taxa-padrao", "50,5")).Esperar(HttpStatusCode.UnprocessableEntity); // ponto decimal
        var nome = Unico("Restaurante ", 20);
        await admin.Put("/api/configuracoes", Um("estabelecimento.nome", nome)).Esperar(HttpStatusCode.OK);

        var (garcom, _) = await app.ClienteAutenticadoAsync(PerfisPadrao.Garcom);
        Assert.Equal(nome, (await garcom.Obter<ConfiguracoesPublicas>("/api/configuracoes/publicas")).NomeEstabelecimento);
        await garcom.Put("/api/configuracoes", Um("estabelecimento.nome", "X")).Esperar(HttpStatusCode.Forbidden);
        await using var db = app.CriarContexto();
        Assert.Contains(db.RegistosAuditoria, a => a.Accao == "configuracoes.alterar" && a.Detalhes!.Contains(nome));
    }
}
