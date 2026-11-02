using System.Net;
using System.Net.Http.Json;
using LAMGestRestaurant.Api.Controllers;
using LAMGestRestaurant.Application.Seguranca;
using LAMGestRestaurant.Infrastructure.Dados;
using LAMGestRestaurant.Infrastructure.Identidade;
using LAMGestRestaurant.Tests.Infraestrutura;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace LAMGestRestaurant.Tests;

[Collection(ColeccaoBaseDados.Nome)]
public class AutenticacaoTests(AplicacaoTeste app)
{
    private const string PalavraPasse = "segredo123";

    [Fact]
    public async Task Saude_responde_sem_sessao()
    {
        var resposta = await app.CreateClient().GetAsync("/api/saude");
        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
    }

    [Fact]
    public async Task Endpoint_protegido_sem_sessao_devolve_401()
    {
        var resposta = await app.CreateClient().GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, resposta.StatusCode);
    }

    [Fact]
    public async Task Login_valido_devolve_permissoes_do_perfil_e_mantem_sessao()
    {
        var nome = await CriarUtilizadorAsync(PerfisPadrao.Cozinha);
        var cliente = app.CreateClient();

        var login = await cliente.PostAsJsonAsync("/api/auth/login", new PedidoLogin(nome, PalavraPasse));
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var sessao = await login.Content.ReadFromJsonAsync<SessaoUtilizador>();
        Assert.Contains(PerfisPadrao.Cozinha, sessao!.Perfis);
        Assert.Contains(Permissoes.CozinhaActualizar, sessao.Permissoes);
        Assert.DoesNotContain(Permissoes.PagamentosRegistar, sessao.Permissoes);

        var me = await cliente.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);

        var logout = await cliente.PostAsync("/api/auth/logout", null);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await cliente.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Palavra_passe_errada_devolve_401_e_fica_auditada()
    {
        var nome = await CriarUtilizadorAsync(PerfisPadrao.Garcom);

        var resposta = await app.CreateClient().PostAsJsonAsync("/api/auth/login", new PedidoLogin(nome, "errada000"));

        Assert.Equal(HttpStatusCode.Unauthorized, resposta.StatusCode);
        await using var db = app.CriarContexto();
        var registos = await db.RegistosAuditoria.Where(r => r.Accao == "login.falhado" && r.Detalhes!.Contains(nome)).CountAsync();
        Assert.Equal(1, registos);
    }

    [Fact]
    public async Task Conta_desactivada_nao_entra()
    {
        var nome = await CriarUtilizadorAsync(PerfisPadrao.Caixa, activo: false);
        var resposta = await app.CreateClient().PostAsJsonAsync("/api/auth/login", new PedidoLogin(nome, PalavraPasse));
        Assert.Equal(HttpStatusCode.Unauthorized, resposta.StatusCode);
    }

    [Fact]
    public async Task Cinco_tentativas_falhadas_bloqueiam_a_conta()
    {
        var nome = await CriarUtilizadorAsync(PerfisPadrao.Caixa);
        var cliente = app.CreateClient();

        for (var i = 0; i < 5; i++)
            await cliente.PostAsJsonAsync("/api/auth/login", new PedidoLogin(nome, "errada000"));
        var comCertaMasBloqueada = await cliente.PostAsJsonAsync("/api/auth/login", new PedidoLogin(nome, PalavraPasse));

        Assert.Equal(HttpStatusCode.Locked, comCertaMasBloqueada.StatusCode);
    }

    [Fact]
    public async Task Alterar_palavra_passe_exige_a_actual_correcta()
    {
        var nome = await CriarUtilizadorAsync(PerfisPadrao.Garcom);
        var cliente = app.CreateClient();
        await cliente.PostAsJsonAsync("/api/auth/login", new PedidoLogin(nome, PalavraPasse));

        var errada = await cliente.PostAsJsonAsync("/api/auth/alterar-palavra-passe", new PedidoAlterarPalavraPasse("errada000", "novaPass123"));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, errada.StatusCode);

        var certa = await cliente.PostAsJsonAsync("/api/auth/alterar-palavra-passe", new PedidoAlterarPalavraPasse(PalavraPasse, "novaPass123"));
        Assert.Equal(HttpStatusCode.NoContent, certa.StatusCode);

        var novoLogin = await app.CreateClient().PostAsJsonAsync("/api/auth/login", new PedidoLogin(nome, "novaPass123"));
        Assert.Equal(HttpStatusCode.OK, novoLogin.StatusCode);
    }

    private Task<string> CriarUtilizadorAsync(string perfil, bool activo = true) => app.ComServicos(async s =>
    {
        await s.GetRequiredService<SemeadorDados>().ExecutarAsync();
        var um = s.GetRequiredService<UserManager<Utilizador>>();
        var u = new Utilizador { UserName = $"t{Guid.NewGuid():N}"[..15], NomeCompleto = "Teste", Activo = activo, CriadoEmUtc = DateTime.UtcNow };
        Assert.True((await um.CreateAsync(u, PalavraPasse)).Succeeded);
        Assert.True((await um.AddToRoleAsync(u, perfil)).Succeeded);
        return u.UserName!;
    });
}
