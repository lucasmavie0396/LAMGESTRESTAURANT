using System.Net;
using System.Net.Http.Json;
using LAMGestRestaurant.Api.Controllers;
using LAMGestRestaurant.Application.Comum;
using LAMGestRestaurant.Application.Seguranca;
using LAMGestRestaurant.Tests.Infraestrutura;

namespace LAMGestRestaurant.Tests;

[Collection(ColeccaoBaseDados.Nome)]
public class UtilizadoresTests(AplicacaoTeste app)
{
    [Fact]
    public async Task Sem_permissao_nao_lista_utilizadores()
    {
        var (garcom, _) = await app.ClienteAutenticadoAsync(PerfisPadrao.Garcom);
        Assert.Equal(HttpStatusCode.Forbidden, (await garcom.GetAsync("/api/utilizadores")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await garcom.GetAsync("/api/perfis")).StatusCode);
    }

    [Fact]
    public async Task Novo_utilizador_tem_de_alterar_palavra_passe_antes_de_usar_a_api()
    {
        var (admin, _) = await app.ClienteAutenticadoAsync(PerfisPadrao.Administrador);
        var nome = $"n{Guid.NewGuid():N}"[..12];

        var criar = await admin.PostAsJsonAsync("/api/utilizadores",
            new CriarUtilizador(nome, "Novo Gerente", "temp1234", [PerfisPadrao.Gerente]));
        Assert.Equal(HttpStatusCode.Created, criar.StatusCode);
        var criado = await criar.Content.ReadFromJsonAsync<UtilizadorResumo>();
        Assert.True(criado!.DeveAlterarPalavraPasse);

        var novo = app.CreateClient();
        var login = await novo.PostAsJsonAsync("/api/auth/login", new PedidoLogin(nome, "temp1234"));
        Assert.True((await login.Content.ReadFromJsonAsync<SessaoUtilizador>())!.DeveAlterarPalavraPasse);

        // Bloqueado fora de /api/auth até alterar.
        Assert.Equal(HttpStatusCode.Forbidden, (await novo.GetAsync("/api/utilizadores")).StatusCode);

        var alterar = await novo.PostAsJsonAsync("/api/auth/alterar-palavra-passe", new PedidoAlterarPalavraPasse("temp1234", "definitiva99"));
        Assert.Equal(HttpStatusCode.NoContent, alterar.StatusCode);
        var me = await novo.GetFromJsonAsync<SessaoUtilizador>("/api/auth/me");
        Assert.False(me!.DeveAlterarPalavraPasse);
    }

    [Fact]
    public async Task Dados_invalidos_devolvem_400_com_mensagens_em_portugues()
    {
        var (admin, _) = await app.ClienteAutenticadoAsync(PerfisPadrao.Administrador);
        var resposta = await admin.PostAsJsonAsync("/api/utilizadores", new CriarUtilizador("com espaço", "", "x", []));

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
        var texto = await resposta.Content.ReadAsStringAsync();
        Assert.Contains("Dados inválidos", texto);
        Assert.Contains("Seleccione pelo menos um perfil", texto);
    }

    [Fact]
    public async Task Palavra_passe_fraca_e_recusada_com_422()
    {
        var (admin, _) = await app.ClienteAutenticadoAsync(PerfisPadrao.Administrador);
        var resposta = await admin.PostAsJsonAsync("/api/utilizadores",
            new CriarUtilizador($"f{Guid.NewGuid():N}"[..12], "Fraco", "abc", [PerfisPadrao.Caixa]));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resposta.StatusCode);
        Assert.Contains("pelo menos 8 caracteres", await resposta.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Administrador_nao_se_pode_desactivar_a_si_proprio()
    {
        var (admin, _) = await app.ClienteAutenticadoAsync(PerfisPadrao.Administrador);
        var eu = await admin.GetFromJsonAsync<SessaoUtilizador>("/api/auth/me");

        var resposta = await admin.PostAsync($"/api/utilizadores/{eu!.Id}/desactivar", null);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resposta.StatusCode);
    }

    [Fact]
    public async Task Utilizador_desactivado_perde_a_sessao()
    {
        var (admin, _) = await app.ClienteAutenticadoAsync(PerfisPadrao.Administrador);
        var (caixa, _) = await app.ClienteAutenticadoAsync(PerfisPadrao.Caixa);
        var id = (await caixa.GetFromJsonAsync<SessaoUtilizador>("/api/auth/me"))!.Id;

        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/api/utilizadores/{id}/desactivar", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await caixa.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Reposicao_de_palavra_passe_obriga_a_alterar_e_desbloqueia()
    {
        var (admin, _) = await app.ClienteAutenticadoAsync(PerfisPadrao.Administrador);
        var nome = await app.CriarUtilizadorAsync(PerfisPadrao.Caixa);
        var anonimo = app.CreateClient();
        for (var i = 0; i < 5; i++)
            await anonimo.PostAsJsonAsync("/api/auth/login", new PedidoLogin(nome, "errada000"));

        var lista = await admin.GetFromJsonAsync<Pagina<UtilizadorResumo>>($"/api/utilizadores?pesquisa={nome}");
        var alvo = Assert.Single(lista!.Itens);
        Assert.NotNull(alvo.BloqueadoAteUtc);

        var repor = await admin.PostAsJsonAsync($"/api/utilizadores/{alvo.Id}/repor-palavra-passe", new ReporPalavraPasse("reposta123"));
        Assert.Equal(HttpStatusCode.NoContent, repor.StatusCode);

        var login = await anonimo.PostAsJsonAsync("/api/auth/login", new PedidoLogin(nome, "reposta123"));
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.True((await login.Content.ReadFromJsonAsync<SessaoUtilizador>())!.DeveAlterarPalavraPasse);
    }

    [Fact]
    public async Task Perfil_administrador_mantem_todas_as_permissoes_e_perfis_de_sistema_nao_se_eliminam()
    {
        var (admin, _) = await app.ClienteAutenticadoAsync(PerfisPadrao.Administrador);
        var perfis = await admin.GetFromJsonAsync<List<PerfilResumo>>("/api/perfis");
        var adminPerfil = perfis!.Single(p => p.Nome == PerfisPadrao.Administrador);
        Assert.Equal(Permissoes.Todas.Count, adminPerfil.Permissoes.Count);

        var reduzir = await admin.PutAsJsonAsync($"/api/perfis/{adminPerfil.Id}",
            new GuardarPerfil(PerfisPadrao.Administrador, null, [Permissoes.DashboardVer]));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, reduzir.StatusCode);

        var eliminar = await admin.DeleteAsync($"/api/perfis/{adminPerfil.Id}");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, eliminar.StatusCode);
    }

    [Fact]
    public async Task Perfil_personalizado_pode_ser_criado_editado_e_eliminado()
    {
        var (admin, _) = await app.ClienteAutenticadoAsync(PerfisPadrao.Administrador);
        var nome = $"Perfil {Guid.NewGuid():N}"[..20];

        var criar = await admin.PostAsJsonAsync("/api/perfis", new GuardarPerfil(nome, "Teste", [Permissoes.PosUsar]));
        Assert.Equal(HttpStatusCode.Created, criar.StatusCode);
        var perfil = await criar.Content.ReadFromJsonAsync<PerfilResumo>();

        var editar = await admin.PutAsJsonAsync($"/api/perfis/{perfil!.Id}",
            new GuardarPerfil(nome, "Teste", [Permissoes.PosUsar, Permissoes.MesasVer]));
        var editado = await editar.Content.ReadFromJsonAsync<PerfilResumo>();
        Assert.Equal([Permissoes.MesasVer, Permissoes.PosUsar], editado!.Permissoes);

        var desconhecida = await admin.PutAsJsonAsync($"/api/perfis/{perfil.Id}", new GuardarPerfil(nome, null, ["inventada.x"]));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, desconhecida.StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/perfis/{perfil.Id}")).StatusCode);
    }
}
