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
    /// <summary>
    /// Fecho de uma sessão de caixa, com espaço para assinaturas. Fecho cego: numa sessão ainda aberta (ponto de situação)
    /// não aparecem recebimentos, esperado nem diferenças, só o fundo, os movimentos manuais e os métodos a contar.
    /// Depois do fecho: valores por método (esperado, contado, diferença) e os produtos vendidos por categoria.
    /// Configurações → Documentos e POS: «contagem cega» imprime só o valor contado; os produtos podem ficar de fora.
    /// </summary>
    private async Task<DocumentoImpressao> FechoCaixaAsync(int sessaoId, CancellationToken ct)
    {
        var s = await caixa.ObterAsync(sessaoId); // aplica a regra: o próprio operador ou caixa.ver-todas
        var fechada = s.Estado == EstadoSessaoCaixa.Fechada;
        var contagemCega = await config.BoolAsync(ChavesConfiguracao.FechoContagemCega);
        var imprimeProdutos = await config.BoolAsync(ChavesConfiguracao.FechoImprimeProdutos, padrao: true);
        var l = await CabecalhoAsync(fechada ? "Fecho de caixa" : "Caixa — ponto de situação", $"Sessão n.º {s.Id} · {s.Terminal}");
        if (fechada && !contagemCega)
        {
            l.Add(LinhaImpressao.NovoCartao($"Pedidos pagos ({s.PedidosPagos})", Valor(s.TotalRecebido ?? 0)));
            l.Add(LinhaImpressao.NovoCartao("Esperado", Valor(s.TotalEsperado ?? 0)));
            l.Add(LinhaImpressao.NovoCartao("Contado", Valor(s.TotalContado ?? 0)));
            l.Add(LinhaImpressao.NovoCartao("Diferença", (s.TotalDiferenca > 0 ? "+" : "") + Valor(s.TotalDiferenca ?? 0), alerta: s.TotalDiferenca is not (null or 0)));
        }
        var sessao = new List<LinhaImpressao> { Lin("Aberta por", s.AbertaPor), Lin("Abertura", DataHora(s.AbertaEmUtc)) };
        if (fechada)
        {
            sessao.Add(Lin("Fechada por", s.FechadaPor ?? "—"));
            sessao.Add(Lin("Fecho", s.FechadaEmUtc is DateTime f ? DataHora(f) : "—"));
        }
        sessao.Add(Lin("Fundo inicial", Valor(s.FundoInicial)));
        Grelha(l, "Sessão", fechada ? null : "ainda aberta", ["Descrição", "Valor"], sessao);
        if (!fechada)
        {
            l.Add(new("Caixa ainda aberto: os valores esperados e as diferenças só aparecem depois do fecho.", Alerta: true));
        }
        if (fechada && contagemCega)
            Grelha(l, "Contagem", null, ["Método", "Contado"],
                s.PorMetodo.Where(m => m.Contar || m.Contado is > 0).Select(m => Lin(m.Metodo, Valor(m.Contado ?? 0))),
                subtotal: ["Total contado", Valor(s.TotalContado ?? 0)]);
        else if (fechada)
        {
            // Só métodos com movimento ou com algo contado (o fecho regista 0 nos restantes; não interessa no papel).
            var metodos = s.PorMetodo.Where(m => m.Fundo != 0 || m.Recebimentos != 0 || m.Entradas != 0 || m.Saidas != 0 || m.Contado is > 0 || m.Diferenca is not (null or 0)).ToList();
            Grelha(l, "Movimento por método", null, ["Método", "Fundo", "Recebido", "Entradas", "Saídas"],
                metodos.Select(m => Lin(m.Metodo, Valor(m.Fundo), Valor(m.Recebimentos ?? 0), Valor(m.Entradas ?? 0), "-" + Valor(m.Saidas ?? 0))));
            Grelha(l, "Contagem por método", null, ["Método", "Esperado", "Contado", "Diferença"],
                metodos.Select(m => Lin(m.Diferenca is not (null or 0), m.Metodo, Valor(m.Esperado ?? 0), Valor(m.Contado ?? 0), (m.Diferenca > 0 ? "+" : "") + Valor(m.Diferenca ?? 0))),
                subtotal: ["Total", Valor(s.TotalEsperado ?? 0), Valor(s.TotalContado ?? 0), (s.TotalDiferenca > 0 ? "+" : "") + Valor(s.TotalDiferenca ?? 0)]);
        }
        else
            Grelha(l, "Métodos a contar no fecho", null, ["Método", "Contado"], s.PorMetodo.Where(m => m.Contar).Select(m => Lin(m.Metodo, "________")));
        if (s.Movimentos.Count > 0)
            Grelha(l, "Movimentos manuais", null, ["Hora", "Tipo", "Motivo", "Valor"],
                s.Movimentos.Select(mv => Lin(Hora(mv.DataUtc, false), mv.Tipo.ToString(), mv.Motivo ?? "—", (mv.Tipo == TipoMovimentoCaixa.Entrada ? "" : "-") + Valor(mv.Valor))),
                texto: 3);
        if (fechada)
        {
            if (contagemCega)
                l.Add(new("TOTAL CONTADO", Valor(s.TotalContado ?? 0), Negrito: true, Grande: true));
            else
            {
                l.Add(new("TOTAL ESPERADO", Valor(s.TotalEsperado ?? 0), Negrito: true, Grande: true));
                l.Add(new("Total contado", Valor(s.TotalContado ?? 0)));
                l.Add(new("DIFERENÇA", (s.TotalDiferenca > 0 ? "+" : "") + Valor(s.TotalDiferenca ?? 0), Negrito: true, Alerta: s.TotalDiferenca is not (null or 0)));
            }
            if (imprimeProdutos)
                await ProdutosDaSessaoAsync(l, s.Id, ct);
        }
        if (!string.IsNullOrWhiteSpace(s.ObservacoesFecho)) l.Add(new($"Obs.: {s.ObservacoesFecho}"));
        Assinaturas(l, "Operador", "Responsável");
        Rodape(l);
        return new DocumentoImpressao($"Caixa {s.Id}", l, cabecalho);
    }

    /// <summary>
    /// Fecho do dia: resumo de todos os caixas do dia de operação (o gravado no fecho; com o dia aberto, ponto de situação),
    /// vendas por meio de pagamento, outros movimentos, controlo e produtos vendidos por categoria. Valores das tabelas em moeda.
    /// </summary>
    private async Task<DocumentoImpressao> FechoDiaAsync(int diaId, CancellationToken ct)
    {
        var r = await dias.ResumoAsync(diaId, ct);
        var fechado = r.Estado == EstadoDia.Fechado;
        var l = await CabecalhoAsync(fechado ? "Fecho do dia" : "Dia — ponto de situação",
            $"Dia de operação {r.Data:dd/MM/yyyy}" + (r.Numero is null ? "" : $" · N.º {r.Numero}"));
        l.Add(LinhaImpressao.NovoCartao("Vendas líquidas", Valor(r.VendasLiquidas)));
        l.Add(LinhaImpressao.NovoCartao("Pedidos pagos", Inteiro(r.Pedidos)));
        l.Add(LinhaImpressao.NovoCartao("Ticket médio", Valor(r.TicketMedio)));
        l.Add(LinhaImpressao.NovoCartao("Diferença de caixa", r.Diferenca is decimal d0 ? (d0 > 0 ? "+" : "") + Valor(d0) : "—", alerta: r.Diferenca is not (null or 0)));
        var dia = new List<LinhaImpressao> { Lin("Dia aberto por", r.AbertoPor), Lin("Abertura", DataHora(r.AbertoEmUtc)) };
        if (fechado)
        {
            dia.Add(Lin("Dia fechado por", r.FechadoPor ?? "—"));
            dia.Add(Lin("Fecho", r.FechadoEmUtc is DateTime f ? DataHora(f) : "—"));
        }
        dia.Add(Lin("Caixas no dia", Inteiro(r.Caixas.Count)));
        Grelha(l, "Dia de operação", fechado ? null : "ainda aberto", ["Descrição", "Valor"], dia);
        if (!fechado)
        {
            l.Add(new("Dia ainda aberto: os valores podem mudar até ao fecho do dia.", Alerta: true));
        }

        var resumo = new List<LinhaImpressao> { Lin("Pedidos pagos", Inteiro(r.Pedidos)), Lin("Vendas brutas", Valor(r.VendasBrutas)) };
        if (r.Descontos != 0) resumo.Add(Lin("Descontos", "-" + Valor(r.Descontos)));
        if (r.TaxasEntrega != 0) resumo.Add(Lin("Taxas de entrega", Valor(r.TaxasEntrega)));
        resumo.Add(LinhaImpressao.Tabela(["Vendas líquidas", Valor(r.VendasLiquidas)], negrito: true));
        resumo.Add(Lin("IVA incluído", Valor(r.Impostos)));
        resumo.Add(Lin("Ticket médio", Valor(r.TicketMedio)));
        resumo.Add(Lin("Fundos iniciais", Valor(r.Caixas.Sum(c => c.Fundo))));
        resumo.Add(Lin("Total recebido", Valor(r.TotalRecebido)));
        resumo.Add(Lin("Total entrado", Valor(r.TotalEntrado)));
        resumo.Add(Lin("Total saído", Valor(r.TotalSaido)));
        Grelha(l, "1. Resumo do dia", null, ["Descrição", "Valor"], resumo,
            subtotal: r.Diferenca is decimal dif ? ["Diferença de caixa", (dif > 0 ? "+" : "") + Valor(dif)] : null);

        Grelha(l, "2. Resumo por caixa", null, ["Caixa", "Recebido", "Esperado", "Contado", "Diferença"],
            r.Caixas.Select(c => Lin(c.Diferenca is not (null or 0),
                $"{c.Terminal} · {c.Operador} · {Hora(c.AbertaEmUtc, false)}–{(c.FechadaEmUtc is DateTime fc ? Hora(fc, false) : "aberto")} ({c.Pedidos} ped.)",
                Num(c.Recebido), c.Esperado is decimal e ? Num(e) : "—", c.Contado is decimal ct2 ? Num(ct2) : "—",
                c.Diferenca is decimal d ? (d > 0 ? "+" : "") + Num(d) : "—")),
            subtotal: ["Total do dia", Num(r.Caixas.Sum(c => c.Recebido)),
                r.Caixas.Any(c => c.Aberta) ? "—" : Num(r.Caixas.Sum(c => c.Esperado ?? 0)),
                r.Caixas.Any(c => c.Aberta) ? "—" : Num(r.Caixas.Sum(c => c.Contado ?? 0)),
                r.Diferenca is decimal td ? (td > 0 ? "+" : "") + Num(td) : "—"],
            vazio: "Nenhum caixa aberto neste dia.");

        var totalMetodos = r.Metodos.Sum(m => m.Total);
        if (r.Caixas.Count is > 1 and <= 3)
            Grelha(l, "3. Vendas por meio de pagamento", null, ["Meio de pagamento", .. r.Caixas.Select(c => c.Terminal), "Total"],
                r.Metodos.Select(m => Lin([m.Metodo, .. m.PorCaixa.Select(Num), Num(m.Total)])),
                subtotal: ["Total", .. r.Caixas.Select(c => Num(c.Recebido)), Num(totalMetodos)], vazio: "Sem pagamentos.");
        else
            Grelha(l, "3. Vendas por meio de pagamento", null, ["Meio de pagamento", "Total", "%"],
                r.Metodos.Select(m => Lin(m.Metodo, Num(m.Total), Pct(m.Total, totalMetodos))),
                subtotal: ["Total", Num(totalMetodos), "100,0"], vazio: "Sem pagamentos.");

        Grelha(l, "4. Outros movimentos de caixa", null, ["Movimento", "Quantidade", "Valor"],
            r.Movimentos.Select(m => Lin(NomeMovimento(m.Tipo), Inteiro(m.Quantidade), (m.Valor > 0 ? "+" : "") + Valor(m.Valor))),
            subtotal: ["Saldo dos movimentos", "", Valor(r.Movimentos.Sum(m => m.Valor))], vazio: "Sem movimentos.");

        var k = r.Controlo;
        Grelha(l, "5. Controlo", null, ["Ocorrência", "Quantidade", "Valor"],
        [
            Lin("Descontos concedidos", Inteiro(k.Descontos), Valor(k.ValorDescontos)),
            Lin("Itens anulados", Inteiro(k.ItensAnulados), Valor(k.ValorItensAnulados)),
            Lin("Pedidos estornados", Inteiro(k.Estornos), Valor(k.ValorEstornos)),
            Lin("Pagamentos anulados", Inteiro(k.PagamentosAnulados), Valor(k.ValorPagamentosAnulados)),
            Lin(k.PedidosEmAberto > 0, "Pedidos em aberto", Inteiro(k.PedidosEmAberto), ""),
        ]);
        // Contas que ficaram abertas: transitam para o dia seguinte (no fecho) ou ainda por fechar (ponto de situação).
        if (r.PedidosAbertos is { Count: > 0 } contas)
            Grelha(l, r.Estado == EstadoDia.Fechado ? "Contas transitadas para o dia seguinte" : "Contas ainda abertas", Resumo(contas.Count, "conta", contas.Sum(c => c.Total - c.Pago)),
                ["Conta", "Local", "Responsável", "Por pagar"],
                contas.Select(c => Lin(c.Numero, c.Local, c.Responsavel ?? "—", Valor(c.Total - c.Pago))),
                subtotal: ["Total por pagar", "", "", Valor(contas.Sum(c => c.Total - c.Pago))], texto: 3);

        var totalProdutos = r.Categorias.Sum(c => c.Total);
        var produtos = new List<LinhaImpressao>();
        foreach (var c in r.Categorias)
        {
            produtos.Add(LinhaImpressao.Tabela([c.Categoria, ComposicaoImpressao.Quantidade(c.Quantidade), Num(c.Total), Pct(c.Total, totalProdutos)], negrito: true));
            produtos.AddRange(c.Produtos.Select(p => Lin("  " + p.Produto, ComposicaoImpressao.Quantidade(p.Quantidade), Num(p.Total), Pct(p.Total, totalProdutos))));
        }
        Grelha(l, "6. Produtos vendidos por categoria", null, ["Produto", "Qtd.", "Total", "%"], produtos,
            subtotal: ["Total (antes de descontos)", ComposicaoImpressao.Quantidade(r.Categorias.Sum(c => c.Quantidade)), Num(totalProdutos), "100,0"],
            vazio: "Sem vendas fechadas neste dia.");
        l.Add(new($"Valores em {moeda}, com IVA incluído."));
        if (!string.IsNullOrWhiteSpace(r.Observacoes)) l.Add(new($"Obs.: {r.Observacoes}"));
        Assinaturas(l, "Gerente", "Conferido por");
        Rodape(l);
        return new DocumentoImpressao($"Fecho do dia {r.Data:dd/MM/yyyy}", l, cabecalho);
    }

    private static string NomeMovimento(TipoMovimentoCaixa tipo) => tipo switch
    {
        TipoMovimentoCaixa.Entrada => "Entradas (recargas, recebimentos, reforços)",
        TipoMovimentoCaixa.Saida => "Saídas",
        TipoMovimentoCaixa.Despesa => "Despesas pagas pelo caixa",
        _ => "Reembolsos (estornos)",
    };

    /// <summary>Produtos dos pedidos fechados nesta sessão (pagos, não estornados), por categoria, com quantidade e valor.</summary>
    private async Task ProdutosDaSessaoAsync(List<LinhaImpressao> l, int sessaoId, CancellationToken ct)
    {
        var itens = await db.PedidoItens.AsNoTracking()
            .Where(i => !i.Cancelado && i.Pedido.SessaoCaixaId == sessaoId && i.Pedido.Estado == EstadoPedido.Pago)
            .GroupBy(i => new { Categoria = i.Produto.Categoria.Nome, i.NomeProduto })
            .Select(g => new { g.Key.Categoria, g.Key.NomeProduto, Quantidade = g.Sum(i => i.Quantidade), Total = g.Sum(i => i.Total) })
            .ToListAsync(ct);
        var pedidos = await db.Pedidos.AsNoTracking().Where(p => p.SessaoCaixaId == sessaoId && p.Estado == EstadoPedido.Pago)
            .GroupBy(_ => 1).Select(g => new { Desconto = g.Sum(p => p.Desconto), Entrega = g.Sum(p => p.TaxaEntrega), Total = g.Sum(p => p.Total) })
            .FirstOrDefaultAsync(ct);
        var produtos = new List<LinhaImpressao>();
        foreach (var categoria in itens.GroupBy(i => i.Categoria).OrderByDescending(g => g.Sum(i => i.Total)))
        {
            produtos.Add(LinhaImpressao.Tabela([categoria.Key, ComposicaoImpressao.Quantidade(categoria.Sum(i => i.Quantidade)), Valor(categoria.Sum(i => i.Total))], negrito: true));
            produtos.AddRange(categoria.OrderByDescending(i => i.Total)
                .Select(p => Lin("  " + p.NomeProduto, ComposicaoImpressao.Quantidade(p.Quantidade), Valor(p.Total))));
        }
        Grelha(l, "Produtos vendidos por categoria", null, ["Produto", "Qtd.", "Valor"], produtos,
            subtotal: ["Total dos produtos", ComposicaoImpressao.Quantidade(itens.Sum(i => i.Quantidade)), Valor(itens.Sum(i => i.Total))],
            vazio: "Sem vendas fechadas nesta sessão.");
        if (itens.Count == 0) return;
        if (pedidos is { Desconto: > 0 }) l.Add(new("Descontos", "-" + Valor(pedidos.Desconto)));
        if (pedidos is { Entrega: > 0 }) l.Add(new("Taxas de entrega", Valor(pedidos.Entrega)));
        if (pedidos is not null) l.Add(new("Total das vendas", Valor(pedidos.Total), Negrito: true));
    }
}
