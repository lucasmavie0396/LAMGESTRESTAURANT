using System.ComponentModel.DataAnnotations;
using LAMGestRestaurant.Domain.Facturacao;

namespace LAMGestRestaurant.Application.Facturacao;

public record DocumentoResumo(
    int Id,
    TipoDocumento Tipo,
    string Numero,
    EstadoDocumento Estado,
    DateTime EmitidoEmUtc,
    string ClienteNome,
    string? ClienteNuit,
    decimal Total,
    decimal EmDivida,
    DateOnly? Vencimento,
    DateOnly? ValidaAte,
    string? Origem);

public record DocumentoLinhaDto(
    int Id,
    int? ProdutoId,
    string? Codigo,
    string Descricao,
    decimal Quantidade,
    decimal PrecoUnitario,
    decimal Desconto,
    decimal PercentagemIva,
    string? MotivoIsencao,
    decimal Base,
    decimal Iva,
    decimal Total,
    /// <summary>Quantidade ainda por creditar (facturas e VD), para notas de crédito parciais.</summary>
    decimal QuantidadeCreditavel);

public record ResumoIvaDto(decimal Percentagem, decimal Base, decimal Iva, decimal Total);

public record LiquidacaoDto(int FacturaId, string FacturaNumero, decimal Valor);

/// <summary>Documento ligado a este (recibos que pagam a factura, notas de crédito, factura convertida da cotação).</summary>
public record DocumentoRelacionado(int Id, TipoDocumento Tipo, string Numero, EstadoDocumento Estado, DateTime EmitidoEmUtc, decimal Valor);

public record DocumentoDto(
    int Id,
    TipoDocumento Tipo,
    string Numero,
    EstadoDocumento Estado,
    DateTime EmitidoEmUtc,
    string? Utilizador,
    DateOnly? Vencimento,
    DateOnly? ValidaAte,
    string EmitenteNome,
    string? EmitenteNuit,
    string? EmitenteEndereco,
    string? EmitenteTelefone,
    int? ClienteId,
    string ClienteNome,
    string? ClienteNuit,
    string? ClienteEndereco,
    int? PedidoId,
    string? PedidoNumero,
    int? DocumentoOrigemId,
    string? DocumentoOrigemNumero,
    TipoDocumento? DocumentoOrigemTipo,
    decimal Subtotal,
    decimal Desconto,
    decimal BaseTributavel,
    decimal Iva,
    decimal Total,
    decimal ValorPago,
    decimal ValorCreditado,
    decimal EmDivida,
    string? MetodoPagamento,
    string? Referencia,
    string? Motivo,
    string? Observacoes,
    DateTime? AnuladoEmUtc,
    string? AnuladoPor,
    string? MotivoAnulacao,
    IReadOnlyList<DocumentoLinhaDto> Linhas,
    IReadOnlyList<ResumoIvaDto> ResumoIva,
    IReadOnlyList<LiquidacaoDto> Liquidacoes,
    IReadOnlyList<DocumentoRelacionado> Relacionados,
    byte[] RowVersion);

public record FiltroDocumentos(
    TipoDocumento? Tipo,
    EstadoDocumento? Estado,
    int? ClienteId,
    DateOnly? Desde,
    DateOnly? Ate,
    string? Pesquisa,
    bool ApenasEmDivida = false,
    int Pagina = 1,
    int TamanhoPagina = 50);

public record NovaLinhaDocumento(
    /// <summary>Produto do catálogo (preço e IVA do produto, salvo preço indicado) ou vazio para uma linha livre.</summary>
    int? ProdutoId,
    [StringLength(300, ErrorMessage = "A descrição não pode exceder 300 caracteres.")] string? Descricao,
    [Range(typeof(decimal), "0.001", "999999", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true,
        ErrorMessage = "A quantidade deve estar entre 0,001 e 999 999.")] decimal Quantidade,
    [Range(typeof(decimal), "0", "999999999", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true,
        ErrorMessage = "Preço inválido.")] decimal? PrecoUnitario,
    [Range(typeof(decimal), "0", "999999999", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true,
        ErrorMessage = "Desconto inválido.")] decimal? Desconto,
    /// <summary>Linha livre: taxa de IVA (vazio = isento).</summary>
    int? TaxaImpostoId);

/// <summary>Factura (cliente obrigatório) ou cotação (cliente da ficha ou só o nome).</summary>
public record NovoDocumento(
    int? ClienteId,
    [StringLength(200)] string? ClienteNome,
    [StringLength(20)] string? ClienteNuit,
    [StringLength(1000)] string? ClienteEndereco,
    /// <summary>Factura: vencimento (vazio = prazo configurado). Cotação: validade (vazio = validade configurada).</summary>
    DateOnly? Data,
    [StringLength(1000)] string? Observacoes,
    [Required, MinLength(1, ErrorMessage = "Indique pelo menos uma linha.")] IReadOnlyList<NovaLinhaDocumento> Linhas);

public record ConverterCotacao(int? ClienteId, DateOnly? Vencimento, byte[]? RowVersion);

public record LiquidacaoNova(
    int FacturaId,
    [Range(typeof(decimal), "0.01", "999999999", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true,
        ErrorMessage = "Valor inválido.")] decimal Valor);

public record NovoRecibo(
    [Range(1, int.MaxValue, ErrorMessage = "Indique o cliente.")] int ClienteId,
    [Range(1, int.MaxValue, ErrorMessage = "Indique a forma de pagamento.")] int MetodoPagamentoId,
    [StringLength(100)] string? Referencia,
    /// <summary>Obrigatória quando o método conta na gaveta (dinheiro): o valor entra nessa sessão de caixa.</summary>
    int? SessaoCaixaId,
    [StringLength(1000)] string? Observacoes,
    [Required, MinLength(1, ErrorMessage = "Indique pelo menos uma factura.")] IReadOnlyList<LiquidacaoNova> Liquidacoes);

public record LinhaCredito(
    int LinhaOrigemId,
    [Range(typeof(decimal), "0.001", "999999", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true,
        ErrorMessage = "Quantidade inválida.")] decimal Quantidade);

public record NovaNotaCredito(
    [Range(1, int.MaxValue, ErrorMessage = "Indique a factura.")] int DocumentoOrigemId,
    [Required(ErrorMessage = "Indique o motivo.")]
    [StringLength(1000, MinimumLength = 3, ErrorMessage = "O motivo deve ter entre 3 e 1000 caracteres.")] string Motivo,
    /// <summary>Vazio = creditar tudo o que ainda não foi creditado.</summary>
    IReadOnlyList<LinhaCredito>? Linhas);

/// <summary>Factura em dívida de um cliente (para o recibo).</summary>
public record FacturaEmDivida(int Id, string Numero, DateTime EmitidoEmUtc, DateOnly? Vencimento, decimal Total, decimal ValorPago, decimal ValorCreditado, decimal EmDivida, bool Vencida);

public record ContaCorrente(int ClienteId, string ClienteNome, string? ClienteNuit, decimal TotalEmDivida, decimal TotalVencido, IReadOnlyList<FacturaEmDivida> Facturas);

public record ImprimirDocumentoFiscal(int? ImpressoraId, int? TerminalId);
