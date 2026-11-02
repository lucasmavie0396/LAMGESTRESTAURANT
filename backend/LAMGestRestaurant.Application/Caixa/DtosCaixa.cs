using System.ComponentModel.DataAnnotations;
using LAMGestRestaurant.Domain.Comum;

namespace LAMGestRestaurant.Application.Caixa;

public record TerminalDto(int Id, string Nome, string? Descricao, bool Activo, int? SessaoAbertaId, string? SessaoAbertaPor);

public record GuardarTerminal(
    [Required(ErrorMessage = "Indique o nome do terminal.")]
    [StringLength(200, ErrorMessage = "O nome não pode exceder 200 caracteres.")]
    string Nome,
    [StringLength(1000)] string? Descricao);

public record MetodoPagamentoDto(int Id, string Codigo, string Nome, bool PermiteTroco, bool ExigeReferencia, bool ContaNaGaveta, bool Activo);

public record AbrirCaixa(
    [Range(1, int.MaxValue, ErrorMessage = "Seleccione o terminal.")] int TerminalId,
    [Range(typeof(decimal), "0", "999999999", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true, ErrorMessage = "O fundo inicial tem de ser zero ou positivo.")] decimal FundoInicial,
    [StringLength(1000)] string? Observacoes);

public record RegistarMovimentoCaixa(
    TipoMovimentoCaixa Tipo,
    [Range(1, int.MaxValue, ErrorMessage = "Seleccione o método.")] int MetodoPagamentoId,
    [Range(typeof(decimal), "0.01", "999999999", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true, ErrorMessage = "O valor tem de ser positivo.")] decimal Valor,
    [Required(ErrorMessage = "Indique o motivo.")]
    [StringLength(1000, MinimumLength = 3, ErrorMessage = "O motivo deve ter entre 3 e 1000 caracteres.")]
    string Motivo);

public record ContagemMetodo(int MetodoPagamentoId,
    [Range(typeof(decimal), "0", "999999999", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true, ErrorMessage = "O valor contado tem de ser zero ou positivo.")] decimal ValorContado);

public record FecharCaixa(
    [Required] IReadOnlyList<ContagemMetodo> Contagens,
    [StringLength(1000)] string? Observacoes,
    byte[]? RowVersion);

/// <summary>Valores por método de pagamento na sessão. Esperado = fundo (só gaveta) + recebimentos + entradas − saídas.</summary>
public record ResumoMetodo(
    int MetodoPagamentoId,
    string Metodo,
    bool ContaNaGaveta,
    decimal Fundo,
    decimal Recebimentos,
    decimal Entradas,
    decimal Saidas,
    decimal Esperado,
    decimal? Contado,
    decimal? Diferenca);

public record MovimentoCaixaDto(int Id, TipoMovimentoCaixa Tipo, string Metodo, decimal Valor, string Motivo, string? Utilizador, DateTime DataUtc);

public record SessaoCaixaDto(
    int Id,
    int TerminalId,
    string Terminal,
    EstadoSessaoCaixa Estado,
    string AbertaPor,
    DateTime AbertaEmUtc,
    decimal FundoInicial,
    string? ObservacoesAbertura,
    string? FechadaPor,
    DateTime? FechadaEmUtc,
    string? ObservacoesFecho,
    int PedidosPagos,
    decimal TotalRecebido,
    decimal TotalEsperado,
    decimal? TotalContado,
    decimal? TotalDiferenca,
    IReadOnlyList<ResumoMetodo> PorMetodo,
    IReadOnlyList<MovimentoCaixaDto> Movimentos,
    byte[] RowVersion);

public record SessaoCaixaResumo(
    int Id,
    string Terminal,
    EstadoSessaoCaixa Estado,
    string AbertaPor,
    DateTime AbertaEmUtc,
    DateTime? FechadaEmUtc,
    decimal FundoInicial,
    decimal? TotalDiferenca);

public record FiltroSessoes(DateTime? DesdeUtc, DateTime? AteUtc, int? TerminalId, EstadoSessaoCaixa? Estado, int Pagina = 1, int Tamanho = 25);
