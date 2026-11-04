using LAMGestRestaurant.Domain.Caixa;
using LAMGestRestaurant.Domain.Clientes;
using LAMGestRestaurant.Domain.Comum;
using LAMGestRestaurant.Domain.Vendas;

namespace LAMGestRestaurant.Domain.Facturacao;

/// <summary>
/// VD: venda paga no acto (emitida ao fechar o pedido no POS). FT: factura a crédito (fica em dívida até ser liquidada por recibos).
/// RC: recibo de pagamento de facturas. CT: cotação/pró-forma (sem valor fiscal). NC: nota de crédito (anula ou corrige uma FT ou VD).
/// </summary>
public enum TipoDocumento { VendaDinheiro, Factura, Recibo, Cotacao, NotaCredito }

/// <summary>Emitido: em vigor. Anulado: só recibos e cotações (com motivo). Convertido: cotação que deu origem a uma factura.</summary>
public enum EstadoDocumento { Emitido, Anulado, Convertido }

/// <summary>
/// Documento comercial/fiscal. Nunca se elimina nem se altera depois de emitido (correcções: nota de crédito;
/// recibos e cotações: anulação com motivo). Os dados do emitente e do cliente são copiados no momento da emissão,
/// para o documento continuar igual mesmo que as configurações ou a ficha do cliente mudem.
/// Os valores são com IVA incluído (como os preços de venda); a base é o total sem IVA.
/// </summary>
public class Documento : Entidade, IConcorrente
{
    public TipoDocumento Tipo { get; set; }
    /// <summary>Ex.: VD-2026-000001 (série por tipo e ano).</summary>
    public string Numero { get; set; } = string.Empty;
    public EstadoDocumento Estado { get; set; } = EstadoDocumento.Emitido;
    public DateTime EmitidoEmUtc { get; set; }
    public int UtilizadorId { get; set; }

    /// <summary>Factura: data limite de pagamento.</summary>
    public DateOnly? Vencimento { get; set; }
    /// <summary>Cotação: validade da proposta.</summary>
    public DateOnly? ValidaAte { get; set; }

    public string EmitenteNome { get; set; } = string.Empty;
    public string? EmitenteNuit { get; set; }
    public string? EmitenteEndereco { get; set; }
    public string? EmitenteTelefone { get; set; }

    public int? ClienteId { get; set; }
    public Cliente? Cliente { get; set; }
    /// <summary>«Consumidor final» quando não há cliente identificado.</summary>
    public string ClienteNome { get; set; } = string.Empty;
    public string? ClienteNuit { get; set; }
    public string? ClienteEndereco { get; set; }

    /// <summary>VD: pedido do POS que a originou.</summary>
    public int? PedidoId { get; set; }
    public Pedido? Pedido { get; set; }
    /// <summary>NC: factura ou VD corrigida. FT: cotação de origem.</summary>
    public int? DocumentoOrigemId { get; set; }
    public Documento? DocumentoOrigem { get; set; }

    /// <summary>Soma das linhas antes de descontos (com IVA).</summary>
    public decimal Subtotal { get; set; }
    public decimal Desconto { get; set; }
    /// <summary>Total sem IVA.</summary>
    public decimal BaseTributavel { get; set; }
    public decimal Iva { get; set; }
    public decimal Total { get; set; }

    /// <summary>Factura: soma dos recibos válidos que a liquidam.</summary>
    public decimal ValorPago { get; set; }
    /// <summary>Factura/VD: soma das notas de crédito emitidas sobre ela.</summary>
    public decimal ValorCreditado { get; set; }

    /// <summary>Recibo: forma de pagamento; em dinheiro (gaveta) entra na sessão de caixa.</summary>
    public int? MetodoPagamentoId { get; set; }
    public MetodoPagamento? MetodoPagamento { get; set; }
    public string? Referencia { get; set; }
    public int? SessaoCaixaId { get; set; }
    public SessaoCaixa? SessaoCaixa { get; set; }

    /// <summary>Nota de crédito: motivo da correcção.</summary>
    public string? Motivo { get; set; }
    public string? Observacoes { get; set; }

    public DateTime? AnuladoEmUtc { get; set; }
    public int? AnuladoPorId { get; set; }
    public string? MotivoAnulacao { get; set; }

    public string? IdempotencyKey { get; set; }

    public List<DocumentoLinha> Linhas { get; set; } = [];
    /// <summary>Recibo: facturas liquidadas.</summary>
    public List<LiquidacaoFactura> Liquidacoes { get; set; } = [];
    public byte[] RowVersion { get; set; } = [];

    /// <summary>Factura: valor ainda por pagar.</summary>
    public decimal EmDivida => Math.Max(0m, Total - ValorPago - ValorCreditado);

    public static string Prefixo(TipoDocumento tipo) => tipo switch
    {
        TipoDocumento.VendaDinheiro => "VD",
        TipoDocumento.Factura => "FT",
        TipoDocumento.Recibo => "RC",
        TipoDocumento.Cotacao => "CT",
        _ => "NC",
    };

    public static string Nome(TipoDocumento tipo) => tipo switch
    {
        TipoDocumento.VendaDinheiro => "Venda a Dinheiro",
        TipoDocumento.Factura => "Factura",
        TipoDocumento.Recibo => "Recibo",
        TipoDocumento.Cotacao => "Cotação",
        _ => "Nota de Crédito",
    };
}

/// <summary>Linha de um documento. Valores com IVA incluído; <see cref="Base"/> + <see cref="Iva"/> = <see cref="Total"/>.</summary>
public class DocumentoLinha : Entidade
{
    public int DocumentoId { get; set; }
    public Documento Documento { get; set; } = null!;
    public int Ordem { get; set; }
    public int? ProdutoId { get; set; }
    public string? Codigo { get; set; }
    public string Descricao { get; set; } = string.Empty;
    public decimal Quantidade { get; set; }
    public decimal PrecoUnitario { get; set; }
    public decimal Desconto { get; set; }
    public decimal PercentagemIva { get; set; }
    /// <summary>Obrigatório quando a taxa é 0 % (motivo legal da isenção).</summary>
    public string? MotivoIsencao { get; set; }
    public decimal Base { get; set; }
    public decimal Iva { get; set; }
    public decimal Total { get; set; }
    /// <summary>Nota de crédito: linha da factura/VD corrigida.</summary>
    public int? LinhaOrigemId { get; set; }
}

/// <summary>Parte de um recibo que paga uma factura.</summary>
public class LiquidacaoFactura : Entidade
{
    public int ReciboId { get; set; }
    public Documento Recibo { get; set; } = null!;
    public int FacturaId { get; set; }
    public Documento Factura { get; set; } = null!;
    public decimal Valor { get; set; }
}
