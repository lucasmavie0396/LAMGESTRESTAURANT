using LAMGestRestaurant.Application.Comum;
using Microsoft.EntityFrameworkCore;

namespace LAMGestRestaurant.Application.Stock;

/// <summary>
/// De que armazém sai o stock de cada produto vendido: o do centro de produção (estação da categoria ou, na falta dela,
/// da categoria principal); sem centro com armazém, o armazém registado no produto; sem nenhum, o armazém principal.
/// Armazéns inactivos são ignorados.
/// </summary>
public class ArmazensAbate(IAppDbContext db)
{
    public async Task<IReadOnlyDictionary<int, int>> ResolverAsync(IReadOnlyCollection<int> produtoIds, CancellationToken ct = default)
    {
        if (produtoIds.Count == 0)
            return new Dictionary<int, int>();
        var activos = await db.Armazens.AsNoTracking().Where(a => a.Activo).Select(a => new { a.Id, a.Principal }).ToListAsync(ct);
        var principal = activos.FirstOrDefault(a => a.Principal)?.Id;
        bool Activo(int? id) => id is int v && activos.Any(a => a.Id == v);

        var produtos = await db.Produtos.AsNoTracking().Where(p => produtoIds.Contains(p.Id))
            .Select(p => new
            {
                p.Id,
                p.ArmazemId,
                DoCentro = p.Categoria.EstacaoPreparacaoId != null
                    ? p.Categoria.EstacaoPreparacao!.ArmazemId
                    : p.Categoria.CategoriaPai != null && p.Categoria.CategoriaPai.EstacaoPreparacaoId != null
                        ? p.Categoria.CategoriaPai.EstacaoPreparacao!.ArmazemId
                        : null,
            })
            .ToListAsync(ct);

        var resultado = new Dictionary<int, int>();
        foreach (var p in produtos)
        {
            var armazem = Activo(p.DoCentro) ? p.DoCentro : Activo(p.ArmazemId) ? p.ArmazemId : principal;
            resultado[p.Id] = armazem ?? throw new RegraNegocioException("Não existe armazém principal activo para a baixa de stock.");
        }
        return resultado;
    }
}
