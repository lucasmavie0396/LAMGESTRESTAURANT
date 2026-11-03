using System.Reflection;
using LAMGestRestaurant.Application.Comum;
using LAMGestRestaurant.Domain.Caixa;
using LAMGestRestaurant.Domain.Catalogo;
using LAMGestRestaurant.Domain.Clientes;
using LAMGestRestaurant.Domain.Comum;
using LAMGestRestaurant.Domain.Compras;
using LAMGestRestaurant.Domain.Pessoal;
using LAMGestRestaurant.Domain.Sala;
using LAMGestRestaurant.Domain.Sistema;
using LAMGestRestaurant.Domain.Stock;
using LAMGestRestaurant.Domain.Vendas;
using LAMGestRestaurant.Infrastructure.Identidade;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace LAMGestRestaurant.Infrastructure.Dados;

public class AppDbContext(
    DbContextOptions<AppDbContext> options,
    IUtilizadorActual utilizadorActual,
    TimeProvider relogio)
    : IdentityDbContext<Utilizador, Perfil, int>(options), IAppDbContext
{
    // Catálogo
    public DbSet<EstacaoPreparacao> EstacoesPreparacao => Set<EstacaoPreparacao>();
    public DbSet<Categoria> Categorias => Set<Categoria>();
    public DbSet<TaxaImposto> TaxasImposto => Set<TaxaImposto>();
    public DbSet<Produto> Produtos => Set<Produto>();
    public DbSet<ImagemProduto> ImagensProduto => Set<ImagemProduto>();

    // Sala
    public DbSet<Zona> Zonas => Set<Zona>();
    public DbSet<Mesa> Mesas => Set<Mesa>();
    public DbSet<Reserva> Reservas => Set<Reserva>();

    // Vendas
    public DbSet<Pedido> Pedidos => Set<Pedido>();
    public DbSet<PedidoItem> PedidoItens => Set<PedidoItem>();
    public DbSet<PedidoHistorico> HistoricoPedidos => Set<PedidoHistorico>();
    public DbSet<TransferenciaMesa> TransferenciasMesa => Set<TransferenciaMesa>();

    // Caixa e pagamentos
    public DbSet<Terminal> Terminais => Set<Terminal>();
    public DbSet<MetodoPagamento> MetodosPagamento => Set<MetodoPagamento>();
    public DbSet<SessaoCaixa> SessoesCaixa => Set<SessaoCaixa>();
    public DbSet<MovimentoCaixa> MovimentosCaixa => Set<MovimentoCaixa>();
    public DbSet<FechoCaixaLinha> FechoCaixaLinhas => Set<FechoCaixaLinha>();
    public DbSet<Pagamento> Pagamentos => Set<Pagamento>();
    public DbSet<Recibo> Recibos => Set<Recibo>();
    public DbSet<SerieDocumento> SeriesDocumento => Set<SerieDocumento>();

    // Stock e receitas
    public DbSet<UnidadeMedida> UnidadesMedida => Set<UnidadeMedida>();
    public DbSet<Armazem> Armazens => Set<Armazem>();
    public DbSet<Artigo> Artigos => Set<Artigo>();
    public DbSet<StockSaldo> StockSaldos => Set<StockSaldo>();
    public DbSet<MovimentoStock> MovimentosStock => Set<MovimentoStock>();
    public DbSet<Inventario> Inventarios => Set<Inventario>();
    public DbSet<InventarioLinha> InventarioLinhas => Set<InventarioLinha>();
    public DbSet<Receita> Receitas => Set<Receita>();
    public DbSet<ReceitaItem> ReceitaItens => Set<ReceitaItem>();

    // Compras
    public DbSet<Fornecedor> Fornecedores => Set<Fornecedor>();
    public DbSet<Compra> Compras => Set<Compra>();
    public DbSet<CompraItem> CompraItens => Set<CompraItem>();

    // Clientes
    public DbSet<Cliente> Clientes => Set<Cliente>();
    public DbSet<MovimentoPontos> MovimentosPontos => Set<MovimentoPontos>();

    // Pessoal e despesas
    public DbSet<Funcionario> Funcionarios => Set<Funcionario>();
    public DbSet<CategoriaDespesa> CategoriasDespesa => Set<CategoriaDespesa>();
    public DbSet<Despesa> Despesas => Set<Despesa>();

    // Sistema
    public DbSet<Configuracao> Configuracoes => Set<Configuracao>();
    public DbSet<Impressora> Impressoras => Set<Impressora>();
    public DbSet<RegistoAuditoria> RegistosAuditoria => Set<RegistoAuditoria>();

    protected override void ConfigureConventions(ModelConfigurationBuilder builder)
    {
        // Valores monetários por omissão; quantidades, taxas e custos unitários são ajustados em AplicarConvencoes.
        builder.Properties<decimal>().HavePrecision(18, 2);
        builder.Properties<DateTime>().HaveConversion<ConversorUtc>();
        builder.Properties<DateTime?>().HaveConversion<ConversorUtcNulo>();
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        ConfigurarIdentidade(builder);
        builder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
        AplicarConvencoes(builder);
    }

    private static void ConfigurarIdentidade(ModelBuilder builder)
    {
        builder.Entity<Utilizador>(e =>
        {
            e.ToTable("Utilizadores");
            e.Property(u => u.NomeCompleto).HasMaxLength(200).IsRequired();
        });
        builder.Entity<Perfil>(e =>
        {
            e.ToTable("Perfis");
            e.Property(p => p.Descricao).HasMaxLength(500);
        });
        builder.Entity<IdentityUserRole<int>>().ToTable("UtilizadorPerfis");
        builder.Entity<IdentityRoleClaim<int>>().ToTable("PerfilPermissoes");
        builder.Entity<IdentityUserClaim<int>>().ToTable("UtilizadorClaims");
        builder.Entity<IdentityUserLogin<int>>().ToTable("UtilizadorLogins");
        builder.Entity<IdentityUserToken<int>>().ToTable("UtilizadorTokens");
    }

    /// <summary>
    /// Convenções aplicadas às entidades de domínio:
    /// - precisão decimal por nome da propriedade (quantidades 18,3; percentagens 9,4; custos unitários 18,4);
    /// - comprimento por omissão dos textos (200; observações/descrições 1000; Detalhes sem limite);
    /// - enums como texto (30);
    /// - rowversion em IConcorrente;
    /// - colunas *PorId / Utilizador*Id com FK para Utilizadores;
    /// - eliminação em cascata desactivada (registos históricos nunca são apagados).
    /// </summary>
    private static void AplicarConvencoes(ModelBuilder builder)
    {
        var dominio = typeof(Entidade).Assembly;

        foreach (var tipo in builder.Model.GetEntityTypes().Where(t => t.ClrType.Assembly == dominio).ToList())
        {
            var entidade = builder.Entity(tipo.ClrType);

            foreach (var propriedade in tipo.GetProperties().ToList())
            {
                var nome = propriedade.Name;
                var clr = Nullable.GetUnderlyingType(propriedade.ClrType) ?? propriedade.ClrType;

                if (clr == typeof(decimal))
                {
                    if (nome.StartsWith("Quantidade") || nome == "StockMinimo")
                        entidade.Property(nome).HasPrecision(18, 3);
                    else if (nome.Contains("Percentagem"))
                        entidade.Property(nome).HasPrecision(9, 4);
                    else if (nome is "CustoUnitario" or "CustoMedio" or "UltimoCusto")
                        entidade.Property(nome).HasPrecision(18, 4);
                    else if (nome == "FactorParaBase")
                        entidade.Property(nome).HasPrecision(18, 6);
                }
                else if (clr == typeof(string) && propriedade.GetMaxLength() is null)
                {
                    if (nome == "Detalhes" && tipo.ClrType == typeof(RegistoAuditoria))
                        continue;
                    var longo = nome.StartsWith("Observacoes") || nome.StartsWith("Descricao") || nome.StartsWith("Motivo")
                                || nome.StartsWith("Endereco") || nome == "Detalhes" || nome == "Valor";
                    entidade.Property(nome).HasMaxLength(longo ? 1000 : 200);
                }
                else if (clr.IsEnum)
                {
                    entidade.Property(nome).HasConversion<string>().HasMaxLength(30);
                }
                else if (clr == typeof(int) && tipo.ClrType != typeof(RegistoAuditoria)
                         && (nome.EndsWith("PorId") || (nome.StartsWith("Utilizador") && nome.EndsWith("Id"))))
                {
                    if (tipo.GetForeignKeys().All(fk => !fk.Properties.Any(p => p.Name == nome)))
                        entidade.HasOne(typeof(Utilizador)).WithMany().HasForeignKey(nome).OnDelete(DeleteBehavior.Restrict);
                }
            }

            if (typeof(IConcorrente).IsAssignableFrom(tipo.ClrType))
                entidade.Property(nameof(IConcorrente.RowVersion)).IsRowVersion();
        }

        // Linhas de documentos ainda editáveis (rascunho de compra, inventário aberto) mantêm a cascata configurada.
        (Type dependente, Type principal)[] comCascata = [(typeof(CompraItem), typeof(Compra)), (typeof(InventarioLinha), typeof(Inventario))];
        foreach (var fk in builder.Model.GetEntityTypes().SelectMany(t => t.GetForeignKeys()))
        {
            if (!fk.IsOwnership && fk.DeleteBehavior == DeleteBehavior.Cascade
                && fk.DeclaringEntityType.ClrType.Assembly == dominio
                && !comCascata.Contains((fk.DeclaringEntityType.ClrType, fk.PrincipalEntityType.ClrType)))
                fk.DeleteBehavior = DeleteBehavior.Restrict;
        }
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        PrepararAlteracoes();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        PrepararAlteracoes();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void PrepararAlteracoes()
    {
        var agora = relogio.GetUtcNow().UtcDateTime;
        var utilizadorId = utilizadorActual.Id;

        foreach (var entrada in ChangeTracker.Entries())
        {
            // Registos imutáveis: só inserção.
            if ((entrada.Entity is RegistoAuditoria or MovimentoStock or PedidoHistorico or MovimentoPontos)
                && (entrada.State is EntityState.Modified or EntityState.Deleted))
                throw new InvalidOperationException($"{entrada.Entity.GetType().Name} não pode ser alterado nem eliminado.");

            if (entrada.Entity is not EntidadeAuditavel auditavel)
                continue;

            if (entrada.State == EntityState.Added)
            {
                auditavel.CriadoEmUtc = agora;
                auditavel.CriadoPorId ??= utilizadorId;
            }
            else if (entrada.State == EntityState.Modified)
            {
                auditavel.AlteradoEmUtc = agora;
                auditavel.AlteradoPorId = utilizadorId;
            }
        }
    }

    private sealed class ConversorUtc() : ValueConverter<DateTime, DateTime>(
        v => v.Kind == DateTimeKind.Utc ? v : v.ToUniversalTime(),
        v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

    private sealed class ConversorUtcNulo() : ValueConverter<DateTime?, DateTime?>(
        v => v.HasValue ? (v.Value.Kind == DateTimeKind.Utc ? v.Value : v.Value.ToUniversalTime()) : v,
        v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : v);
}
