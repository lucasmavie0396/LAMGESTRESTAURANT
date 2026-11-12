using System.ComponentModel.DataAnnotations;
using LAMGestRestaurant.Domain.Comum;

namespace LAMGestRestaurant.Application.Vendas;

public record PedidoItemDto(
    int Id,
    int ProdutoId,
    string NomeProduto,
    decimal PrecoUnitario,
    decimal Quantidade,
    decimal Total,
    string? Observacoes,
    bool EnviaCozinha,
    EstadoItemCozinha EstadoCozinha,
    DateTime? EnviadoCozinhaEmUtc,
    bool Cancelado,
    string? MotivoCancelamento,
    string Unidade = "un");

public record PagamentoDto(
    int Id,
    string Metodo,
    decimal Valor,
    decimal ValorRecebido,
    decimal Troco,
    string? Referencia,
    EstadoPagamento Estado,
    DateTime DataUtc,
    string? Utilizador,
    string? MotivoAnulacao);

public record PedidoDto(
    int Id,
    string Numero,
    TipoPedido Tipo,
    EstadoPedido Estado,
    EstadoEntrega? EstadoEntrega,
    int? MesaId,
    string? MesaNome,
    int? ClienteId,
    string? ClienteNome,
    int? GarcomId,
    string? GarcomNome,
    int NumeroPessoas,
    string? ContactoEntrega,
    string? EnderecoEntrega,
    string? Observacoes,
    decimal Subtotal,
    decimal Desconto,
    decimal Impostos,
    decimal TaxaEntrega,
    decimal Total,
    decimal TotalPago,
    decimal EmDivida,
    DateTime CriadoEmUtc,
    string? CriadoPor,
    DateTime? FechadoEmUtc,
    string? ReciboNumero,
    string? MotivoCancelamento,
    IReadOnlyList<PedidoItemDto> Itens,
    IReadOnlyList<PagamentoDto> Pagamentos,
    byte[] RowVersion,
    /// <summary>Delivery/Take Away: nome de quem recebe.</summary>
    string? NomeEntrega = null,
    /// <summary>Utilizador responsável (quem abriu a mesa, ou a quem foi passada).</summary>
    int? ResponsavelId = null,
    string? Responsavel = null);

public record PedidoResumo(
    int Id,
    string Numero,
    TipoPedido Tipo,
    EstadoPedido Estado,
    EstadoEntrega? EstadoEntrega,
    int? MesaId,
    string? MesaNome,
    string? ContactoEntrega,
    string? EnderecoEntrega,
    decimal Total,
    decimal TotalPago,
    int NumeroItens,
    int ItensPorEnviar,
    int ItensProntos,
    DateTime CriadoEmUtc,
    string? NomeEntrega = null);

public record NovoItem(
    [Range(1, int.MaxValue, ErrorMessage = "Produto inválido.")] int ProdutoId,
    [Range(typeof(decimal), "0.001", "999", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true,
        ErrorMessage = "A quantidade deve estar entre 0,001 e 999.")] decimal Quantidade,
    [StringLength(300, ErrorMessage = "As observações não podem exceder 300 caracteres.")] string? Observacoes);

public record NovoPedido(
    TipoPedido Tipo,
    int? MesaId,
    int? ClienteId,
    int? GarcomId,
    [Range(0, 200, ErrorMessage = "Número de pessoas inválido.")] int NumeroPessoas,
    [StringLength(200)] string? ContactoEntrega,
    [StringLength(1000)] string? EnderecoEntrega,
    [StringLength(1000)] string? Observacoes,
    /// <summary>Confirma o uso de uma mesa marcada como Reservada.</summary>
    bool ConfirmarMesaReservada,
    IReadOnlyList<NovoItem>? Itens,
    /// <summary>Delivery: nome de quem recebe (obrigatório, salvo com cliente associado).</summary>
    [StringLength(150, ErrorMessage = "O nome não pode exceder 150 caracteres.")] string? NomeEntrega = null);

public record AdicionarItens([Required, MinLength(1, ErrorMessage = "Indique pelo menos um item.")] IReadOnlyList<NovoItem> Itens, byte[]? RowVersion);

public record AlterarItem(
    [Range(typeof(decimal), "0.001", "999", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true,
        ErrorMessage = "A quantidade deve estar entre 0,001 e 999.")] decimal Quantidade,
    [StringLength(300)] string? Observacoes,
    byte[]? RowVersion);

public record ComVersao(byte[]? RowVersion);

/// <summary>Comentário livre (ex.: «sem batata»); vazio retira o comentário.</summary>
public record Comentario([StringLength(300, ErrorMessage = "O comentário tem no máximo 300 caracteres.")] string? Texto, byte[]? RowVersion);

public record AssociarCliente(int? ClienteId, byte[]? RowVersion);

public record ComMotivo(
    [Required(ErrorMessage = "Indique o motivo.")]
    [StringLength(1000, MinimumLength = 3, ErrorMessage = "O motivo deve ter entre 3 e 1000 caracteres.")]
    string Motivo,
    byte[]? RowVersion);

public record AplicarDesconto(
    [Range(typeof(decimal), "0", "999999999", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true,
        ErrorMessage = "Desconto inválido.")] decimal? Valor,
    [Range(typeof(decimal), "0", "100", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true,
        ErrorMessage = "A percentagem deve estar entre 0 e 100.")] decimal? Percentagem,
    byte[]? RowVersion);

public record TransferirMesa([Range(1, int.MaxValue, ErrorMessage = "Seleccione a mesa de destino.")] int MesaDestinoId, [StringLength(1000)] string? Motivo, byte[]? RowVersion);

public record ActualizarEntrega(EstadoEntrega Estado, int? EstafetaId, byte[]? RowVersion);

/// <summary>Passar a mesa (pedido aberto) a outro utilizador responsável, ex.: na mudança de turno.</summary>
public record PassarResponsavel([Range(1, int.MaxValue, ErrorMessage = "Escolha o novo responsável.")] int UtilizadorId, byte[]? RowVersion);

public record RegistarPagamento(
    [Range(1, int.MaxValue, ErrorMessage = "Sessão de caixa inválida.")] int SessaoCaixaId,
    [Range(1, int.MaxValue, ErrorMessage = "Seleccione o método de pagamento.")] int MetodoPagamentoId,
    [Range(typeof(decimal), "0.01", "999999999", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true,
        ErrorMessage = "O valor a pagar tem de ser positivo.")] decimal Valor,
    [Range(typeof(decimal), "0.01", "999999999", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true,
        ErrorMessage = "O valor recebido tem de ser positivo.")] decimal ValorRecebido,
    [StringLength(100, ErrorMessage = "A referência não pode exceder 100 caracteres.")] string? Referencia);

/// <summary>Uma parte de um pagamento com vários métodos (ex.: 500 em numerário + 300 por M-Pesa).</summary>
public record LinhaPagamento(
    [Range(1, int.MaxValue, ErrorMessage = "Seleccione o método de pagamento.")] int MetodoPagamentoId,
    [Range(typeof(decimal), "0.01", "999999999", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true,
        ErrorMessage = "O valor a pagar tem de ser positivo.")] decimal Valor,
    [Range(typeof(decimal), "0.01", "999999999", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true,
        ErrorMessage = "O valor recebido tem de ser positivo.")] decimal ValorRecebido,
    [StringLength(100, ErrorMessage = "A referência não pode exceder 100 caracteres.")] string? Referencia);

/// <summary>Vários pagamentos do mesmo pedido confirmados de uma vez (tudo ou nada, uma só Idempotency-Key).</summary>
public record RegistarPagamentos(
    [Range(1, int.MaxValue, ErrorMessage = "Sessão de caixa inválida.")] int SessaoCaixaId,
    [Required(ErrorMessage = "Indique pelo menos um pagamento.")]
    [MinLength(1, ErrorMessage = "Indique pelo menos um pagamento.")]
    [MaxLength(ServicoPagamentos.MaximoLinhas, ErrorMessage = "No máximo 10 pagamentos de uma vez.")]
    IReadOnlyList<LinhaPagamento> Linhas,
    /// <summary>O recibo sai na impressora do próprio dispositivo (PDA): o servidor não o imprime ao fechar o pedido.</summary>
    bool ReciboNoDispositivo = false,
    /// <summary>Fatura a Crédito: cliente da factura (fica associado ao pedido).</summary>
    int? ClienteId = null);

/// <summary>EntregaActiva: só Take Away/Delivery ainda não entregues nem cancelados (ecrã de Delivery).</summary>
public record FiltroPedidos(EstadoPedido? Estado, TipoPedido? Tipo, int? MesaId, DateTime? DesdeUtc, DateTime? AteUtc, string? Pesquisa,
    bool EntregaActiva = false, int Pagina = 1, int Tamanho = 50);

/// <summary>Estorno total de um pedido pago: o valor é devolvido pela sessão de caixa aberta do operador.</summary>
public record EstornarPedido(
    [Range(1, int.MaxValue, ErrorMessage = "Sessão de caixa inválida.")] int SessaoCaixaId,
    [Range(1, int.MaxValue, ErrorMessage = "Seleccione o método de reembolso.")] int MetodoPagamentoId,
    [Required(ErrorMessage = "Indique o motivo.")]
    [StringLength(1000, MinimumLength = 3, ErrorMessage = "O motivo deve ter entre 3 e 1000 caracteres.")]
    string Motivo,
    byte[]? RowVersion);

// ---------- Recibo ----------

public record ReciboLinha(string Produto, decimal Quantidade, decimal PrecoUnitario, decimal Total);

public record ReciboDto(
    string Numero,
    string PedidoNumero,
    DateTime EmitidoEmUtc,
    string Estabelecimento,
    string? Endereco,
    string? Telefone,
    string? Nuit,
    string? Mesa,
    TipoPedido Tipo,
    string? Operador,
    IReadOnlyList<ReciboLinha> Linhas,
    decimal Subtotal,
    decimal Desconto,
    decimal Impostos,
    decimal TaxaEntrega,
    decimal Total,
    IReadOnlyList<PagamentoDto> Pagamentos,
    string Moeda,
    string? Rodape);
