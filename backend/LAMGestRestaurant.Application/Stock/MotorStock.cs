using LAMGestRestaurant.Application.Comum;
using LAMGestRestaurant.Domain.Comum;
using LAMGestRestaurant.Domain.Sistema;
using LAMGestRestaurant.Domain.Stock;
using Microsoft.EntityFrameworkCore;

namespace LAMGestRestaurant.Application.Stock;

public static class Unidades
{
    /// <summary>Converte uma quantidade entre unidades da mesma dimensão (ex.: 500 g → 0,5 kg).</summary>
    public static decimal Converter(decimal quantidade, UnidadeMedida de, UnidadeMedida para, string artigo)
    {
        if (de.Id == para.Id) return quantidade;
        if (de.Dimensao != para.Dimensao)
            throw new RegraNegocioException($"Unidades incompatíveis para {artigo}: {de.Codigo} e {para.Codigo}.");
        return quantidade * de.FactorParaBase / para.FactorParaBase;
    }
}

/// <summary>
/// Ponto único para movimentar stock fora das vendas (compras, ajustes, perdas, transferências, inventários):
/// cria o movimento imutável e actualiza o saldo, aplicando a regra de stock não negativo.
/// Tem de ser usado dentro da transacção do chamador.
/// </summary>
public class MotorStock(IAppDbContext db, ConfiguracoesSistema config, IUtilizadorActual actual, TimeProvider relogio)
{
    private readonly Dictionary<(int, int), StockSaldo> saldos = [];
    private bool? negativoGlobal;

    public async Task<StockSaldo> SaldoAsync(int artigoId, int armazemId)
    {
        if (saldos.TryGetValue((artigoId, armazemId), out var s)) return s;
        s = await db.StockSaldos.FirstOrDefaultAsync(x => x.ArtigoId == artigoId && x.ArmazemId == armazemId)
            ?? db.StockSaldos.Add(new StockSaldo { ArtigoId = artigoId, ArmazemId = armazemId }).Entity;
        saldos[(artigoId, armazemId)] = s;
        return s;
    }

    /// <summary>Movimenta «quantidade» (com sinal, na unidade do artigo) e devolve o movimento criado.</summary>
    public async Task<MovimentoStock> MovimentarAsync(Artigo artigo, int armazemId, TipoMovimentoStock tipo, decimal quantidade,
        decimal custoUnitario, string? origemTipo, int? origemId, string? motivo, bool ignorarNegativo = false)
    {
        quantidade = decimal.Round(quantidade, 3, MidpointRounding.AwayFromZero);
        if (quantidade == 0)
            throw new RegraNegocioException($"Quantidade nula para {artigo.Nome}.");
        var saldo = await SaldoAsync(artigo.Id, armazemId);
        var novo = saldo.Quantidade + quantidade;
        if (novo < 0 && quantidade < 0 && !ignorarNegativo && !artigo.PermiteStockNegativo
            && !(negativoGlobal ??= await config.BoolAsync(ChavesConfiguracao.PermitirStockNegativo)))
            throw new RegraNegocioException(
                $"Stock insuficiente de {artigo.Nome}: disponível {saldo.Quantidade:0.###}, a retirar {-quantidade:0.###}.");
        saldo.Quantidade = novo;
        var movimento = new MovimentoStock
        {
            ArtigoId = artigo.Id,
            ArmazemId = armazemId,
            Tipo = tipo,
            Quantidade = quantidade,
            CustoUnitario = custoUnitario,
            OrigemTipo = origemTipo,
            OrigemId = origemId,
            Motivo = motivo,
            UtilizadorId = actual.Id,
            DataUtc = relogio.GetUtcNow().UtcDateTime,
        };
        db.MovimentosStock.Add(movimento);
        return movimento;
    }

    /// <summary>Saldo total do artigo em todos os armazéns (inclui alterações ainda não gravadas deste motor).</summary>
    public async Task<decimal> SaldoTotalAsync(int artigoId)
    {
        var gravado = await db.StockSaldos.AsNoTracking().Where(s => s.ArtigoId == artigoId).ToListAsync();
        return gravado.Sum(s => saldos.TryGetValue((s.ArtigoId, s.ArmazemId), out var local) ? local.Quantidade : s.Quantidade)
               + saldos.Where(k => k.Key.Item1 == artigoId && gravado.All(g => g.ArmazemId != k.Key.Item2)).Sum(k => k.Value.Quantidade);
    }
}
