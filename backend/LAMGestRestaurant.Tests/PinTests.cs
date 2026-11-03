using System.Net;
using System.Net.Http.Json;
using LAMGestRestaurant.Api.Controllers;
using LAMGestRestaurant.Application.Seguranca;
using LAMGestRestaurant.Infrastructure.Identidade;
using LAMGestRestaurant.Tests.Infraestrutura;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using static LAMGestRestaurant.Tests.Infraestrutura.ExtensoesHttp;

namespace LAMGestRestaurant.Tests;

/// <summary>Ecrã de entrada táctil: código (PIN) por utilizador, bloqueio e quem fica de fora.</summary>
[Collection(ColeccaoBaseDados.Nome)]
public class PinTests(AplicacaoTeste app)
{
    private async Task<(int Id, string Nome)> UtilizadorAsync(string perfil)
    {
        var nome = await app.CriarUtilizadorAsync(perfil);
        var id = await app.ComServicos(async s => (await s.GetRequiredService<UserManager<Utilizador>>().FindByNameAsync(nome))!.Id);
        return (id, nome);
    }

    private async Task<HttpClient> AdministradorAsync() => (await app.ClienteAutenticadoAsync(PerfisPadrao.Administrador)).Cliente;

    private static Task<HttpResponseMessage> EntrarComPin(HttpClient c, int id, string pin) =>
        c.PostAsJsonAsync("/api/auth/login-pin", new LoginPin(id, pin));

    [Fact]
    public async Task Garcom_com_codigo_aparece_no_ecra_tactil_e_entra_com_o_codigo()
    {
        var admin = await AdministradorAsync();
        var (id, nome) = await UtilizadorAsync(PerfisPadrao.Garcom);
        await admin.Post($"/api/utilizadores/{id}/pin", new DefinirPin("2580", true)).Esperar(HttpStatusCode.NoContent);

        var anonimo = app.CreateClient();
        var lista = await anonimo.Obter<List<UtilizadorTactil>>("/api/auth/tactil");
        var botao = Assert.Single(lista, u => u.Id == id);
        Assert.Equal("Teste", botao.Nome); // nome completo, não o nome de utilizador
        Assert.DoesNotContain(nome, System.Text.Json.JsonSerializer.Serialize(lista));

        var resposta = await EntrarComPin(anonimo, id, "2580").Esperar(HttpStatusCode.OK);
        var sessao = (await resposta.Content.ReadFromJsonAsync<SessaoUtilizador>())!;
        Assert.Contains(PerfisPadrao.Garcom, sessao.Perfis);
        await anonimo.GetAsync("/api/auth/me").Esperar(HttpStatusCode.OK);

        // O resumo do utilizador mostra que tem código.
        var resumo = await admin.Obter<UtilizadorResumo>($"/api/utilizadores/{id}");
        Assert.True(resumo.TemPin && resumo.EcraTactil);
    }

    [Fact]
    public async Task Codigo_errado_cinco_vezes_bloqueia_a_conta()
    {
        var admin = await AdministradorAsync();
        var (id, _) = await UtilizadorAsync(PerfisPadrao.Caixa);
        await admin.Post($"/api/utilizadores/{id}/pin", new DefinirPin("90817", true)).Esperar(HttpStatusCode.NoContent);
        var c = app.CreateClient();

        for (var i = 0; i < 4; i++)
            await EntrarComPin(c, id, "11223").Esperar(HttpStatusCode.Unauthorized);
        await EntrarComPin(c, id, "11223").Esperar((HttpStatusCode)423);
        // Bloqueado: nem o código certo entra.
        await EntrarComPin(c, id, "90817").Esperar((HttpStatusCode)423);

        // O administrador desbloqueia.
        await admin.Post($"/api/utilizadores/{id}/desbloquear", new { }).Esperar(HttpStatusCode.NoContent);
        await EntrarComPin(c, id, "90817").Esperar(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("12")]
    [InlineData("1234567")]
    [InlineData("12a4")]
    [InlineData("1111")]
    [InlineData("1234")]
    [InlineData("98765")]
    public async Task Codigos_fracos_ou_invalidos_sao_recusados(string pin)
    {
        var admin = await AdministradorAsync();
        var (id, _) = await UtilizadorAsync(PerfisPadrao.Garcom);
        await admin.Post($"/api/utilizadores/{id}/pin", new DefinirPin(pin, true)).Esperar(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Administrador_entra_so_com_palavra_passe_e_nao_aparece_no_ecra_tactil()
    {
        var admin = await AdministradorAsync();
        var (outroAdmin, _) = await UtilizadorAsync(PerfisPadrao.Administrador);
        await admin.Post($"/api/utilizadores/{outroAdmin}/pin", new DefinirPin("2580", true)).Esperar(HttpStatusCode.UnprocessableEntity);

        // Um garçom com código que passa a administrador deixa de poder usar o código.
        var (id, _) = await UtilizadorAsync(PerfisPadrao.Garcom);
        await admin.Post($"/api/utilizadores/{id}/pin", new DefinirPin("3691", true)).Esperar(HttpStatusCode.NoContent);
        await admin.Put($"/api/utilizadores/{id}", new EditarUtilizador("Teste", [PerfisPadrao.Garcom, PerfisPadrao.Administrador])).Esperar(HttpStatusCode.OK);
        var anonimo = app.CreateClient();
        Assert.DoesNotContain(await anonimo.Obter<List<UtilizadorTactil>>("/api/auth/tactil"), u => u.Id == id);
        await EntrarComPin(anonimo, id, "3691").Esperar(HttpStatusCode.Unauthorized);
        await anonimo.GetAsync("/api/auth/me").Esperar(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Retirar_o_codigo_tira_do_ecra_e_o_proprio_muda_o_codigo_com_a_palavra_passe()
    {
        var admin = await AdministradorAsync();
        var (id, nome) = await UtilizadorAsync(PerfisPadrao.Cozinha);
        await admin.Post($"/api/utilizadores/{id}/pin", new DefinirPin("4826", true)).Esperar(HttpStatusCode.NoContent);

        // O próprio muda o código: precisa da palavra-passe.
        var proprio = app.CreateClient();
        await proprio.PostAsJsonAsync("/api/auth/login", new PedidoLogin(nome, AplicacaoTeste.PalavraPasseTeste)).Esperar(HttpStatusCode.OK);
        await proprio.Post("/api/auth/alterar-pin", new AlterarMeuPin("errada", "7351")).Esperar(HttpStatusCode.UnprocessableEntity);
        await proprio.Post("/api/auth/alterar-pin", new AlterarMeuPin(AplicacaoTeste.PalavraPasseTeste, "7351")).Esperar(HttpStatusCode.NoContent);
        var c = app.CreateClient();
        await EntrarComPin(c, id, "4826").Esperar(HttpStatusCode.Unauthorized);
        await EntrarComPin(c, id, "7351").Esperar(HttpStatusCode.OK);

        // Retirado: sai do ecrã e o código deixa de servir.
        await admin.Post($"/api/utilizadores/{id}/pin", new DefinirPin(null, false, Retirar: true)).Esperar(HttpStatusCode.NoContent);
        var anonimo = app.CreateClient();
        Assert.DoesNotContain(await anonimo.Obter<List<UtilizadorTactil>>("/api/auth/tactil"), u => u.Id == id);
        await EntrarComPin(anonimo, id, "7351").Esperar(HttpStatusCode.Unauthorized);
    }
}
