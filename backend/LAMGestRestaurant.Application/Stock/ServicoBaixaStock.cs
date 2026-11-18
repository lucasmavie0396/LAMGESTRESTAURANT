using LAMGestRestaurant.Application.Comum;
using LAMGestRestaurant.Domain.Comum;
using LAMGestRestaurant.Domain.Sistema;
using LAMGestRestaurant.Domain.Stock;
using LAMGestRestaurant.Domain.Vendas;
using Microsoft.EntityFrameworkCore;

namespace LAMGestRestaurant.Application.Stock;

/// <summary>
/// Baixa de stock das vendas, pela ficha técnica (receita activa) de cada produto, no armazém indicado por <see cref="ArmazensAbate"/>.
/// Garantias contra duplicação: o pedido é marcado com StockBaixado e cada movimento tem origem
/// (PedidoItem, id, artigo) com índice único na BD. Produtos sem receita não movimentam stock;
/// os acompanhamentos escolhidos gastam os artigos da opção (mesmo que o prato não tenha ficha).
/// Cada linha (da ficha ou do acompanhamento) pode indicar o armazém de onde sai; sem ele, o de <see cref="ArmazensAbate"/>.
/// Stock negativo é recusado, salvo se o artigo ou a configuração global o permitirem.
/// Tem de ser chamado dentro da transacção do fecho do pedido.
/// </summary>
public class ServicoBaixaStock(IAppDbContext db, ConfiguracoesSistema config, IUtilizadorActual actual, TimeProvider relogio, ArmazensAbate abate)
{
    public const string OrigemPedidoItem = "PedidoItem";

    public async Task BaixarVendaAsync(Pedido pedido)
    {
        if (pedido.StockBaixado)
            return;

        var itens = pedido.Itens.Where(i => !i.Cancelado).ToList();
        var produtoIds = itens.Select(i => i.ProdutoId).Distinct().ToList();
        // Produtos marcados «não controla stock» (ex.: serviços) não mexem no stock, mesmo que tenham ficha técnica.
        var receitas = await db.Receitas.AsNoTracking()
            .Where(r => r.Activa && produtoIds.Contains(r.ProdutoId) && r.Produto.ControlaStock)
            .Include(r => r.Itens).ThenInclude(i => i.UnidadeMedida)
            .Include(r => r.Itens).ThenInclude(i => i.Artigo).ThenInclude(a => a.UnidadeMedida)
            .ToDictionaryAsync(r => r.ProdutoId);
        // Acompanhamentos escolhidos (ex.: arroz, salada): gastam os artigos da opção, por cada unidade do prato.
        var itemIds = itens.Select(i => i.Id).ToList();
        var acompanhamentos = (await db.PedidoItensAcompanhamentos.AsNoTracking()
                .Where(a => itemIds.Contains(a.PedidoItemId))
                .Include(a => a.OpcaoAcompanhamento).ThenInclude(o => o.Itens).ThenInclude(i => i.UnidadeMedida)
                .Include(a => a.OpcaoAcompanhamento).ThenInclude(o => o.Itens).ThenInclude(i => i.Artigo).ThenInclude(a => a.UnidadeMedida)
                .AsSplitQuery()
                .ToListAsync())
            .GroupBy(a => a.PedidoItemId)
            .ToDictionary(g => g.Key, g => g.SelectMany(a => a.OpcaoAcompanhamento.Itens).ToList());

        var comConsumo = itens.Where(i => receitas.ContainsKey(i.ProdutoId) || acompanhamentos.ContainsKey(i.Id)).ToList();
        if (comConsumo.Count > 0)
        {
            var armazens = await abate.ResolverAsync([.. comConsumo.Select(i => i.ProdutoId).Distinct()]);
            var todosArmazens = await db.Armazens.AsNoTracking().ToDictionaryAsync(a => a.Id);
            var nomesArmazens = todosArmazens.ToDictionary(a => a.Key, a => a.Value.Nome);
            var permitirNegativoGlobal = await config.BoolAsync(ChavesConfiguracao.PermitirStockNegativo);
            var agora = relogio.GetUtcNow().UtcDateTime;
            var saldos = new Dictionary<(int Artigo, int Armazem), StockSaldo>();

            foreach (var item in comConsumo)
            {
                var automatico = armazens[item.ProdutoId];
                // Armazém escolhido na linha (ficha técnica ou acompanhamento); vazio ou inactivo = o automático do produto.
                int Armazem(int? escolhido) => escolhido is int a && todosArmazens.TryGetValue(a, out var x) && x.Activo ? a : automatico;
                var linhas = new List<(Artigo Artigo, int Armazem, decimal Quantidade)>();
                if (receitas.TryGetValue(item.ProdutoId, out var receita))
                    linhas.AddRange(receita.Itens.Select(ri => (ri.Artigo, Armazem(ri.ArmazemId), Converter(ri.Quantidade, ri.UnidadeMedida, ri.Artigo))));
                if (acompanhamentos.TryGetValue(item.Id, out var opcoes))
                    linhas.AddRange(opcoes.Select(oi => (oi.Artigo, Armazem(oi.ArmazemId), Converter(oi.Quantidade, oi.UnidadeMedida, oi.Artigo))));

                // Agrega por artigo e armazém (a ficha e os acompanhamentos podem usar o mesmo artigo; um movimento por artigo, armazém e item).
                // (Agrupa-se pelo Id: sem tracking, cada linha tem a sua instância de Artigo.)
                var consumos = linhas
                    .GroupBy(l => (l.Artigo.Id, l.Armazem))
                    .Select(g => (Artigo: g.First().Artigo, ArmazemId: g.Key.Armazem,
                        Quantidade: decimal.Round(g.Sum(l => item.Quantidade * l.Quantidade), 3, MidpointRounding.AwayFromZero)));

                foreach (var (artigo, armazemId, quantidade) in consumos)
                {
                    if (quantidade <= 0) continue;
                    if (!saldos.TryGetValue((artigo.Id, armazemId), out var saldo))
                    {
                        saldo = await db.StockSaldos.FirstOrDefaultAsync(s => s.ArtigoId == artigo.Id && s.ArmazemId == armazemId)
                                ?? db.StockSaldos.Add(new StockSaldo { ArtigoId = artigo.Id, ArmazemId = armazemId, Quantidade = 0 }).Entity;
                        saldos[(artigo.Id, armazemId)] = saldo;
                    }
                    var restante = saldo.Quantidade - quantidade;
                    if (restante < 0 && !artigo.PermiteStockNegativo && !permitirNegativoGlobal)
                        throw new RegraNegocioException(
                            $"Stock insuficiente de {artigo.Nome} em {nomesArmazens[armazemId]}: disponível {saldo.Quantidade:0.###} {artigo.UnidadeMedida.Codigo}, " +
                            $"necessário {quantidade:0.###} {artigo.UnidadeMedida.Codigo}. Registe a entrada de stock ou peça autorização.");
                    saldo.Quantidade = restante;
                    db.MovimentosStock.Add(new MovimentoStock
                    {
                        ArtigoId = artigo.Id,
                        ArmazemId = armazemId,
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

    /// <summary>
    /// Estorno de uma venda: cada saída de venda dos itens do pedido gera uma entrada de sinal contrário
    /// (EstornoVenda, mesma origem) e repõe o saldo. Os movimentos originais nunca são alterados.
    /// </summary>
    public async Task EstornarVendaAsync(Pedido pedido, string motivo)
    {
        if (!pedido.StockBaixado)
            return;
        var itens = pedido.Itens.Select(i => (int?)i.Id).ToList();
        var saidas = await db.MovimentosStock.AsNoTracking()
            .Where(m => m.Tipo == TipoMovimentoStock.SaidaVenda && m.OrigemTipo == OrigemPedidoItem && itens.Contains(m.OrigemId))
            .ToListAsync();
        var agora = relogio.GetUtcNow().UtcDateTime;
        foreach (var saida in saidas)
        {
            var saldo = await db.StockSaldos.FirstAsync(s => s.ArtigoId == saida.ArtigoId && s.ArmazemId == saida.ArmazemId);
            saldo.Quantidade -= saida.Quantidade; // a saída é negativa: repõe
            db.MovimentosStock.Add(new MovimentoStock
            {
                ArtigoId = saida.ArtigoId,
                ArmazemId = saida.ArmazemId,
                Tipo = TipoMovimentoStock.EstornoVenda,
                Quantidade = -saida.Quantidade,
                CustoUnitario = saida.CustoUnitario,
                OrigemTipo = OrigemPedidoItem,
                OrigemId = saida.OrigemId,
                Motivo = $"Estorno {pedido.Numero}: {motivo}",
                UtilizadorId = actual.Id,
                DataUtc = agora,
            });
        }
        pedido.StockBaixado = false;
    }

    /// <summary>Quantidade da linha da receita convertida para a unidade de stock do artigo.</summary>
    private static decimal Converter(decimal quantidade, UnidadeMedida unidade, Artigo artigo)
    {
        if (unidade.Dimensao != artigo.UnidadeMedida.Dimensao)
            throw new RegraNegocioException(
                $"A ficha técnica usa {unidade.Codigo} para {artigo.Nome}, que é gerido em {artigo.UnidadeMedida.Codigo}: unidades incompatíveis.");
        return quantidade * unidade.FactorParaBase / artigo.UnidadeMedida.FactorParaBase;
    }
}
