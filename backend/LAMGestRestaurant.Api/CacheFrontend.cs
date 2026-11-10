using Microsoft.AspNetCore.StaticFiles;

namespace LAMGestRestaurant.Api;

/// <summary>
/// Cache do frontend compilado. Sem cabeçalho Cache-Control o navegador usa uma frescura heurística (10% da idade do ficheiro)
/// e, depois de uma actualização, podia continuar horas com o index.html antigo — e portanto sem os menus novos.
/// O index.html é sempre revalidado (no-cache: pergunta ao servidor, que responde 304 se não mudou); os ficheiros de /assets
/// têm o hash do conteúdo no nome, por isso podem ficar em cache para sempre.
/// </summary>
public static class CacheFrontend
{
    public const string Revalidar = "no-cache";
    public const string Imutavel = "public, max-age=31536000, immutable";

    public static StaticFileOptions Opcoes() => new() { OnPrepareResponse = Aplicar };

    private static void Aplicar(StaticFileResponseContext ctx) =>
        ctx.Context.Response.Headers.CacheControl = ctx.Context.Request.Path.StartsWithSegments("/assets") ? Imutavel : Revalidar;
}
