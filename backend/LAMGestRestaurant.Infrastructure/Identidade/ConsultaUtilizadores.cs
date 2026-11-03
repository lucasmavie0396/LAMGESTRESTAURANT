using LAMGestRestaurant.Application.Comum;
using LAMGestRestaurant.Infrastructure.Dados;
using Microsoft.EntityFrameworkCore;

namespace LAMGestRestaurant.Infrastructure.Identidade;

public class ConsultaUtilizadores(AppDbContext db) : IConsultaUtilizadores
{
    public async Task<IReadOnlyDictionary<int, string>> NomesAsync(IEnumerable<int> ids, CancellationToken ct = default)
    {
        var lista = ids.Distinct().ToList();
        if (lista.Count == 0)
            return new Dictionary<int, string>();
        return await db.Users.AsNoTracking()
            .Where(u => lista.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.NomeCompleto, ct);
    }

    public async Task<IReadOnlyList<Opcao>> TodosAsync(CancellationToken ct = default) =>
        await db.Users.AsNoTracking().Where(u => u.Activo).OrderBy(u => u.NomeCompleto)
            .Select(u => new Opcao(u.Id, u.NomeCompleto + " (" + u.UserName + ")")).ToListAsync(ct);
}
