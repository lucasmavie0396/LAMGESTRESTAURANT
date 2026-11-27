using System.ComponentModel.DataAnnotations;
using System.Globalization;
using LAMGestRestaurant.Application.Comum;
using LAMGestRestaurant.Application.Seguranca;
using LAMGestRestaurant.Domain.Catalogo;
using LAMGestRestaurant.Domain.Sistema;
using Microsoft.EntityFrameworkCore;

namespace LAMGestRestaurant.Application.Catalogo;

/// <summary><see cref="Retalho"/>: é a tabela do botão «Retalho» do balcão.</summary>
public record TabelaPrecoDto(int Id, string Nome, string? Descricao, bool Activo, int Produtos, IReadOnlyList<string> Zonas, bool Retalho);

public record GuardarTabelaPreco(
    [Required(ErrorMessage = "Indique o nome da tabela.")][StringLength(100, ErrorMessage = "O nome não pode exceder 100 caracteres.")] string Nome,
    [StringLength(300)] string? Descricao);

/// <summary>Preço de um produto numa tabela (vazio = usa o preço normal).</summary>
public record PrecoTabelaProduto(int TabelaPrecoId,
    [Range(typeof(decimal), "0", "999999999", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true, ErrorMessage = "O preço tem de ser zero ou positivo.")] decimal? Preco);

/// <summary>Preço de um produto numa tabela activa (no ProdutoDto).</summary>
public record PrecoTabelaDto(int TabelaPrecoId, decimal Preco);

/// <summary>
/// Tabelas de preço (por zona ou «Retalho» no balcão). Não se apagam: desactivam-se, e só quando nenhuma zona nem o retalho as
/// usa. Os preços por tabela gravam-se com o produto (alterá-los exige produtos.alterar-preco) e ficam na auditoria.
/// </summary>
public class ServicoTabelasPreco(IAppDbContext db, IUtilizadorActual actual, IAuditoria auditoria, ConfiguracoesSistema config)
{
    public async Task<IReadOnlyList<TabelaPrecoDto>> ListarAsync(bool incluirInactivas, CancellationToken ct)
    {
        var retalho = await RetalhoAsync();
        var tabelas = await db.TabelasPreco.AsNoTracking().Where(t => incluirInactivas || t.Activo).OrderBy(t => t.Nome)
            .Select(t => new
            {
                t.Id, t.Nome, t.Descricao, t.Activo,
                Produtos = db.PrecosProdutos.Count(p => p.TabelaPrecoId == t.Id),
                Zonas = db.Zonas.Where(z => z.TabelaPrecoId == t.Id && z.Activo).Select(z => z.Nome).ToList(),
            })
            .ToListAsync(ct);
        return [.. tabelas.Select(t => new TabelaPrecoDto(t.Id, t.Nome, t.Descricao, t.Activo, t.Produtos, t.Zonas, t.Id == retalho))];
    }

    public async Task<TabelaPrecoDto> CriarAsync(GuardarTabelaPreco dados)
    {
        var nome = dados.Nome.Trim();
        if (await db.TabelasPreco.AnyAsync(t => t.Nome == nome))
            throw new ConflitoException("Já existe uma tabela de preço com este nome.");
        await using var transacao = await db.Database.BeginTransactionAsync();
        var tabela = new TabelaPreco { Nome = nome, Descricao = Limpar(dados.Descricao) };
        db.TabelasPreco.Add(tabela);
        await db.SaveChangesAsync();
        auditoria.Registar("tabela-precos.criar", "TabelaPreco", tabela.Id, new { tabela.Nome });
        await db.SaveChangesAsync();
        await transacao.CommitAsync();
        return (await ListarAsync(true, default)).Single(t => t.Id == tabela.Id);
    }

    public async Task<TabelaPrecoDto> EditarAsync(int id, GuardarTabelaPreco dados)
    {
        var tabela = await db.TabelasPreco.FindAsync(id) ?? throw new NaoEncontradoException("Tabela de preço não encontrada.");
        var nome = dados.Nome.Trim();
        if (await db.TabelasPreco.AnyAsync(t => t.Id != id && t.Nome == nome))
            throw new ConflitoException("Já existe uma tabela de preço com este nome.");
        tabela.Nome = nome;
        tabela.Descricao = Limpar(dados.Descricao);
        auditoria.Registar("tabela-precos.editar", "TabelaPreco", id, new { tabela.Nome });
        await db.SaveChangesAsync();
        return (await ListarAsync(true, default)).Single(t => t.Id == id);
    }

    public async Task<TabelaPrecoDto> DefinirActivaAsync(int id, bool activa)
    {
        var tabela = await db.TabelasPreco.FindAsync(id) ?? throw new NaoEncontradoException("Tabela de preço não encontrada.");
        if (!activa)
        {
            var zonas = await db.Zonas.Where(z => z.TabelaPrecoId == id && z.Activo).Select(z => z.Nome).ToListAsync();
            if (zonas.Count > 0)
                throw new RegraNegocioException($"A tabela está nas zonas {string.Join(", ", zonas)}. Tire-a das zonas primeiro.");
            if (await RetalhoAsync() == id)
                throw new RegraNegocioException("É a tabela do retalho. Escolha outra (ou nenhuma) em Configurações → Documentos e POS.");
        }
        tabela.Activo = activa;
        auditoria.Registar(activa ? "tabela-precos.activar" : "tabela-precos.desactivar", "TabelaPreco", id, new { tabela.Nome });
        await db.SaveChangesAsync();
        return (await ListarAsync(true, default)).Single(t => t.Id == id);
    }

    /// <summary>
    /// Grava os preços do produto nas tabelas indicadas (vazio = tira o preço da tabela). Chamar dentro da transacção do produto,
    /// com o produto já gravado. Num produto existente, mudar preços exige produtos.alterar-preco.
    /// </summary>
    internal async Task AplicarAsync(Produto produto, IReadOnlyList<PrecoTabelaProduto>? precos, bool novo)
    {
        if (precos is null) return;
        var ids = precos.Select(p => p.TabelaPrecoId).Distinct().ToList();
        if (ids.Count != precos.Count)
            throw new RegraNegocioException("A mesma tabela de preço aparece mais de uma vez.");
        var tabelas = await db.TabelasPreco.Where(t => ids.Contains(t.Id)).ToDictionaryAsync(t => t.Id, t => t.Nome);
        if (tabelas.Count != ids.Count)
            throw new RegraNegocioException("Tabela de preço inexistente.");
        var actuais = await db.PrecosProdutos.Where(p => p.ProdutoId == produto.Id && ids.Contains(p.TabelaPrecoId)).ToDictionaryAsync(p => p.TabelaPrecoId);
        var mudancas = new List<object>();
        foreach (var p in precos)
        {
            var preco = p.Preco is decimal v ? decimal.Round(v, 2, MidpointRounding.AwayFromZero) : (decimal?)null;
            actuais.TryGetValue(p.TabelaPrecoId, out var existente);
            if (existente?.Preco == preco) continue;
            if (existente is null && preco is null) continue;
            mudancas.Add(new { Tabela = tabelas[p.TabelaPrecoId], Anterior = existente?.Preco, Novo = preco });
            if (preco is null) db.PrecosProdutos.Remove(existente!);
            else if (existente is null) db.PrecosProdutos.Add(new PrecoProduto { TabelaPrecoId = p.TabelaPrecoId, ProdutoId = produto.Id, Preco = preco.Value });
            else existente.Preco = preco.Value;
        }
        if (mudancas.Count == 0) return;
        if (!novo && !actual.TemPermissao(Permissoes.ProdutosAlterarPreco))
            throw new RegraNegocioException("Não tem permissão para alterar preços de venda.");
        auditoria.Registar("produto.alterar-preco-tabela", "Produto", produto.Id, new { produto.Codigo, Precos = mudancas });
        await db.SaveChangesAsync();
    }

    private async Task<int?> RetalhoAsync() =>
        int.TryParse(await config.TextoAsync(ChavesConfiguracao.PosTabelaRetalho), NumberStyles.None, CultureInfo.InvariantCulture, out var id) ? id : null;

    private static string? Limpar(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
