using System.ComponentModel.DataAnnotations;
using LAMGestRestaurant.Domain.Comum;

namespace LAMGestRestaurant.Application.Stock;

public record UnidadeDto(int Id, string Codigo, string Nome, DimensaoUnidade Dimensao, decimal FactorParaBase, bool Activo = true);

public record GuardarUnidade(
    [Required(ErrorMessage = "Indique o código.")][StringLength(10, ErrorMessage = "O código não pode exceder 10 caracteres.")] string Codigo,
    [Required(ErrorMessage = "Indique o nome.")][StringLength(100, ErrorMessage = "O nome não pode exceder 100 caracteres.")] string Nome,
    DimensaoUnidade Dimensao,
    [Range(typeof(decimal), "0.000001", "1000000", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true,
        ErrorMessage = "O factor tem de ser positivo.")] decimal FactorParaBase);

public record ArmazemDto(int Id, string Nome, bool Principal, bool Activo);

public record ArtigoDto(
    int Id,
    string Codigo,
    string Nome,
    TipoArtigo Tipo,
    int UnidadeMedidaId,
    string Unidade,
    DimensaoUnidade Dimensao,
    decimal StockMinimo,
    decimal CustoMedio,
    decimal UltimoCusto,
    bool PermiteStockNegativo,
    bool Activo,
    decimal Saldo,
    bool AbaixoMinimo,
    decimal ValorStock,
    IReadOnlyList<SaldoArmazem> PorArmazem);

public record SaldoArmazem(int ArmazemId, string Armazem, decimal Quantidade);

public record GuardarArtigo(
    [Required(ErrorMessage = "Indique o código.")]
    [StringLength(30, ErrorMessage = "O código não pode exceder 30 caracteres.")]
    [RegularExpression(@"^[A-Za-z0-9._-]+$", ErrorMessage = "O código só pode ter letras, algarismos e . _ -")]
    string Codigo,
    [Required(ErrorMessage = "Indique o nome.")]
    [StringLength(150, ErrorMessage = "O nome não pode exceder 150 caracteres.")]
    string Nome,
    TipoArtigo Tipo,
    [Range(1, int.MaxValue, ErrorMessage = "Seleccione a unidade de medida.")] int UnidadeMedidaId,
    [Range(typeof(decimal), "0", "99999999", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true,
        ErrorMessage = "O stock mínimo tem de ser zero ou positivo.")] decimal StockMinimo,
    bool PermiteStockNegativo);

public record FiltroArtigos(string? Pesquisa, TipoArtigo? Tipo, bool? Activo, bool AbaixoMinimo = false, int Pagina = 1, int Tamanho = 25);

public record MovimentoStockDto(
    int Id,
    DateTime DataUtc,
    int ArtigoId,
    string Artigo,
    string Unidade,
    string Armazem,
    TipoMovimentoStock Tipo,
    decimal Quantidade,
    decimal CustoUnitario,
    string? OrigemTipo,
    int? OrigemId,
    string? Motivo,
    string? Utilizador);

public record FiltroMovimentos(int? ArtigoId, int? ArmazemId, TipoMovimentoStock? Tipo, DateTime? DesdeUtc, DateTime? AteUtc, int Pagina = 1, int Tamanho = 50);

/// <summary>Ajuste: quantidade com sinal (≠ 0). Perda: quantidade positiva (sai do stock).</summary>
public record AjustarStock(
    [Range(1, int.MaxValue, ErrorMessage = "Seleccione o artigo.")] int ArtigoId,
    [Range(1, int.MaxValue, ErrorMessage = "Seleccione o armazém.")] int ArmazemId,
    TipoMovimentoStock Tipo,
    [Range(typeof(decimal), "-99999999", "99999999", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true,
        ErrorMessage = "Quantidade inválida.")] decimal Quantidade,
    [Required(ErrorMessage = "Indique o motivo.")]
    [StringLength(1000, MinimumLength = 3, ErrorMessage = "O motivo deve ter entre 3 e 1000 caracteres.")]
    string Motivo);

public record TransferirStock(
    [Range(1, int.MaxValue, ErrorMessage = "Seleccione o artigo.")] int ArtigoId,
    [Range(1, int.MaxValue, ErrorMessage = "Seleccione o armazém de origem.")] int ArmazemOrigemId,
    [Range(1, int.MaxValue, ErrorMessage = "Seleccione o armazém de destino.")] int ArmazemDestinoId,
    [Range(typeof(decimal), "0.001", "99999999", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true,
        ErrorMessage = "A quantidade tem de ser positiva.")] decimal Quantidade,
    [StringLength(1000)] string? Motivo);

public record LinhaTransferencia(
    [Range(1, int.MaxValue, ErrorMessage = "Seleccione o artigo.")] int ArtigoId,
    [Range(typeof(decimal), "0.001", "99999999", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true,
        ErrorMessage = "A quantidade tem de ser positiva.")] decimal Quantidade);

/// <summary>Transferência de vários artigos entre dois armazéns: entram todos ou nenhum.</summary>
public record TransferirVarios(
    [Range(1, int.MaxValue, ErrorMessage = "Seleccione o armazém de origem.")] int ArmazemOrigemId,
    [Range(1, int.MaxValue, ErrorMessage = "Seleccione o armazém de destino.")] int ArmazemDestinoId,
    [MinLength(1, ErrorMessage = "Indique pelo menos um artigo.")] IReadOnlyList<LinhaTransferencia> Linhas,
    [StringLength(1000)] string? Motivo);

public record GuardarArmazem(
    [Required(ErrorMessage = "Indique o nome.")][StringLength(200)] string Nome);

public record InventarioLinhaDto(int Id, int ArtigoId, string Codigo, string Artigo, string Unidade, decimal QuantidadeSistema, decimal QuantidadeContada, decimal Diferenca);

public record InventarioDto(
    int Id,
    int ArmazemId,
    string Armazem,
    EstadoInventario Estado,
    DateTime CriadoEmUtc,
    string? CriadoPor,
    DateTime? ConfirmadoEmUtc,
    string? ConfirmadoPor,
    string? Observacoes,
    IReadOnlyList<InventarioLinhaDto> Linhas,
    byte[] RowVersion);

public record InventarioResumo(int Id, string Armazem, EstadoInventario Estado, DateTime CriadoEmUtc, DateTime? ConfirmadoEmUtc, int Linhas, int ComDiferenca);

public record NovoInventario([Range(1, int.MaxValue, ErrorMessage = "Seleccione o armazém.")] int ArmazemId, [StringLength(1000)] string? Observacoes);

public record ContagemLinha(int LinhaId,
    [Range(typeof(decimal), "0", "99999999", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true,
        ErrorMessage = "A quantidade contada tem de ser zero ou positiva.")] decimal QuantidadeContada);

public record GuardarContagem(IReadOnlyList<ContagemLinha> Linhas, [StringLength(1000)] string? Observacoes, byte[]? RowVersion);
