using System.Net;
using LAMGestRestaurant.Application.Seguranca;
using LAMGestRestaurant.Domain.Sistema;
using LAMGestRestaurant.Infrastructure.Dados;
using LAMGestRestaurant.Infrastructure.Sistema;
using LAMGestRestaurant.Tests.Infraestrutura;
using Microsoft.EntityFrameworkCore;

namespace LAMGestRestaurant.Tests;

/// <summary>Limpeza de dados técnicos: só o que já não serve, com mínimos, permissão e auditoria; exportações na auditoria.</summary>
[Collection(ColeccaoBaseDados.Nome)]
public class LimpezaTests(AplicacaoTeste app)
{
    [Fact]
    public async Task Apaga_so_os_trabalhos_de_impressao_concluidos_ha_mais_dos_dias_pedidos()
    {
        var c = await CenarioVenda.CriarAsync(app);
        var marca = Guid.NewGuid().ToString("N")[..10];
        await app.ComServicos(async s =>
        {
            var db = s.GetRequiredService<AppDbContext>();
            var terminal = await db.Terminais.Select(t => t.Id).FirstAsync();
            TrabalhoImpressaoPosto Trabalho(string sufixo, DateTime? concluido) => new()
            {
                TerminalId = terminal, ImpressoraId = 1, NomeWindows = "POS-80", LarguraPapelMm = 80, Titulo = $"{marca}-{sufixo}",
                Documento = new string('x', 5000), CriadoEmUtc = DateTime.UtcNow.AddDays(-60), ConcluidoEmUtc = concluido,
            };
            db.TrabalhosImpressaoPosto.AddRange(Trabalho("antigo", DateTime.UtcNow.AddDays(-45)), Trabalho("recente", DateTime.UtcNow.AddDays(-2)),
                Trabalho("pendente", null));
            await db.SaveChangesAsync();
            return true;
        });

        var estado = await c.Gerente.Obter<EstadoLimpeza>("/api/sistema/limpeza?diasTrabalhos=30");
        var trabalhos = Assert.Single(estado.Itens, i => i.Tipo == TipoLimpeza.TrabalhosImpressao);
        Assert.True(trabalhos.Quantidade >= 1 && trabalhos.Mb > 0);
        Assert.Contains(estado.Itens, i => i.Tipo == TipoLimpeza.RegistoTransaccoes);
        Assert.True(estado.BaseMb > 0);

        await c.Gerente.Post("/api/sistema/limpeza", new PedidoLimpeza(TipoLimpeza.TrabalhosImpressao, 3)).Esperar(HttpStatusCode.UnprocessableEntity); // mínimo 7 dias
        await c.Gerente.Post("/api/sistema/limpeza", new PedidoLimpeza(TipoLimpeza.Logs, 1)).Esperar(HttpStatusCode.UnprocessableEntity);
        await c.Gerente.Post("/api/sistema/limpeza", new PedidoLimpeza(TipoLimpeza.Copias, 2)).Esperar(HttpStatusCode.UnprocessableEntity); // ficam sempre 7
        var r = await (await c.Gerente.Post("/api/sistema/limpeza", new PedidoLimpeza(TipoLimpeza.TrabalhosImpressao, 30)).Esperar(HttpStatusCode.OK)).Ler<ResultadoLimpeza>();
        Assert.True(r.Apagados >= 1);

        var restam = await app.ComServicos(async s => await s.GetRequiredService<AppDbContext>().TrabalhosImpressaoPosto
            .Where(t => t.Titulo.StartsWith(marca)).Select(t => t.Titulo).OrderBy(t => t).ToListAsync());
        Assert.Equal([$"{marca}-pendente", $"{marca}-recente"], restam);
        Assert.True(await app.ComServicos(async s => await s.GetRequiredService<AppDbContext>().RegistosAuditoria.AnyAsync(a => a.Accao == "sistema.limpeza")));

        // Só quem gere as configurações.
        var (caixa, _) = await app.ClienteAutenticadoAsync(PerfisPadrao.Caixa);
        Assert.Equal(HttpStatusCode.Forbidden, (await caixa.GetAsync("/api/sistema/limpeza")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await caixa.Post("/api/sistema/limpeza", new PedidoLimpeza(TipoLimpeza.TrabalhosImpressao, 30))).StatusCode);
    }

    [Fact]
    public async Task Exportacao_de_lista_fica_na_auditoria()
    {
        var (caixa, _) = await app.ClienteAutenticadoAsync(PerfisPadrao.Caixa);
        var lista = "Clientes " + Guid.NewGuid().ToString("N")[..6];
        await caixa.Post("/api/exportacoes", new { lista, formato = "xlsx", linhas = 12, filtros = "Activos" }).Esperar(HttpStatusCode.NoContent);
        await caixa.Post("/api/exportacoes", new { lista, formato = "exe", linhas = 1 }).Esperar(HttpStatusCode.BadRequest);
        Assert.True(await app.ComServicos(async s => await s.GetRequiredService<AppDbContext>().RegistosAuditoria
            .AnyAsync(a => a.Accao == "lista.exportar" && a.Detalhes != null && a.Detalhes.Contains(lista))));
    }
}
