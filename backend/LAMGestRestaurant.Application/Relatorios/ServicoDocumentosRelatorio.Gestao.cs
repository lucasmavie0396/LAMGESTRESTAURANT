using System.Globalization;
using LAMGestRestaurant.Application.Caixa;
using LAMGestRestaurant.Application.Comum;
using LAMGestRestaurant.Application.Impressao;
using LAMGestRestaurant.Application.Seguranca;
using LAMGestRestaurant.Domain.Comum;
using LAMGestRestaurant.Domain.Sistema;
using Microsoft.EntityFrameworkCore;

namespace LAMGestRestaurant.Application.Relatorios;

public partial class ServicoDocumentosRelatorio
{
    private async Task<DocumentoImpressao> DespesasAsync(DateOnly desde, DateOnly ate, CancellationToken ct)
    {
        var (inicio, fim) = await config.IntervaloUtcAsync(desde, ate);
        var despesas = await db.Despesas.AsNoTracking()
            .Where(d => d.DataUtc >= inicio && d.DataUtc < fim)
            .OrderBy(d => d.DataUtc)
            .Select(d => new { d.DataUtc, Categoria = d.CategoriaDespesa.Nome, d.Descricao, d.Valor, Metodo = d.MetodoPagamento.Nome, d.Referencia, d.Estado, Caixa = d.SessaoCaixaId })
            .ToListAsync(ct);
        var confirmadas = despesas.Where(d => d.Estado == EstadoDespesa.Confirmada).ToList();
        var anuladas = despesas.Where(d => d.Estado == EstadoDespesa.Anulada).ToList();
        var total = confirmadas.Sum(d => d.Valor);

        var l = await CabecalhoAsync("Relatório de despesas", Periodo(desde, ate));
        l.Add(LinhaImpressao.NovoCartao($"Despesas ({confirmadas.Count})", Valor(total)));
        l.Add(LinhaImpressao.NovoCartao("Categorias", Inteiro(confirmadas.Select(d => d.Categoria).Distinct().Count())));
        l.Add(LinhaImpressao.NovoCartao($"Anuladas ({anuladas.Count})", Valor(anuladas.Sum(d => d.Valor)), alerta: anuladas.Count > 0));
        Grelha(l, "Despesas confirmadas", Resumo(confirmadas.Count, "despesa", total), ["Data", "Categoria", "Descrição", "Pagamento", "Valor"],
            confirmadas.Select(d => Lin(Hora(d.DataUtc, true), d.Categoria, d.Descricao,
                string.Join(" · ", new[] { d.Metodo, d.Referencia is null ? null : $"Ref. {d.Referencia}", d.Caixa is int s ? $"caixa n.º {s}" : null }.Where(x => x is not null)),
                Valor(d.Valor))),
            subtotal: ["Total", "", "", "", Valor(total)], texto: 4, vazio: "Sem despesas no período.");
        Grelha(l, "Por categoria", null, ["Categoria", "Despesas", "Valor", "%"],
            confirmadas.GroupBy(d => d.Categoria).OrderByDescending(g => g.Sum(d => d.Valor))
                .Select(g => Lin(g.Key, Inteiro(g.Count()), Valor(g.Sum(d => d.Valor)), Pct(g.Sum(d => d.Valor), total))));
        l.Add(new($"TOTAL ({confirmadas.Count})", Valor(total), Negrito: true, Grande: true));
        if (anuladas.Count > 0)
            l.Add(new($"Anuladas (não contam): {anuladas.Count}", Valor(anuladas.Sum(d => d.Valor))));
        Rodape(l);
        return new DocumentoImpressao("Despesas " + Periodo(desde, ate), l, cabecalho);
    }

    private async Task<DocumentoImpressao> FinanceiroAsync(DateOnly desde, DateOnly ate, CancellationToken ct)
    {
        var r = await gestao.FinanceiroAsync(desde, ate, ct);
        var l = await CabecalhoAsync("Relatório financeiro", Periodo(desde, ate));
        l.Add(LinhaImpressao.NovoCartao("Vendas (com impostos)", Valor(r.TotalVendas)));
        l.Add(LinhaImpressao.NovoCartao("Despesas", Valor(r.Despesas)));
        l.Add(LinhaImpressao.NovoCartao("Resultado estimado", Valor(r.ResultadoEstimado), alerta: r.ResultadoEstimado < 0));
        Grelha(l, "Resultado", null, ["Descrição", "Valor"],
        [
            Lin("Vendas (com impostos)", Valor(r.TotalVendas)),
            Lin("Impostos incluídos", "-" + Valor(r.Impostos)),
            LinhaImpressao.Tabela(["Vendas sem impostos", Valor(r.VendasSemImpostos)], negrito: true),
            Lin("Custo estimado das vendas", "-" + Valor(r.CustoEstimadoVendas)),
            Lin("Despesas", "-" + Valor(r.Despesas)),
        ], subtotal: ["Resultado estimado", Valor(r.ResultadoEstimado)]);
        l.Add(new($"Fora do resultado: estornos {Valor(r.Estornos)} ({r.PedidosEstornados}); compras {Valor(r.ComprasConfirmadas)} ({r.NumeroCompras})"));
        Grelha(l, "Recebimentos por método", null, ["Método", "Quantidade", "Valor"],
            r.RecebimentosPorMetodo.Select(m => Lin(m.Rotulo, Inteiro(m.Quantidade), Valor(m.Valor))),
            subtotal: ["Total", Inteiro(r.RecebimentosPorMetodo.Sum(m => m.Quantidade)), Valor(r.RecebimentosPorMetodo.Sum(m => m.Valor))]);
        Grelha(l, "Despesas por categoria", null, ["Categoria", "Quantidade", "Valor"],
            r.DespesasPorCategoria.Select(m => Lin(m.Rotulo, Inteiro(m.Quantidade), Valor(m.Valor))),
            subtotal: ["Total", Inteiro(r.DespesasPorCategoria.Sum(m => m.Quantidade)), Valor(r.DespesasPorCategoria.Sum(m => m.Valor))]);
        Grelha(l, "Fechos de caixa", r.SessoesAbertas > 0 ? $"caixas ainda abertos: {r.SessoesAbertas}" : null, ["Sessão", "Terminal", "Aberta por", "Diferença"],
            r.SessoesFechadas.Select(s => Lin(s.Diferenca != 0, $"N.º {s.SessaoId}", s.Terminal, s.AbertaPor, (s.Diferenca > 0 ? "+" : "") + Valor(s.Diferenca))),
            subtotal: ["Diferença total", "", "", Valor(r.SessoesFechadas.Sum(s => s.Diferenca))], texto: 3, vazio: "Sem caixas fechados no período.");
        l.Add(new("RESULTADO ESTIMADO", Valor(r.ResultadoEstimado), Negrito: true, Grande: true));
        Rodape(l);
        return new DocumentoImpressao("Financeiro " + Periodo(desde, ate), l, cabecalho);
    }
}
