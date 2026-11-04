using LAMGestRestaurant.Licenciamento;

namespace LAMGest.GeradorLicencas.Tests;

/// <summary>Gerador de licenças numa pasta temporária (nunca toca na chave verdadeira do fabricante).</summary>
public sealed class GeradorTests : IDisposable
{
    private readonly string pasta = Path.Combine(Path.GetTempPath(), "lamgest-licencas-" + Guid.NewGuid().ToString("N"));
    private static readonly DateOnly Hoje = new(2026, 11, 3);

    public void Dispose()
    {
        if (Directory.Exists(pasta))
            Directory.Delete(pasta, recursive: true);
    }

    [Fact]
    public void Licenca_emitida_e_verificada_pela_chave_publica_e_fica_no_registo()
    {
        var armazem = new ArmazemLicencas(pasta);
        var publica = armazem.CriarChave();
        Assert.Throws<InvalidOperationException>(() => armazem.CriarChave()); // nunca substitui a chave

        var l = armazem.Emitir("  Restaurante Sol  ", "400123456", new DateOnly(2027, 11, 3), Hoje, "Contrato 2026");
        using var chave = ChavesLicenca.Publica(publica);
        var dados = FormatoLicenca.Ler(l.Texto, chave, out _)!;
        Assert.Equal(("Restaurante Sol", "400123456", Hoje, new DateOnly(2027, 11, 3)), (dados.Nome, dados.Nuit, dados.EmitidaEm, dados.ValidaAte));

        // Renovação: mesmo NUIT, licença nova (outro Id); o estabelecimento aparece uma vez, com a última.
        var renovada = armazem.Emitir("Restaurante Sol", "400123456", ArmazemLicencas.ValidadeRenovacao(dados.ValidaAte, Hoje), Hoje);
        Assert.NotEqual(l.Dados.Id, renovada.Dados.Id);
        Assert.Equal(new DateOnly(2028, 11, 3), renovada.Dados.ValidaAte);
        Assert.Equal(2, new ArmazemLicencas(pasta).Registo().Count);
        Assert.Equal(renovada.Dados.Id, Assert.Single(new ArmazemLicencas(pasta).Estabelecimentos()).Dados.Id);
    }

    [Fact]
    public void Com_a_chave_do_sistema_so_emite_licencas_que_o_sistema_aceita()
    {
        var publica = new ArmazemLicencas(pasta).CriarChave();
        Assert.NotNull(new ArmazemLicencas(pasta, publica).Emitir("Sol", "400123456", Hoje.AddYears(1), Hoje));

        // Outra chave neste computador: recusa e não fica no registo.
        var outra = Path.Combine(pasta, "outra");
        new ArmazemLicencas(outra).CriarChave();
        var errado = new ArmazemLicencas(outra, publica);
        Assert.Contains("não é a embutida", Assert.Throws<InvalidOperationException>(() => errado.Emitir("Sol", "400123456", Hoje.AddYears(1), Hoje)).Message);
        Assert.Empty(errado.Registo());
    }

    [Fact]
    public void Apagar_tira_so_essa_licenca_do_registo()
    {
        var armazem = new ArmazemLicencas(pasta);
        armazem.CriarChave();
        var antiga = armazem.Emitir("Sol", "400123456", Hoje.AddYears(1), Hoje);
        var nova = armazem.Emitir("Sol", "400123456", Hoje.AddYears(2), Hoje);
        armazem.Apagar(antiga.Dados.Id);
        Assert.Equal(nova.Dados.Id, Assert.Single(new ArmazemLicencas(pasta).Registo()).Dados.Id);
        Assert.Throws<InvalidOperationException>(() => armazem.Apagar(antiga.Dados.Id));
    }

    [Fact]
    public void Renovar_uma_licenca_expirada_conta_um_ano_a_partir_de_hoje()
    {
        Assert.Equal(new DateOnly(2027, 11, 3), ArmazemLicencas.ValidadeRenovacao(new DateOnly(2026, 5, 1), Hoje));
        Assert.Equal(new DateOnly(2027, 12, 31), ArmazemLicencas.ValidadeRenovacao(new DateOnly(2026, 12, 31), Hoje));
    }

    [Theory]
    [InlineData("", "400123456", "Indique o nome")]
    [InlineData("Sol", "12345", "9 algarismos")]
    [InlineData("Sol", "400123456", "depois de hoje")]
    public void Dados_invalidos_sao_recusados(string nome, string nuit, string erro)
    {
        var armazem = new ArmazemLicencas(pasta);
        armazem.CriarChave();
        var ex = Assert.Throws<ArgumentException>(() => armazem.Emitir(nome, nuit, erro == "depois de hoje" ? Hoje : Hoje.AddYears(1), Hoje));
        Assert.Contains(erro, ex.Message);
    }

    [Fact]
    public void Copia_de_seguranca_repoe_a_mesma_chave_noutro_computador_e_exige_a_palavra_passe()
    {
        var original = new ArmazemLicencas(pasta);
        var publica = original.CriarChave();
        var copia = Path.Combine(pasta, "copia.pem");
        Assert.Throws<ArgumentException>(() => original.ExportarCopia(copia, "curta"));
        original.ExportarCopia(copia, "palavra-passe-longa");
        Assert.DoesNotContain("PRIVATE KEY-----\n", File.ReadAllText(copia).Replace("ENCRYPTED PRIVATE KEY", "")); // só cifrada

        var outroPc = new ArmazemLicencas(Path.Combine(pasta, "outro"));
        Assert.Throws<InvalidOperationException>(() => outroPc.ImportarCopia(copia, "errada-errada"));
        Assert.Equal(publica, outroPc.ImportarCopia(copia, "palavra-passe-longa"));
        // Licenças do outro computador verificam-se com a mesma chave pública.
        using var chave = ChavesLicenca.Publica(publica);
        Assert.NotNull(FormatoLicenca.Ler(outroPc.Emitir("Sol", "400123456", Hoje.AddYears(1), Hoje).Texto, chave, out _));

        // Um computador com outra chave não a deixa substituir.
        var terceiro = new ArmazemLicencas(Path.Combine(pasta, "terceiro"));
        terceiro.CriarChave();
        Assert.Throws<InvalidOperationException>(() => terceiro.ImportarCopia(copia, "palavra-passe-longa"));
    }
}
