using LAMGestRestaurant.Domain.Comum;
using LAMGestRestaurant.Domain.Vendas;

namespace LAMGestRestaurant.Domain.Caixa;

/// <summary>Computador/posto de caixa registado no sistema.</summary>
public class Terminal : Entidade, IDesactivavel
{
    public string Nome { get; set; } = string.Empty;
    /// <summary>Número do posto indicado no instalador do computador (atalho com ?posto=N). Único quando preenchido.</summary>
    public int? Numero { get; set; }
    public string? Descricao { get; set; }
    public bool Activo { get; set; } = true;
    /// <summary>Agente de impressão do posto: hash SHA-256 da chave (a chave só existe no computador do posto).</summary>
    public string? AgenteTokenHash { get; set; }
    public DateTime? AgenteVistoEmUtc { get; set; }
    /// <summary>Impressoras instaladas no Windows do posto, enviadas pelo agente (JSON).</summary>
    public string? AgenteImpressoras { get; set; }
    public string? AgenteVersao { get; set; }

    /// <summary>Último sinal do navegador deste posto (enviado a cada minuto enquanto o sistema está aberto).</summary>
    public DateTime? VistoEmUtc { get; set; }
    public int? VistoUtilizadorId { get; set; }
    /// <summary>Endereço IP de onde veio o último sinal.</summary>
    public string? VistoEndereco { get; set; }

    /// <summary>Posto escolhido para a ligação dos tablets (app Android) pela rede. No máximo um.</summary>
    public bool LigacaoTablets { get; set; }
    /// <summary>Hash SHA-256 do código de emparelhamento dos tablets (o código só se mostra ao gerá-lo).</summary>
    public string? TabletsCodigoHash { get; set; }
    public DateTime? TabletsCodigoGeradoEmUtc { get; set; }
}

public class MetodoPagamento : Entidade, IDesactivavel
{
    public string Codigo { get; set; } = string.Empty;
    public string Nome { get; set; } = string.Empty;
    /// <summary>Só métodos com troco (ex.: Dinheiro) aceitam valor recebido superior ao valor a pagar.</summary>
    public bool PermiteTroco { get; set; }
    /// <summary>Exige referência da transacção (ex.: M-Pesa, Transferência).</summary>
    public bool ExigeReferencia { get; set; }
    /// <summary>Conta para o dinheiro físico esperado na gaveta no fecho.</summary>
    public bool ContaNaGaveta { get; set; }
    public int Ordem { get; set; }
    public bool Activo { get; set; } = true;
}

/// <summary>Regra: no máximo uma sessão aberta por terminal (índice único filtrado).</summary>
public class SessaoCaixa : EntidadeAuditavel, IConcorrente
{
    public int TerminalId { get; set; }
    public Terminal Terminal { get; set; } = null!;
    public int UtilizadorAberturaId { get; set; }
    public DateTime AbertaEmUtc { get; set; }
    public decimal FundoInicial { get; set; }
    public EstadoSessaoCaixa Estado { get; set; } = EstadoSessaoCaixa.Aberta;
    public int? UtilizadorFechoId { get; set; }
    public DateTime? FechadaEmUtc { get; set; }
    public string? ObservacoesAbertura { get; set; }
    public string? ObservacoesFecho { get; set; }
    public List<MovimentoCaixa> Movimentos { get; set; } = [];
    public List<FechoCaixaLinha> LinhasFecho { get; set; } = [];
    public byte[] RowVersion { get; set; } = [];
}

/// <summary>Entradas e saídas manuais de caixa (não inclui pagamentos de pedidos).</summary>
public class MovimentoCaixa : Entidade
{
    public int SessaoCaixaId { get; set; }
    public SessaoCaixa SessaoCaixa { get; set; } = null!;
    public TipoMovimentoCaixa Tipo { get; set; }
    public int MetodoPagamentoId { get; set; }
    public MetodoPagamento MetodoPagamento { get; set; } = null!;
    /// <summary>Valor sempre positivo; o sinal resulta do tipo.</summary>
    public decimal Valor { get; set; }
    public string Motivo { get; set; } = string.Empty;
    public int? DespesaId { get; set; }
    public int UtilizadorId { get; set; }
    public DateTime DataUtc { get; set; }
}

/// <summary>Contagem por método de pagamento no fecho da sessão.</summary>
public class FechoCaixaLinha : Entidade
{
    public int SessaoCaixaId { get; set; }
    public SessaoCaixa SessaoCaixa { get; set; } = null!;
    public int MetodoPagamentoId { get; set; }
    public MetodoPagamento MetodoPagamento { get; set; } = null!;
    public decimal ValorEsperado { get; set; }
    public decimal ValorContado { get; set; }
    public decimal Diferenca { get; set; }
}

public class Pagamento : EntidadeAuditavel
{
    public int PedidoId { get; set; }
    public Pedido Pedido { get; set; } = null!;
    public int SessaoCaixaId { get; set; }
    public SessaoCaixa SessaoCaixa { get; set; } = null!;
    public int MetodoPagamentoId { get; set; }
    public MetodoPagamento MetodoPagamento { get; set; } = null!;
    /// <summary>Valor imputado ao pedido.</summary>
    public decimal Valor { get; set; }
    public decimal ValorRecebido { get; set; }
    public decimal Troco { get; set; }
    public string? Referencia { get; set; }
    public EstadoPagamento Estado { get; set; } = EstadoPagamento.Valido;
    public DateTime DataUtc { get; set; }
    public int UtilizadorId { get; set; }
    public DateTime? AnuladoEmUtc { get; set; }
    public int? AnuladoPorId { get; set; }
    public string? MotivoAnulacao { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
}

/// <summary>Recibo interno (não é documento fiscal certificado).</summary>
public class Recibo : Entidade
{
    public string Numero { get; set; } = string.Empty;
    public int PedidoId { get; set; }
    public Pedido Pedido { get; set; } = null!;
    public decimal Total { get; set; }
    public DateTime EmitidoEmUtc { get; set; }
    public int UtilizadorId { get; set; }
    public bool Anulado { get; set; }
    public DateTime? AnuladoEmUtc { get; set; }
    public int? AnuladoPorId { get; set; }
    public string? MotivoAnulacao { get; set; }
}

/// <summary>Numeração sequencial por série e ano. Incrementada sob concorrência optimista.</summary>
public class SerieDocumento : Entidade, IConcorrente
{
    public string Codigo { get; set; } = string.Empty;
    public string Prefixo { get; set; } = string.Empty;
    public int Ano { get; set; }
    public int UltimoNumero { get; set; }
    public byte[] RowVersion { get; set; } = [];
}
