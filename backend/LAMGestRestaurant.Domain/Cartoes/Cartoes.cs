using LAMGestRestaurant.Domain.Comum;

namespace LAMGestRestaurant.Domain.Cartoes;

public enum TipoMovimentoCartao { Carregamento, Recarga, Pagamento, AnulacaoPagamento, Estorno, Descarga }

/// <summary>
/// Cartão Evento (pré-pago): cartão magnético, código de barras ou QR code com o número. O saldo só muda por movimentos
/// (carregamento, recarga, pagamento, anulação, estorno), que ficam todos registados; o cartão nunca se apaga, bloqueia-se.
/// </summary>
public class CartaoEvento : EntidadeAuditavel, IConcorrente
{
    /// <summary>Número lido pelo leitor (pista magnética, código de barras ou QR code). Único.</summary>
    public string Numero { get; set; } = string.Empty;
    public string? Titular { get; set; }
    public decimal Saldo { get; set; }
    public bool Activo { get; set; } = true;
    public byte[] RowVersion { get; set; } = [];
}

/// <summary>Movimento do saldo de um cartão (só inserção). Valor positivo entra no cartão; negativo sai.</summary>
public class MovimentoCartao : Entidade
{
    public int CartaoEventoId { get; set; }
    public CartaoEvento? CartaoEvento { get; set; }
    public TipoMovimentoCartao Tipo { get; set; }
    public decimal Valor { get; set; }
    public decimal SaldoApos { get; set; }
    /// <summary>Carregamentos e recargas: como o dinheiro entrou no caixa.</summary>
    public int? MetodoPagamentoId { get; set; }
    public Caixa.MetodoPagamento? MetodoPagamento { get; set; }
    public int? SessaoCaixaId { get; set; }
    public int? MovimentoCaixaId { get; set; }
    public Caixa.MovimentoCaixa? MovimentoCaixa { get; set; }
    /// <summary>Pagamentos, anulações e estornos: o pagamento do pedido.</summary>
    public int? PagamentoId { get; set; }
    public Caixa.Pagamento? Pagamento { get; set; }
    public int? PedidoId { get; set; }
    /// <summary>Descarga de saldo a que este movimento pertence.</summary>
    public int? DescargaId { get; set; }
    public DescargaCartoes? Descarga { get; set; }
    public string? Motivo { get; set; }
    public int UtilizadorId { get; set; }
    public DateTime DataUtc { get; set; }
    /// <summary>Carregamentos e recargas: reenvios com a mesma chave não duplicam.</summary>
    public string? IdempotencyKey { get; set; }
}

/// <summary>
/// Descarga do saldo restante de um ou mais cartões (ex.: fim do evento). Com devolução, o dinheiro sai do caixa de quem a faz;
/// sem devolução, o saldo é só retirado. Nunca se altera nem se elimina.
/// </summary>
public class DescargaCartoes : Entidade
{
    public DateTime DataUtc { get; set; }
    public int UtilizadorId { get; set; }
    public bool DevolveuDinheiro { get; set; }
    public int? MetodoPagamentoId { get; set; }
    public Caixa.MetodoPagamento? MetodoPagamento { get; set; }
    public int? SessaoCaixaId { get; set; }
    public string? Referencia { get; set; }
    public string Motivo { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public int NumeroCartoes { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
}
