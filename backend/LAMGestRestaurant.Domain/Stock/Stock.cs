using LAMGestRestaurant.Domain.Catalogo;
using LAMGestRestaurant.Domain.Comum;

namespace LAMGestRestaurant.Domain.Stock;

/// <summary>
/// Unidade de medida. A conversão faz-se dentro da mesma dimensão através de FactorParaBase
/// (ex.: g = 0.001 kg; ml = 0.001 l). Unidades de dimensões diferentes não são convertíveis.
/// </summary>
public class UnidadeMedida : Entidade, IDesactivavel
{
    public string Codigo { get; set; } = string.Empty;
    public string Nome { get; set; } = string.Empty;
    public DimensaoUnidade Dimensao { get; set; }
    public decimal FactorParaBase { get; set; } = 1m;
    public bool Activo { get; set; } = true;
}

public class Armazem : Entidade, IDesactivavel
{
    public string Nome { get; set; } = string.Empty;
    public bool Principal { get; set; }
    public bool Activo { get; set; } = true;
}

/// <summary>Artigo de stock: ingrediente, artigo de revenda (ex.: refrigerante) ou consumível.</summary>
public class Artigo : EntidadeAuditavel, IDesactivavel
{
    public string Codigo { get; set; } = string.Empty;
    public string Nome { get; set; } = string.Empty;
    public TipoArtigo Tipo { get; set; }
    /// <summary>Unidade em que o stock é guardado.</summary>
    public int UnidadeMedidaId { get; set; }
    public UnidadeMedida UnidadeMedida { get; set; } = null!;
    public decimal StockMinimo { get; set; }
    /// <summary>Custo médio ponderado por unidade de stock, actualizado na confirmação de compras.</summary>
    public decimal CustoMedio { get; set; }
    public decimal UltimoCusto { get; set; }
    /// <summary>Excepção autorizada à regra de stock não negativo.</summary>
    public bool PermiteStockNegativo { get; set; }
    public bool Activo { get; set; } = true;
}

/// <summary>Saldo actual por artigo/armazém. É derivável dos movimentos, mas é mantido para
/// leituras rápidas e validação de stock negativo sob concorrência (rowversion).</summary>
public class StockSaldo : Entidade, IConcorrente
{
    public int ArtigoId { get; set; }
    public Artigo Artigo { get; set; } = null!;
    public int ArmazemId { get; set; }
    public Armazem Armazem { get; set; } = null!;
    public decimal Quantidade { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

/// <summary>
/// Movimento de stock imutável. Quantidade com sinal (positivo entra, negativo sai), na unidade do artigo.
/// OrigemTipo/OrigemId identificam o documento (ex.: "PedidoItem"/123) e um índice único impede
/// movimentos duplicados do mesmo tipo para a mesma origem.
/// </summary>
public class MovimentoStock : Entidade
{
    public int ArtigoId { get; set; }
    public Artigo Artigo { get; set; } = null!;
    public int ArmazemId { get; set; }
    public Armazem Armazem { get; set; } = null!;
    public TipoMovimentoStock Tipo { get; set; }
    public decimal Quantidade { get; set; }
    public decimal CustoUnitario { get; set; }
    public string? OrigemTipo { get; set; }
    public int? OrigemId { get; set; }
    public string? Motivo { get; set; }
    public int? UtilizadorId { get; set; }
    public DateTime DataUtc { get; set; }
}

public class Inventario : EntidadeAuditavel, IConcorrente
{
    public int ArmazemId { get; set; }
    public Armazem Armazem { get; set; } = null!;
    public EstadoInventario Estado { get; set; } = EstadoInventario.Aberto;
    public string? Observacoes { get; set; }
    public DateTime? ConfirmadoEmUtc { get; set; }
    public int? ConfirmadoPorId { get; set; }
    public List<InventarioLinha> Linhas { get; set; } = [];
    public byte[] RowVersion { get; set; } = [];
}

public class InventarioLinha : Entidade
{
    public int InventarioId { get; set; }
    public Inventario Inventario { get; set; } = null!;
    public int ArtigoId { get; set; }
    public Artigo Artigo { get; set; } = null!;
    /// <summary>Saldo do sistema no momento da contagem.</summary>
    public decimal QuantidadeSistema { get; set; }
    public decimal QuantidadeContada { get; set; }
}

/// <summary>Ficha técnica versionada: alterar uma receita cria nova versão e desactiva a anterior.</summary>
public class Receita : EntidadeAuditavel
{
    public int ProdutoId { get; set; }
    public Produto Produto { get; set; } = null!;
    public int Versao { get; set; }
    public bool Activa { get; set; } = true;
    public DateTime ValidaDesdeUtc { get; set; }
    public DateTime? ValidaAteUtc { get; set; }
    public string? Observacoes { get; set; }
    public List<ReceitaItem> Itens { get; set; } = [];
}

public class ReceitaItem : Entidade
{
    public int ReceitaId { get; set; }
    public Receita Receita { get; set; } = null!;
    public int ArtigoId { get; set; }
    public Artigo Artigo { get; set; } = null!;
    /// <summary>Quantidade por 1 unidade do produto vendido, na unidade indicada.</summary>
    public decimal Quantidade { get; set; }
    public int UnidadeMedidaId { get; set; }
    public UnidadeMedida UnidadeMedida { get; set; } = null!;
}
