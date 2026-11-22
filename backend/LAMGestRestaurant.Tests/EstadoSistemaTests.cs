using System.Net;
using System.Net.Http.Json;
using LAMGestRestaurant.Application.Seguranca;
using LAMGestRestaurant.Infrastructure.Sistema;
using LAMGestRestaurant.Tests.Infraestrutura;
using Microsoft.Extensions.Configuration;
using Microsoft.Data.SqlClient;

namespace LAMGestRestaurant.Tests;

[Collection(ColeccaoBaseDados.Nome)]
public class EstadoSistemaTests(AplicacaoTeste app)
{
    private static readonly DateTime Agora = new(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc);

    private static LeituraSistema Leitura(EstadoCopias? copias = null, EstadoEspaco? espaco = null, DateTime? criada = null) =>
        new("LAMGestRestaurantDB", criada ?? Agora.AddDays(-30),
            copias ?? new EstadoCopias(Agora.AddHours(-12), true, "ok", Agora.AddHours(-12), "x.bak", 10),
            espaco ?? new EstadoEspaco(500, ServicoEstadoSistema.LimiteExpressMb, "C:", 50_000, 100_000));

    [Fact]
    public void Sem_problemas_nao_ha_avisos() => Assert.Empty(ServicoEstadoSistema.Avisos(Leitura(), Agora));

    [Fact]
    public void Copia_que_falhou_ou_antiga_da_aviso()
    {
        var falhou = ServicoEstadoSistema.Avisos(Leitura(new EstadoCopias(Agora.AddHours(-1), false, "disco cheio", Agora.AddHours(-25), null, null)), Agora);
        var a = Assert.Single(falhou);
        Assert.Equal("critico", a.Nivel);
        Assert.Contains("disco cheio", a.Texto);

        Assert.Equal("aviso", Assert.Single(ServicoEstadoSistema.Avisos(Leitura(new EstadoCopias(null, null, null, Agora.AddHours(-40), null, null)), Agora)).Nivel);
        var dias = Assert.Single(ServicoEstadoSistema.Avisos(Leitura(new EstadoCopias(null, null, null, Agora.AddDays(-5), null, null)), Agora));
        Assert.Equal("critico", dias.Nivel);
        Assert.Contains("5 dias", dias.Texto);
    }

    [Fact]
    public void Sem_nenhuma_copia_so_avisa_depois_do_primeiro_dia_da_base()
    {
        var nunca = new EstadoCopias(null, null, null, null, null, null);
        Assert.Empty(ServicoEstadoSistema.Avisos(Leitura(nunca, criada: Agora.AddHours(-5)), Agora));
        Assert.Equal("Sem cópias de segurança", Assert.Single(ServicoEstadoSistema.Avisos(Leitura(nunca), Agora)).Titulo);
    }

    [Fact]
    public void Base_perto_do_limite_do_Express_e_disco_cheio_dao_aviso()
    {
        var limite = ServicoEstadoSistema.LimiteExpressMb;
        Assert.Equal("aviso", Assert.Single(ServicoEstadoSistema.Avisos(Leitura(espaco: new EstadoEspaco(limite * 0.75m, limite, "C:", 50_000, 100_000)), Agora)).Nivel);
        var cheia = Assert.Single(ServicoEstadoSistema.Avisos(Leitura(espaco: new EstadoEspaco(limite * 0.95m, limite, "C:", 50_000, 100_000)), Agora));
        Assert.Equal("critico", cheia.Nivel);
        Assert.Contains("9,5 GB de 10,0 GB", cheia.Texto);
        // Outras edições não têm limite.
        Assert.Empty(ServicoEstadoSistema.Avisos(Leitura(espaco: new EstadoEspaco(limite * 2, null, "C:", 50_000, 100_000)), Agora));

        Assert.Equal("aviso", Assert.Single(ServicoEstadoSistema.Avisos(Leitura(espaco: new EstadoEspaco(500, limite, "C:", 1_500, 20_000)), Agora)).Nivel);
        Assert.Equal("critico", Assert.Single(ServicoEstadoSistema.Avisos(Leitura(espaco: new EstadoEspaco(500, limite, "C:", 800, 100_000)), Agora)).Nivel);
        // Disco grande quase cheio: menos de 10 % livre é aviso, menos de 3 % é crítico.
        Assert.Equal("aviso", Assert.Single(ServicoEstadoSistema.Avisos(Leitura(espaco: new EstadoEspaco(500, limite, "C:", 4_500, 80_000)), Agora)).Nivel);
        Assert.Equal("critico", Assert.Single(ServicoEstadoSistema.Avisos(Leitura(espaco: new EstadoEspaco(500, limite, "C:", 2_200, 80_000)), Agora)).Nivel);
        Assert.Contains("só 2,1 GB", ServicoEstadoSistema.Avisos(Leitura(espaco: new EstadoEspaco(500, limite, "C:", 2_200, 80_000)), Agora)[0].Texto);
    }

    [Fact]
    public void Le_o_resultado_escrito_pelo_script_de_copias()
    {
        var pasta = Path.Combine(Path.GetTempPath(), "lamgest-estado-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(pasta);
        try
        {
            // Como o scriptsackup-sql.ps1 o escreve (PowerShell 5.1: UTF-8, indentação própria).
            File.WriteAllText(Path.Combine(pasta, "backup-estado-LAMGestRestaurantDB.json"), """
                {
                    "Base":  "LAMGestRestaurantDB",
                    "DataUtc":  "2026-11-20T18:08:38Z",
                    "Sucesso":  false,
                    "Mensagem":  "Sqlcmd: Error: instância não encontrada",
                    "UltimaCopiaUtc":  "2026-11-19T18:08:29Z",
                    "Ficheiro":  "",
                    "TamanhoMB":  0
                }
                """);
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["Copias:PastaEstado"] = pasta }).Build();
            var servico = new ServicoEstadoSistema(null!, config, TimeProvider.System);

            var c = servico.LerFicheiroCopias("LAMGestRestaurantDB");
            Assert.False(c.UltimaComSucesso);
            Assert.Equal(new DateTime(2026, 11, 20, 18, 8, 38, DateTimeKind.Utc), c.UltimaExecucaoUtc);
            Assert.Equal(new DateTime(2026, 11, 19, 18, 8, 29, DateTimeKind.Utc), c.UltimaCopiaUtc);
            Assert.Contains("instância não encontrada", c.Mensagem);

            // Outra base, sem ficheiro: sem registo.
            Assert.Null(servico.LerFicheiroCopias("LAMGestRestaurantOutra").UltimaCopiaUtc);
        }
        finally
        {
            Directory.Delete(pasta, recursive: true);
        }
    }

    [Fact]
    public async Task Estado_mostra_a_base_e_o_seu_tamanho()
    {
        var cliente = await app.ClienteComPermissoesAsync(Permissoes.ConfiguracoesGerir);
        var estado = await cliente.GetFromJsonAsync<EstadoSistema>("/api/sistema/estado");
        Assert.Equal(new SqlConnectionStringBuilder(app.ConnectionString).InitialCatalog, estado!.BaseDados);
        Assert.True(estado.Espaco.BaseMb > 0);
    }

    [Fact]
    public async Task Estado_so_para_quem_gere_as_configuracoes()
    {
        var cliente = await app.ClienteComPermissoesAsync(Permissoes.PosUsar);
        Assert.Equal(HttpStatusCode.Forbidden, (await cliente.GetAsync("/api/sistema/estado")).StatusCode);
    }

    [Fact]
    public async Task Erros_do_ecra_ficam_no_log_mesmo_sem_sessao()
    {
        var cliente = app.CreateClient();
        var ok = await cliente.PostAsJsonAsync("/api/sistema/erros-cliente",
            new { mensagem = "TypeError: x is undefined\nlinha falsa", pilha = "at Pos (PaginaPos.tsx:10)", pagina = "/pos", origem = "ecra" });
        Assert.Equal(HttpStatusCode.NoContent, ok.StatusCode);

        var semMensagem = await cliente.PostAsJsonAsync("/api/sistema/erros-cliente", new { pagina = "/pos" });
        Assert.Equal(HttpStatusCode.BadRequest, semMensagem.StatusCode);
    }
}
