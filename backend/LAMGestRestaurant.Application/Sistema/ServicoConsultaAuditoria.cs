using LAMGestRestaurant.Application.Comum;
using Microsoft.EntityFrameworkCore;

namespace LAMGestRestaurant.Application.Sistema;

public sealed record RegistoAuditoriaDto(long Id, DateTime DataUtc, int? UtilizadorId, string? Utilizador, string Accao, string? Entidade, string? EntidadeId,
    string? Detalhes, string? EnderecoIp);

/// <summary>
/// <see cref="Modulo"/>: início do código da acção (ex.: «pedido», «documento», «caixa»). <see cref="Pesquisa"/>: procura na acção,
/// no registo afectado (entidade e n.º) e nos detalhes (ex.: o número de um pedido ou de uma factura).
/// </summary>
public sealed record FiltroAuditoria(DateOnly? Desde, DateOnly? Ate, int? UtilizadorId, string? Modulo, string? Accao, string? Entidade, string? EntidadeId,
    string? Pesquisa, int Pagina = 1, int Tamanho = 50);

public sealed record UtilizadorAuditoria(int Id, string Nome);

/// <summary>Valores existentes para os filtros: módulos, acções, entidades e utilizadores que aparecem nos registos.</summary>
public sealed record OpcoesAuditoria(IReadOnlyList<string> Modulos, IReadOnlyList<string> Accoes, IReadOnlyList<string> Entidades, IReadOnlyList<UtilizadorAuditoria> Utilizadores);

/// <summary>
/// Consulta da auditoria (só leitura): os registos são só de inserção e nunca se alteram nem se apagam (regra 10).
/// Mais recentes primeiro, com filtros por período, utilizador, módulo, acção, registo afectado e texto.
/// </summary>
public class ServicoConsultaAuditoria(IAppDbContext db, ConfiguracoesSistema config)
{
    public async Task<Pagina<RegistoAuditoriaDto>> ListarAsync(FiltroAuditoria f, CancellationToken ct = default)
    {
        var (pagina, tamanho) = Pagina<RegistoAuditoriaDto>.Normalizar(f.Pagina, f.Tamanho);
        var q = db.RegistosAuditoria.AsNoTracking();
        if (f.Desde is not null || f.Ate is not null)
        {
            var desde = f.Desde ?? f.Ate!.Value;
            var ate = f.Ate ?? f.Desde!.Value;
            if (ate < desde)
                throw new RegraNegocioException("A data final é anterior à inicial.");
            var (inicio, fim) = await config.IntervaloUtcAsync(desde, ate);
            q = q.Where(r => r.DataUtc >= inicio && r.DataUtc < fim);
        }
        if (f.UtilizadorId is int u) q = q.Where(r => r.UtilizadorId == u);
        if (!string.IsNullOrWhiteSpace(f.Modulo))
        {
            var modulo = f.Modulo.Trim();
            q = q.Where(r => r.Accao == modulo || r.Accao.StartsWith(modulo + "."));
        }
        if (!string.IsNullOrWhiteSpace(f.Accao)) q = q.Where(r => r.Accao == f.Accao.Trim());
        if (!string.IsNullOrWhiteSpace(f.Entidade)) q = q.Where(r => r.Entidade == f.Entidade.Trim());
        if (!string.IsNullOrWhiteSpace(f.EntidadeId)) q = q.Where(r => r.EntidadeId == f.EntidadeId.Trim());
        if (!string.IsNullOrWhiteSpace(f.Pesquisa))
        {
            var p = f.Pesquisa.Trim();
            q = q.Where(r => r.Accao.Contains(p) || (r.NomeUtilizador != null && r.NomeUtilizador.Contains(p)) || (r.Entidade != null && r.Entidade.Contains(p))
                             || r.EntidadeId == p || (r.Detalhes != null && r.Detalhes.Contains(p)) || r.EnderecoIp == p);
        }
        var total = await q.CountAsync(ct);
        var itens = await q.OrderByDescending(r => r.DataUtc).ThenByDescending(r => r.Id).Skip((pagina - 1) * tamanho).Take(tamanho)
            .Select(r => new RegistoAuditoriaDto(r.Id, r.DataUtc, r.UtilizadorId, r.NomeUtilizador, r.Accao, r.Entidade, r.EntidadeId, r.Detalhes, r.EnderecoIp))
            .ToListAsync(ct);
        return new Pagina<RegistoAuditoriaDto>(itens, total, pagina, tamanho);
    }

    public async Task<OpcoesAuditoria> OpcoesAsync(CancellationToken ct = default)
    {
        var accoes = await db.RegistosAuditoria.AsNoTracking().Select(r => r.Accao).Distinct().OrderBy(a => a).ToListAsync(ct);
        var entidades = await db.RegistosAuditoria.AsNoTracking().Where(r => r.Entidade != null).Select(r => r.Entidade!).Distinct().OrderBy(e => e).ToListAsync(ct);
        var utilizadores = await db.RegistosAuditoria.AsNoTracking().Where(r => r.UtilizadorId != null)
            .GroupBy(r => r.UtilizadorId!.Value)
            .Select(g => new UtilizadorAuditoria(g.Key, g.OrderByDescending(r => r.Id).Select(r => r.NomeUtilizador).FirstOrDefault() ?? "?"))
            .ToListAsync(ct);
        var modulos = accoes.Select(a => a.Split('.')[0]).Distinct().Order().ToList();
        return new OpcoesAuditoria(modulos, accoes, entidades, [.. utilizadores.OrderBy(u => u.Nome)]);
    }
}
