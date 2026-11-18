using System.ComponentModel.DataAnnotations;
using LAMGestRestaurant.Application.Comum;
using LAMGestRestaurant.Application.Stock;
using LAMGestRestaurant.Domain.Catalogo;
using LAMGestRestaurant.Domain.Vendas;
using Microsoft.EntityFrameworkCore;

namespace LAMGestRestaurant.Application.Catalogo;

public record OpcaoAcompanhamentoLinhaDto(int ArtigoId, string Artigo, decimal Quantidade, int UnidadeMedidaId, string Unidade);
public record OpcaoAcompanhamentoDto(int Id, string Nome, IReadOnlyList<OpcaoAcompanhamentoLinhaDto> Linhas);
public record ProdutoDoGrupo(int Id, string Nome);
public record GrupoAcompanhamentosDto(int Id, string Nome, int Minimo, int Maximo, bool Activo,
    IReadOnlyList<OpcaoAcompanhamentoDto> Opcoes, IReadOnlyList<ProdutoDoGrupo> Produtos);

/// <summary>Opção a gravar: com Id, altera a existente; sem Id, cria. As que deixam de vir ficam inactivas (o histórico mantém-se).</summary>
public record GuardarOpcaoAcompanhamento(
    int? Id,
    [Required(ErrorMessage = "Indique o nome da opção.")]
    [StringLength(100, MinimumLength = 1, ErrorMessage = "O nome da opção tem no máximo 100 caracteres.")] string Nome,
    IReadOnlyList<NovaLinhaReceita>? Linhas);

public record GuardarGrupoAcompanhamentos(
    [Required(ErrorMessage = "Indique o nome do grupo.")]
    [StringLength(100, MinimumLength = 2, ErrorMessage = "O nome deve ter entre 2 e 100 caracteres.")] string Nome,
    [Range(0, 20, ErrorMessage = "O mínimo deve estar entre 0 e 20.")] int Minimo,
    [Range(1, 20, ErrorMessage = "O máximo deve estar entre 1 e 20.")] int Maximo,
    [Required, MinLength(1, ErrorMessage = "Indique pelo menos uma opção.")] IReadOnlyList<GuardarOpcaoAcompanhamento> Opcoes,
    IReadOnlyList<int>? ProdutoIds);

/// <summary>Grupo de acompanhamentos de um prato, como o POS o mostra (só opções activas).</summary>
public record GrupoPos(int Id, string Nome, int Minimo, int Maximo, IReadOnlyList<OpcaoPos> Opcoes);
public record OpcaoPos(int Id, string Nome);

/// <summary>
/// Acompanhamentos dos pratos (ex.: arroz, batata, salada): escolhidos no POS ao lançar o prato, saem no talão,
/// gastam stock pelos artigos da opção e não mudam o preço. Nunca se vendem sozinhos.
/// </summary>
public class ServicoAcompanhamentos(IAppDbContext db, IAuditoria auditoria)
{
    public async Task<IReadOnlyList<GrupoAcompanhamentosDto>> ListarAsync(bool incluirInactivos, CancellationToken ct)
    {
        var q = db.GruposAcompanhamentos.AsNoTracking();
        if (!incluirInactivos) q = q.Where(g => g.Activo);
        var grupos = await q.OrderBy(g => g.Nome)
            .Include(g => g.Opcoes).ThenInclude(o => o.Itens).ThenInclude(i => i.Artigo)
            .Include(g => g.Opcoes).ThenInclude(o => o.Itens).ThenInclude(i => i.UnidadeMedida)
            .Include(g => g.Produtos).ThenInclude(p => p.Produto)
            .AsSplitQuery()
            .ToListAsync(ct);
        return [.. grupos.Select(ParaDto)];
    }

    public async Task<GrupoAcompanhamentosDto> CriarAsync(GuardarGrupoAcompanhamentos dados)
    {
        await ValidarAsync(dados, null);
        await using var transacao = await db.Database.BeginTransactionAsync();
        var grupo = new GrupoAcompanhamentos();
        db.GruposAcompanhamentos.Add(grupo);
        Aplicar(grupo, dados);
        await SincronizarProdutosAsync(grupo, dados.ProdutoIds);
        await db.SaveChangesAsync();
        auditoria.Registar("acompanhamentos.criar", "GrupoAcompanhamentos", grupo.Id, Resumo(dados));
        await db.SaveChangesAsync();
        await transacao.CommitAsync();
        return await ObterAsync(grupo.Id);
    }

    public async Task<GrupoAcompanhamentosDto> EditarAsync(int id, GuardarGrupoAcompanhamentos dados)
    {
        await ValidarAsync(dados, id);
        await using var transacao = await db.Database.BeginTransactionAsync();
        var grupo = await db.GruposAcompanhamentos
            .Include(g => g.Opcoes).ThenInclude(o => o.Itens)
            .Include(g => g.Produtos)
            .AsSplitQuery()
            .FirstOrDefaultAsync(g => g.Id == id) ?? throw new NaoEncontradoException("Grupo de acompanhamentos não encontrado.");
        if (dados.Opcoes.Any(o => o.Id is int oid && grupo.Opcoes.All(x => x.Id != oid)))
            throw new RegraNegocioException("Uma das opções não pertence a este grupo. Actualize o ecrã.");
        Aplicar(grupo, dados);
        await SincronizarProdutosAsync(grupo, dados.ProdutoIds);
        auditoria.Registar("acompanhamentos.editar", "GrupoAcompanhamentos", id, Resumo(dados));
        await db.SaveChangesAsync();
        await transacao.CommitAsync();
        return await ObterAsync(id);
    }

    public async Task DefinirActivoAsync(int id, bool activo)
    {
        var grupo = await db.GruposAcompanhamentos.FirstOrDefaultAsync(g => g.Id == id) ?? throw new NaoEncontradoException("Grupo de acompanhamentos não encontrado.");
        grupo.Activo = activo;
        auditoria.Registar(activo ? "acompanhamentos.activar" : "acompanhamentos.desactivar", "GrupoAcompanhamentos", id, new { grupo.Nome });
        await db.SaveChangesAsync();
    }

    /// <summary>Grupos activos do prato, pela ordem do nome, com as opções activas (o que o POS mostra ao lançar o prato).</summary>
    public async Task<IReadOnlyList<GrupoPos>> DoProdutoAsync(int produtoId, CancellationToken ct) =>
        await GruposPosQuery([produtoId]).Select(x => x.Grupo).ToListAsync(ct);

    /// <summary>Grupos activos (com opções activas) por produto.</summary>
    internal async Task<Dictionary<int, List<GrupoPos>>> DosProdutosAsync(IReadOnlyCollection<int> produtoIds) =>
        (await GruposPosQuery(produtoIds).ToListAsync())
            .GroupBy(x => x.ProdutoId).ToDictionary(g => g.Key, g => g.Select(x => x.Grupo).ToList());

    private sealed record ProdutoGrupoPos(int ProdutoId, GrupoPos Grupo);

    private IQueryable<ProdutoGrupoPos> GruposPosQuery(IReadOnlyCollection<int> produtoIds) =>
        db.ProdutosGruposAcompanhamentos.AsNoTracking()
            .Where(pg => produtoIds.Contains(pg.ProdutoId) && pg.Grupo.Activo && pg.Grupo.Opcoes.Any(o => o.Activo))
            .OrderBy(pg => pg.Grupo.Nome)
            .Select(pg => new ProdutoGrupoPos(pg.ProdutoId, new GrupoPos(pg.Grupo.Id, pg.Grupo.Nome, pg.Grupo.Minimo, pg.Grupo.Maximo,
                pg.Grupo.Opcoes.Where(o => o.Activo).OrderBy(o => o.Ordem).Select(o => new OpcaoPos(o.Id, o.Nome)).ToList())));

    /// <summary>
    /// Valida as escolhas de um prato no POS e devolve as linhas a gravar (pela ordem do grupo e da opção).
    /// Cada opção tem de ser de um grupo activo do prato; cada grupo respeita o mínimo e o máximo.
    /// </summary>
    internal static List<PedidoItemAcompanhamento> Escolher(string produto, IReadOnlyList<GrupoPos> grupos, IReadOnlyList<int>? escolhidas)
    {
        var ids = escolhidas ?? [];
        if (ids.Distinct().Count() != ids.Count)
            throw new RegraNegocioException($"{produto}: cada acompanhamento só pode ser escolhido uma vez.");
        var porOpcao = grupos.SelectMany(g => g.Opcoes.Select(o => (Grupo: g, Opcao: o))).ToDictionary(x => x.Opcao.Id);
        if (ids.Any(id => !porOpcao.ContainsKey(id)))
            throw new RegraNegocioException($"{produto}: acompanhamento inexistente ou inactivo. Actualize o ecrã.");
        var linhas = new List<PedidoItemAcompanhamento>();
        foreach (var g in grupos)
        {
            var doGrupo = g.Opcoes.Where(o => ids.Contains(o.Id)).ToList();
            if (doGrupo.Count < g.Minimo)
                throw new RegraNegocioException(g.Minimo == 1
                    ? $"{produto}: escolha o {g.Nome.ToLowerInvariant()}."
                    : $"{produto}: escolha pelo menos {g.Minimo} em «{g.Nome}».");
            if (doGrupo.Count > g.Maximo)
                throw new RegraNegocioException($"{produto}: no máximo {g.Maximo} em «{g.Nome}».");
            linhas.AddRange(doGrupo.Select(o => new PedidoItemAcompanhamento { OpcaoAcompanhamentoId = o.Id, Nome = o.Nome }));
        }
        return linhas;
    }

    private async Task<GrupoAcompanhamentosDto> ObterAsync(int id) =>
        (await ListarAsync(true, default)).First(g => g.Id == id);

    private async Task ValidarAsync(GuardarGrupoAcompanhamentos dados, int? id)
    {
        var nome = dados.Nome.Trim();
        if (dados.Maximo < dados.Minimo)
            throw new RegraNegocioException("O máximo não pode ser menor que o mínimo.");
        if (dados.Minimo > dados.Opcoes.Count)
            throw new RegraNegocioException("O mínimo é maior que o número de opções.");
        if (await db.GruposAcompanhamentos.AnyAsync(g => g.Nome == nome && g.Id != id))
            throw new RegraNegocioException($"Já existe o grupo «{nome}».");
        if (dados.Opcoes.Select(o => o.Nome.Trim().ToLowerInvariant()).Distinct().Count() != dados.Opcoes.Count)
            throw new RegraNegocioException("Há opções com o mesmo nome.");
        var linhas = dados.Opcoes.SelectMany(o => o.Linhas ?? []).ToList();
        foreach (var o in dados.Opcoes)
            if ((o.Linhas ?? []).GroupBy(l => l.ArtigoId).Any(g => g.Count() > 1))
                throw new RegraNegocioException($"«{o.Nome.Trim()}»: cada artigo só pode aparecer uma vez. Some as quantidades numa única linha.");
        if (linhas.Count == 0)
            return;
        var artigoIds = linhas.Select(l => l.ArtigoId).Distinct().ToList();
        var artigos = await db.Artigos.AsNoTracking().Include(a => a.UnidadeMedida).Where(a => artigoIds.Contains(a.Id) && a.Activo).ToDictionaryAsync(a => a.Id);
        var unidades = await db.UnidadesMedida.AsNoTracking().ToDictionaryAsync(u => u.Id);
        foreach (var l in linhas)
        {
            if (!artigos.TryGetValue(l.ArtigoId, out var artigo))
                throw new RegraNegocioException("Artigo inexistente ou inactivo num acompanhamento.");
            if (!unidades.TryGetValue(l.UnidadeMedidaId, out var unidade))
                throw new RegraNegocioException("Unidade de medida inválida.");
            if (unidade.Dimensao != artigo.UnidadeMedida.Dimensao)
                throw new RegraNegocioException($"{artigo.Nome} é gerido em {artigo.UnidadeMedida.Codigo}: não pode usar {unidade.Codigo}.");
        }
    }

    private void Aplicar(GrupoAcompanhamentos grupo, GuardarGrupoAcompanhamentos dados)
    {
        grupo.Nome = dados.Nome.Trim();
        grupo.Minimo = dados.Minimo;
        grupo.Maximo = dados.Maximo;
        // As opções retiradas ficam inactivas: podem estar em pedidos.
        foreach (var antiga in grupo.Opcoes.Where(o => dados.Opcoes.All(n => n.Id != o.Id)))
            antiga.Activo = false;
        foreach (var (n, ordem) in dados.Opcoes.Select((n, i) => (n, i)))
        {
            var opcao = n.Id is int oid ? grupo.Opcoes.First(o => o.Id == oid) : new OpcaoAcompanhamento();
            if (n.Id is null) grupo.Opcoes.Add(opcao);
            opcao.Nome = n.Nome.Trim();
            opcao.Ordem = ordem;
            opcao.Activo = true;
            // Os artigos da opção actualizam-se no lugar (índice único opção + artigo); os retirados apagam-se
            // (são configuração, não histórico: o stock já gasto está nos movimentos).
            var linhas = n.Linhas ?? [];
            foreach (var retirado in opcao.Itens.Where(i => linhas.All(l => l.ArtigoId != i.ArtigoId)).ToList())
            {
                db.Entry(retirado).State = EntityState.Deleted; // relação obrigatória: apaga-se pela entrada, não tirando da lista
            }
            foreach (var l in linhas)
            {
                var item = opcao.Itens.FirstOrDefault(i => i.ArtigoId == l.ArtigoId);
                if (item is null) opcao.Itens.Add(item = new OpcaoAcompanhamentoItem { ArtigoId = l.ArtigoId });
                item.Quantidade = decimal.Round(l.Quantidade, 3, MidpointRounding.AwayFromZero);
                item.UnidadeMedidaId = l.UnidadeMedidaId;
            }
        }
    }

    private async Task SincronizarProdutosAsync(GrupoAcompanhamentos grupo, IReadOnlyList<int>? produtoIds)
    {
        var ids = (produtoIds ?? []).Distinct().ToList();
        var existentes = await db.Produtos.Where(p => ids.Contains(p.Id)).Select(p => p.Id).ToListAsync();
        if (existentes.Count != ids.Count)
            throw new RegraNegocioException("Um dos pratos não existe. Actualize o ecrã.");
        foreach (var retirado in grupo.Produtos.Where(p => !ids.Contains(p.ProdutoId)).ToList())
        {
            db.Entry(retirado).State = EntityState.Deleted;
        }
        foreach (var id in ids.Where(id => grupo.Produtos.All(p => p.ProdutoId != id)))
            grupo.Produtos.Add(new ProdutoGrupoAcompanhamentos { ProdutoId = id });
    }

    private static object Resumo(GuardarGrupoAcompanhamentos d) =>
        new { Nome = d.Nome.Trim(), d.Minimo, d.Maximo, Opcoes = d.Opcoes.Select(o => o.Nome.Trim()).ToList(), Pratos = d.ProdutoIds?.Count ?? 0 };

    private static GrupoAcompanhamentosDto ParaDto(GrupoAcompanhamentos g) => new(
        g.Id, g.Nome, g.Minimo, g.Maximo, g.Activo,
        [.. g.Opcoes.Where(o => o.Activo).OrderBy(o => o.Ordem).Select(o => new OpcaoAcompanhamentoDto(o.Id, o.Nome,
            [.. o.Itens.OrderBy(i => i.Artigo.Nome).Select(i => new OpcaoAcompanhamentoLinhaDto(i.ArtigoId, i.Artigo.Nome, i.Quantidade, i.UnidadeMedidaId, i.UnidadeMedida.Codigo))]))],
        [.. g.Produtos.OrderBy(p => p.Produto.Nome).Select(p => new ProdutoDoGrupo(p.ProdutoId, p.Produto.Nome))]);
}
