using System.Net;
using LAMGestRestaurant.Application.Caixa;
using LAMGestRestaurant.Application.Impressao;
using LAMGestRestaurant.Application.Seguranca;
using LAMGestRestaurant.Domain.Comum;
using LAMGestRestaurant.Domain.Sistema;
using LAMGestRestaurant.Tests.Infraestrutura;
using Microsoft.EntityFrameworkCore;
using static LAMGestRestaurant.Tests.Infraestrutura.ExtensoesHttp;

namespace LAMGestRestaurant.Tests;

/// <summary>
/// Abrir Dia / Fechar Dia (menu Caixa): sem dia aberto os caixas não abrem; o dia só fecha com os caixas todos fechados
/// e o fecho grava o resumo de todos os caixas (número FD, vendas, meios de pagamento, produtos por categoria).
/// As datas são únicas na base de testes: este é o único teste que abre dias.
/// </summary>
[Collection(ColeccaoBaseDados.Nome)]
public class DiaOperacaoTests(AplicacaoTeste app)
{
    private const string Url = "/api/caixa/dias";

    private async Task ExigirDiaAsync(bool exigir)
    {
        await using var db = app.CriarContexto();
        var c = await db.Configuracoes.SingleAsync(x => x.Chave == ChavesConfiguracao.ExigirDiaAberto);
        c.Valor = exigir ? "true" : "false";
        await db.SaveChangesAsync();
    }

    private async Task ContasAbertasAsync(bool permitir)
    {
        await using var db = app.CriarContexto();
        var c = await db.Configuracoes.SingleAsync(x => x.Chave == ChavesConfiguracao.FecharDiaComContasAbertas);
        c.Valor = permitir ? "true" : "false";
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Abrir_dia_liberta_os_caixas_e_fechar_dia_resume_todos_os_caixas()
    {
        await ExigirDiaAsync(true);
        try
        {
            var (admin, _) = await app.ClienteAutenticadoAsync(PerfisPadrao.Administrador);
            var (caixa, _) = await app.ClienteAutenticadoAsync(PerfisPadrao.Caixa);
            var terminal = await (await admin.Post("/api/terminais", new GuardarTerminal(Unico("Bar "), null)).Esperar(HttpStatusCode.Created)).Ler<TerminalDto>();
            var dinheiro = (await admin.Obter<List<MetodoPagamentoDto>>("/api/caixa/metodos-pagamento")).Single(m => m.Codigo == "DINHEIRO").Id;

            // Sem dia aberto, o caixa não abre; o perfil Caixa não abre o dia.
            var estado = await caixa.Obter<EstadoDiaDto>($"{Url}/estado");
            Assert.True(estado.Exigir);
            Assert.Null(estado.Aberto);
            var recusa = await caixa.Post("/api/caixa/sessoes", new AbrirCaixa(terminal.Id, 200m, null)).Esperar(HttpStatusCode.UnprocessableEntity);
            Assert.Contains("Abrir Dia", await recusa.Content.ReadAsStringAsync());
            await caixa.Post(Url, new AbrirDia(null, null)).Esperar(HttpStatusCode.Forbidden);
            await admin.Post(Url, new AbrirDia(estado.Hoje.AddDays(1), null)).Esperar(HttpStatusCode.UnprocessableEntity); // dia futuro

            var dia = await (await admin.Post(Url, new AbrirDia(null, "Dia de teste")).Esperar(HttpStatusCode.Created)).Ler<DiaOperacaoDto>();
            Assert.Equal((estado.Hoje, EstadoDia.Aberto, (string?)null), (dia.Data, dia.Estado, dia.Numero));
            await admin.Post(Url, new AbrirDia(null, null)).Esperar(HttpStatusCode.Conflict); // só um dia aberto

            // Com o dia aberto, os caixas abrem e ficam no dia.
            var c = await CenarioVenda.CriarAsync(app);
            var sessaoCaixa = await (await caixa.Post("/api/caixa/sessoes", new AbrirCaixa(terminal.Id, 200m, null)).Esperar(HttpStatusCode.Created)).Ler<SessaoCaixaDto>();
            var pedido = await c.AbrirMesaAsync(c.Gerente, c.Mesa1, (c.Prato, 2), (c.Bebida, 1));
            Assert.Equal(961m, pedido.Total);
            await c.PagarAsync(c.Gerente, pedido.Id, "DINHEIRO", 961m).Esperar(HttpStatusCode.Created);

            // Fechar o dia com caixas abertos é recusado; a versão lida antes de os caixas abrirem já não serve.
            var falta = await admin.Post($"{Url}/{dia.Id}/fechar", new FecharDia(null, (await admin.Obter<EstadoDiaDto>($"{Url}/estado")).Aberto!.RowVersion))
                .Esperar(HttpStatusCode.UnprocessableEntity);
            Assert.Contains("ainda aberto", await falta.Content.ReadAsStringAsync());
            var ponto = await admin.Obter<ResumoDia>($"{Url}/{dia.Id}/resumo");
            Assert.Equal((2, 2, 1, 961m, (decimal?)null), (ponto.Caixas.Count, ponto.Pendencias.Count, ponto.Pedidos, ponto.VendasLiquidas, ponto.Diferenca));

            var versaoGerente = (await c.Gerente.Obter<SessaoCaixaDto>($"/api/caixa/sessoes/{c.Sessao.Id}")).RowVersion;
            await c.Gerente.Post($"/api/caixa/sessoes/{c.Sessao.Id}/fechar", new FecharCaixa([new ContagemMetodo(dinheiro, 1961m)], null, versaoGerente)).Esperar(HttpStatusCode.OK);
            await caixa.Post($"/api/caixa/sessoes/{sessaoCaixa.Id}/fechar", new FecharCaixa([new ContagemMetodo(dinheiro, 190m)], "Faltam 10", sessaoCaixa.RowVersion)).Esperar(HttpStatusCode.OK);
            await admin.Post($"{Url}/{dia.Id}/fechar", new FecharDia(null, dia.RowVersion)).Esperar(HttpStatusCode.Conflict);

            // O perfil Caixa não consulta nem fecha o dia.
            await caixa.GetAsync($"{Url}/{dia.Id}/resumo").Esperar(HttpStatusCode.Forbidden);
            await caixa.GetAsync($"/api/relatorios/documento?tipo=FechoDia&diaId={dia.Id}").Esperar(HttpStatusCode.Forbidden);

            // Conta aberta: com «fechar o dia com contas abertas» desligado o dia não fecha; ligado, fecha e a conta transita.
            var aberta = await c.AbrirMesaAsync(c.Gerente, c.Mesa2, (c.Bebida, 1));
            await ContasAbertasAsync(false);
            try
            {
                var bloqueio = await admin.Post($"{Url}/{dia.Id}/fechar", new FecharDia(null, (await admin.Obter<EstadoDiaDto>($"{Url}/estado")).Aberto!.RowVersion))
                    .Esperar(HttpStatusCode.UnprocessableEntity);
                Assert.Contains($"conta {aberta.Numero}", await bloqueio.Content.ReadAsStringAsync());
            }
            finally
            {
                await ContasAbertasAsync(true);
            }

            var versao = (await admin.Obter<EstadoDiaDto>($"{Url}/estado")).Aberto!.RowVersion;
            var r = await (await admin.Post($"{Url}/{dia.Id}/fechar", new FecharDia("Tudo conferido", versao)).Esperar(HttpStatusCode.OK)).Ler<ResumoDia>();
            Assert.Contains(r.PedidosAbertos!, p => p.Numero == aberta.Numero && p.Total == aberta.Total && p.Local == $"Mesa {c.Mesa2.Nome}");
            var documento = await admin.Obter<DocumentoImpressao>($"/api/relatorios/documento?tipo=FechoDia&diaId={dia.Id}");
            Assert.Contains(documento.Linhas, l => l.Faixa && l.Texto == "Contas transitadas para o dia seguinte");
            Assert.StartsWith("FD-", r.Numero);
            Assert.Equal((EstadoDia.Fechado, 1, 961m, 961m, -10m, 0), (r.Estado, r.Pedidos, r.VendasLiquidas, r.TotalRecebido, r.Diferenca, r.Pendencias.Count));
            Assert.Equal([961m, 0m], r.Caixas.Select(x => x.Recebido));
            Assert.Equal((-10m, 1200m), (r.Caixas[1].Diferenca!.Value, r.Caixas.Sum(x => x.Fundo)));
            var metodo = Assert.Single(r.Metodos);
            Assert.Equal(("Dinheiro", 961m), (metodo.Metodo, metodo.Total));
            var categoria = Assert.Single(r.Categorias);
            Assert.Equal((3m, 961m), (categoria.Quantidade, categoria.Total));
            Assert.Equal(["Frango grelhado", "Refrigerante"], categoria.Produtos.Select(p => p.Produto));

            // Fechado: não reabre, não volta a fechar, e os caixas deixam de abrir até ao novo dia.
            await admin.Post($"{Url}/{dia.Id}/fechar", new FecharDia(null, null)).Esperar(HttpStatusCode.UnprocessableEntity);
            await admin.Post(Url, new AbrirDia(estado.Hoje, null)).Esperar(HttpStatusCode.Conflict);
            await caixa.Post("/api/caixa/sessoes", new AbrirCaixa(terminal.Id, 0m, null)).Esperar(HttpStatusCode.UnprocessableEntity);
            var depois = await admin.Obter<EstadoDiaDto>($"{Url}/estado");
            Assert.Equal((null, r.Numero), (depois.Aberto, depois.UltimoFechado?.Numero));
            Assert.Equal(r.Numero, (await admin.Obter<ResumoDia>($"{Url}/{dia.Id}/resumo")).Numero); // o gravado no fecho

            var texto = TextoDocumento.De(await admin.Obter<DocumentoImpressao>($"/api/relatorios/documento?tipo=FechoDia&diaId={dia.Id}"));
            Assert.Contains("FECHO DO DIA", texto);
            Assert.Contains(r.Numero!, texto);
            Assert.Contains("Vendas líquidas | 961,00 MT", texto);
            Assert.Contains("Diferença de caixa | -10,00 MT", texto);
            Assert.Contains("Frango grelhado | 2 | 901,00", texto);
            Assert.Contains("Obs.: Tudo conferido", texto);

            await using var db = app.CriarContexto();
            var auditoria = await db.RegistosAuditoria
                .Where(a => a.Entidade == "DiaOperacao" && a.EntidadeId == dia.Id.ToString()).Select(a => a.Accao).ToListAsync();
            Assert.Equal(["caixa.dia-abrir", "caixa.dia-fechar"], auditoria.Order());
        }
        finally
        {
            await ExigirDiaAsync(false);
        }
    }

    /// <summary>
    /// O servidor esteve com a data adiantada e o dia foi aberto nessa data (aqui: daqui a 400 dias, gravado directamente). Sem caixas,
    /// o gestor anula-o: fica «Anulado», com o motivo, e deixa de ocupar a data e de impedir as datas seguintes.
    /// </summary>
    [Fact]
    public async Task Dia_aberto_com_a_data_errada_anula_se_sem_caixas_e_deixa_de_ocupar_a_data()
    {
        await ExigirDiaAsync(true);
        var criados = new List<int>();
        try
        {
            var (admin, _) = await app.ClienteAutenticadoAsync(PerfisPadrao.Administrador);
            var (caixa, _) = await app.ClienteAutenticadoAsync(PerfisPadrao.Caixa);
            var estado = await admin.Obter<EstadoDiaDto>($"{Url}/estado");
            Assert.InRange(estado.AgoraUtc, DateTime.UtcNow.AddMinutes(-2), DateTime.UtcNow.AddMinutes(2)); // hora do servidor para o aviso do ecrã
            var futura = estado.Hoje.AddDays(400);

            async Task<DiaOperacaoDto> AbertoNoFuturoAsync()
            {
                await using var db = app.CriarContexto();
                var dia = new Domain.Caixa.DiaOperacao { Data = futura, UtilizadorAberturaId = await db.Users.Select(u => u.Id).FirstAsync(), AbertoEmUtc = DateTime.UtcNow };
                db.DiasOperacao.Add(dia);
                await db.SaveChangesAsync();
                criados.Add(dia.Id);
                return (await admin.Obter<EstadoDiaDto>($"{Url}/estado")).Aberto!;
            }

            var dia = await AbertoNoFuturoAsync();
            Assert.Equal(futura, dia.Data);
            await caixa.Post($"{Url}/{dia.Id}/anular", new AnularDia("Relógio adiantado", dia.RowVersion)).Esperar(HttpStatusCode.Forbidden);
            await admin.Post($"{Url}/{dia.Id}/anular", new AnularDia("", dia.RowVersion)).Esperar(HttpStatusCode.BadRequest);

            // Com um caixa no dia não se anula (as vendas nunca ficam escondidas).
            var terminal = await (await admin.Post("/api/terminais", new GuardarTerminal(Unico("Anular "), null)).Esperar(HttpStatusCode.Created)).Ler<TerminalDto>();
            var sessao = await (await caixa.Post("/api/caixa/sessoes", new AbrirCaixa(terminal.Id, 0m, null)).Esperar(HttpStatusCode.Created)).Ler<SessaoCaixaDto>();
            var versao = (await admin.Obter<EstadoDiaDto>($"{Url}/estado")).Aberto!.RowVersion;
            Assert.Contains("caixas", await (await admin.Post($"{Url}/{dia.Id}/anular", new AnularDia("Relógio adiantado", versao))
                .Esperar(HttpStatusCode.UnprocessableEntity)).Content.ReadAsStringAsync());
            await using (var db = app.CriarContexto())
                await db.SessoesCaixa.Where(s => s.Id == sessao.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.DiaOperacaoId, (int?)null));

            versao = (await admin.Obter<EstadoDiaDto>($"{Url}/estado")).Aberto!.RowVersion;
            var anulado = await (await admin.Post($"{Url}/{dia.Id}/anular", new AnularDia(" Relógio do servidor adiantado ", versao)).Esperar(HttpStatusCode.OK)).Ler<DiaOperacaoDto>();
            Assert.Equal((EstadoDia.Anulado, "Relógio do servidor adiantado"), (anulado.Estado, anulado.MotivoAnulacao));
            Assert.NotNull(anulado.AnuladoPor);
            Assert.Null((await admin.Obter<EstadoDiaDto>($"{Url}/estado")).Aberto);
            await admin.Post($"{Url}/{dia.Id}/anular", new AnularDia("Outra vez", anulado.RowVersion)).Esperar(HttpStatusCode.UnprocessableEntity);
            await admin.Post($"{Url}/{dia.Id}/fechar", new FecharDia(null, anulado.RowVersion)).Esperar(HttpStatusCode.UnprocessableEntity);

            // A mesma data volta a poder ser aberta (o dia anulado não a ocupa).
            var outro = await AbertoNoFuturoAsync();
            await admin.Post($"{Url}/{outro.Id}/anular", new AnularDia("Teste", outro.RowVersion)).Esperar(HttpStatusCode.OK);
            await using (var db = app.CriarContexto())
                Assert.Equal(1, await db.RegistosAuditoria.CountAsync(a => a.Accao == "caixa.dia-anular" && a.EntidadeId == dia.Id.ToString()));
        }
        finally
        {
            await using (var db = app.CriarContexto())
                await db.DiasOperacao.Where(d => criados.Contains(d.Id)).ExecuteDeleteAsync();
            await ExigirDiaAsync(false);
        }
    }
}
