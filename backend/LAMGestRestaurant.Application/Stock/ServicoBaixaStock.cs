using LAMGestRestaurant.Application.Comum;
using LAMGestRestaurant.Domain.Comum;
using LAMGestRestaurant.Domain.Sistema;
using LAMGestRestaurant.Domain.Stock;
using LAMGestRestaurant.Domain.Vendas;
using Microsoft.EntityFrameworkCore;

namespace LAMGestRestaurant.Application.Stock;

/// <summary>
/// Baixa de stock das vendas, pela ficha técnica (receita activa) de cada produto, no armazém principal.
/// Garantias contra duplicação: o pedido é marcado com StockBaixado e cada movimento tem origem
/// (PedidoItem, id) com índice único na BD. Produtos sem receita não movimentam stock.
/// Stock negativo é recusado, salvo se o artigo ou a configuração global o permitirem.
/// Tem de ser chamado dentro da transacção do fecho do pedido.
/// </summary>
public class ServicoBaixaStock(IAppDbContext db, ConfiguracoesSistema config, IUtilizadorActual actual, TimeProvider relogio)
{
    public const string OrigemPedidoItem = "PedidoItem";

    public async Task BaixarVendaAsync(Pedido pedido)
    {
        if (pedido.StockBaixado)
            return;

        var itens = pedido.Itens.Where(i => !i.Cancelado).ToList();
        var produtoIds = itens.Select(i => i.ProdutoId).Distinct().ToList();
        var receitas = await db.Receitas.AsNoTracking()
            .Where(r => r.Activa && produtoIds.Contains(r.ProdutoId))
            .Include(r => r.Itens).ThenInclude(i => i.UnidadeMedida)
            .Include(r => r.Itens).ThenInclude(i => i.Artigo).ThenInclude(a => a.UnidadeMedida)
            .ToDictionaryAsync(r => r.ProdutoId);

        if (receitas.Count > 0)
        {
            var armazem = await db.Armazens.FirstOrDefaultAsync(a => a.Principal && a.Activo)
                          ?? throw new RegraNegocioException("Não existe armazém principal activo para a baixa de stock.");
            var permitirNegativoGlobal = await config.BoolAsync(ChavesConfiguracao.PermitirStockNegativo);
            var agora = relogio.GetUtcNow().UtcDateTime;
            var saldos = new Dictionary<int, StockSaldo>();

            foreach (var item in itens)
            {
                if (!receitas.TryGetValue(item.ProdutoId, out var receita))
                    continue;

                // Agrega por artigo (a mesma receita pode ter o artigo em linhas/unidades diferentes).
                // (Agrupa-se pelo Id: sem tracking, cada linha tem a sua instância de Artigo.)
                var consumos = receita.Itens
                    .GroupBy(ri => ri.ArtigoId)
                    .Select(g => (Artigo: g.First().Artigo,
                        Quantidade: decimal.Round(g.Sum(ri => item.Quantidade * Converter(ri, g.First().Artigo)), 3, MidpointRounding.AwayFromZero)));

                foreach (var (artigo, quantidade) in consumos)
                {
                    if (quantidade <= 0) continue;
                    if (!saldos.TryGetValue(artigo.Id, out var saldo))
                    {
                        saldo = await db.StockSaldos.FirstOrDefaultAsync(s => s.ArtigoId == artigo.Id && s.ArmazemId == armazem.Id)
                                ?? db.StockSaldos.Add(new StockSaldo { ArtigoId = artigo.Id, ArmazemId = armazem.Id, Quantidade = 0 }).Entity;
                        saldos[artigo.Id] = saldo;
                    }
                    var restante = saldo.Quantidade - quantidade;
                    if (restante < 0 && !artigo.PermiteStockNegativo && !permitirNegativoGlobal)
                        throw new RegraNegocioException(
                            $"Stock insuficiente de {artigo.Nome}: disponível {saldo.Quantidade:0.###} {artigo.UnidadeMedida.Codigo}, " +
                            $"necessário {quantidade:0.###} {artigo.UnidadeMedida.Codigo}. Registe a entrada de stock ou peça autorização.");
                    saldo.Quantidade = restante;
                    db.MovimentosStock.Add(new MovimentoStock
                    {
                        ArtigoId = artigo.Id,
                        ArmazemId = armazem.Id,
                        Tipo = TipoMovimentoStock.SaidaVenda,
                        Quantidade = -quantidade,
                        CustoUnitario = artigo.CustoMedio,
                        OrigemTipo = OrigemPedidoItem,
                        OrigemId = item.Id,
                        Motivo = $"Venda {pedido.Numero}",
                        UtilizadorId = actual.Id,
                        DataUtc = agora,
                    });
                }
            }
        }
        pedido.StockBaixado = true;
    }

    /// <summary>Quantidade da linha da receita convertida para a unidade de stock do artigo.</summary>
    private static decimal Converter(ReceitaItem linha, Artigo artigo)
    {
        if (linha.UnidadeMedida.Dimensao != artigo.UnidadeMedida.Dimensao)
            throw new RegraNegocioException(
                $"A ficha técnica usa {linha.UnidadeMedida.Codigo} para {artigo.Nome}, que é gerido em {artigo.UnidadeMedida.Codigo}: unidades incompatíveis.");
        return linha.Quantidade * linha.UnidadeMedida.FactorParaBase / artigo.UnidadeMedida.FactorParaBase;
    }
}
