using System.Security.Claims;
using LAMGestRestaurant.Application.Comum;
using LAMGestRestaurant.Application.Seguranca;
using LAMGestRestaurant.Infrastructure.Dados;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace LAMGestRestaurant.Infrastructure.Identidade;

/// <summary>
/// Gestão de utilizadores e perfis. Cada operação corre numa transacção, regista auditoria e
/// renova o carimbo de segurança dos utilizadores afectados (as sessões abertas reflectem a
/// alteração no prazo de 1 minuto — ver SecurityStampValidatorOptions).
/// Regra de protecção: tem de existir sempre pelo menos um administrador activo.
/// </summary>
public class ServicoUtilizadores(
    AppDbContext db,
    UserManager<Utilizador> utilizadores,
    RoleManager<Perfil> perfis,
    IUtilizadorActual actual,
    IAuditoria auditoria,
    TimeProvider relogio)
{
    // ---------- Utilizadores ----------

    public async Task<Pagina<UtilizadorResumo>> ListarAsync(
        string? pesquisa, bool? activo, string? ordenar, bool descendente, int pagina, int tamanho, CancellationToken ct)
    {
        (pagina, tamanho) = Pagina<UtilizadorResumo>.Normalizar(pagina, tamanho);
        var consulta = db.Users.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(pesquisa))
        {
            var p = pesquisa.Trim();
            consulta = consulta.Where(u => u.UserName!.Contains(p) || u.NomeCompleto.Contains(p));
        }
        if (activo is not null)
            consulta = consulta.Where(u => u.Activo == activo);

        var total = await consulta.CountAsync(ct);
        IOrderedQueryable<Utilizador> ordenada = (ordenar?.ToLowerInvariant(), descendente) switch
        {
            ("nomeutilizador", false) => consulta.OrderBy(u => u.UserName),
            ("nomeutilizador", true) => consulta.OrderByDescending(u => u.UserName),
            ("ultimologin", false) => consulta.OrderBy(u => u.UltimoLoginUtc),
            ("ultimologin", true) => consulta.OrderByDescending(u => u.UltimoLoginUtc),
            (_, true) => consulta.OrderByDescending(u => u.NomeCompleto),
            _ => consulta.OrderBy(u => u.NomeCompleto),
        };
        var pagUtilizadores = await ordenada.ThenBy(u => u.Id)
            .Skip((pagina - 1) * tamanho).Take(tamanho).ToListAsync(ct);

        var ids = pagUtilizadores.Select(u => u.Id).ToList();
        var perfisPorUtilizador = await (from ur in db.UserRoles
                                         join r in db.Roles on ur.RoleId equals r.Id
                                         where ids.Contains(ur.UserId)
                                         select new { ur.UserId, r.Name }).ToListAsync(ct);

        var agora = relogio.GetUtcNow();
        var itens = pagUtilizadores.Select(u => Resumo(u,
            perfisPorUtilizador.Where(x => x.UserId == u.Id).Select(x => x.Name!).Order().ToList(), agora)).ToList();
        return new Pagina<UtilizadorResumo>(itens, total, pagina, tamanho);
    }

    public async Task<UtilizadorResumo> ObterAsync(int id)
    {
        var u = await Encontrar(id);
        return Resumo(u, [.. (await utilizadores.GetRolesAsync(u)).Order()], relogio.GetUtcNow());
    }

    public async Task<UtilizadorResumo> CriarAsync(CriarUtilizador dados)
    {
        await ValidarPerfisExistem(dados.Perfis);
        await using var transacao = await db.Database.BeginTransactionAsync();

        var u = new Utilizador
        {
            UserName = dados.NomeUtilizador.Trim(),
            NomeCompleto = dados.NomeCompleto.Trim(),
            Activo = true,
            DeveAlterarPalavraPasse = true,
            CriadoEmUtc = relogio.GetUtcNow().UtcDateTime,
        };
        Verificar(await utilizadores.CreateAsync(u, dados.PalavraPasseTemporaria));
        Verificar(await utilizadores.AddToRolesAsync(u, dados.Perfis.Distinct()));

        auditoria.Registar("utilizador.criar", "Utilizador", u.Id, new { u.UserName, Perfis = dados.Perfis });
        await db.SaveChangesAsync();
        await transacao.CommitAsync();
        return await ObterAsync(u.Id);
    }

    public async Task<UtilizadorResumo> EditarAsync(int id, EditarUtilizador dados)
    {
        await ValidarPerfisExistem(dados.Perfis);
        await using var transacao = await db.Database.BeginTransactionAsync();
        var u = await Encontrar(id);

        var actuais = await utilizadores.GetRolesAsync(u);
        var novos = dados.Perfis.Distinct().ToList();
        var remover = actuais.Except(novos).ToList();
        var adicionar = novos.Except(actuais).ToList();

        if (remover.Contains(PerfisPadrao.Administrador))
            await GarantirOutroAdministradorActivo(u.Id, "retirar o perfil de administrador");

        u.NomeCompleto = dados.NomeCompleto.Trim();
        Verificar(await utilizadores.UpdateAsync(u));
        if (remover.Count > 0) Verificar(await utilizadores.RemoveFromRolesAsync(u, remover));
        if (adicionar.Count > 0) Verificar(await utilizadores.AddToRolesAsync(u, adicionar));
        if (remover.Count > 0 || adicionar.Count > 0)
            Verificar(await utilizadores.UpdateSecurityStampAsync(u));

        auditoria.Registar("utilizador.editar", "Utilizador", u.Id, new { PerfisRemovidos = remover, PerfisAdicionados = adicionar });
        await db.SaveChangesAsync();
        await transacao.CommitAsync();
        return await ObterAsync(id);
    }

    public async Task DefinirActivoAsync(int id, bool activo)
    {
        await using var transacao = await db.Database.BeginTransactionAsync();
        var u = await Encontrar(id);
        if (u.Activo == activo)
            return;
        if (!activo)
        {
            if (u.Id == actual.Id)
                throw new RegraNegocioException("Não pode desactivar a sua própria conta.");
            if (await utilizadores.IsInRoleAsync(u, PerfisPadrao.Administrador))
                await GarantirOutroAdministradorActivo(u.Id, "desactivar este administrador");
        }

        u.Activo = activo;
        Verificar(await utilizadores.UpdateAsync(u));
        // Termina as sessões abertas do utilizador desactivado.
        Verificar(await utilizadores.UpdateSecurityStampAsync(u));
        auditoria.Registar(activo ? "utilizador.activar" : "utilizador.desactivar", "Utilizador", u.Id, new { u.UserName });
        await db.SaveChangesAsync();
        await transacao.CommitAsync();
    }

    public async Task ReporPalavraPasseAsync(int id, string novaPalavraPasse)
    {
        await using var transacao = await db.Database.BeginTransactionAsync();
        var u = await Encontrar(id);
        var token = await utilizadores.GeneratePasswordResetTokenAsync(u);
        Verificar(await utilizadores.ResetPasswordAsync(u, token, novaPalavraPasse));
        u.DeveAlterarPalavraPasse = true;
        Verificar(await utilizadores.UpdateAsync(u));
        // A reposição também desbloqueia a conta.
        Verificar(await utilizadores.SetLockoutEndDateAsync(u, null));
        Verificar(await utilizadores.ResetAccessFailedCountAsync(u));
        auditoria.Registar("utilizador.repor-palavra-passe", "Utilizador", u.Id, new { u.UserName });
        await db.SaveChangesAsync();
        await transacao.CommitAsync();
    }

    public async Task DesbloquearAsync(int id)
    {
        var u = await Encontrar(id);
        Verificar(await utilizadores.SetLockoutEndDateAsync(u, null));
        Verificar(await utilizadores.ResetAccessFailedCountAsync(u));
        auditoria.Registar("utilizador.desbloquear", "Utilizador", u.Id, new { u.UserName });
        await db.SaveChangesAsync();
    }

    // ---------- Perfis ----------

    public async Task<IReadOnlyList<PerfilResumo>> ListarPerfisAsync(CancellationToken ct)
    {
        var lista = await db.Roles.AsNoTracking().OrderBy(r => r.Name).ToListAsync(ct);
        var claims = await db.RoleClaims.AsNoTracking().Where(c => c.ClaimType == Permissoes.ClaimType).ToListAsync(ct);
        var contagens = await db.UserRoles.GroupBy(ur => ur.RoleId).Select(g => new { g.Key, Total = g.Count() }).ToListAsync(ct);

        return [.. lista.Select(r => new PerfilResumo(
            r.Id, r.Name!, r.Descricao, r.Sistema,
            contagens.FirstOrDefault(c => c.Key == r.Id)?.Total ?? 0,
            [.. claims.Where(c => c.RoleId == r.Id).Select(c => c.ClaimValue!).Order()]))];
    }

    public async Task<PerfilResumo> CriarPerfilAsync(GuardarPerfil dados)
    {
        ValidarPermissoes(dados.Permissoes);
        await using var transacao = await db.Database.BeginTransactionAsync();
        var perfil = new Perfil(dados.Nome.Trim()) { Descricao = dados.Descricao?.Trim(), Sistema = false };
        Verificar(await perfis.CreateAsync(perfil));
        foreach (var p in dados.Permissoes.Distinct())
            Verificar(await perfis.AddClaimAsync(perfil, new Claim(Permissoes.ClaimType, p)));

        auditoria.Registar("perfil.criar", "Perfil", perfil.Id, new { perfil.Name, dados.Permissoes });
        await db.SaveChangesAsync();
        await transacao.CommitAsync();
        return (await ListarPerfisAsync(default)).Single(p => p.Id == perfil.Id);
    }

    public async Task<PerfilResumo> EditarPerfilAsync(int id, GuardarPerfil dados)
    {
        ValidarPermissoes(dados.Permissoes);
        await using var transacao = await db.Database.BeginTransactionAsync();
        var perfil = await perfis.FindByIdAsync(id.ToString()) ?? throw new NaoEncontradoException("Perfil não encontrado.");

        if (perfil.Sistema && !string.Equals(perfil.Name, dados.Nome.Trim(), StringComparison.Ordinal))
            throw new RegraNegocioException("Os perfis iniciais do sistema não podem ser renomeados.");

        var actuais = (await perfis.GetClaimsAsync(perfil)).Where(c => c.Type == Permissoes.ClaimType).ToList();
        var novas = dados.Permissoes.Distinct().ToList();

        if (perfil.Name == PerfisPadrao.Administrador && novas.Count != Permissoes.Todas.Count)
            throw new RegraNegocioException("O perfil Administrador tem sempre todas as permissões.");

        perfil.Name = dados.Nome.Trim();
        perfil.Descricao = dados.Descricao?.Trim();
        Verificar(await perfis.UpdateAsync(perfil));

        var removidas = actuais.Where(c => !novas.Contains(c.Value)).ToList();
        var adicionadas = novas.Where(p => actuais.All(c => c.Value != p)).ToList();
        foreach (var c in removidas)
            Verificar(await perfis.RemoveClaimAsync(perfil, c));
        foreach (var p in adicionadas)
            Verificar(await perfis.AddClaimAsync(perfil, new Claim(Permissoes.ClaimType, p)));

        if (removidas.Count > 0 || adicionadas.Count > 0)
            foreach (var u in await utilizadores.GetUsersInRoleAsync(perfil.Name))
                Verificar(await utilizadores.UpdateSecurityStampAsync(u));

        auditoria.Registar("perfil.editar", "Perfil", perfil.Id, new
        {
            perfil.Name,
            Removidas = removidas.Select(c => c.Value),
            Adicionadas = adicionadas,
        });
        await db.SaveChangesAsync();
        await transacao.CommitAsync();
        return (await ListarPerfisAsync(default)).Single(p => p.Id == id);
    }

    public async Task EliminarPerfilAsync(int id)
    {
        var perfil = await perfis.FindByIdAsync(id.ToString()) ?? throw new NaoEncontradoException("Perfil não encontrado.");
        if (perfil.Sistema)
            throw new RegraNegocioException("Os perfis iniciais do sistema não podem ser eliminados.");
        if (await db.UserRoles.AnyAsync(ur => ur.RoleId == id))
            throw new RegraNegocioException("O perfil está atribuído a utilizadores. Retire-o primeiro.");

        await using var transacao = await db.Database.BeginTransactionAsync();
        Verificar(await perfis.DeleteAsync(perfil));
        auditoria.Registar("perfil.eliminar", "Perfil", id, new { perfil.Name });
        await db.SaveChangesAsync();
        await transacao.CommitAsync();
    }

    public static IReadOnlyList<PermissaoInfo> CatalogoPermissoes() =>
        [.. Permissoes.Todas.Select(p => new PermissaoInfo(p.Key, p.Key.Split('.')[0], p.Value))];

    // ---------- Auxiliares ----------

    private async Task<Utilizador> Encontrar(int id) =>
        await utilizadores.FindByIdAsync(id.ToString()) ?? throw new NaoEncontradoException("Utilizador não encontrado.");

    private async Task GarantirOutroAdministradorActivo(int excluirId, string accao)
    {
        var admins = await utilizadores.GetUsersInRoleAsync(PerfisPadrao.Administrador);
        if (!admins.Any(a => a.Id != excluirId && a.Activo))
            throw new RegraNegocioException($"Não é possível {accao}: tem de existir pelo menos outro administrador activo.");
    }

    private async Task ValidarPerfisExistem(IEnumerable<string> nomes)
    {
        var lista = nomes.Distinct().ToList();
        var existentes = await db.Roles.Where(r => lista.Contains(r.Name!)).Select(r => r.Name).ToListAsync();
        var inexistentes = lista.Except(existentes!).ToList();
        if (inexistentes.Count > 0)
            throw new RegraNegocioException($"Perfil inexistente: {string.Join(", ", inexistentes)}.");
    }

    private static void ValidarPermissoes(IEnumerable<string> permissoes)
    {
        var desconhecidas = permissoes.Where(p => !Permissoes.Todas.ContainsKey(p)).ToList();
        if (desconhecidas.Count > 0)
            throw new RegraNegocioException($"Permissão desconhecida: {string.Join(", ", desconhecidas)}.");
    }

    private static UtilizadorResumo Resumo(Utilizador u, IReadOnlyList<string> perfisUtilizador, DateTimeOffset agora) => new(
        u.Id, u.UserName!, u.NomeCompleto, u.Activo, u.DeveAlterarPalavraPasse,
        u.LockoutEnd > agora ? u.LockoutEnd.Value.UtcDateTime : null,
        u.UltimoLoginUtc, perfisUtilizador, u.PinHash is not null, u.EcraTactil);

    private static void Verificar(IdentityResult resultado)
    {
        if (!resultado.Succeeded)
            throw new RegraNegocioException(string.Join(" ", resultado.Errors.Select(e => e.Description)));
    }
}
