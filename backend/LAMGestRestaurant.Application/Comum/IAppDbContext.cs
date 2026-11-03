using LAMGestRestaurant.Domain.Caixa;
using LAMGestRestaurant.Domain.Catalogo;
using LAMGestRestaurant.Domain.Clientes;
using LAMGestRestaurant.Domain.Compras;
using LAMGestRestaurant.Domain.Pessoal;
using LAMGestRestaurant.Domain.Sala;
using LAMGestRestaurant.Domain.Sistema;
using LAMGestRestaurant.Domain.Stock;
using LAMGestRestaurant.Domain.Vendas;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace LAMGestRestaurant.Application.Comum;

/// <summary>Acesso aos dados para os serviços de caso de uso (implementado por AppDbContext).</summary>
public interface IAppDbContext
{
    DbSet<EstacaoPreparacao> EstacoesPreparacao { get; }
    DbSet<Categoria> Categorias { get; }
    DbSet<TaxaImposto> TaxasImposto { get; }
    DbSet<Produto> Produtos { get; }
    DbSet<ImagemProduto> ImagensProduto { get; }

    DbSet<Zona> Zonas { get; }
    DbSet<Mesa> Mesas { get; }
    DbSet<Reserva> Reservas { get; }

    DbSet<Pedido> Pedidos { get; }
    DbSet<PedidoItem> PedidoItens { get; }
    DbSet<PedidoHistorico> HistoricoPedidos { get; }
    DbSet<TransferenciaMesa> TransferenciasMesa { get; }

    DbSet<Terminal> Terminais { get; }
    DbSet<MetodoPagamento> MetodosPagamento { get; }
    DbSet<SessaoCaixa> SessoesCaixa { get; }
    DbSet<MovimentoCaixa> MovimentosCaixa { get; }
    DbSet<FechoCaixaLinha> FechoCaixaLinhas { get; }
    DbSet<Pagamento> Pagamentos { get; }
    DbSet<Recibo> Recibos { get; }
    DbSet<SerieDocumento> SeriesDocumento { get; }

    DbSet<UnidadeMedida> UnidadesMedida { get; }
    DbSet<Armazem> Armazens { get; }
    DbSet<Artigo> Artigos { get; }
    DbSet<StockSaldo> StockSaldos { get; }
    DbSet<MovimentoStock> MovimentosStock { get; }
    DbSet<Inventario> Inventarios { get; }
    DbSet<InventarioLinha> InventarioLinhas { get; }
    DbSet<Receita> Receitas { get; }
    DbSet<ReceitaItem> ReceitaItens { get; }

    DbSet<Fornecedor> Fornecedores { get; }
    DbSet<Compra> Compras { get; }
    DbSet<CompraItem> CompraItens { get; }

    DbSet<Cliente> Clientes { get; }
    DbSet<MovimentoPontos> MovimentosPontos { get; }

    DbSet<Funcionario> Funcionarios { get; }
    DbSet<CategoriaDespesa> CategoriasDespesa { get; }
    DbSet<Despesa> Despesas { get; }

    DbSet<Configuracao> Configuracoes { get; }
    DbSet<Impressora> Impressoras { get; }

    DatabaseFacade Database { get; }
    EntityEntry<TEntity> Entry<TEntity>(TEntity entity) where TEntity : class;
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

public static class ExtensoesDados
{
    /// <summary>
    /// Concorrência optimista: usa a versão que o cliente leu como valor original, para que o
    /// SaveChanges falhe (409) se outra sessão tiver alterado o registo entretanto.
    /// </summary>
    public static void VerificarVersao<T>(this IAppDbContext db, T entidade, byte[]? versaoLida) where T : class, Domain.Comum.IConcorrente
    {
        if (versaoLida is null || versaoLida.Length == 0)
            throw new RegraNegocioException("Falta a versão do registo. Actualize os dados e tente novamente.");
        db.Entry(entidade).Property(e => e.RowVersion).OriginalValue = versaoLida;
    }
}
