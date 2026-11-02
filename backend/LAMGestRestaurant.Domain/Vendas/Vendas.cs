using LAMGestRestaurant.Domain.Caixa;
using LAMGestRestaurant.Domain.Catalogo;
using LAMGestRestaurant.Domain.Clientes;
using LAMGestRestaurant.Domain.Comum;
using LAMGestRestaurant.Domain.Pessoal;
using LAMGestRestaurant.Domain.Sala;

namespace LAMGestRestaurant.Domain.Vendas;

public class Pedido : EntidadeAuditavel, IConcorrente
{
    /// <summary>Número sequencial legível (ex.: P2026-000123), atribuído pela série "PED".</summary>
    public string Numero { get; set; } = string.Empty;
    public TipoPedido Tipo { get; set; }
    public EstadoPedido Estado { get; set; } = EstadoPedido.Aberto;
    /// <summary>Só para Take Away / Delivery.</summary>
    public EstadoEntrega? EstadoEntrega { get; set; }

    public int? MesaId { get; set; }
    public Mesa? Mesa { get; set; }
    public int? ClienteId { get; set; }
    public Cliente? Cliente { get; set; }
    public int? GarcomId { get; set; }
    public Funcionario? Garcom { get; set; }
    public int? EstafetaId { get; set; }
    public Funcionario? Estafeta { get; set; }
    /// <summary>Sessão de caixa onde o pedido foi pago (preenchida no fecho).</summary>
    public int? SessaoCaixaId { get; set; }
    public SessaoCaixa? SessaoCaixa { get; set; }

    public int NumeroPessoas { get; set; }
    public string? ContactoEntrega { get; set; }
    public string? EnderecoEntrega { get; set; }
    public string? Observacoes { get; set; }

    // Totais calculados sempre no servidor a partir dos itens não cancelados.
    public decimal Subtotal { get; set; }
    public decimal Desconto { get; set; }
    public decimal Impostos { get; set; }
    public decimal TaxaEntrega { get; set; }
    public decimal Total { get; set; }
    public decimal TotalPago { get; set; }

    public DateTime? FechadoEmUtc { get; set; }
    public DateTime? CanceladoEmUtc { get; set; }
    public int? CanceladoPorId { get; set; }
    public string? MotivoCancelamento { get; set; }

    /// <summary>Verdadeiro depois de a baixa de stock da venda ter sido feita (garante uma única baixa).</summary>
    public bool StockBaixado { get; set; }

    /// <summary>Chave enviada pelo cliente no cabeçalho Idempotency-Key ao criar o pedido.</summary>
    public string? IdempotencyKey { get; set; }

    public List<PedidoItem> Itens { get; set; } = [];
    public List<PedidoHistorico> Historico { get; set; } = [];
    public byte[] RowVersion { get; set; } = [];
}

public class PedidoItem : EntidadeAuditavel
{
    public int PedidoId { get; set; }
    public Pedido Pedido { get; set; } = null!;
    public int ProdutoId { get; set; }
    public Produto Produto { get; set; } = null!;

    // Nome, preço e taxa são copiados no momento da venda para preservar o histórico
    // mesmo que o produto seja alterado depois (redundância intencional).
    public string NomeProduto { get; set; } = string.Empty;
    public decimal PrecoUnitario { get; set; }
    public decimal TaxaImpostoPercentagem { get; set; }

    public decimal Quantidade { get; set; }
    public decimal Desconto { get; set; }
    public decimal ValorImposto { get; set; }
    public decimal Total { get; set; }
    public string? Observacoes { get; set; }

    public bool EnviaCozinha { get; set; }
    public EstadoItemCozinha EstadoCozinha { get; set; } = EstadoItemCozinha.NaoEnviado;
    public DateTime? EnviadoCozinhaEmUtc { get; set; }
    public DateTime? ProntoEmUtc { get; set; }

    public bool Cancelado { get; set; }
    public DateTime? CanceladoEmUtc { get; set; }
    public int? CanceladoPorId { get; set; }
    public string? MotivoCancelamento { get; set; }
}

/// <summary>Registo de mudanças de estado do pedido e dos itens (inclui cozinha e transferências).</summary>
public class PedidoHistorico : Entidade
{
    public int PedidoId { get; set; }
    public Pedido Pedido { get; set; } = null!;
    public int? PedidoItemId { get; set; }
    public string Accao { get; set; } = string.Empty;
    public string? EstadoAnterior { get; set; }
    public string? EstadoNovo { get; set; }
    public string? Detalhes { get; set; }
    public int? UtilizadorId { get; set; }
    public DateTime DataUtc { get; set; }
}

public class TransferenciaMesa : Entidade
{
    public int PedidoId { get; set; }
    public Pedido Pedido { get; set; } = null!;
    public int MesaOrigemId { get; set; }
    public Mesa MesaOrigem { get; set; } = null!;
    public int MesaDestinoId { get; set; }
    public Mesa MesaDestino { get; set; } = null!;
    public string? Motivo { get; set; }
    public int UtilizadorId { get; set; }
    public DateTime DataUtc { get; set; }
}
