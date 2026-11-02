using System.Net;
using LAMGestRestaurant.Application.Caixa;
using LAMGestRestaurant.Application.Comum;
using LAMGestRestaurant.Application.Seguranca;
using LAMGestRestaurant.Domain.Comum;
using LAMGestRestaurant.Tests.Infraestrutura;
using static LAMGestRestaurant.Tests.Infraestrutura.ExtensoesHttp;

namespace LAMGestRestaurant.Tests;

[Collection(ColeccaoBaseDados.Nome)]
public class CaixaTests(AplicacaoTeste app)
{
    private async Task<TerminalDto> CriarTerminalAsync()
    {
        var (admin, _) = await app.ClienteAutenticadoAsync(PerfisPadrao.Administrador);
        return await (await admin.Post("/api/terminais", new GuardarTerminal(Unico("Caixa "), null)).Esperar(HttpStatusCode.Created)).Ler<TerminalDto>();
    }

    private static async Task<int> MetodoAsync(HttpClient c, string codigo) =>
        (await c.Obter<List<MetodoPagamentoDto>>("/api/caixa/metodos-pagamento")).Single(m => m.Codigo == codigo).Id;

    [Fact]
    public async Task Fluxo_completo_abrir_movimentar_e_fechar_com_diferenca()
    {
        var terminal = await CriarTerminalAsync();
        var (caixa, _) = await app.ClienteAutenticadoAsync(PerfisPadrao.Caixa);
        var dinheiro = await MetodoAsync(caixa, "DINHEIRO");

        var sessao = await (await caixa.Post("/api/caixa/sessoes", new AbrirCaixa(terminal.Id, 1000m, "Turno da manhã")).Esperar(HttpStatusCode.Created))
            .Ler<SessaoCaixaDto>();
        Assert.Equal(EstadoSessaoCaixa.Aberta, sessao.Estado);
        Assert.Equal(1000m, sessao.PorMetodo.Single(m => m.MetodoPagamentoId == dinheiro).Esperado);

        await caixa.Post($"/api/caixa/sessoes/{sessao.Id}/movimentos", new RegistarMovimentoCaixa(TipoMovimentoCaixa.Entrada, dinheiro, 500m, "Reforço de trocos"))
            .Esperar(HttpStatusCode.OK);
        // Não se retira mais do que o esperado na gaveta (1500).
        await caixa.Post($"/api/caixa/sessoes/{sessao.Id}/movimentos", new RegistarMovimentoCaixa(TipoMovimentoCaixa.Saida, dinheiro, 2000m, "Sangria"))
            .Esperar(HttpStatusCode.UnprocessableEntity);
        sessao = await (await caixa.Post($"/api/caixa/sessoes/{sessao.Id}/movimentos", new RegistarMovimentoCaixa(TipoMovimentoCaixa.Saida, dinheiro, 300m, "Sangria para o cofre"))
            .Esperar(HttpStatusCode.OK)).Ler<SessaoCaixaDto>();

        var linha = sessao.PorMetodo.Single(m => m.MetodoPagamentoId == dinheiro);
        Assert.Equal((1000m, 500m, 300m, 1200m), (linha.Fundo, linha.Entradas, linha.Saidas, linha.Esperado));
        Assert.Equal(2, sessao.Movimentos.Count);

        // Fecho sem contagem do dinheiro é recusado.
        await caixa.Post($"/api/caixa/sessoes/{sessao.Id}/fechar", new FecharCaixa([], null, sessao.RowVersion)).Esperar(HttpStatusCode.UnprocessableEntity);

        var fechada = await (await caixa.Post($"/api/caixa/sessoes/{sessao.Id}/fechar",
            new FecharCaixa([new ContagemMetodo(dinheiro, 1150m)], "Faltam 50", sessao.RowVersion)).Esperar(HttpStatusCode.OK)).Ler<SessaoCaixaDto>();
        Assert.Equal(EstadoSessaoCaixa.Fechada, fechada.Estado);
        Assert.Equal(-50m, fechada.TotalDiferenca);
        Assert.Equal(-50m, fechada.PorMetodo.Single(m => m.MetodoPagamentoId == dinheiro).Diferenca);

        // Uma sessão fechada não aceita movimentos nem novo fecho.
        await caixa.Post($"/api/caixa/sessoes/{sessao.Id}/movimentos", new RegistarMovimentoCaixa(TipoMovimentoCaixa.Entrada, dinheiro, 10m, "Depois do fecho"))
            .Esperar(HttpStatusCode.UnprocessableEntity);
        await caixa.Post($"/api/caixa/sessoes/{sessao.Id}/fechar", new FecharCaixa([new ContagemMetodo(dinheiro, 1150m)], null, fechada.RowVersion))
            .Esperar(HttpStatusCode.UnprocessableEntity);

        await using var db = app.CriarContexto();
        Assert.Contains(db.RegistosAuditoria, a => a.Accao == "caixa.fechar" && a.EntidadeId == sessao.Id.ToString());
    }

    [Fact]
    public async Task Nao_abre_duas_sessoes_no_mesmo_terminal_nem_duas_do_mesmo_utilizador()
    {
        var t1 = await CriarTerminalAsync();
        var t2 = await CriarTerminalAsync();
        var (caixaA, _) = await app.ClienteAutenticadoAsync(PerfisPadrao.Caixa);
        var (caixaB, _) = await app.ClienteAutenticadoAsync(PerfisPadrao.Caixa);

        await caixaA.Post("/api/caixa/sessoes", new AbrirCaixa(t1.Id, 0m, null)).Esperar(HttpStatusCode.Created);
        await caixaB.Post("/api/caixa/sessoes", new AbrirCaixa(t1.Id, 0m, null)).Esperar(HttpStatusCode.Conflict);
        await caixaA.Post("/api/caixa/sessoes", new AbrirCaixa(t2.Id, 0m, null)).Esperar(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Aberturas_simultaneas_no_mesmo_terminal_so_uma_vence()
    {
        var terminal = await CriarTerminalAsync();
        var clientes = new List<HttpClient>();
        for (var i = 0; i < 4; i++)
            clientes.Add((await app.ClienteAutenticadoAsync(PerfisPadrao.Caixa)).Cliente);

        var respostas = await Task.WhenAll(clientes.Select(c => c.Post("/api/caixa/sessoes", new AbrirCaixa(terminal.Id, 100m, null))));

        Assert.Equal(1, respostas.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.All(respostas.Where(r => r.StatusCode != HttpStatusCode.Created), r => Assert.Equal(HttpStatusCode.Conflict, r.StatusCode));
    }

    [Fact]
    public async Task Sessao_de_outro_caixa_so_e_visivel_para_quem_tem_ver_todas()
    {
        var terminal = await CriarTerminalAsync();
        var (dono, _) = await app.ClienteAutenticadoAsync(PerfisPadrao.Caixa);
        var (outro, _) = await app.ClienteAutenticadoAsync(PerfisPadrao.Caixa);
        var (gerente, _) = await app.ClienteAutenticadoAsync(PerfisPadrao.Gerente);
        var sessao = await (await dono.Post("/api/caixa/sessoes", new AbrirCaixa(terminal.Id, 0m, null)).Esperar(HttpStatusCode.Created)).Ler<SessaoCaixaDto>();

        Assert.Equal(HttpStatusCode.Forbidden, (await outro.GetAsync($"/api/caixa/sessoes/{sessao.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await gerente.GetAsync($"/api/caixa/sessoes/{sessao.Id}")).StatusCode);

        var listaOutro = await outro.Obter<Pagina<SessaoCaixaResumo>>("/api/caixa/sessoes");
        Assert.DoesNotContain(listaOutro.Itens, s => s.Id == sessao.Id);
    }

    [Fact]
    public async Task Terminal_com_sessao_aberta_nao_se_desactiva()
    {
        var terminal = await CriarTerminalAsync();
        var (caixa, _) = await app.ClienteAutenticadoAsync(PerfisPadrao.Caixa);
        var (admin, _) = await app.ClienteAutenticadoAsync(PerfisPadrao.Administrador);
        await caixa.Post("/api/caixa/sessoes", new AbrirCaixa(terminal.Id, 0m, null)).Esperar(HttpStatusCode.Created);

        await admin.Post($"/api/terminais/{terminal.Id}/activo", new { activo = false }).Esperar(HttpStatusCode.UnprocessableEntity);
    }
}
