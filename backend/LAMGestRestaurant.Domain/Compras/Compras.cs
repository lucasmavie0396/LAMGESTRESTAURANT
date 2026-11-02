using LAMGestRestaurant.Domain.Comum;
using LAMGestRestaurant.Domain.Stock;

namespace LAMGestRestaurant.Domain.Compras;

public class Fornecedor : EntidadeAuditavel, IDesactivavel
{
    public string Nome { get; set; } = string.Empty;
    public string? Nuit { get; set; }
    public string? Telefone { get; set; }
    public string? Email { get; set; }
    public string? Endereco { get; set; }
    public string? Observacoes { get; set; }
    public bool Activo { get; set; } = true;
}

/// <summary>
/// Compra/factura de fornecedor. O stock só é movimentado na confirmação (uma vez) e
/// estornado com movimentos de sinal contrário na anulação. O histórico de custos obtém-se
/// das linhas de compras confirmadas e dos movimentos de stock.
/// </summary>
public class Compra : EntidadeAuditavel, IConcorrente
{
    public int FornecedorId { get; set; }
    public Fornecedor Fornecedor { get; set; } = null!;
    public string NumeroDocumento { get; set; } = string.Empty;
    public DateOnly DataDocumento { get; set; }
    public DateOnly? DataVencimento { get; set; }
    public int ArmazemId { get; set; }
    public Armazem Armazem { get; set; } = null!;
    public EstadoCompra Estado { get; set; } = EstadoCompra.Rascunho;
    public decimal Subtotal { get; set; }
    public decimal Desconto { get; set; }
    public decimal Impostos { get; set; }
    public decimal Total { get; set; }
    public string? Observacoes { get; set; }
    public DateTime? ConfirmadaEmUtc { get; set; }
    public int? ConfirmadaPorId { get; set; }
    public DateTime? AnuladaEmUtc { get; set; }
    public int? AnuladaPorId { get; set; }
    public string? MotivoAnulacao { get; set; }
    public List<CompraItem> Itens { get; set; } = [];
    public byte[] RowVersion { get; set; } = [];
}

public class CompraItem : Entidade
{
    public int CompraId { get; set; }
    public Compra Compra { get; set; } = null!;
    public int ArtigoId { get; set; }
    public Artigo Artigo { get; set; } = null!;
    public decimal Quantidade { get; set; }
    public int UnidadeMedidaId { get; set; }
    public UnidadeMedida UnidadeMedida { get; set; } = null!;
    public decimal CustoUnitario { get; set; }
    public decimal Desconto { get; set; }
    public decimal TaxaImpostoPercentagem { get; set; }
    public decimal ValorImposto { get; set; }
    public decimal Total { get; set; }
}
