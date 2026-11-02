namespace LAMGestRestaurant.Application.Comum;

/// <summary>Resultado paginado devolvido pelas listagens.</summary>
public record Pagina<T>(IReadOnlyList<T> Itens, int Total, int NumeroPagina, int TamanhoPagina)
{
    public const int TamanhoMaximo = 200;

    public static (int pagina, int tamanho) Normalizar(int pagina, int tamanho) =>
        (Math.Max(1, pagina), Math.Clamp(tamanho, 1, TamanhoMaximo));
}
