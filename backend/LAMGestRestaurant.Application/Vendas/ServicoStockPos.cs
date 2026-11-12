using LAMGestRestaurant.Application.Comum;
using Microsoft.EntityFrameworkCore;

namespace LAMGestRestaurant.Application.Vendas;

/// <summary>
/// Stock de um produto no POS, em unidades do produto. <see cref="Baixo"/>: o artigo está no stock mínimo ou abaixo dele.
/// </summary>
public record StockPos(int ProdutoId, decimal Quantidade, bool Baixo);

/// <summary>
/// Stock disponível dos produtos que controlam stock com ficha técnica de um só artigo (a criada automaticamente):
/// saldo do artigo no armazém do produto (ou no principal), convertido em unidades vendidas. Só leitura.
/// </summary>
public class ServicoStockPos(IAppDbContext db)
{
    public async Task<IReadOnlyList<StockPos>> ListarAsync(CancellationToken ct)
    {
        var principal = await db.Armazens.AsNoTracking().Where(a => a.Principal && a.Activo).Select(a => (int?)a.Id).FirstOrDefaultAsync(ct);
        var fichas = await db.Receitas.AsNoTracking()
            .Where(r => r.Activa && r.Itens.Count == 1 && r.Produto.ControlaStock && r.Produto.Activo)
            .Select(r => new
            {
                r.ProdutoId,
                r.Produto.ArmazemId,
                Item = r.Itens.Select(i => new
                {
                    i.ArtigoId, i.Quantidade, FactorItem = i.UnidadeMedida.FactorParaBase, FactorArtigo = i.Artigo.UnidadeMedida.FactorParaBase,
                    i.Artigo.StockMinimo,
                }).First(),
            })
            .ToListAsync(ct);
        var artigos = fichas.Select(f => f.Item.ArtigoId).Distinct().ToList();
        var saldos = await db.StockSaldos.AsNoTracking().Where(s => artigos.Contains(s.ArtigoId))
            .ToDictionaryAsync(s => (s.ArtigoId, s.ArmazemId), s => s.Quantidade, ct);

        var lista = new List<StockPos>();
        foreach (var f in fichas)
        {
            if ((f.ArmazemId ?? principal) is not int armazem ||f.Item.Quantidade <= 0 || f.Item.FactorArtigo <= 0)
                continue;
            var saldo = saldos.GetValueOrDefault((f.Item.ArtigoId, armazem));
            // Consumo de uma unidade vendida, na unidade do artigo.
            var consumo = f.Item.Quantidade * f.Item.FactorItem / f.Item.FactorArtigo;
            lista.Add(new StockPos(f.ProdutoId, decimal.Round(saldo / consumo, 3, MidpointRounding.ToZero), saldo <= f.Item.StockMinimo));
        }
        return lista;
    }
}
