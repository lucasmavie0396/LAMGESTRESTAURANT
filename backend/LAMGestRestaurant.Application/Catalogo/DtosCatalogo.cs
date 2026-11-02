using System.ComponentModel.DataAnnotations;
using LAMGestRestaurant.Domain.Comum;

namespace LAMGestRestaurant.Application.Catalogo;

public record CategoriaDto(
    int Id,
    string Nome,
    string? Descricao,
    int? CategoriaPaiId,
    string? CategoriaPaiNome,
    int? EstacaoPreparacaoId,
    string? EstacaoPreparacaoNome,
    string? Cor,
    int Ordem,
    bool Activo,
    int NumeroProdutosActivos);

public record GuardarCategoria(
    [Required(ErrorMessage = "Indique o nome da categoria.")]
    [StringLength(100, ErrorMessage = "O nome não pode exceder 100 caracteres.")]
    string Nome,
    [StringLength(1000)] string? Descricao,
    int? CategoriaPaiId,
    int? EstacaoPreparacaoId,
    [RegularExpression("^#[0-9a-fA-F]{6}$", ErrorMessage = "Cor inválida (use o formato #RRGGBB).")] string? Cor,
    [Range(0, 9999, ErrorMessage = "Ordem inválida.")] int Ordem);

public record ProdutoDto(
    int Id,
    string Codigo,
    string? CodigoBarras,
    string Nome,
    string? Descricao,
    int CategoriaId,
    string CategoriaNome,
    TipoProduto Tipo,
    decimal PrecoVenda,
    decimal Custo,
    int? TaxaImpostoId,
    string? TaxaImpostoNome,
    decimal? TaxaImpostoPercentagem,
    string? ImagemUrl,
    bool EnviaCozinha,
    bool Disponivel,
    bool Activo,
    byte[] RowVersion);

public record GuardarProduto(
    [Required(ErrorMessage = "Indique o código interno.")]
    [StringLength(30, ErrorMessage = "O código não pode exceder 30 caracteres.")]
    [RegularExpression(@"^[A-Za-z0-9._-]+$", ErrorMessage = "O código só pode ter letras, algarismos e . _ -")]
    string Codigo,
    [StringLength(50, ErrorMessage = "O código de barras não pode exceder 50 caracteres.")]
    [RegularExpression(@"^[0-9A-Za-z-]*$", ErrorMessage = "Código de barras inválido.")]
    string? CodigoBarras,
    [Required(ErrorMessage = "Indique o nome do produto.")]
    [StringLength(150, ErrorMessage = "O nome não pode exceder 150 caracteres.")]
    string Nome,
    [StringLength(1000)] string? Descricao,
    [Range(1, int.MaxValue, ErrorMessage = "Seleccione a categoria.")] int CategoriaId,
    TipoProduto Tipo,
    [Range(typeof(decimal), "0", "999999999", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true, ErrorMessage = "O preço tem de ser zero ou positivo.")] decimal PrecoVenda,
    [Range(typeof(decimal), "0", "999999999", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true, ErrorMessage = "O custo tem de ser zero ou positivo.")] decimal Custo,
    int? TaxaImpostoId,
    [StringLength(500)] string? ImagemUrl,
    bool EnviaCozinha,
    bool Disponivel,
    /// <summary>Obrigatória na edição (concorrência optimista).</summary>
    byte[]? RowVersion);

public record FiltroProdutos(
    string? Pesquisa,
    int? CategoriaId,
    TipoProduto? Tipo,
    bool? Activo,
    bool? Disponivel,
    string? Ordenar,
    bool Descendente = false,
    int Pagina = 1,
    int Tamanho = 25);

public record TaxaImpostoDto(int Id, string Nome, decimal Percentagem, bool Activo);

public record EstacaoPreparacaoDto(int Id, string Nome, bool Activo);
