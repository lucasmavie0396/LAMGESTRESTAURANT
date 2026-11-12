using LAMGestRestaurant.Domain.Comum;
using LAMGestRestaurant.Infrastructure.Dados;
using LAMGestRestaurant.Tests.Infraestrutura;
using Microsoft.EntityFrameworkCore;

namespace LAMGestRestaurant.Tests;

[Collection(ColeccaoBaseDados.Nome)]
public class ModeloDadosTests(AplicacaoTeste app)
{
    [Fact]
    public void Dominio_nao_usa_float_nem_double()
    {
        var proibidos = typeof(Entidade).Assembly.GetTypes()
            .SelectMany(t => t.GetProperties().Select(p => (t, p)))
            .Where(x => (Nullable.GetUnderlyingType(x.p.PropertyType) ?? x.p.PropertyType) is var tipo
                        && (tipo == typeof(float) || tipo == typeof(double)))
            .Select(x => $"{x.t.Name}.{x.p.Name}")
            .ToList();

        Assert.Empty(proibidos);
    }

    [Fact]
    public void Todos_os_decimais_tem_precisao_explicita()
    {
        using var db = app.CriarContexto();
        var semPrecisao = db.Model.GetEntityTypes()
            .SelectMany(t => t.GetProperties().Select(p => (t, p)))
            .Where(x => (Nullable.GetUnderlyingType(x.p.ClrType) ?? x.p.ClrType) == typeof(decimal)
                        && (x.p.GetPrecision() is null || x.p.GetScale() is null))
            .Select(x => $"{x.t.ClrType.Name}.{x.p.Name}")
            .ToList();

        Assert.Empty(semPrecisao);
    }

    [Theory]
    [InlineData(typeof(Domain.Vendas.PedidoItem), "Quantidade", 18, 3)]
    [InlineData(typeof(Domain.Vendas.PedidoItem), "PrecoUnitario", 18, 2)]
    [InlineData(typeof(Domain.Catalogo.TaxaImposto), "Percentagem", 9, 4)]
    [InlineData(typeof(Domain.Stock.Artigo), "CustoMedio", 18, 4)]
    [InlineData(typeof(Domain.Stock.UnidadeMedida), "FactorParaBase", 18, 6)]
    public void Precisao_decimal_segue_as_convencoes(Type entidade, string propriedade, int precisao, int escala)
    {
        using var db = app.CriarContexto();
        var p = db.Model.FindEntityType(entidade)!.FindProperty(propriedade)!;
        Assert.Equal(precisao, p.GetPrecision());
        Assert.Equal(escala, p.GetScale());
    }

    [Fact]
    public void Entidades_concorrentes_usam_rowversion()
    {
        using var db = app.CriarContexto();
        var tipos = db.Model.GetEntityTypes().Where(t => typeof(IConcorrente).IsAssignableFrom(t.ClrType)).ToList();

        Assert.NotEmpty(tipos);
        Assert.All(tipos, t => Assert.True(t.FindProperty(nameof(IConcorrente.RowVersion))!.IsConcurrencyToken, t.ClrType.Name));
    }

    [Fact]
    public async Task Nao_ha_alteracoes_ao_modelo_sem_migration()
    {
        await using var db = app.CriarContexto();
        Assert.False(db.Database.HasPendingModelChanges(), "O modelo foi alterado sem criar uma migration.");
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
    }

    [Fact]
    public async Task Semeador_e_idempotente()
    {
        // Outros testes criam perfis próprios; compara-se o estado antes/depois de uma segunda execução.
        Task<(int, int, int, int)> Executar() => app.ComServicos(async s =>
        {
            await s.GetRequiredService<SemeadorDados>().ExecutarAsync();
            var db = s.GetRequiredService<AppDbContext>();
            return (await db.Roles.CountAsync(), await db.RoleClaims.CountAsync(), await db.MetodosPagamento.CountAsync(), await db.Configuracoes.CountAsync());
        });

        var primeira = await Executar();
        var segunda = await Executar();

        Assert.Equal(primeira, segunda);
        await using var dados = app.CriarContexto();
        Assert.Equal(6, await dados.Roles.CountAsync(r => r.Sistema));
        Assert.True(segunda.Item3 >= 7); // 7 semeados; outros testes podem criar modos de pagamento
        Assert.Equal(42, segunda.Item4); // inclui as 3 da facturação, as 21 de Documentos e POS, as 3 das etiquetas e a do dia de operação
    }
}
