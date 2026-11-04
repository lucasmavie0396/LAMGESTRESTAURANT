using System.Net;
using LAMGestRestaurant.Application.Caixa;
using LAMGestRestaurant.Application.Comum;
using LAMGestRestaurant.Application.Impressao;
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
        // Fecho cego: a API não revela o esperado de uma sessão aberta, só os métodos a contar.
        var aberta = sessao.PorMetodo.Single(m => m.MetodoPagamentoId == dinheiro);
        Assert.Equal((true, 1000m, (decimal?)null, (decimal?)null), (aberta.Contar, aberta.Fundo, aberta.Esperado, sessao.TotalEsperado));
        Assert.Equal(1000m, await app.EsperadoAsync(sessao.Id, dinheiro));

        await caixa.Post($"/api/caixa/sessoes/{sessao.Id}/movimentos", new RegistarMovimentoCaixa(TipoMovimentoCaixa.Entrada, dinheiro, 500m, "Reforço de trocos"))
            .Esperar(HttpStatusCode.OK);
        // Não se retira mais do que o esperado na gaveta (1500).
        await caixa.Post($"/api/caixa/sessoes/{sessao.Id}/movimentos", new RegistarMovimentoCaixa(TipoMovimentoCaixa.Saida, dinheiro, 2000m, "Sangria"))
            .Esperar(HttpStatusCode.UnprocessableEntity);
        sessao = await (await caixa.Post($"/api/caixa/sessoes/{sessao.Id}/movimentos", new RegistarMovimentoCaixa(TipoMovimentoCaixa.Saida, dinheiro, 300m, "Sangria para o cofre"))
            .Esperar(HttpStatusCode.OK)).Ler<SessaoCaixaDto>();

        var linha = sessao.PorMetodo.Single(m => m.MetodoPagamentoId == dinheiro);
        Assert.Equal((1000m, (decimal?)null, (decimal?)null, (decimal?)null), (linha.Fundo, linha.Entradas, linha.Saidas, linha.Esperado));
        Assert.Equal(1200m, await app.EsperadoAsync(sessao.Id, dinheiro));
        Assert.Equal(2, sessao.Movimentos.Count);

        // Fecho sem contagem do dinheiro é recusado.
        await caixa.Post($"/api/caixa/sessoes/{sessao.Id}/fechar", new FecharCaixa([], null, sessao.RowVersion)).Esperar(HttpStatusCode.UnprocessableEntity);

        var fechada = await (await caixa.Post($"/api/caixa/sessoes/{sessao.Id}/fechar",
            new FecharCaixa([new ContagemMetodo(dinheiro, 1150m)], "Faltam 50", sessao.RowVersion)).Esperar(HttpStatusCode.OK)).Ler<SessaoCaixaDto>();
        Assert.Equal(EstadoSessaoCaixa.Fechada, fechada.Estado);
        Assert.Equal(-50m, fechada.TotalDiferenca);
        var fecho = fechada.PorMetodo.Single(m => m.MetodoPagamentoId == dinheiro);
        Assert.Equal((500m, 300m, 1200m, 1150m, -50m), (fecho.Entradas, fecho.Saidas, fecho.Esperado, fecho.Contado, fecho.Diferenca)); // revelados depois do fecho

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

    [Fact]
    public async Task Numero_do_posto_e_unico_e_consulta_se_sem_sessao()
    {
        var (admin, _) = await app.ClienteAutenticadoAsync(PerfisPadrao.Administrador);
        var numero = Random.Shared.Next(100, 999);
        var posto = await (await admin.Post("/api/terminais", new GuardarTerminal(Unico("Posto "), null, numero)).Esperar(HttpStatusCode.Created)).Ler<TerminalDto>();
        Assert.Equal(numero, posto.Numero);
        await admin.Post("/api/terminais", new GuardarTerminal(Unico("Outro "), null, numero)).Esperar(HttpStatusCode.Conflict);

        // O instalador e o atalho do posto consultam sem sessão; só postos activos.
        var anonimo = app.CreateClient();
        var publico = await anonimo.Obter<PostoPublico>($"/api/postos/{numero}");
        Assert.Equal((posto.Id, posto.Nome), (publico.TerminalId, publico.Nome));
        await admin.Post($"/api/terminais/{posto.Id}/activo", new { activo = false }).Esperar(HttpStatusCode.NoContent);
        Assert.Equal(HttpStatusCode.NotFound, (await anonimo.GetAsync($"/api/postos/{numero}")).StatusCode);

        await admin.Post("/api/terminais", new GuardarTerminal(Unico("Mau "), null, 0)).Esperar(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Fecho_cego_ponto_de_situacao_sem_esperado_e_fecho_com_produtos_por_categoria()
    {
        var c = await CenarioVenda.CriarAsync(app);
        var pedido = await c.AbrirMesaAsync(c.Gerente, c.Mesa1, (c.Prato, 2), (c.Bebida, 3)); // 901 + 180 = 1 081
        await c.PagarAsync(c.Gerente, pedido.Id, "DINHEIRO", pedido.Total).Esperar(HttpStatusCode.Created);
        static string Texto(DocumentoImpressao d) => TextoDocumento.De(d).Replace(" | ", " ");
        var url = $"/api/relatorios/documento?tipo=FechoCaixa&sessaoId={c.Sessao.Id}";

        // Aberta: nem esperado (1 000 + 1 081 = 2 081), nem recebido, nem diferença; só o que há a contar.
        var ponto = Texto(await c.Gerente.Obter<DocumentoImpressao>(url));
        foreach (var oculto in new[] { "2 081", "1 081", "TOTAL ESPERADO", "Recebido", "DIFERENÇA", "PRODUTOS VENDIDOS" })
            Assert.DoesNotContain(oculto, ponto);
        Assert.Contains("Métodos a contar no fecho", ponto);
        var sessao = await c.Gerente.Obter<SessaoCaixaDto>($"/api/caixa/sessoes/{c.Sessao.Id}");
        Assert.All(sessao.PorMetodo, m => Assert.Null(m.Esperado));

        // Fecho com 2 076 contados (faltam 5): o documento passa a mostrar tudo e os produtos por categoria.
        await c.Gerente.Post($"/api/caixa/sessoes/{c.Sessao.Id}/fechar", new FecharCaixa(
            [.. sessao.PorMetodo.Where(m => m.Contar).Select(m => new ContagemMetodo(m.MetodoPagamentoId, m.MetodoPagamentoId == c.Metodos["DINHEIRO"] ? 2076m : 0m))],
            null, sessao.RowVersion)).Esperar(HttpStatusCode.OK);
        var fecho = Texto(await c.Gerente.Obter<DocumentoImpressao>(url));
        foreach (var visivel in new[] { "TOTAL ESPERADO 2 081,00", "DIFERENÇA -5,00", "PRODUTOS VENDIDOS POR CATEGORIA",
                     $"{c.Prato.CategoriaNome} 1 081,00", "2 x Frango grelhado 901,00", "3 x Refrigerante 180,00", "Total das vendas 1 081,00" })
            Assert.Contains(visivel, fecho);
    }
}
