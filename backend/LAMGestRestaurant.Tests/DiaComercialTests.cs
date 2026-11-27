using System.Net;
using LAMGestRestaurant.Api.Controllers;
using LAMGestRestaurant.Application.Comum;
using LAMGestRestaurant.Application.Relatorios;
using LAMGestRestaurant.Domain.Sistema;
using LAMGestRestaurant.Infrastructure.Dados;
using LAMGestRestaurant.Tests.Infraestrutura;
using Microsoft.EntityFrameworkCore;

namespace LAMGestRestaurant.Tests;

/// <summary>
/// Dia comercial: com o dia a terminar às 06:00, uma venda às 02:00 conta no dia anterior em todos os relatórios
/// (o intervalo de datas e o agrupamento por dia vêm do mesmo sítio). Repõe a meia-noite no fim (base partilhada).
/// </summary>
[Collection(ColeccaoBaseDados.Nome)]
public class DiaComercialTests(AplicacaoTeste app)
{
    private static readonly TimeZoneInfo Maputo = TimeZoneInfo.FindSystemTimeZoneById("Africa/Maputo");
    private static DateTime Utc(DateOnly dia, int hora) => TimeZoneInfo.ConvertTimeToUtc(dia.ToDateTime(new TimeOnly(hora, 0)), Maputo);

    private Task Definir(HttpClient c, string horas) =>
        c.Put("/api/configuracoes", new GuardarConfiguracoes(new Dictionary<string, string> { [ChavesConfiguracao.FimDiaComercial] = horas })).Esperar(HttpStatusCode.OK);

    [Fact]
    public async Task Vendas_de_madrugada_contam_no_dia_anterior_quando_o_dia_termina_mais_tarde()
    {
        var c = await CenarioVenda.CriarAsync(app);
        var dia = new DateOnly(2020, 3, 10); // dia antigo, sem outras vendas da base de testes
        // Só de 0 a 8 horas.
        var recusado = await c.Gerente.Put("/api/configuracoes", new GuardarConfiguracoes(new Dictionary<string, string> { [ChavesConfiguracao.FimDiaComercial] = "9" }));
        Assert.False(recusado.IsSuccessStatusCode);

        // Uma venda paga às 02:00 do dia seguinte.
        var pedido = await c.AbrirMesaAsync(c.Gerente, c.Mesa1, (c.Prato, 1));
        await c.PagarAsync(c.Gerente, pedido.Id, "DINHEIRO", pedido.Total).Esperar(HttpStatusCode.Created);
        await app.ComServicos(async s =>
        {
            var db = s.GetRequiredService<AppDbContext>();
            var p = await db.Pedidos.SingleAsync(x => x.Id == pedido.Id);
            p.FechadoEmUtc = Utc(dia.AddDays(1), 2);
            await db.SaveChangesAsync();
            return true;
        });
        Task<RelatorioVendas> Vendas(DateOnly d) => c.Gerente.Obter<RelatorioVendas>($"/api/relatorios/vendas?desde={d:yyyy-MM-dd}&ate={d:yyyy-MM-dd}");

        try
        {
            // Meia-noite (como antes): a venda das 02:00 é do dia seguinte.
            Assert.Equal(0m, (await Vendas(dia)).TotalVendas);
            Assert.Equal(pedido.Total, (await Vendas(dia.AddDays(1))).TotalVendas);

            // O dia termina às 06:00: a mesma venda passa a contar no dia anterior, também no gráfico por dia.
            await Definir(c.Gerente, "6");
            var r = await Vendas(dia);
            Assert.Equal(pedido.Total, r.TotalVendas);
            Assert.Equal(pedido.Total, Assert.Single(r.PorDia).Total);
            Assert.Equal(0m, (await Vendas(dia.AddDays(1))).TotalVendas);

            var (inicio, fim, diaDe, publicas) = await app.ComServicos(async s =>
            {
                var config = s.GetRequiredService<ConfiguracoesSistema>();
                var (i, f) = await config.IntervaloUtcAsync(dia, dia);
                var diaComercial = await config.DiaComercialAsync();
                var pub = await s.GetRequiredService<Application.Sistema.ServicoConfiguracoes>().PublicasAsync(default);
                return (i, f, diaComercial, pub);
            });
            Assert.Equal((Utc(dia, 6), Utc(dia.AddDays(1), 6)), (inicio, fim));
            Assert.Equal(dia, diaDe(Utc(dia.AddDays(1), 5)));
            Assert.Equal(dia.AddDays(1), diaDe(Utc(dia.AddDays(1), 6)));
            Assert.Equal(6, publicas.FimDiaComercial);
        }
        finally
        {
            await Definir(c.Gerente, "0");
        }
    }
}
