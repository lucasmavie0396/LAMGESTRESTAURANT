using LAMGestRestaurant.Domain.Caixa;
using LAMGestRestaurant.Domain.Sistema;
using LAMGestRestaurant.Infrastructure.Identidade;
using LAMGestRestaurant.Tests.Infraestrutura;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace LAMGestRestaurant.Tests;

[Collection(ColeccaoBaseDados.Nome)]
public class IntegridadeTests(AplicacaoTeste app)
{
    [Fact]
    public async Task Nao_permite_duas_sessoes_de_caixa_abertas_no_mesmo_terminal()
    {
        var utilizadorId = await CriarUtilizadorAsync();
        await using var db = app.CriarContexto();
        var terminal = new Terminal { Nome = $"T-{Guid.NewGuid():N}"[..20] };
        db.Terminais.Add(terminal);
        await db.SaveChangesAsync();

        db.SessoesCaixa.Add(new SessaoCaixa { TerminalId = terminal.Id, UtilizadorAberturaId = utilizadorId, AbertaEmUtc = DateTime.UtcNow, FundoInicial = 1000m });
        await db.SaveChangesAsync();

        await using var outra = app.CriarContexto();
        outra.SessoesCaixa.Add(new SessaoCaixa { TerminalId = terminal.Id, UtilizadorAberturaId = utilizadorId, AbertaEmUtc = DateTime.UtcNow, FundoInicial = 0m });
        await Assert.ThrowsAsync<DbUpdateException>(() => outra.SaveChangesAsync());
    }

    [Fact]
    public async Task Alteracao_concorrente_da_mesma_sessao_e_detectada()
    {
        var utilizadorId = await CriarUtilizadorAsync();
        int sessaoId;
        await using (var db = app.CriarContexto())
        {
            var terminal = new Terminal { Nome = $"T-{Guid.NewGuid():N}"[..20] };
            db.Terminais.Add(terminal);
            var sessao = new SessaoCaixa { Terminal = terminal, UtilizadorAberturaId = utilizadorId, AbertaEmUtc = DateTime.UtcNow };
            db.SessoesCaixa.Add(sessao);
            await db.SaveChangesAsync();
            sessaoId = sessao.Id;
        }

        await using var a = app.CriarContexto();
        await using var b = app.CriarContexto();
        var sa = await a.SessoesCaixa.SingleAsync(s => s.Id == sessaoId);
        var sb = await b.SessoesCaixa.SingleAsync(s => s.Id == sessaoId);

        sa.ObservacoesAbertura = "A";
        await a.SaveChangesAsync();

        sb.ObservacoesAbertura = "B";
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => b.SaveChangesAsync());
    }

    [Fact]
    public async Task Registo_de_auditoria_nao_pode_ser_alterado_nem_eliminado()
    {
        await using var db = app.CriarContexto();
        var registo = new RegistoAuditoria { Accao = "teste", DataUtc = DateTime.UtcNow };
        db.RegistosAuditoria.Add(registo);
        await db.SaveChangesAsync();

        registo.Accao = "adulterado";
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());

        db.ChangeTracker.Clear();
        db.RegistosAuditoria.Remove(registo);
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Pagamento_com_valor_recebido_inferior_e_recusado_pela_base_de_dados()
    {
        // Defesa em profundidade: a regra também é validada no serviço, mas a BD recusa valores incoerentes.
        await using var db = app.CriarContexto();
        var sql = "INSERT INTO Pagamentos (PedidoId, SessaoCaixaId, MetodoPagamentoId, Valor, ValorRecebido, Troco, Estado, DataUtc, UtilizadorId, IdempotencyKey, CriadoEmUtc) " +
                  "VALUES (1, 1, 1, 100, 50, 0, 'Valido', SYSUTCDATETIME(), 1, NEWID(), SYSUTCDATETIME())";
        var ex = await Assert.ThrowsAnyAsync<Exception>(() => db.Database.ExecuteSqlRawAsync(sql));
        Assert.Contains("CK_Pagamentos_Valores", ex.Message);
    }

    private Task<int> CriarUtilizadorAsync() => app.ComServicos(async s =>
    {
        var um = s.GetRequiredService<UserManager<Utilizador>>();
        var u = new Utilizador { UserName = $"u{Guid.NewGuid():N}"[..15], NomeCompleto = "Utilizador de teste", CriadoEmUtc = DateTime.UtcNow };
        var r = await um.CreateAsync(u, "teste1234");
        Assert.True(r.Succeeded, string.Join(" ", r.Errors.Select(e => e.Description)));
        return u.Id;
    });
}
