using LAMGestRestaurant.Domain.Caixa;
using LAMGestRestaurant.Domain.Catalogo;
using LAMGestRestaurant.Domain.Sala;
using LAMGestRestaurant.Domain.Vendas;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LAMGestRestaurant.Infrastructure.Dados.Configuracoes;

public class EstacaoPreparacaoConfig : IEntityTypeConfiguration<EstacaoPreparacao>
{
    public void Configure(EntityTypeBuilder<EstacaoPreparacao> e) => e.HasIndex(x => x.Nome).IsUnique();
}

public class CategoriaConfig : IEntityTypeConfiguration<Categoria>
{
    public void Configure(EntityTypeBuilder<Categoria> e)
    {
        e.Property(x => x.Nome).HasMaxLength(100).IsRequired();
        e.Property(x => x.Cor).HasMaxLength(9);
        e.HasOne(x => x.CategoriaPai).WithMany(x => x.Subcategorias).HasForeignKey(x => x.CategoriaPaiId);
        e.HasIndex(x => new { x.CategoriaPaiId, x.Nome }).IsUnique();
    }
}

public class TaxaImpostoConfig : IEntityTypeConfiguration<TaxaImposto>
{
    public void Configure(EntityTypeBuilder<TaxaImposto> e)
    {
        e.HasIndex(x => x.Nome).IsUnique();
        e.ToTable(t => t.HasCheckConstraint("CK_TaxasImposto_Percentagem", "[Percentagem] >= 0 AND [Percentagem] <= 100"));
    }
}

public class ProdutoConfig : IEntityTypeConfiguration<Produto>
{
    public void Configure(EntityTypeBuilder<Produto> e)
    {
        e.Property(x => x.Codigo).HasMaxLength(30).IsRequired();
        e.Property(x => x.CodigoBarras).HasMaxLength(50);
        e.Property(x => x.Nome).HasMaxLength(150).IsRequired();
        e.Property(x => x.ImagemUrl).HasMaxLength(500);
        e.HasIndex(x => x.Codigo).IsUnique();
        e.HasIndex(x => x.CodigoBarras).IsUnique().HasFilter("[CodigoBarras] IS NOT NULL");
        e.HasIndex(x => x.Nome);
        e.HasIndex(x => new { x.CategoriaId, x.Activo });
        e.ToTable(t =>
        {
            t.HasCheckConstraint("CK_Produtos_PrecoVenda", "[PrecoVenda] >= 0");
            t.HasCheckConstraint("CK_Produtos_Custo", "[Custo] >= 0");
        });
    }
}

public class ZonaConfig : IEntityTypeConfiguration<Zona>
{
    public void Configure(EntityTypeBuilder<Zona> e) => e.HasIndex(x => x.Nome).IsUnique();
}

public class MesaConfig : IEntityTypeConfiguration<Mesa>
{
    public void Configure(EntityTypeBuilder<Mesa> e)
    {
        e.Property(x => x.Nome).HasMaxLength(30).IsRequired();
        e.HasIndex(x => x.Nome).IsUnique();
        e.HasIndex(x => new { x.ZonaId, x.Estado });
        e.ToTable(t => t.HasCheckConstraint("CK_Mesas_Capacidade", "[Capacidade] > 0"));
    }
}

public class ReservaConfig : IEntityTypeConfiguration<Reserva>
{
    public void Configure(EntityTypeBuilder<Reserva> e)
    {
        e.HasIndex(x => x.InicioUtc);
        e.HasIndex(x => new { x.MesaId, x.InicioUtc });
        e.HasIndex(x => x.Estado);
        e.HasIndex(x => x.Telefone);
        e.ToTable(t =>
        {
            t.HasCheckConstraint("CK_Reservas_Pessoas", "[NumeroPessoas] > 0");
            t.HasCheckConstraint("CK_Reservas_Duracao", "[DuracaoMinutos] > 0");
        });
    }
}

public class PedidoConfig : IEntityTypeConfiguration<Pedido>
{
    public void Configure(EntityTypeBuilder<Pedido> e)
    {
        e.Property(x => x.Numero).HasMaxLength(30).IsRequired();
        e.Property(x => x.IdempotencyKey).HasMaxLength(64);
        e.HasIndex(x => x.Numero).IsUnique();
        e.HasIndex(x => x.IdempotencyKey).IsUnique().HasFilter("[IdempotencyKey] IS NOT NULL");
        e.HasIndex(x => new { x.Estado, x.Tipo });
        e.HasIndex(x => new { x.MesaId, x.Estado });
        e.HasIndex(x => x.CriadoEmUtc);
        e.HasIndex(x => x.FechadoEmUtc);
        e.HasIndex(x => x.SessaoCaixaId);
        e.HasIndex(x => x.ClienteId);
        e.HasOne(x => x.Garcom).WithMany().HasForeignKey(x => x.GarcomId);
        e.HasOne(x => x.Estafeta).WithMany().HasForeignKey(x => x.EstafetaId);
        e.HasMany(x => x.Itens).WithOne(x => x.Pedido).HasForeignKey(x => x.PedidoId);
        e.HasMany(x => x.Historico).WithOne(x => x.Pedido).HasForeignKey(x => x.PedidoId);
        e.ToTable(t =>
        {
            t.HasCheckConstraint("CK_Pedidos_Totais", "[Subtotal] >= 0 AND [Desconto] >= 0 AND [Impostos] >= 0 AND [TaxaEntrega] >= 0 AND [Total] >= 0 AND [TotalPago] >= 0");
            t.HasCheckConstraint("CK_Pedidos_Mesa", "[Tipo] <> 'Mesa' OR [MesaId] IS NOT NULL");
        });
    }
}

public class PedidoItemConfig : IEntityTypeConfiguration<PedidoItem>
{
    public void Configure(EntityTypeBuilder<PedidoItem> e)
    {
        e.Property(x => x.NomeProduto).HasMaxLength(150).IsRequired();
        e.HasIndex(x => new { x.EstadoCozinha, x.EnviadoCozinhaEmUtc });
        e.HasIndex(x => x.ProdutoId);
        e.ToTable(t =>
        {
            t.HasCheckConstraint("CK_PedidoItens_Quantidade", "[Quantidade] > 0");
            t.HasCheckConstraint("CK_PedidoItens_Valores", "[PrecoUnitario] >= 0 AND [Desconto] >= 0 AND [Total] >= 0");
        });
    }
}

public class PedidoHistoricoConfig : IEntityTypeConfiguration<PedidoHistorico>
{
    public void Configure(EntityTypeBuilder<PedidoHistorico> e)
    {
        e.Property(x => x.Accao).HasMaxLength(50).IsRequired();
        e.Property(x => x.EstadoAnterior).HasMaxLength(30);
        e.Property(x => x.EstadoNovo).HasMaxLength(30);
        e.HasOne<PedidoItem>().WithMany().HasForeignKey(x => x.PedidoItemId);
        e.HasIndex(x => new { x.PedidoId, x.DataUtc });
    }
}

public class TransferenciaMesaConfig : IEntityTypeConfiguration<TransferenciaMesa>
{
    public void Configure(EntityTypeBuilder<TransferenciaMesa> e)
    {
        e.HasOne(x => x.MesaOrigem).WithMany().HasForeignKey(x => x.MesaOrigemId);
        e.HasOne(x => x.MesaDestino).WithMany().HasForeignKey(x => x.MesaDestinoId);
        e.HasIndex(x => x.PedidoId);
    }
}

public class TerminalConfig : IEntityTypeConfiguration<Terminal>
{
    public void Configure(EntityTypeBuilder<Terminal> e) => e.HasIndex(x => x.Nome).IsUnique();
}

public class MetodoPagamentoConfig : IEntityTypeConfiguration<MetodoPagamento>
{
    public void Configure(EntityTypeBuilder<MetodoPagamento> e)
    {
        e.Property(x => x.Codigo).HasMaxLength(20).IsRequired();
        e.HasIndex(x => x.Codigo).IsUnique();
    }
}

public class SessaoCaixaConfig : IEntityTypeConfiguration<SessaoCaixa>
{
    public void Configure(EntityTypeBuilder<SessaoCaixa> e)
    {
        // Só pode existir uma sessão aberta por terminal.
        e.HasIndex(x => x.TerminalId).IsUnique().HasFilter("[Estado] = 'Aberta'").HasDatabaseName("UX_SessoesCaixa_TerminalAberta");
        e.HasIndex(x => new { x.UtilizadorAberturaId, x.Estado });
        e.HasIndex(x => x.AbertaEmUtc);
        e.HasMany(x => x.Movimentos).WithOne(x => x.SessaoCaixa).HasForeignKey(x => x.SessaoCaixaId);
        e.HasMany(x => x.LinhasFecho).WithOne(x => x.SessaoCaixa).HasForeignKey(x => x.SessaoCaixaId);
        e.ToTable(t => t.HasCheckConstraint("CK_SessoesCaixa_Fundo", "[FundoInicial] >= 0"));
    }
}

public class MovimentoCaixaConfig : IEntityTypeConfiguration<MovimentoCaixa>
{
    public void Configure(EntityTypeBuilder<MovimentoCaixa> e)
    {
        e.HasOne<Domain.Pessoal.Despesa>().WithMany().HasForeignKey(x => x.DespesaId);
        e.HasIndex(x => x.DataUtc);
        e.ToTable(t => t.HasCheckConstraint("CK_MovimentosCaixa_Valor", "[Valor] > 0"));
    }
}

public class FechoCaixaLinhaConfig : IEntityTypeConfiguration<FechoCaixaLinha>
{
    public void Configure(EntityTypeBuilder<FechoCaixaLinha> e) =>
        e.HasIndex(x => new { x.SessaoCaixaId, x.MetodoPagamentoId }).IsUnique();
}

public class PagamentoConfig : IEntityTypeConfiguration<Pagamento>
{
    public void Configure(EntityTypeBuilder<Pagamento> e)
    {
        e.Property(x => x.IdempotencyKey).HasMaxLength(64).IsRequired();
        e.Property(x => x.Referencia).HasMaxLength(100);
        e.HasIndex(x => x.IdempotencyKey).IsUnique();
        e.HasIndex(x => x.PedidoId);
        e.HasIndex(x => new { x.SessaoCaixaId, x.Estado });
        e.HasIndex(x => x.DataUtc);
        e.ToTable(t => t.HasCheckConstraint("CK_Pagamentos_Valores", "[Valor] > 0 AND [ValorRecebido] >= [Valor] AND [Troco] >= 0"));
    }
}

public class ReciboConfig : IEntityTypeConfiguration<Recibo>
{
    public void Configure(EntityTypeBuilder<Recibo> e)
    {
        e.Property(x => x.Numero).HasMaxLength(30).IsRequired();
        e.HasIndex(x => x.Numero).IsUnique();
        e.HasIndex(x => x.PedidoId);
    }
}

public class SerieDocumentoConfig : IEntityTypeConfiguration<SerieDocumento>
{
    public void Configure(EntityTypeBuilder<SerieDocumento> e)
    {
        e.Property(x => x.Codigo).HasMaxLength(10).IsRequired();
        e.Property(x => x.Prefixo).HasMaxLength(10).IsRequired();
        e.HasIndex(x => new { x.Codigo, x.Ano }).IsUnique();
    }
}
