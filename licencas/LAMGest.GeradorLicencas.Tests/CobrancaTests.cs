using LAMGestRestaurant.Licenciamento;
using Microsoft.Data.SqlClient;

namespace LAMGest.GeradorLicencas.Tests;

/// <summary>Regras da cobrança (sem base de dados).</summary>
public class RegrasCobrancaTests
{
    private static readonly DateOnly Hoje = new(2026, 11, 25);

    [Fact]
    public void Pagamento_em_dia_continua_do_fim_do_periodo_anterior()
    {
        Assert.Equal(new DateOnly(2027, 1, 10), RegrasCobranca.NovaValidade(new DateOnly(2026, 12, 10), Hoje, 1)); // adiantado
        Assert.Equal(new DateOnly(2027, 2, 28), RegrasCobranca.NovaValidade(new DateOnly(2026, 11, 30), Hoje, 3)); // trimestre (fim de mês)
    }

    [Fact]
    public void Pagamento_dentro_da_tolerancia_nao_oferece_dias_e_depois_do_bloqueio_conta_de_hoje()
    {
        // Expirou há 7 dias (ainda na tolerância do sistema): continua do fim do período.
        Assert.Equal(new DateOnly(2026, 12, 18), RegrasCobranca.NovaValidade(new DateOnly(2026, 11, 18), Hoje, 1));
        // Expirou há 8 dias (o sistema já bloqueou): conta a partir do pagamento.
        Assert.Equal(new DateOnly(2026, 12, 25), RegrasCobranca.NovaValidade(new DateOnly(2026, 11, 17), Hoje, 1));
        // Primeiro pagamento.
        Assert.Equal(new DateOnly(2027, 11, 25), RegrasCobranca.NovaValidade(null, Hoje, 12));
        Assert.Throws<ArgumentException>(() => RegrasCobranca.NovaValidade(null, Hoje, 0));
    }

    [Theory]
    [InlineData(EstadoCliente.Activo, null, Situacao.SemLicenca)]
    [InlineData(EstadoCliente.Activo, "2027-01-10", Situacao.EmDia)]
    [InlineData(EstadoCliente.Activo, "2026-12-02", Situacao.AVencer)]
    [InlineData(EstadoCliente.Activo, "2026-11-25", Situacao.AVencer)]
    [InlineData(EstadoCliente.Activo, "2026-11-24", Situacao.EmAtraso)]
    [InlineData(EstadoCliente.Suspenso, "2027-01-10", Situacao.Suspenso)]
    [InlineData(EstadoCliente.Cancelado, null, Situacao.Cancelado)]
    public void Situacao_do_cliente(EstadoCliente estado, string? validaAte, Situacao esperada) =>
        Assert.Equal(esperada, RegrasCobranca.SituacaoDe(estado, validaAte is null ? null : DateOnly.Parse(validaAte, System.Globalization.CultureInfo.InvariantCulture), Hoje));

    [Fact]
    public void Textos_da_situacao_e_valores()
    {
        Assert.Equal("Em atraso há 1 dia (bloqueia dentro de 7 dias)", RegrasCobranca.Descrever(Situacao.EmAtraso, Hoje.AddDays(-1), Hoje));
        Assert.Equal("Em atraso há 10 dias (sistema bloqueado)", RegrasCobranca.Descrever(Situacao.EmAtraso, Hoje.AddDays(-10), Hoje));
        Assert.Equal("Vence em 1 dia", RegrasCobranca.Descrever(Situacao.AVencer, Hoje.AddDays(1), Hoje));
        Assert.True(RegrasCobranca.LerValor("2 500,50", out var v) && v == 2500.50m);
        Assert.True(RegrasCobranca.LerValor("2500", out v) && v == 2500m);
        Assert.False(RegrasCobranca.LerValor("2500,555", out _));
        Assert.False(RegrasCobranca.LerValor("abc", out _));
        Assert.Equal(7500m, RegrasCobranca.ValorSugerido(2500m, 3));
        Assert.Contains("válida até 10/01/2027", RegrasCobranca.Mensagem("Sol", new DateOnly(2027, 1, 10), "LAMGEST1.x"));
    }

    [Theory]
    [InlineData("MZullo_Rest")]
    [InlineData("LAMGestRestaurantDB")]
    [InlineData("master")]
    [InlineData("base; DROP")]
    public void Nunca_usa_a_base_de_outro_sistema(string nome) => Assert.Throws<ArgumentException>(() => BaseLicencas.ValidarNome(nome));
}

/// <summary>
/// Base das licenças num SQL Server de testes (LAMGEST_TESTES_SQL, por omissão .\SYSON), numa base própria apagada no fim;
/// a chave de assinatura é de teste, numa pasta temporária.
/// </summary>
public sealed class BaseLicencasTests : IDisposable
{
    private static readonly DateOnly Hoje = DateOnly.FromDateTime(DateTime.Today);
    private readonly string pasta = Path.Combine(Path.GetTempPath(), "lamgest-cobranca-" + Guid.NewGuid().ToString("N"));
    private readonly ArmazemLicencas armazem;
    private readonly BaseLicencas db;
    private readonly string publica;

    public BaseLicencasTests()
    {
        armazem = new ArmazemLicencas(pasta);
        publica = armazem.CriarChave();
        db = new BaseLicencas(new LigacaoBase(Environment.GetEnvironmentVariable("LAMGEST_TESTES_SQL") ?? @".\SYSON",
            $"LicTeste_{Guid.NewGuid():N}_Testes"), "TESTE\\operador");
        db.Preparar();
    }

    public void Dispose()
    {
        db.ApagarBaseDeTestes();
        if (Directory.Exists(pasta))
            Directory.Delete(pasta, recursive: true);
    }

    [Fact]
    public void Pagamento_emite_a_licenca_ate_ao_fim_do_periodo_e_o_cliente_fica_em_dia()
    {
        db.Preparar(); // repetir não altera nada
        var id = db.CriarCliente(" Restaurante Sol ", "400123456", "84 000 0000", Plano.Mensal, 2500m, null);
        Assert.Throws<InvalidOperationException>(() => db.CriarCliente("Outro", "400123456", null, Plano.Mensal, 0, null)); // NUIT único

        var (pagamento, licenca) = db.RegistarPagamento(armazem, id, Hoje, 7500m, 3, "M-Pesa ABC", Hoje);
        Assert.Equal(Hoje.AddMonths(3), pagamento.PeriodoAte);
        using var chave = ChavesLicenca.Publica(publica);
        var dados = FormatoLicenca.Ler(licenca!.Texto, chave, out _)!;
        Assert.Equal(("Restaurante Sol", "400123456", Hoje.AddMonths(3)), (dados.Nome, dados.Nuit, dados.ValidaAte));

        var c = db.Cliente(id);
        Assert.Equal((Hoje.AddMonths(3), Hoje, Situacao.EmDia), (c.ValidaAte, c.UltimoPagamento, RegrasCobranca.SituacaoDe(c.Estado, c.ValidaAte, Hoje)));
        Assert.Equal(pagamento.Id, Assert.Single(db.Licencas(id)).PagamentoId);

        // O pagamento seguinte continua do fim do período.
        var (_, segunda) = db.RegistarPagamento(armazem, id, Hoje, 2500m, 1, null, Hoje);
        Assert.Equal(Hoje.AddMonths(3).AddMonths(1), segunda!.Dados.ValidaAte);
        Assert.Equal(2, db.Pagamentos(id).Count);
        Assert.Contains(db.Historico(id), h => h.Accao == "pagamento.registar" && h.Utilizador == "TESTE\\operador");
    }

    [Fact]
    public void Cliente_suspenso_nao_recebe_licencas_ate_ser_reactivado()
    {
        var id = db.CriarCliente("Bar Maré", "400765432", null, Plano.Mensal, 2000m, null);
        db.MudarEstado(id, EstadoCliente.Suspenso, "Falta de pagamento de Novembro");
        Assert.Contains("suspenso", Assert.Throws<InvalidOperationException>(() => db.RegistarPagamento(armazem, id, Hoje, 2000m, 1, null, Hoje)).Message);
        Assert.Throws<InvalidOperationException>(() => db.EmitirSemPagamento(armazem, id, Hoje.AddDays(10), "Teste", Hoje));
        Assert.Empty(db.Pagamentos(id)); // nada ficou a meio
        Assert.Throws<ArgumentException>(() => db.MudarEstado(id, EstadoCliente.Activo, ""));

        db.MudarEstado(id, EstadoCliente.Activo, "Pagamento regularizado");
        Assert.NotNull(db.RegistarPagamento(armazem, id, Hoje, 2000m, 1, null, Hoje).Licenca);
        Assert.Equal(["cliente.criar", "cliente.suspender", "cliente.reactivar", "pagamento.registar", "licenca.emitir"],
            db.Historico(id).Select(h => h.Accao).Reverse());
    }

    [Fact]
    public void Pagamento_anulado_fica_marcado_e_nao_conta_como_ultimo_pagamento()
    {
        var id = db.CriarCliente("Sol", "400123456", null, Plano.Mensal, 2500m, null);
        var (p, _) = db.RegistarPagamento(armazem, id, Hoje, 2500m, 1, "errado", Hoje);
        Assert.Throws<ArgumentException>(() => db.AnularPagamento(p.Id, ""));
        db.AnularPagamento(p.Id, "Valor registado no cliente errado");
        Assert.Throws<InvalidOperationException>(() => db.AnularPagamento(p.Id, "outra vez"));
        var anulado = Assert.Single(db.Pagamentos(id));
        Assert.Equal((true, "Valor registado no cliente errado"), (anulado.Anulado, anulado.MotivoAnulacao));
        Assert.Null(db.Cliente(id).UltimoPagamento);
        // A licença emitida continua registada (vale no cliente até à data dela).
        Assert.Single(db.Licencas(id));
    }

    [Fact]
    public void Pagamento_antigo_com_o_periodo_terminado_fica_registado_sem_licenca()
    {
        var id = db.CriarCliente("Sol", "400123456", null, Plano.Mensal, 2500m, null);
        var (p, licenca) = db.RegistarPagamento(armazem, id, Hoje.AddMonths(-2), 2500m, 1, null, Hoje);
        Assert.Null(licenca);
        Assert.Equal(Hoje.AddMonths(-1), p.PeriodoAte);
        Assert.Empty(db.Licencas(id));
        Assert.Throws<ArgumentException>(() => db.RegistarPagamento(armazem, id, Hoje.AddDays(1), 2500m, 1, null, Hoje)); // data futura
    }

    [Fact]
    public void Licenca_sem_pagamento_exige_motivo()
    {
        var id = db.CriarCliente("Sol", "400123456", null, Plano.Mensal, 0m, null);
        Assert.Throws<ArgumentException>(() => db.EmitirSemPagamento(armazem, id, Hoje.AddDays(30), " ", Hoje));
        var l = db.EmitirSemPagamento(armazem, id, Hoje.AddDays(30), "Período de teste", Hoje);
        var registo = Assert.Single(db.Licencas(id));
        Assert.Equal((l.Dados.Id, (int?)null, "Período de teste"), (registo.LicencaId, registo.PagamentoId, registo.Observacoes));
    }

    [Fact]
    public void Importa_o_registo_antigo_uma_vez_por_NUIT()
    {
        armazem.Emitir("Sol Antigo", "400123456", Hoje.AddYears(1), Hoje);
        armazem.Emitir("Sol Nascente", "400123456", Hoje.AddYears(2), Hoje);
        armazem.Emitir("Maré", "400765432", Hoje.AddYears(1), Hoje);
        Assert.Equal(3, db.Importar(armazem.Registo()));
        Assert.Equal(0, db.Importar(armazem.Registo())); // repetir não duplica
        var clientes = db.Clientes();
        Assert.Equal(2, clientes.Count);
        var sol = clientes.Single(c => c.Nuit == "400123456");
        Assert.Equal(("Sol Nascente", Hoje.AddYears(2), Plano.Anual), (sol.Nome, sol.ValidaAte, sol.Plano));
        Assert.True(db.TemLicencas());
    }

    [Fact]
    public void O_historico_so_recebe_linhas_novas()
    {
        db.CriarCliente("Sol", "400123456", null, Plano.Mensal, 0m, null);
        using var c = new SqlConnection(new SqlConnectionStringBuilder
        {
            DataSource = Environment.GetEnvironmentVariable("LAMGEST_TESTES_SQL") ?? @".\SYSON", InitialCatalog = db.NomeBase, IntegratedSecurity = true, TrustServerCertificate = true,
        }.ConnectionString);
        c.Open();
        Assert.Throws<SqlException>(() => new SqlCommand("DELETE dbo.Historico", c).ExecuteNonQuery());
        Assert.Throws<SqlException>(() => new SqlCommand("UPDATE dbo.Historico SET Detalhe = 'x'", c).ExecuteNonQuery());
    }

    [Fact]
    public void Copia_de_seguranca_da_base()
    {
        var ficheiro = db.Copia();
        try
        {
            Assert.EndsWith(".bak", ficheiro);
            Assert.True(File.Exists(ficheiro) || ficheiro.StartsWith(@"\\", StringComparison.Ordinal));
        }
        finally
        {
            try { File.Delete(ficheiro); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }
}
