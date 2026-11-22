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
    /// <summary>Cartões Evento: resumo do período (ou do caixa), dinheiro recebido por método e saldos actuais por cartão.</summary>
    private async Task<DocumentoImpressao> CartoesEventoAsync(DateOnly desde, DateOnly ate, int? sessaoId, CancellationToken ct)
    {
        var r = await cartoes.GerarAsync(desde, ate, ct, sessaoId);
        var origem = sessaoId is int s0
            ? await db.SessoesCaixa.AsNoTracking().Where(x => x.Id == s0).Select(x => $"Caixa n.º {x.Id} · {x.Terminal.Nome}").FirstOrDefaultAsync(ct)
              ?? throw new NaoEncontradoException("Sessão de caixa não encontrada.")
            : Periodo(desde, ate);
        var l = await CabecalhoAsync("Cartões Evento", origem);
        var s = r.Resumo;
        l.Add(LinhaImpressao.NovoCartao("Saldo em circulação", Valor(s.SaldoEmCirculacao)));
        l.Add(LinhaImpressao.NovoCartao("Cartões com saldo", Inteiro(s.CartoesComSaldo)));
        l.Add(LinhaImpressao.NovoCartao("Cartões activos", Inteiro(s.CartoesActivos)));
        l.Add(LinhaImpressao.NovoCartao("Cartões novos", Inteiro(s.CartoesNovos)));
        var movimentos = new List<LinhaImpressao>
        {
            Lin("Carregamentos", Inteiro(s.Carregamentos), Valor(s.Carregado)),
            Lin("Recargas", Inteiro(s.Recargas), Valor(s.Recarregado)),
            Lin("Pagamentos com cartão", Inteiro(s.Pagamentos), Valor(s.Pago)),
            Lin("Devolvido aos cartões", Inteiro(s.Devolucoes), Valor(s.Devolvido)),
        };
        if (s.Descargas > 0) movimentos.Add(Lin("Saldo descarregado", Inteiro(s.Descargas), Valor(s.Descarregado)));
        Grelha(l, "Movimentos", null, ["Movimento", "Quantidade", "Valor"], movimentos);
        if (r.PorMetodo.Count > 0)
            Grelha(l, "Dinheiro recebido por método", null, ["Método", "Quantidade", "Valor"],
                r.PorMetodo.Select(m => Lin(m.Metodo, Inteiro(m.Quantidade), Valor(m.Valor))),
                subtotal: ["Total", Inteiro(r.PorMetodo.Sum(m => m.Quantidade)), Valor(r.PorMetodo.Sum(m => m.Valor))]);
        if (r.PorOperador.Count > 0)
            Grelha(l, "Por operador", null, ["Operador", "Carregado", "Pago"], r.PorOperador.Select(o => Lin(o.Operador, Valor(o.Carregado), Valor(o.Pago))));
        if (r.Saldos.Count > 0)
            Grelha(l, "Saldos por cartão", null, ["Cartão", "Titular", "Saldo"],
                r.Saldos.Select(c => Lin(!c.Activo, c.Numero + (c.Activo ? "" : " (bloqueado)"), c.Titular ?? "—", Valor(c.Saldo))), texto: 2);
        l.Add(new("SALDO EM CIRCULAÇÃO", Valor(s.SaldoEmCirculacao), Negrito: true, Grande: true));
        Rodape(l);
        return new DocumentoImpressao("Cartões Evento " + origem, l, cabecalho);
    }

    /// <summary>Facturas a crédito do período (situação de cada uma) e dívida de hoje por cliente, por antiguidade.</summary>
    private async Task<DocumentoImpressao> FacturasCreditoAsync(DateOnly desde, DateOnly ate, int? clienteId, CancellationToken ct)
    {
        var r = await contas.FacturasCreditoAsync(desde, ate, clienteId, ct: ct);
        var l = await CabecalhoAsync("Facturas a crédito", Periodo(desde, ate));
        if (clienteId is int id && await db.Clientes.AsNoTracking().Where(c => c.Id == id).Select(c => new { c.Nome, c.Nuit, c.Telefone }).FirstOrDefaultAsync(ct) is { } cliente)
            Cliente(l, cliente.Nome, cliente.Nuit, cliente.Telefone);
        var s = r.Resumo;
        l.Add(LinhaImpressao.NovoCartao($"Facturado ({s.Facturas})", Valor(s.Facturado)));
        l.Add(LinhaImpressao.NovoCartao("Pago (recibos)", Valor(s.Pago)));
        l.Add(LinhaImpressao.NovoCartao("Creditado", Valor(s.Creditado)));
        l.Add(LinhaImpressao.NovoCartao("Em dívida", Valor(s.EmDivida), alerta: s.Vencido > 0));

        Grelha(l, "Facturas do período", Resumo(r.Facturas.Count, "factura", r.Facturas.Sum(f => f.Total)),
            ["Data", "Número", "Cliente", "Situação", "Total", "Em dívida"],
            r.Facturas.Select(f => Lin(f.Situacao == Facturacao.SituacaoFactura.Vencida, Dia(f.EmitidoEmUtc), f.Numero, f.ClienteNome,
                string.Join(" · ", new[] { NomeSituacao(f.Situacao, f.DiasAtraso), f.Vencimento is DateOnly v ? $"venc. {v:dd/MM/yyyy}" : null }.Where(x => x is not null)),
                Valor(f.Total), f.EmDivida > 0 ? Valor(f.EmDivida) : "—")),
            subtotal: ["Total", "", "", "", Valor(r.Facturas.Sum(f => f.Total)), Valor(r.Facturas.Sum(f => f.EmDivida))], texto: 4,
            vazio: "Sem facturas a crédito no período.");
        if (r.Cortado) l.Add(new($"(Lista limitada às primeiras {Facturacao.ServicoContaCorrente.MaximoLinhas} facturas.)"));

        // Antiguidade da dívida: em A4 todas as colunas; nos talões só o vencido e o total (não cabem 7 valores).
        var titulo = $"Dívida de clientes hoje ({r.Hoje:dd/MM/yyyy})";
        string[] subtotalA4 = ["Total", "", Valor(r.Dividas.Sum(d => d.NaoVencido)), Valor(r.Dividas.Sum(d => d.Ate30)), Valor(r.Dividas.Sum(d => d.De31a60)),
            Valor(r.Dividas.Sum(d => d.De61a90)), Valor(r.Dividas.Sum(d => d.Mais90)), Valor(r.TotalEmDivida)];
        var inicio = l.Count;
        Grelha(l, titulo, null, ["Cliente", "Fact.", "Não vencido", "1-30 d", "31-60 d", "61-90 d", "+90 d", "Em dívida"],
            r.Dividas.Select(d => Lin(d.Ate30 + d.De31a60 + d.De61a90 + d.Mais90 > 0, d.ClienteNome, Inteiro(d.Facturas), Valor(d.NaoVencido), Valor(d.Ate30),
                Valor(d.De31a60), Valor(d.De61a90), Valor(d.Mais90), Valor(d.EmDivida))),
            subtotal: r.Dividas.Count > 0 ? subtotalA4 : null, vazio: "Nenhum cliente em dívida.");
        for (var i = inicio; i < l.Count; i++) l[i] = l[i] with { SoA4 = true };
        inicio = l.Count;
        Grelha(l, titulo, null, ["Cliente", "Vencido", "Em dívida"],
            r.Dividas.Select(d => Lin(d.Ate30 + d.De31a60 + d.De61a90 + d.Mais90 > 0, d.ClienteNome, Valor(d.Ate30 + d.De31a60 + d.De61a90 + d.Mais90), Valor(d.EmDivida))),
            subtotal: r.Dividas.Count > 0 ? ["Total", Valor(r.TotalVencido), Valor(r.TotalEmDivida)] : null, vazio: "Nenhum cliente em dívida.");
        for (var i = inicio; i < l.Count; i++) l[i] = l[i] with { SoTalao = true };
        if (r.Dividas.Count > 0)
            l.Add(new("TOTAL EM DÍVIDA", Valor(r.TotalEmDivida), Negrito: true, Grande: true));
        Rodape(l);
        return new DocumentoImpressao("Facturas a crédito " + Periodo(desde, ate), l, cabecalho);
    }

    /// <summary>Histórico de documentos do cliente (todos os tipos), com os totais dos documentos em vigor.</summary>
    private async Task<DocumentoImpressao> HistoricoClienteAsync(int clienteId, DateOnly desde, DateOnly ate, CancellationToken ct)
    {
        var r = await contas.HistoricoAsync(clienteId, desde, ate, ct: ct);
        var l = await CabecalhoAsync("Histórico de facturas do cliente", Periodo(desde, ate));
        Cliente(l, r.ClienteNome, r.ClienteNuit, null);
        var t = r.Totais;
        l.Add(LinhaImpressao.NovoCartao("Documentos em vigor", Inteiro(t.Documentos)));
        l.Add(LinhaImpressao.NovoCartao("Facturado a crédito", Valor(t.Facturado)));
        l.Add(LinhaImpressao.NovoCartao("Em dívida", Valor(t.EmDivida), alerta: t.EmDivida > 0));
        Grelha(l, "Documentos", Resumo(r.Documentos.Count, "documento", null), ["Data", "Número", "Tipo", "Detalhe", "Em dívida", "Total"],
            r.Documentos.Select(d =>
            {
                var anulado = d.Estado == Domain.Facturacao.EstadoDocumento.Anulado;
                var detalhe = new[] { d.Origem, d.MetodoPagamento, d.Vencimento is DateOnly v ? $"venc. {v:dd/MM/yyyy}" : null }.Where(x => x is not null);
                return Lin(anulado, Dia(d.EmitidoEmUtc), d.Numero, Domain.Facturacao.Documento.Nome(d.Tipo) + (anulado ? " (ANULADO)" : ""), string.Join(" · ", detalhe),
                    d.EmDivida > 0 ? Valor(d.EmDivida) : "—", Valor(d.Total));
            }), texto: 4, vazio: "Sem documentos no período.");
        if (r.Cortado) l.Add(new($"(Lista limitada aos primeiros {Facturacao.ServicoContaCorrente.MaximoLinhas} documentos.)"));
        Grelha(l, $"Totais (documentos em vigor: {t.Documentos})", null, ["Descrição", "Valor"],
        [
            Lin("Vendas a dinheiro", Valor(t.VendasDinheiro)),
            Lin("Facturas a crédito", Valor(t.Facturado)),
            Lin("Recebido (recibos)", Valor(t.Recebido)),
            Lin("Notas de crédito", Valor(t.Creditado)),
        ], subtotal: ["EM DÍVIDA (facturas do período)", Valor(t.EmDivida)]);
        Rodape(l);
        return new DocumentoImpressao($"Histórico {r.ClienteNome} {Periodo(desde, ate)}", l, cabecalho);
    }

    /// <summary>Extrato da conta corrente: saldo anterior, movimentos com saldo acumulado, saldo final e facturas em aberto.</summary>
    private async Task<DocumentoImpressao> ExtratoClienteAsync(int clienteId, DateOnly desde, DateOnly ate, CancellationToken ct)
    {
        var r = await contas.ExtratoAsync(clienteId, desde, ate, ct);
        var l = await CabecalhoAsync("Extrato de conta corrente", Periodo(desde, ate));
        Cliente(l, r.ClienteNome, r.ClienteNuit, r.ClienteTelefone);
        l.Add(LinhaImpressao.NovoCartao($"Saldo anterior a {desde:dd/MM/yyyy}", Valor(r.SaldoAnterior)));
        l.Add(LinhaImpressao.NovoCartao("Débitos", Valor(r.Debitos)));
        l.Add(LinhaImpressao.NovoCartao("Créditos", Valor(r.Creditos)));
        l.Add(LinhaImpressao.NovoCartao($"Saldo em {ate:dd/MM/yyyy}", Valor(r.SaldoFinal), alerta: r.SaldoFinal > 0));
        var movimentos = new List<LinhaImpressao> { LinhaImpressao.Tabela([Dia(desde.ToDateTime(TimeOnly.MinValue)), "", $"Saldo anterior a {desde:dd/MM/yyyy}", "", "", Valor(r.SaldoAnterior)], negrito: true) };
        movimentos.AddRange(r.Movimentos.Select(m => Lin(Dia(m.DataUtc), m.Numero, m.Descricao, m.Debito > 0 ? Valor(m.Debito) : "", m.Credito > 0 ? Valor(m.Credito) : "", Valor(m.Saldo))));
        Grelha(l, "Movimentos", r.Movimentos.Count == 0 ? "sem movimentos no período" : null, ["Data", "Documento", "Descrição", "Débito", "Crédito", "Saldo"], movimentos,
            subtotal: [$"Saldo em {ate:dd/MM/yyyy}", "", "", Valor(r.Debitos), Valor(r.Creditos), Valor(r.SaldoFinal)], texto: 3);
        if (r.Movimentos.Count == 0) l.Add(new("Sem movimentos no período."));
        l.Add(new($"SALDO EM {ate:dd/MM/yyyy}", Valor(r.SaldoFinal), Negrito: true, Grande: true));
        Grelha(l, "Situação hoje: facturas em aberto", Resumo(r.FacturasEmAberto.Count, "factura", r.EmDividaHoje) + (r.VencidoHoje > 0 ? $" · vencido {Valor(r.VencidoHoje)}" : ""),
            ["Factura", "Vencimento", "Em dívida"],
            r.FacturasEmAberto.Select(f => Lin(f.Vencida, f.Numero, f.Vencimento is DateOnly v ? $"{v:dd/MM/yyyy}{(f.Vencida ? " VENCIDA" : "")}" : "—", Valor(f.EmDivida))),
            subtotal: ["Total em dívida", "", Valor(r.EmDividaHoje)], texto: 2, vazio: "Nenhuma factura em aberto.");
        Rodape(l);
        return new DocumentoImpressao($"Extrato {r.ClienteNome} {Periodo(desde, ate)}", l, cabecalho);
    }

    private static void Cliente(List<LinhaImpressao> l, string nome, string? nuit, string? telefone)
    {
        var dados = new[] { nuit is null ? null : $"NUIT {nuit}", telefone is null ? null : $"Tel. {telefone}" }.Where(x => x is not null).ToList();
        l.Add(LinhaImpressao.NovaFaixa($"Cliente: {nome}", dados.Count > 0 ? string.Join(" · ", dados) : null));
    }

    private static string NomeSituacao(Facturacao.SituacaoFactura s, int diasAtraso) => s switch
    {
        Facturacao.SituacaoFactura.Paga => "Paga",
        Facturacao.SituacaoFactura.Creditada => "Creditada",
        Facturacao.SituacaoFactura.Parcial => "Parcial",
        Facturacao.SituacaoFactura.Vencida => $"Vencida há {diasAtraso} d",
        _ => "Em dívida",
    };

    /// <summary>Descargas de saldo: cada cartão com o saldo descarregado e o acumulado; totais devolvido e só retirado.</summary>
    private async Task<DocumentoImpressao> DescargasCartoesAsync(DateOnly desde, DateOnly ate, int? sessaoId, CancellationToken ct)
    {
        var r = await cartoes.DescargasAsync(desde, ate, sessaoId, ct);
        var origem = sessaoId is int s
            ? await db.SessoesCaixa.AsNoTracking().Where(x => x.Id == s).Select(x => $"Caixa n.º {x.Id} · {x.Terminal.Nome}").FirstOrDefaultAsync(ct)
              ?? throw new NaoEncontradoException("Sessão de caixa não encontrada.")
            : Periodo(desde, ate);
        var l = await CabecalhoAsync("Descargas de cartões", origem);
        l.Add(LinhaImpressao.NovoCartao($"Cartões ({r.Cartoes})", Valor(r.Total)));
        l.Add(LinhaImpressao.NovoCartao("Devolvido em dinheiro", Valor(r.Devolvido)));
        l.Add(LinhaImpressao.NovoCartao("Só retirado do saldo", Valor(r.Retirado)));
        Grelha(l, "Descargas", null, ["Data", "Cartão", "Titular", "Destino", "Operador / motivo", "Saldo", "Acumulado"],
            r.Linhas.Select(d => Lin(Dia(d.DataUtc), d.Numero, d.Titular ?? "—", d.DevolveuDinheiro ? $"devolvido por {d.Metodo}" : "só retirado",
                string.Join(" · ", new[] { d.Operador, d.Motivo }.Where(x => x is not null)), Valor(d.Saldo), Valor(d.Acumulado))),
            texto: 5, vazio: "Sem descargas.");
        if (r.Cortado) l.Add(new($"(Lista limitada aos primeiros {Cartoes.ServicoRelatoriosCartoes.MaximoMovimentos} cartões.)"));
        Grelha(l, "Totais", null, ["Descrição", "Valor"],
            [Lin("Devolvido em dinheiro", Valor(r.Devolvido)), Lin("Só retirado do saldo", Valor(r.Retirado))]);
        l.Add(new($"TOTAL DESCARREGADO ({r.Cartoes})", Valor(r.Total), Negrito: true, Grande: true));
        Rodape(l);
        return new DocumentoImpressao("Descargas de cartões " + origem, l, cabecalho);
    }
}
