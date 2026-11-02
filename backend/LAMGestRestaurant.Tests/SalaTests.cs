using System.Net;
using LAMGestRestaurant.Application.Sala;
using LAMGestRestaurant.Application.Seguranca;
using LAMGestRestaurant.Domain.Comum;
using LAMGestRestaurant.Tests.Infraestrutura;
using static LAMGestRestaurant.Tests.Infraestrutura.ExtensoesHttp;

namespace LAMGestRestaurant.Tests;

[Collection(ColeccaoBaseDados.Nome)]
public class SalaTests(AplicacaoTeste app)
{
    private async Task<(HttpClient gerente, ZonaDto zona)> PrepararAsync()
    {
        var (gerente, _) = await app.ClienteAutenticadoAsync(PerfisPadrao.Gerente);
        var zona = await (await gerente.Post("/api/zonas", new GuardarZona(Unico("Sala "), 1)).Esperar(HttpStatusCode.Created)).Ler<ZonaDto>();
        return (gerente, zona);
    }

    [Fact]
    public async Task Cria_mesa_e_recusa_nome_duplicado_e_capacidade_invalida()
    {
        var (gerente, zona) = await PrepararAsync();
        var nome = Unico("M", 8);
        var mesa = await (await gerente.Post("/api/mesas", new GuardarMesa(nome, zona.Id, 4, null)).Esperar(HttpStatusCode.Created)).Ler<MesaDto>();
        Assert.Equal(EstadoMesa.Livre, mesa.Estado);
        Assert.Equal(0m, mesa.TotalAberto);

        await gerente.Post("/api/mesas", new GuardarMesa(nome, zona.Id, 2, null)).Esperar(HttpStatusCode.Conflict);
        await gerente.Post("/api/mesas", new GuardarMesa(Unico("M", 8), zona.Id, 0, null)).Esperar(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Estado_manual_so_alterna_entre_livre_e_indisponivel()
    {
        var (gerente, zona) = await PrepararAsync();
        var mesa = await (await gerente.Post("/api/mesas", new GuardarMesa(Unico("M", 8), zona.Id, 4, null)).Esperar(HttpStatusCode.Created)).Ler<MesaDto>();

        var indisponivel = await (await gerente.Post($"/api/mesas/{mesa.Id}/estado", new AlterarEstadoMesa(EstadoMesa.Indisponivel, mesa.RowVersion))
            .Esperar(HttpStatusCode.OK)).Ler<MesaDto>();
        Assert.Equal(EstadoMesa.Indisponivel, indisponivel.Estado);

        await gerente.Post($"/api/mesas/{mesa.Id}/estado", new AlterarEstadoMesa(EstadoMesa.Ocupada, indisponivel.RowVersion))
            .Esperar(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Zona_com_mesas_activas_nao_se_desactiva()
    {
        var (gerente, zona) = await PrepararAsync();
        await gerente.Post("/api/mesas", new GuardarMesa(Unico("M", 8), zona.Id, 4, null)).Esperar(HttpStatusCode.Created);
        await gerente.Post($"/api/zonas/{zona.Id}/activo", new { activo = false }).Esperar(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Garcom_ve_mesas_mas_nao_as_gere()
    {
        var (_, zona) = await PrepararAsync();
        var (garcom, _) = await app.ClienteAutenticadoAsync(PerfisPadrao.Garcom);
        await garcom.GetAsync("/api/mesas").ContinueWith(t => Assert.Equal(HttpStatusCode.OK, t.Result.StatusCode));
        await garcom.Post("/api/mesas", new GuardarMesa(Unico("M", 8), zona.Id, 4, null)).Esperar(HttpStatusCode.Forbidden);
    }
}
