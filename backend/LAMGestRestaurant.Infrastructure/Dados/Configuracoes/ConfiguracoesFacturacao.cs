using LAMGestRestaurant.Domain.Facturacao;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LAMGestRestaurant.Infrastructure.Dados.Configuracoes;

public class DocumentoConfig : IEntityTypeConfiguration<Documento>
{
    public void Configure(EntityTypeBuilder<Documento> e)
    {
        e.ToTable("Documentos", t =>
        {
            t.HasCheckConstraint("CK_Documentos_Valores", "[Total] >= 0 AND [ValorPago] >= 0 AND [ValorCreditado] >= 0 AND [ValorPago] + [ValorCreditado] <= [Total]");
        });
        e.Property(x => x.Numero).HasMaxLength(30).IsRequired();
        e.Property(x => x.EmitenteNome).HasMaxLength(200).IsRequired();
        e.Property(x => x.EmitenteNuit).HasMaxLength(20);
        e.Property(x => x.EmitenteTelefone).HasMaxLength(50);
        e.Property(x => x.ClienteNome).HasMaxLength(200).IsRequired();
        e.Property(x => x.ClienteNuit).HasMaxLength(20);
        e.Property(x => x.Referencia).HasMaxLength(100);
        e.Property(x => x.IdempotencyKey).HasMaxLength(64);
        e.Ignore(x => x.EmDivida);
        e.HasIndex(x => x.Numero).IsUnique();
        e.HasIndex(x => x.IdempotencyKey).IsUnique().HasFilter("[IdempotencyKey] IS NOT NULL");
        e.HasIndex(x => new { x.Tipo, x.EmitidoEmUtc });
        e.HasIndex(x => new { x.ClienteId, x.Tipo, x.Estado });
        e.HasIndex(x => x.PedidoId);
        e.HasIndex(x => x.DocumentoOrigemId);
        e.HasOne(x => x.DocumentoOrigem).WithMany().HasForeignKey(x => x.DocumentoOrigemId);
    }
}

public class DocumentoLinhaConfig : IEntityTypeConfiguration<DocumentoLinha>
{
    public void Configure(EntityTypeBuilder<DocumentoLinha> e)
    {
        e.ToTable("DocumentoLinhas");
        e.Property(x => x.Codigo).HasMaxLength(30);
        e.Property(x => x.Descricao).HasMaxLength(300).IsRequired();
        e.Property(x => x.MotivoIsencao).HasMaxLength(300);
        e.HasIndex(x => new { x.DocumentoId, x.Ordem }).IsUnique();
        e.HasIndex(x => x.LinhaOrigemId);
    }
}

public class LiquidacaoFacturaConfig : IEntityTypeConfiguration<LiquidacaoFactura>
{
    public void Configure(EntityTypeBuilder<LiquidacaoFactura> e)
    {
        e.ToTable("LiquidacoesFactura", t => t.HasCheckConstraint("CK_LiquidacoesFactura_Valor", "[Valor] > 0"));
        e.HasOne(x => x.Recibo).WithMany(x => x.Liquidacoes).HasForeignKey(x => x.ReciboId);
        e.HasOne(x => x.Factura).WithMany().HasForeignKey(x => x.FacturaId);
        e.HasIndex(x => new { x.ReciboId, x.FacturaId }).IsUnique();
        e.HasIndex(x => x.FacturaId);
    }
}
