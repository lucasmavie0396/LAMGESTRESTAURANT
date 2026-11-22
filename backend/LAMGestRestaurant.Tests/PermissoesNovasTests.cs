using System.Security.Claims;
using LAMGestRestaurant.Application.Seguranca;
using LAMGestRestaurant.Domain.Sistema;
using LAMGestRestaurant.Infrastructure.Dados;
using LAMGestRestaurant.Infrastructure.Identidade;
using LAMGestRestaurant.Tests.Infraestrutura;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace LAMGestRestaurant.Tests;

/// <summary>Actualizações: as permissões novas chegam aos perfis padrão sem desfazer as escolhas do administrador.</summary>
[Collection(ColeccaoBaseDados.Nome)]
public class PermissoesNovasTests(AplicacaoTeste app)
{
    private Task<HashSet<string>> PermissoesAsync(string perfil) => app.ComServicos(async s =>
    {
        var rm = s.GetRequiredService<RoleManager<Perfil>>();
        var p = await rm.FindByNameAsync(perfil);
        return (await rm.GetClaimsAsync(p!)).Where(c => c.Type == Permissoes.ClaimType).Select(c => c.Value).ToHashSet();
    });

    private Task Tirar(string perfil, params string[] permissoes) => app.ComServicos(async s =>
    {
        var rm = s.GetRequiredService<RoleManager<Perfil>>();
        var p = await rm.FindByNameAsync(perfil);
        foreach (var permissao in permissoes)
            await rm.RemoveClaimAsync(p!, new Claim(Permissoes.ClaimType, permissao));
        return true;
    });

    private Task Dar(string perfil, params string[] permissoes) => app.ComServicos(async s =>
    {
        var rm = s.GetRequiredService<RoleManager<Perfil>>();
        var p = await rm.FindByNameAsync(perfil);
        var actuais = (await rm.GetClaimsAsync(p!)).Select(c => c.Value).ToHashSet();
        foreach (var permissao in permissoes.Where(x => !actuais.Contains(x)))
            await rm.AddClaimAsync(p!, new Claim(Permissoes.ClaimType, permissao));
        return true;
    });

    private Task DefinirRegisto(string? valor) => app.ComServicos(async s =>
    {
        var db = s.GetRequiredService<AppDbContext>();
        var r = await db.Configuracoes.FirstOrDefaultAsync(c => c.Chave == ChavesConfiguracao.PermissoesDistribuidas);
        if (valor is null && r is not null)
            db.Configuracoes.Remove(r);
        else if (valor is not null)
        {
            if (r is null)
                db.Configuracoes.Add(r = new Configuracao { Chave = ChavesConfiguracao.PermissoesDistribuidas });
            r.Valor = valor;
        }
        await db.SaveChangesAsync();
        return true;
    });

    private Task Semear() => app.ComServicos(async s =>
    {
        await s.GetRequiredService<SemeadorDados>().ExecutarAsync();
        return true;
    });

    [Fact]
    public async Task Base_antiga_recebe_as_permissoes_criadas_depois_sem_repor_as_que_o_administrador_tirou()
    {
        try
        {
            // Base de uma versão anterior: sem registo; o Gerente nunca recebeu «Abrir e fechar o dia» e o administrador
            // tirou-lhe «Ajustar stock» (de propósito).
            await DefinirRegisto(null);
            await Tirar(PerfisPadrao.Gerente, Permissoes.CaixaDia, Permissoes.StockAjustar);
            await Tirar(PerfisPadrao.Caixa, Permissoes.PosMesasOutros);

            await Semear();

            var gerente = await PermissoesAsync(PerfisPadrao.Gerente);
            Assert.Contains(Permissoes.CaixaDia, gerente);
            Assert.DoesNotContain(Permissoes.StockAjustar, gerente);
            Assert.DoesNotContain(Permissoes.ConfiguracoesGerir, gerente); // não é do Gerente por omissão
            Assert.Contains(Permissoes.PosMesasOutros, await PermissoesAsync(PerfisPadrao.Caixa));
            Assert.DoesNotContain(Permissoes.PosMesasOutros, await PermissoesAsync(PerfisPadrao.Garcom)); // o Garçom não a tem por omissão

            await using var db = app.CriarContexto();
            var registo = await db.Configuracoes.SingleAsync(c => c.Chave == ChavesConfiguracao.PermissoesDistribuidas);
            Assert.Equal(Permissoes.Todas.Count, registo.Valor.Split(',').Length);
            var auditoria = await db.RegistosAuditoria.Where(r => r.Accao == "perfis.permissoes-novas").OrderByDescending(r => r.Id).FirstAsync();
            Assert.Contains(Permissoes.CaixaDia, auditoria.Detalhes);

            // Correr outra vez (cada arranque) não volta a dar o que o administrador tirar entretanto.
            await Tirar(PerfisPadrao.Gerente, Permissoes.CaixaDia);
            await Semear();
            Assert.DoesNotContain(Permissoes.CaixaDia, await PermissoesAsync(PerfisPadrao.Gerente));
        }
        finally
        {
            await Dar(PerfisPadrao.Gerente, Permissoes.CaixaDia, Permissoes.StockAjustar);
        }
    }

    [Fact]
    public async Task Permissao_nova_de_uma_versao_futura_chega_aos_perfis_padrao_que_a_tem_por_omissao()
    {
        try
        {
            // Simula uma permissão criada nesta versão: ainda não está no registo nem nos perfis existentes.
            var todas = Permissoes.Todas.Keys.Where(p => p != Permissoes.RelatoriosVendas);
            await DefinirRegisto(string.Join(',', todas));
            await Tirar(PerfisPadrao.Gerente, Permissoes.RelatoriosVendas);

            await Semear();

            Assert.Contains(Permissoes.RelatoriosVendas, await PermissoesAsync(PerfisPadrao.Gerente));
            Assert.DoesNotContain(Permissoes.RelatoriosVendas, await PermissoesAsync(PerfisPadrao.Garcom));
        }
        finally
        {
            await Dar(PerfisPadrao.Gerente, Permissoes.RelatoriosVendas);
        }
    }
}
