using System.ComponentModel.DataAnnotations;
using LAMGestRestaurant.Domain.Comum;

namespace LAMGestRestaurant.Application.Compras;

public record FornecedorDto(int Id, string Nome, string? Nuit, string? Telefone, string? Email, string? Endereco, string? Observacoes, bool Activo,
    int NumeroCompras, decimal TotalCompras);

public record GuardarFornecedor(
    [Required(ErrorMessage = "Indique o nome do fornecedor.")]
    [StringLength(150, ErrorMessage = "O nome não pode exceder 150 caracteres.")] string Nome,
    [RegularExpression(@"^\d{9}$", ErrorMessage = "O NUIT tem 9 algarismos.")] string? Nuit,
    [StringLength(30)] string? Telefone,
    [EmailAddress(ErrorMessage = "E-mail inválido.")][StringLength(200)] string? Email,
    [StringLength(1000)] string? Endereco,
    [StringLength(1000)] string? Observacoes);

public record CompraItemDto(int Id, int ArtigoId, string Codigo, string Artigo, decimal Quantidade, int UnidadeMedidaId, string Unidade,
    decimal CustoUnitario, decimal Desconto, decimal TaxaImpostoPercentagem, decimal ValorImposto, decimal Total);

public record CompraDto(
    int Id,
    int FornecedorId,
    string Fornecedor,
    string NumeroDocumento,
    DateOnly DataDocumento,
    DateOnly? DataVencimento,
    int ArmazemId,
    string Armazem,
    EstadoCompra Estado,
    decimal Subtotal,
    decimal Desconto,
    decimal Impostos,
    decimal Total,
    string? Observacoes,
    string? CriadoPor,
    DateTime? ConfirmadaEmUtc,
    string? ConfirmadaPor,
    DateTime? AnuladaEmUtc,
    string? MotivoAnulacao,
    IReadOnlyList<CompraItemDto> Itens,
    byte[] RowVersion);

public record CompraResumo(int Id, string Fornecedor, string NumeroDocumento, DateOnly DataDocumento, EstadoCompra Estado, decimal Total, int NumeroItens);

/// <summary>Custo unitário sem IVA, na unidade indicada. Desconto em valor (MT) sobre a linha.</summary>
public record NovaLinhaCompra(
    [Range(1, int.MaxValue, ErrorMessage = "Seleccione o artigo.")] int ArtigoId,
    [Range(typeof(decimal), "0.001", "99999999", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true,
        ErrorMessage = "A quantidade tem de ser positiva.")] decimal Quantidade,
    [Range(1, int.MaxValue, ErrorMessage = "Seleccione a unidade.")] int UnidadeMedidaId,
    [Range(typeof(decimal), "0", "999999999", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true,
        ErrorMessage = "O custo tem de ser zero ou positivo.")] decimal CustoUnitario,
    [Range(typeof(decimal), "0", "999999999", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true,
        ErrorMessage = "O desconto tem de ser zero ou positivo.")] decimal Desconto,
    [Range(typeof(decimal), "0", "100", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true,
        ErrorMessage = "A taxa de imposto deve estar entre 0 e 100%.")] decimal TaxaImpostoPercentagem);

public record GuardarCompra(
    [Range(1, int.MaxValue, ErrorMessage = "Seleccione o fornecedor.")] int FornecedorId,
    [Required(ErrorMessage = "Indique o número do documento do fornecedor.")]
    [StringLength(50, ErrorMessage = "O número do documento não pode exceder 50 caracteres.")] string NumeroDocumento,
    DateOnly DataDocumento,
    DateOnly? DataVencimento,
    [Range(1, int.MaxValue, ErrorMessage = "Seleccione o armazém.")] int ArmazemId,
    [StringLength(1000)] string? Observacoes,
    [Required, MinLength(1, ErrorMessage = "Indique pelo menos uma linha.")] IReadOnlyList<NovaLinhaCompra> Itens,
    byte[]? RowVersion);

public record FiltroCompras(int? FornecedorId, EstadoCompra? Estado, DateOnly? Desde, DateOnly? Ate, string? Pesquisa, int Pagina = 1, int Tamanho = 25);

public record ResumoCompras(DateOnly Desde, DateOnly Ate, decimal Total, int NumeroCompras, IReadOnlyList<Relatorios.LinhaValor> PorFornecedor);
