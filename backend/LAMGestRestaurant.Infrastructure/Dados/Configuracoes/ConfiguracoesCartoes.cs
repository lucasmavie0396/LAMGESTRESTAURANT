using LAMGestRestaurant.Domain.Cartoes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LAMGestRestaurant.Infrastructure.Dados.Configuracoes;

public class CartaoEventoConfig : IEntityTypeConfiguration<CartaoEvento>
{
    public void Configure(EntityTypeBuilder<CartaoEvento> e)
    {
        e.ToTable("CartoesEvento", t => t.HasCheckConstraint("CK_CartoesEvento_Saldo", "[Saldo] >= 0"));
        e.Property(x => x.Numero).HasMaxLength(40).IsRequired();
        e.Property(x => x.Titular).HasMaxLength(150);
        e.HasIndex(x => x.Numero).IsUnique();
    }
}

public class MovimentoCartaoConfig : IEntityTypeConfiguration<MovimentoCartao>
{
    public void Configure(EntityTypeBuilder<MovimentoCartao> e)
    {
        e.ToTable("MovimentosCartao");
        e.HasOne(x => x.CartaoEvento).WithMany().HasForeignKey(x => x.CartaoEventoId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne(x => x.MetodoPagamento).WithMany().HasForeignKey(x => x.MetodoPagamentoId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne(x => x.MovimentoCaixa).WithMany().HasForeignKey(x => x.MovimentoCaixaId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne(x => x.Pagamento).WithMany().HasForeignKey(x => x.PagamentoId).OnDelete(DeleteBehavior.Restrict);
        e.Property(x => x.Motivo).HasMaxLength(300);
        e.Property(x => x.IdempotencyKey).HasMaxLength(64);
        e.HasIndex(x => x.IdempotencyKey).IsUnique().HasFilter("[IdempotencyKey] IS NOT NULL");
        e.HasIndex(x => new { x.CartaoEventoId, x.Id });
        e.HasIndex(x => x.DataUtc);
        e.HasIndex(x => x.PagamentoId);
        e.HasOne(x => x.Descarga).WithMany().HasForeignKey(x => x.DescargaId).OnDelete(DeleteBehavior.Restrict);
        e.HasIndex(x => x.DescargaId);
    }
}

public class DescargaCartoesConfig : IEntityTypeConfiguration<DescargaCartoes>
{
    public void Configure(EntityTypeBuilder<DescargaCartoes> e)
    {
        e.ToTable("DescargasCartoes", t => t.HasCheckConstraint("CK_DescargasCartoes_Total", "[Total] > 0"));
        e.HasOne(x => x.MetodoPagamento).WithMany().HasForeignKey(x => x.MetodoPagamentoId).OnDelete(DeleteBehavior.Restrict);
        e.Property(x => x.Motivo).HasMaxLength(250).IsRequired();
        e.Property(x => x.Referencia).HasMaxLength(100);
        e.Property(x => x.IdempotencyKey).HasMaxLength(64).IsRequired();
        e.HasIndex(x => x.IdempotencyKey).IsUnique();
        e.HasIndex(x => x.DataUtc);
    }
}
