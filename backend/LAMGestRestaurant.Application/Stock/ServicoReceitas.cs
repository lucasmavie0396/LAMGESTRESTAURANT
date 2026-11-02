using System.ComponentModel.DataAnnotations;
using LAMGestRestaurant.Application.Comum;
using LAMGestRestaurant.Domain.Stock;
using Microsoft.EntityFrameworkCore;

namespace LAMGestRestaurant.Application.Stock;

public record ReceitaLinhaDto(int ArtigoId, string Codigo, string Artigo, decimal Quantidade, int UnidadeMedidaId, string Unidade, decimal CustoEstimado);

public record ReceitaDto(
    int Id,
    int ProdutoId,
    string Produto,
    decimal PrecoVenda,
    int Versao,
    bool Activa,
    DateTime ValidaDesdeUtc,
    DateTime? ValidaAteUtc,
    string? Observacoes,
    decimal CustoEstimado,
    decimal? MargemPercentagem,
    IReadOnlyList<ReceitaLinhaDto> Linhas);

/// <summary>Estado da ficha técnica de cada produto (para a lista do ecrã).</summary>
public record ProdutoFicha(int ProdutoId, string Codigo, string Produto, string Categoria, decimal PrecoVenda, bool Activo,
    int? VersaoActiva, decimal? CustoEstimado, int NumeroIngredientes);

public record NovaLinhaReceita(
    [Range(1, int.MaxValue, ErrorMessage = "Seleccione o artigo.")] int ArtigoId,
    [Range(typeof(decimal), "0.001", "99999", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true,
        ErrorMessage = "A quantidade tem de ser positiva.")] decimal Quantidade,
    [Range(1, int.MaxValue, ErrorMessage = "Seleccione a unidade.")] int UnidadeMedidaId);

public record GuardarReceita([Required, MinLength(1, ErrorMessage = "Indique pelo menos um ingrediente.")] IReadOnlyList<NovaLinhaReceita> Linhas,
    [StringLength(1000)] string? Observacoes);

/// <summary>
/// Fichas técnicas versionadas. Alterar uma ficha cria uma nova versão e encerra a anterior (ValidaAte),
/// para que o custo histórico das vendas continue rastreável. As vendas usam sempre a versão activa
/// no momento do fecho do pedido.
/// </summary>
public class ServicoReceitas(IAppDbContext db, IAuditoria auditoria, TimeProvider relogio)
{
    public async Task<IReadOnlyList<ProdutoFicha>> ListarAsync(string? pesquisa, CancellationToken ct)
    {
        var q = db.Produtos.AsNoTracking().Where(p => p.Activo);
        if (!string.IsNullOrWhiteSpace(pesquisa))
            q = q.Where(p => p.Nome.Contains(pesquisa.Trim()) || p.Codigo.Contains(pesquisa.Trim()));
        var produtos = await q.OrderBy(p => p.Categoria.Nome).ThenBy(p => p.Nome)
            .Select(p => new { p.Id, p.Codigo, p.Nome, Categoria = p.Categoria.Nome, p.PrecoVenda, p.Activo }).ToListAsync(ct);
        var ids = produtos.Select(p => p.Id).ToList();
        var activas = await CarregarAsync(db.Receitas.AsNoTracking().Where(r => r.Activa && ids.Contains(r.ProdutoId)), ct);
        return [.. produtos.Select(p =>
        {
            var r = activas.FirstOrDefault(x => x.ProdutoId == p.Id);
            return new ProdutoFicha(p.Id, p.Codigo, p.Nome, p.Categoria, p.PrecoVenda, p.Activo, r?.Versao, r is null ? null : Custo(r), r?.Itens.Count ?? 0);
        })];
    }

    /// <summary>Todas as versões da ficha do produto, da mais recente para a mais antiga.</summary>
    public async Task<IReadOnlyList<ReceitaDto>> VersoesAsync(int produtoId, CancellationToken ct)
    {
        var produto = await db.Produtos.AsNoTracking().FirstOrDefaultAsync(p => p.Id == produtoId, ct) ?? throw new NaoEncontradoException("Produto não encontrado.");
        var receitas = await CarregarAsync(db.Receitas.AsNoTracking().Where(r => r.ProdutoId == produtoId), ct);
        return [.. receitas.OrderByDescending(r => r.Versao).Select(r => ParaDto(r, produto.Nome, produto.PrecoVenda))];
    }

    public async Task<ReceitaDto> GuardarAsync(int produtoId, GuardarReceita dados)
    {
        var produto = await db.Produtos.FirstOrDefaultAsync(p => p.Id == produtoId && p.Activo) ?? throw new RegraNegocioException("Produto inexistente ou inactivo.");
        if (dados.Linhas.GroupBy(l => l.ArtigoId).Any(g => g.Count() > 1))
            throw new RegraNegocioException("Cada artigo só pode aparecer uma vez na ficha. Some as quantidades numa única linha.");
        var artigoIds = dados.Linhas.Select(l => l.ArtigoId).ToList();
        var artigos = await db.Artigos.Include(a => a.UnidadeMedida).Where(a => artigoIds.Contains(a.Id) && a.Activo).ToDictionaryAsync(a => a.Id);
        var unidades = await db.UnidadesMedida.ToDictionaryAsync(u => u.Id);
        foreach (var l in dados.Linhas)
        {
            if (!artigos.TryGetValue(l.ArtigoId, out var artigo))
                throw new RegraNegocioException("Artigo inexistente ou inactivo na ficha técnica.");
            if (!unidades.TryGetValue(l.UnidadeMedidaId, out var unidade))
                throw new RegraNegocioException("Unidade de medida inválida.");
            if (unidade.Dimensao != artigo.UnidadeMedida.Dimensao)
                throw new RegraNegocioException($"{artigo.Nome} é gerido em {artigo.UnidadeMedida.Codigo}: não pode usar {unidade.Codigo} na ficha.");
        }

        var agora = relogio.GetUtcNow().UtcDateTime;
        await using var transacao = await db.Database.BeginTransactionAsync();
        var anterior = await db.Receitas.FirstOrDefaultAsync(r => r.ProdutoId == produtoId && r.Activa);
        var versao = (await db.Receitas.Where(r => r.ProdutoId == produtoId).MaxAsync(r => (int?)r.Versao) ?? 0) + 1;
        if (anterior is not null)
        {
            // Primeiro encerra a versão activa (índice único: uma activa por produto).
            anterior.Activa = false;
            anterior.ValidaAteUtc = agora;
            await db.SaveChangesAsync();
        }
        var receita = new Receita
        {
            ProdutoId = produtoId,
            Versao = versao,
            Activa = true,
            ValidaDesdeUtc = agora,
            Observacoes = string.IsNullOrWhiteSpace(dados.Observacoes) ? null : dados.Observacoes.Trim(),
            Itens = [.. dados.Linhas.Select(l => new ReceitaItem
            {
                ArtigoId = l.ArtigoId,
                Quantidade = decimal.Round(l.Quantidade, 3, MidpointRounding.AwayFromZero),
                UnidadeMedidaId = l.UnidadeMedidaId,
            })],
        };
        db.Receitas.Add(receita);
        await db.SaveChangesAsync();
        auditoria.Registar("receita.guardar", "Produto", produtoId, new { produto.Codigo, Versao = versao, Ingredientes = dados.Linhas.Count });
        await db.SaveChangesAsync();
        await transacao.CommitAsync();
        return (await VersoesAsync(produtoId, default)).First();
    }

    /// <summary>Retira a ficha activa: o produto deixa de baixar stock (o histórico das versões mantém-se).</summary>
    public async Task RetirarAsync(int produtoId)
    {
        var activa = await db.Receitas.FirstOrDefaultAsync(r => r.ProdutoId == produtoId && r.Activa)
                     ?? throw new RegraNegocioException("O produto não tem ficha técnica activa.");
        activa.Activa = false;
        activa.ValidaAteUtc = relogio.GetUtcNow().UtcDateTime;
        auditoria.Registar("receita.retirar", "Produto", produtoId, new { activa.Versao });
        await db.SaveChangesAsync();
    }

    private static async Task<List<Receita>> CarregarAsync(IQueryable<Receita> q, CancellationToken ct) =>
        await q.Include(r => r.Itens).ThenInclude(i => i.Artigo).ThenInclude(a => a.UnidadeMedida)
            .Include(r => r.Itens).ThenInclude(i => i.UnidadeMedida)
            .AsSplitQuery()
            .ToListAsync(ct);

    private static decimal CustoLinha(ReceitaItem i) =>
        Unidades.Converter(i.Quantidade, i.UnidadeMedida, i.Artigo.UnidadeMedida, i.Artigo.Nome) * i.Artigo.CustoMedio;

    /// <summary>Custo estimado com o custo médio actual dos artigos.</summary>
    private static decimal Custo(Receita r) => decimal.Round(r.Itens.Sum(CustoLinha), 2, MidpointRounding.AwayFromZero);

    private static ReceitaDto ParaDto(Receita r, string produto, decimal preco)
    {
        var custo = Custo(r);
        // Margem sobre o preço sem IVA não é conhecida aqui; usa-se o preço de venda (indicativo).
        return new ReceitaDto(r.Id, r.ProdutoId, produto, preco, r.Versao, r.Activa, r.ValidaDesdeUtc, r.ValidaAteUtc, r.Observacoes, custo,
            preco > 0 ? decimal.Round((preco - custo) / preco * 100m, 1, MidpointRounding.AwayFromZero) : null,
            [.. r.Itens.OrderBy(i => i.Artigo.Nome).Select(i => new ReceitaLinhaDto(i.ArtigoId, i.Artigo.Codigo, i.Artigo.Nome, i.Quantidade,
                i.UnidadeMedidaId, i.UnidadeMedida.Codigo, decimal.Round(CustoLinha(i), 2, MidpointRounding.AwayFromZero)))]);
    }
}
