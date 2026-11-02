using LAMGestRestaurant.Domain.Compras;
using LAMGestRestaurant.Domain.Stock;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LAMGestRestaurant.Infrastructure.Dados.Configuracoes;

public class UnidadeMedidaConfig : IEntityTypeConfiguration<UnidadeMedida>
{
    public void Configure(EntityTypeBuilder<UnidadeMedida> e)
    {
        e.Property(x => x.Codigo).HasMaxLength(10).IsRequired();
        e.HasIndex(x => x.Codigo).IsUnique();
        e.ToTable(t => t.HasCheckConstraint("CK_UnidadesMedida_Factor", "[FactorParaBase] > 0"));
    }
}

public class ArmazemConfig : IEntityTypeConfiguration<Armazem>
{
    public void Configure(EntityTypeBuilder<Armazem> e)
    {
        e.HasIndex(x => x.Nome).IsUnique();
        e.HasIndex(x => x.Principal).IsUnique().HasFilter("[Principal] = 1");
    }
}

public class ArtigoConfig : IEntityTypeConfiguration<Artigo>
{
    public void Configure(EntityTypeBuilder<Artigo> e)
    {
        e.Property(x => x.Codigo).HasMaxLength(30).IsRequired();
        e.Property(x => x.Nome).HasMaxLength(150).IsRequired();
        e.HasIndex(x => x.Codigo).IsUnique();
        e.HasIndex(x => x.Nome);
        e.ToTable(t => t.HasCheckConstraint("CK_Artigos_Valores", "[StockMinimo] >= 0 AND [CustoMedio] >= 0 AND [UltimoCusto] >= 0"));
    }
}

public class StockSaldoConfig : IEntityTypeConfiguration<StockSaldo>
{
    public void Configure(EntityTypeBuilder<StockSaldo> e) =>
        e.HasIndex(x => new { x.ArtigoId, x.ArmazemId }).IsUnique();
}

public class MovimentoStockConfig : IEntityTypeConfiguration<MovimentoStock>
{
    public void Configure(EntityTypeBuilder<MovimentoStock> e)
    {
        e.Property(x => x.OrigemTipo).HasMaxLength(30);
        e.HasIndex(x => new { x.ArtigoId, x.DataUtc });
        e.HasIndex(x => x.DataUtc);
        // Garante que o mesmo documento de origem não movimenta o mesmo artigo duas vezes com o mesmo tipo.
        e.HasIndex(x => new { x.Tipo, x.OrigemTipo, x.OrigemId, x.ArtigoId, x.ArmazemId })
            .IsUnique()
            .HasFilter("[OrigemId] IS NOT NULL")
            .HasDatabaseName("UX_MovimentosStock_Origem");
        e.ToTable(t => t.HasCheckConstraint("CK_MovimentosStock_Quantidade", "[Quantidade] <> 0"));
    }
}

public class InventarioConfig : IEntityTypeConfiguration<Inventario>
{
    public void Configure(EntityTypeBuilder<Inventario> e)
    {
        e.HasMany(x => x.Linhas).WithOne(x => x.Inventario).HasForeignKey(x => x.InventarioId).OnDelete(DeleteBehavior.Cascade);
        e.HasIndex(x => new { x.ArmazemId, x.Estado });
    }
}

public class InventarioLinhaConfig : IEntityTypeConfiguration<InventarioLinha>
{
    public void Configure(EntityTypeBuilder<InventarioLinha> e) =>
        e.HasIndex(x => new { x.InventarioId, x.ArtigoId }).IsUnique();
}

public class ReceitaConfig : IEntityTypeConfiguration<Receita>
{
    public void Configure(EntityTypeBuilder<Receita> e)
    {
        e.HasMany(x => x.Itens).WithOne(x => x.Receita).HasForeignKey(x => x.ReceitaId);
        e.HasIndex(x => new { x.ProdutoId, x.Versao }).IsUnique();
        // Uma única versão activa por produto.
        e.HasIndex(x => x.ProdutoId).IsUnique().HasFilter("[Activa] = 1").HasDatabaseName("UX_Receitas_ProdutoActiva");
    }
}

public class ReceitaItemConfig : IEntityTypeConfiguration<ReceitaItem>
{
    public void Configure(EntityTypeBuilder<ReceitaItem> e)
    {
        e.HasIndex(x => new { x.ReceitaId, x.ArtigoId }).IsUnique();
        e.ToTable(t => t.HasCheckConstraint("CK_ReceitaItens_Quantidade", "[Quantidade] > 0"));
    }
}

public class FornecedorConfig : IEntityTypeConfiguration<Fornecedor>
{
    public void Configure(EntityTypeBuilder<Fornecedor> e)
    {
        e.Property(x => x.Nome).HasMaxLength(150).IsRequired();
        e.Property(x => x.Nuit).HasMaxLength(20);
        e.HasIndex(x => x.Nome);
        e.HasIndex(x => x.Nuit).IsUnique().HasFilter("[Nuit] IS NOT NULL");
    }
}

public class CompraConfig : IEntityTypeConfiguration<Compra>
{
    public void Configure(EntityTypeBuilder<Compra> e)
    {
        e.Property(x => x.NumeroDocumento).HasMaxLength(50).IsRequired();
        // O mesmo documento do fornecedor só pode ser registado uma vez (excepto se anulado).
        e.HasIndex(x => new { x.FornecedorId, x.NumeroDocumento }).IsUnique().HasFilter("[Estado] <> 'Anulada'");
        e.HasIndex(x => x.DataDocumento);
        e.HasIndex(x => x.Estado);
        // As linhas de um rascunho podem ser eliminadas com ele; compras confirmadas nunca são eliminadas.
        e.HasMany(x => x.Itens).WithOne(x => x.Compra).HasForeignKey(x => x.CompraId).OnDelete(DeleteBehavior.Cascade);
        e.ToTable(t => t.HasCheckConstraint("CK_Compras_Totais", "[Subtotal] >= 0 AND [Desconto] >= 0 AND [Impostos] >= 0 AND [Total] >= 0"));
    }
}

public class CompraItemConfig : IEntityTypeConfiguration<CompraItem>
{
    public void Configure(EntityTypeBuilder<CompraItem> e)
    {
        e.HasIndex(x => x.ArtigoId);
        e.ToTable(t => t.HasCheckConstraint("CK_CompraItens_Valores", "[Quantidade] > 0 AND [CustoUnitario] >= 0 AND [Desconto] >= 0 AND [Total] >= 0"));
    }
}
