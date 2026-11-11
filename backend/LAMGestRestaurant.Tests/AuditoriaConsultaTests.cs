using System.Net;
using LAMGestRestaurant.Application.Clientes;
using LAMGestRestaurant.Application.Comum;
using LAMGestRestaurant.Application.Seguranca;
using LAMGestRestaurant.Application.Sistema;
using LAMGestRestaurant.Domain.Comum;
using LAMGestRestaurant.Tests.Infraestrutura;
using static LAMGestRestaurant.Tests.Infraestrutura.ExtensoesHttp;

namespace LAMGestRestaurant.Tests;

/// <summary>Menu Auditoria: consulta com filtros, só para quem tem auditoria.ver, e sem forma de alterar os registos.</summary>
[Collection(ColeccaoBaseDados.Nome)]
public class AuditoriaConsultaTests(AplicacaoTeste app)
{
    private const string Url = "/api/auditoria";

    private static string Hoje() =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("Africa/Maputo"))).ToString("yyyy-MM-dd");

    [Fact]
    public async Task Consulta_filtra_por_modulo_registo_utilizador_e_texto_e_so_quem_tem_permissao_ve()
    {
        var (gerente, nome) = await app.ClienteAutenticadoAsync(PerfisPadrao.Administrador);
        var cliente = await (await gerente.Post("/api/clientes", new GuardarCliente(TipoCliente.Particular, Unico("Auditado "), null, null, null, null, null, null, null))
            .Esperar(HttpStatusCode.OK)).Ler<ClienteDto>();
        await gerente.Put($"/api/clientes/{cliente.Id}", new GuardarCliente(TipoCliente.Particular, cliente.Nome + " (editado)", null, null, null, null, null, null, cliente.RowVersion))
            .Esperar(HttpStatusCode.OK);

        var opcoes = await gerente.Obter<OpcoesAuditoria>($"{Url}/opcoes");
        Assert.Contains("cliente", opcoes.Modulos);
        Assert.Contains("cliente.criar", opcoes.Accoes);
        var eu = Assert.Single(opcoes.Utilizadores, u => u.Nome == nome);

        // Por registo afectado: as duas operações sobre este cliente, a mais recente primeiro.
        var doCliente = await gerente.Obter<Pagina<RegistoAuditoriaDto>>($"{Url}?entidade=Cliente&entidadeId={cliente.Id}");
        Assert.Equal(["cliente.editar", "cliente.criar"], doCliente.Itens.Select(r => r.Accao));
        Assert.All(doCliente.Itens, r => Assert.Equal((eu.Id, nome), (r.UtilizadorId!.Value, r.Utilizador)));
        Assert.Contains(cliente.Nome, doCliente.Itens[1].Detalhes);

        // Por módulo, utilizador, dia e texto (procura também nos detalhes).
        var porModulo = await gerente.Obter<Pagina<RegistoAuditoriaDto>>($"{Url}?modulo=cliente&utilizadorId={eu.Id}&desde={Hoje()}&ate={Hoje()}");
        Assert.Equal(2, porModulo.Total);
        var porTexto = await gerente.Obter<Pagina<RegistoAuditoriaDto>>($"{Url}?pesquisa={Uri.EscapeDataString(cliente.Nome)}");
        Assert.Contains(porTexto.Itens, r => r.Accao == "cliente.criar" && r.EntidadeId == cliente.Id.ToString());
        Assert.Equal(0, (await gerente.Obter<Pagina<RegistoAuditoriaDto>>($"{Url}?modulo=cliente&utilizadorId={eu.Id}&desde=2020-01-01&ate=2020-01-02")).Total);
        await gerente.GetAsync($"{Url}?desde={Hoje()}&ate=2020-01-01").Esperar(HttpStatusCode.UnprocessableEntity);
        // Várias acções de uma vez (atalhos do ecrã): só as indicadas.
        var varias = await gerente.Obter<Pagina<RegistoAuditoriaDto>>($"{Url}?entidade=Cliente&entidadeId={cliente.Id}&accoes=cliente.editar&accoes=pedido.cancelar");
        Assert.Equal("cliente.editar", Assert.Single(varias.Itens).Accao);
        Assert.Equal(2, (await gerente.Obter<Pagina<RegistoAuditoriaDto>>($"{Url}?entidade=Cliente&entidadeId={cliente.Id}&accoes=cliente.editar&accoes=cliente.criar")).Total);
        var pagina = await gerente.Obter<Pagina<RegistoAuditoriaDto>>($"{Url}?tamanho=1&pagina=2");
        Assert.Single(pagina.Itens);

        // Só com auditoria.ver; e não há como alterar ou apagar.
        await (await app.ClienteComPermissoesAsync(Permissoes.ClientesVer)).GetAsync(Url).Esperar(HttpStatusCode.Forbidden);
        var auditor = await app.ClienteComPermissoesAsync(Permissoes.AuditoriaVer);
        await auditor.GetAsync(Url).Esperar(HttpStatusCode.OK);
        var id = doCliente.Itens[0].Id;
        Assert.Contains((await auditor.DeleteAsync($"{Url}/{id}")).StatusCode, new[] { HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed });
        Assert.Contains((await auditor.PutAsync($"{Url}/{id}", null)).StatusCode, new[] { HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed });
    }
}
