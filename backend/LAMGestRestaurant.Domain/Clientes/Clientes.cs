using LAMGestRestaurant.Domain.Comum;

namespace LAMGestRestaurant.Domain.Clientes;

/// <summary>Telefone e cartão de fidelidade são únicos quando preenchidos (evita duplicação).</summary>
public class Cliente : EntidadeAuditavel, IDesactivavel, IConcorrente
{
    public TipoCliente Tipo { get; set; } = TipoCliente.Particular;
    public string Nome { get; set; } = string.Empty;
    public string? Telefone { get; set; }
    public string? Email { get; set; }
    public string? Endereco { get; set; }
    public string? Nuit { get; set; }
    public string? Observacoes { get; set; }
    public string? CartaoFidelidade { get; set; }
    /// <summary>Saldo de pontos mantido em sincronia com MovimentoPontos (ver comentário em StockSaldo).</summary>
    public int SaldoPontos { get; set; }
    public bool Activo { get; set; } = true;
    public byte[] RowVersion { get; set; } = [];
}

public class MovimentoPontos : Entidade
{
    public int ClienteId { get; set; }
    public Cliente Cliente { get; set; } = null!;
    public int? PedidoId { get; set; }
    public TipoMovimentoPontos Tipo { get; set; }
    /// <summary>Pontos com sinal.</summary>
    public int Pontos { get; set; }
    public string? Motivo { get; set; }
    public int? UtilizadorId { get; set; }
    public DateTime DataUtc { get; set; }
}
