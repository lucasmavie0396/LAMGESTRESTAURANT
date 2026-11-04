using LAMGestRestaurant.Domain.Clientes;
using LAMGestRestaurant.Domain.Pessoal;
using LAMGestRestaurant.Domain.Sistema;
using LAMGestRestaurant.Domain.Vendas;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LAMGestRestaurant.Infrastructure.Dados.Configuracoes;

public class ClienteConfig : IEntityTypeConfiguration<Cliente>
{
    public void Configure(EntityTypeBuilder<Cliente> e)
    {
        e.Property(x => x.Nome).HasMaxLength(150).IsRequired();
        e.Property(x => x.Telefone).HasMaxLength(30);
        e.Property(x => x.Nuit).HasMaxLength(20);
        e.Property(x => x.CartaoFidelidade).HasMaxLength(30);
        e.HasIndex(x => x.Nome);
        e.HasIndex(x => x.Telefone).IsUnique().HasFilter("[Telefone] IS NOT NULL");
        e.HasIndex(x => x.CartaoFidelidade).IsUnique().HasFilter("[CartaoFidelidade] IS NOT NULL");
        e.HasIndex(x => x.Nuit);
        e.ToTable(t => t.HasCheckConstraint("CK_Clientes_Pontos", "[SaldoPontos] >= 0"));
    }
}

public class MovimentoPontosConfig : IEntityTypeConfiguration<MovimentoPontos>
{
    public void Configure(EntityTypeBuilder<MovimentoPontos> e)
    {
        e.HasOne<Pedido>().WithMany().HasForeignKey(x => x.PedidoId);
        e.HasIndex(x => new { x.ClienteId, x.DataUtc });
    }
}

public class FuncionarioConfig : IEntityTypeConfiguration<Funcionario>
{
    public void Configure(EntityTypeBuilder<Funcionario> e)
    {
        e.Property(x => x.Nome).HasMaxLength(150).IsRequired();
        e.Property(x => x.Telefone).HasMaxLength(30);
        e.Property(x => x.DocumentoIdentificacao).HasMaxLength(50);
        e.HasIndex(x => x.Nome);
        e.HasIndex(x => x.UtilizadorId).IsUnique().HasFilter("[UtilizadorId] IS NOT NULL");
    }
}

public class CategoriaDespesaConfig : IEntityTypeConfiguration<CategoriaDespesa>
{
    public void Configure(EntityTypeBuilder<CategoriaDespesa> e) => e.HasIndex(x => x.Nome).IsUnique();
}

public class DespesaConfig : IEntityTypeConfiguration<Despesa>
{
    public void Configure(EntityTypeBuilder<Despesa> e)
    {
        e.Property(x => x.Descricao).HasMaxLength(500).IsRequired();
        e.Property(x => x.Referencia).HasMaxLength(100);
        e.HasIndex(x => x.DataUtc);
        e.HasIndex(x => new { x.CategoriaDespesaId, x.DataUtc });
        e.HasIndex(x => x.SessaoCaixaId);
        e.ToTable(t => t.HasCheckConstraint("CK_Despesas_Valor", "[Valor] > 0"));
    }
}

public class ConfiguracaoConfig : IEntityTypeConfiguration<Configuracao>
{
    public void Configure(EntityTypeBuilder<Configuracao> e)
    {
        e.Property(x => x.Chave).HasMaxLength(100).IsRequired();
        e.HasIndex(x => x.Chave).IsUnique();
    }
}

public class ImpressoraConfig : IEntityTypeConfiguration<Impressora>
{
    public void Configure(EntityTypeBuilder<Impressora> e)
    {
        e.HasIndex(x => x.Nome).IsUnique();
        // Mesmo tamanho da antiga coluna Endereco (renomeada), para a migration ser só uma mudança de nome.
        e.Property(x => x.NomeWindows).HasMaxLength(1000);
        e.HasOne(x => x.Terminal).WithMany().HasForeignKey(x => x.TerminalId);
        e.HasOne(x => x.Posto).WithMany().HasForeignKey(x => x.PostoId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class TrabalhoImpressaoPostoConfig : IEntityTypeConfiguration<TrabalhoImpressaoPosto>
{
    public void Configure(EntityTypeBuilder<TrabalhoImpressaoPosto> e)
    {
        e.ToTable("TrabalhosImpressaoPosto");
        e.HasOne(x => x.Terminal).WithMany().HasForeignKey(x => x.TerminalId).OnDelete(DeleteBehavior.Restrict);
        e.Property(x => x.NomeWindows).HasMaxLength(1000);
        e.Property(x => x.Titulo).HasMaxLength(200);
        e.Property(x => x.Documento).HasMaxLength(1_000_000);
        e.Property(x => x.Erro).HasMaxLength(1000);
        e.HasIndex(x => new { x.TerminalId, x.ConcluidoEmUtc });
    }
}

public class LicencaSistemaConfig : IEntityTypeConfiguration<LicencaSistema>
{
    public void Configure(EntityTypeBuilder<LicencaSistema> e)
    {
        e.ToTable("Licencas");
        e.HasIndex(x => x.LicencaId).IsUnique();
        e.Property(x => x.NomeComercial).HasMaxLength(150);
        e.Property(x => x.Nuit).HasMaxLength(9);
        e.Property(x => x.Texto).HasMaxLength(2000);
    }
}

public class RegistoAuditoriaConfig : IEntityTypeConfiguration<RegistoAuditoria>
{
    public void Configure(EntityTypeBuilder<RegistoAuditoria> e)
    {
        e.ToTable("RegistosAuditoria");
        e.Property(x => x.Accao).HasMaxLength(60).IsRequired();
        e.Property(x => x.NomeUtilizador).HasMaxLength(256);
        e.Property(x => x.Entidade).HasMaxLength(60);
        e.Property(x => x.EntidadeId).HasMaxLength(60);
        e.Property(x => x.EnderecoIp).HasMaxLength(45);
        // Sem FK para Utilizadores: o registo tem de sobreviver a qualquer alteração de contas.
        e.HasIndex(x => x.DataUtc);
        e.HasIndex(x => new { x.Entidade, x.EntidadeId });
        e.HasIndex(x => new { x.UtilizadorId, x.DataUtc });
        e.HasIndex(x => x.Accao);
    }
}
