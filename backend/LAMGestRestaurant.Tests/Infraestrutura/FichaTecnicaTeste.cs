using LAMGestRestaurant.Infrastructure.Dados;
using Microsoft.EntityFrameworkCore;

namespace LAMGestRestaurant.Tests.Infraestrutura;

/// <summary>
/// Fichas técnicas criadas directamente na base pelos testes: como no editor, a nova versão encerra as anteriores
/// (todo o produto que controla stock nasce com a ficha 1:1 automática, versão 1).
/// </summary>
public static class FichaTecnicaTeste
{
    public static async Task<int> ProximaVersaoAsync(AppDbContext db, int produtoId)
    {
        var anteriores = await db.Receitas.Where(r => r.ProdutoId == produtoId).ToListAsync();
        foreach (var r in anteriores.Where(r => r.Activa))
        {
            r.Activa = false;
            r.ValidaAteUtc = DateTime.UtcNow;
        }
        return anteriores.Select(r => r.Versao).DefaultIfEmpty(0).Max() + 1;
    }
}
