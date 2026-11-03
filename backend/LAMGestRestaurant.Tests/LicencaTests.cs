using System.Net;
using System.Net.Http.Json;
using LAMGestRestaurant.Api.Controllers;
using LAMGestRestaurant.Application.Licencas;
using LAMGestRestaurant.Application.Seguranca;
using LAMGestRestaurant.Licenciamento;
using LAMGestRestaurant.Tests.Infraestrutura;
using Microsoft.EntityFrameworkCore;
using static LAMGestRestaurant.Tests.Infraestrutura.ExtensoesHttp;

namespace LAMGestRestaurant.Tests;

/// <summary>Formato e assinatura das licenças (sem base de dados).</summary>
public class FormatoLicencaTests
{
    private static DadosLicenca Dados() => new(Guid.NewGuid(), "Restaurante Sol", "400123456", new DateOnly(2026, 11, 1), new DateOnly(2027, 11, 1));

    [Fact]
    public void Licenca_assinada_le_se_com_a_chave_publica_mesmo_com_quebras_de_linha()
    {
        using var chave = ChavesLicenca.Nova();
        var dados = Dados();
        var texto = FormatoLicenca.Codificar(dados, chave);
        Assert.StartsWith("LAMGEST1.", texto);
        using var publica = ChavesLicenca.Publica(ChavesLicenca.ExportarPublica(chave));
        var comQuebras = string.Join("\r\n", texto.Chunk(40).Select(c => new string(c))) + "  ";
        Assert.Equal(dados, FormatoLicenca.Ler(comQuebras, publica, out var erro));
        Assert.Null(erro);
    }

    [Fact]
    public void Texto_alterado_ou_de_outra_chave_e_recusado()
    {
        using var chave = ChavesLicenca.Nova();
        using var outra = ChavesLicenca.Nova();
        var texto = FormatoLicenca.Codificar(Dados(), chave);

        Assert.Null(FormatoLicenca.Ler(texto, outra, out var erro));
        Assert.Contains("assinatura", erro);

        // Trocar a validade nos dados (mantendo a assinatura) invalida a licença.
        var partes = texto.Split('.');
        var json = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(partes[1].Replace('-', '+').Replace('_', '/').PadRight((partes[1].Length + 3) / 4 * 4, '=')));
        var falso = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(json.Replace("2027-11-01", "2099-11-01"))).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        Assert.Null(FormatoLicenca.Ler($"{partes[0]}.{falso}.{partes[2]}", chave, out _));

        Assert.Null(FormatoLicenca.Ler("qualquer coisa", chave, out erro));
        Assert.Contains("LAMGEST1", erro);
        Assert.Throws<ArgumentException>(() => FormatoLicenca.Codificar(Dados() with { Nuit = "123" }, chave));
    }

    [Fact]
    public void Chave_publica_do_fabricante_esta_embutida()
    {
        using var chave = ChavesLicenca.PublicaEmbutida();
        Assert.Equal(256, chave.KeySize);
    }
}

/// <summary>Licença no sistema: estado, bloqueio, activação e protecções. Cada teste repõe uma licença válida no fim.</summary>
[Collection(ColeccaoBaseDados.Nome)]
public class LicencaTests(AplicacaoTeste app)
{
    private static DateOnly Hoje => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("Africa/Maputo")));

    private Task<EstadoLicencaDto> EstadoAsync() => app.CreateClient().Obter<EstadoLicencaDto>("/api/licenca/estado");

    private Task ReporAsync() => app.InstalarLicencaAsync(Hoje.AddYears(1));

    [Fact]
    public async Task Estado_e_publico_e_os_avisos_seguem_a_validade()
    {
        try
        {
            var valida = await EstadoAsync(); // sem sessão (ecrã de entrada)
            Assert.Equal((EstadoLicenca.Valida, false, AplicacaoTeste.NuitTeste), (valida.Estado, valida.Bloqueada, valida.Nuit));

            await app.InstalarLicencaAsync(Hoje.AddDays(10));
            var aviso = await EstadoAsync();
            Assert.Equal((EstadoLicenca.AExpirar, 10), (aviso.Estado, aviso.DiasRestantes));
            Assert.Contains("10 dias", aviso.Mensagem);

            await app.InstalarLicencaAsync(Hoje.AddDays(-3));
            var tolerancia = await EstadoAsync();
            Assert.Equal((EstadoLicenca.Tolerancia, false), (tolerancia.Estado, tolerancia.Bloqueada));
            Assert.Contains("bloqueia dentro de 5 dias", tolerancia.Mensagem);

            await app.InstalarLicencaAsync(Hoje.AddDays(-ServicoLicenca.DiasTolerancia - 1));
            var expirada = await EstadoAsync();
            Assert.Equal((EstadoLicenca.Expirada, true), (expirada.Estado, expirada.Bloqueada));
        }
        finally
        {
            await ReporAsync();
        }
    }

    [Fact]
    public async Task Licenca_expirada_bloqueia_gravacoes_mas_nao_consultas_entrar_nem_activar()
    {
        var c = await CenarioVenda.CriarAsync(app);
        try
        {
            await app.InstalarLicencaAsync(Hoje.AddDays(-30));
            var bloqueio = await c.Gerente.PostComChave("/api/pedidos", CenarioVenda.Chave(), new Application.Vendas.NovoPedido(
                Domain.Comum.TipoPedido.TakeAway, null, null, null, 0, null, null, null, false, [new Application.Vendas.NovoItem(c.Bebida.Id, 1, null)]));
            Assert.Equal(HttpStatusCode.Forbidden, bloqueio.StatusCode);
            Assert.Contains("licenca-bloqueada", await bloqueio.Content.ReadAsStringAsync());
            await c.Gerente.GetAsync("/api/pedidos").Esperar(HttpStatusCode.OK);
            await c.Gerente.GetAsync($"/api/relatorios/vendas?desde={Hoje:yyyy-MM-dd}&ate={Hoje:yyyy-MM-dd}").Esperar(HttpStatusCode.OK);

            // Entrar continua possível (para o administrador activar a licença renovada).
            var (outro, _) = await app.ClienteAutenticadoAsync(PerfisPadrao.Administrador);
            var renovada = AplicacaoTeste.LicencaTeste(Hoje.AddYears(1));
            var estado = await (await outro.Post("/api/licenca/activar", new ActivarLicenca(renovada)).Esperar(HttpStatusCode.OK)).Ler<EstadoLicencaDto>();
            Assert.Equal(EstadoLicenca.Valida, estado.Estado);

            // Desbloqueado logo (sem esperar pela cache).
            await c.Gerente.PostComChave("/api/pedidos", CenarioVenda.Chave(), new Application.Vendas.NovoPedido(
                Domain.Comum.TipoPedido.TakeAway, null, null, null, 0, null, null, null, false, [new Application.Vendas.NovoItem(c.Bebida.Id, 1, null)])).Esperar(HttpStatusCode.Created);
        }
        finally
        {
            await ReporAsync();
        }
    }

    [Fact]
    public async Task Activar_grava_o_nome_e_o_nuit_e_recusa_licencas_invalidas_expiradas_ou_mais_curtas()
    {
        var (admin, _) = await app.ClienteAutenticadoAsync(PerfisPadrao.Administrador);
        try
        {
            var texto = AplicacaoTeste.LicencaTeste(Hoje.AddYears(2), nome: "Restaurante Renovado");
            await admin.Post("/api/licenca/activar", new ActivarLicenca(texto)).Esperar(HttpStatusCode.OK);
            var configs = await admin.Obter<List<Application.Sistema.ConfiguracaoDto>>("/api/configuracoes");
            Assert.Equal("Restaurante Renovado", configs.Single(c => c.Chave == "estabelecimento.nome").Valor);
            var historico = await admin.Obter<List<LicencaDto>>("/api/licenca/historico");
            Assert.Equal("Restaurante Renovado", historico[0].NomeComercial);
            Assert.NotNull(historico[0].ActivadaPor);

            // A mesma outra vez: repõe (não duplica). Uma mais curta do mesmo NUIT, uma expirada e uma de outra chave: recusadas.
            await admin.Post("/api/licenca/activar", new ActivarLicenca(texto)).Esperar(HttpStatusCode.OK);
            Assert.Equal(historico.Count, (await admin.Obter<List<LicencaDto>>("/api/licenca/historico")).Count);
            await admin.Post("/api/licenca/activar", new ActivarLicenca(AplicacaoTeste.LicencaTeste(Hoje.AddYears(1)))).Esperar(HttpStatusCode.UnprocessableEntity);
            await admin.Post("/api/licenca/activar", new ActivarLicenca(AplicacaoTeste.LicencaTeste(Hoje.AddDays(-1), emitidaEm: Hoje.AddYears(-1)))).Esperar(HttpStatusCode.UnprocessableEntity);
            using var falsa = ChavesLicenca.Nova();
            var deOutraChave = FormatoLicenca.Codificar(new DadosLicenca(Guid.NewGuid(), "X", AplicacaoTeste.NuitTeste, Hoje, Hoje.AddYears(5)), falsa);
            await admin.Post("/api/licenca/activar", new ActivarLicenca(deOutraChave)).Esperar(HttpStatusCode.UnprocessableEntity);

            // Só quem gere as configurações activa licenças; o NUIT fica preso ao da licença.
            var (garcom, _) = await app.ClienteAutenticadoAsync(PerfisPadrao.Garcom);
            await garcom.Post("/api/licenca/activar", new ActivarLicenca(AplicacaoTeste.LicencaTeste(Hoje.AddYears(3)))).Esperar(HttpStatusCode.Forbidden);
            await admin.Put("/api/configuracoes", new GuardarConfiguracoes(new Dictionary<string, string> { ["estabelecimento.nuit"] = "400999999" }))
                .Esperar(HttpStatusCode.UnprocessableEntity);
        }
        finally
        {
            await ReporAsync();
        }
    }

    [Fact]
    public async Task Alterar_a_validade_na_base_ou_atrasar_o_relogio_invalida_a_licenca()
    {
        try
        {
            await using (var db = app.CriarContexto())
            {
                var actual = await db.Licencas.OrderByDescending(l => l.ActivadaEmUtc).FirstAsync();
                actual.ValidaAte = actual.ValidaAte.AddYears(10); // «prolongar» sem licença nova
                await db.SaveChangesAsync();
            }
            app.Services.GetRequiredService<CacheEstadoLicenca>().Invalidar();
            var adulterada = await EstadoAsync();
            Assert.Equal((EstadoLicenca.Invalida, true), (adulterada.Estado, adulterada.Bloqueada));

            await ReporAsync();
            await using (var db = app.CriarContexto())
            {
                var actual = await db.Licencas.OrderByDescending(l => l.ActivadaEmUtc).FirstAsync();
                actual.UltimaVerificacaoUtc = DateTime.UtcNow.AddDays(5); // o sistema já tinha sido usado «no futuro»
                await db.SaveChangesAsync();
            }
            app.Services.GetRequiredService<CacheEstadoLicenca>().Invalidar();
            var relogio = await EstadoAsync();
            Assert.Equal(EstadoLicenca.Invalida, relogio.Estado);
            Assert.Contains("relógio", relogio.Mensagem);
        }
        finally
        {
            await ReporAsync();
        }
    }
}
