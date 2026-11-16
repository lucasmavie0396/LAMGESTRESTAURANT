using System.Net;
using LAMGestRestaurant.Application.Seguranca;
using LAMGestRestaurant.Application.Sistema;
using LAMGestRestaurant.Tests.Infraestrutura;
using Microsoft.EntityFrameworkCore;
using static LAMGestRestaurant.Tests.Infraestrutura.ExtensoesHttp;

namespace LAMGestRestaurant.Tests;

/// <summary>
/// Lojas do mesmo dono ligadas pela VPN: identidade pública de cada servidor, lista para o seletor, acrescentar com confirmação
/// (nunca a própria loja nem repetidas), importar a lista de outra loja e retirar. Os outros servidores são simulados.
/// </summary>
[Collection(ColeccaoBaseDados.Nome)]
public class LojasTests(AplicacaoTeste app)
{
    private static ListaLojas Servidor(string id, string nome, params LojaDto[] outras) => new(new IdentidadeLoja(id, nome, "400000099", "#16a34a"), outras);

    [Fact]
    public async Task Acrescentar_importar_e_retirar_lojas_com_confirmacao_do_servidor()
    {
        var anonimo = app.CreateClient();
        var eu = await anonimo.Obter<IdentidadeLoja>("/api/loja");
        Assert.Matches("^[0-9a-f]{32}$", eu.Identificador);
        Assert.Empty((await anonimo.Obter<ListaLojas>("/api/lojas")).Outras);

        const string baixa = "http://100.70.1.11:8080", polana = "http://100.70.1.12:8080", matola = "http://100.70.1.13:8080";
        app.Lojas.Servidores[baixa] = Servidor("loja-baixa", "Loja Baixa (teste)");
        app.Lojas.Servidores[polana] = Servidor("loja-polana", "Loja Polana (teste)",
            new LojaDto(1, "loja-baixa", "Loja Baixa (teste)", null, baixa, null),
            new LojaDto(2, "loja-matola", "Loja Matola (teste)", null, matola, "#dc2626"),
            new LojaDto(3, eu.Identificador, eu.Nome, null, "http://100.70.1.10:8080", null));
        app.Lojas.Servidores["http://100.70.1.10:8080"] = new ListaLojas(eu, []);

        var (admin, _) = await app.ClienteAutenticadoAsync(PerfisPadrao.Administrador);
        try
        {
            await (await app.ClienteComPermissoesAsync(Permissoes.LojasMudar)).Post("/api/lojas", new NovaLoja(baixa)).Esperar(HttpStatusCode.Forbidden);
            var ligada = await (await admin.Post("/api/lojas", new NovaLoja("100.70.1.11:8080/")).Esperar(HttpStatusCode.OK)).Ler<LojaDto>();
            Assert.Equal(("loja-baixa", "Loja Baixa (teste)", baixa, "400000099"), (ligada.Identificador, ligada.Nome, ligada.Endereco, ligada.Nuit));
            await admin.Post("/api/lojas", new NovaLoja(baixa)).Esperar(HttpStatusCode.Conflict);
            var propria = await admin.Post("/api/lojas", new NovaLoja("http://100.70.1.10:8080")).Esperar(HttpStatusCode.UnprocessableEntity);
            Assert.Contains("desta própria loja", await propria.Content.ReadAsStringAsync());
            var semVpn = await admin.Post("/api/lojas", new NovaLoja("http://100.70.1.99:8080")).Esperar(HttpStatusCode.UnprocessableEntity);
            Assert.Contains("VPN", await semVpn.Content.ReadAsStringAsync());
            await admin.Post("/api/lojas", new NovaLoja("http://100.70.1.11:8080/pos")).Esperar(HttpStatusCode.UnprocessableEntity);

            // Importar a lista da Polana: entra a Polana e a Matola; a Baixa já existia e esta loja nunca entra.
            var lista = await (await admin.Post("/api/lojas/importar", new NovaLoja(polana)).Esperar(HttpStatusCode.OK)).Ler<ListaLojas>();
            Assert.Equal(["Loja Baixa (teste)", "Loja Matola (teste)", "Loja Polana (teste)"], lista.Outras.Select(l => l.Nome));
            Assert.DoesNotContain(lista.Outras, l => l.Identificador == eu.Identificador);
            Assert.Equal(3, (await anonimo.Obter<ListaLojas>("/api/lojas")).Outras.Count); // o seletor do ecrã de entrada

            await admin.DeleteAsync($"/api/lojas/{ligada.Id}").Esperar(HttpStatusCode.NoContent);
            await using var db = app.CriarContexto();
            Assert.True(await db.RegistosAuditoria.AnyAsync(r => r.Accao == "loja.retirar" && r.EntidadeId == ligada.Id.ToString()));
        }
        finally
        {
            foreach (var l in (await anonimo.Obter<ListaLojas>("/api/lojas")).Outras)
                await admin.DeleteAsync($"/api/lojas/{l.Id}");
        }
    }
}
