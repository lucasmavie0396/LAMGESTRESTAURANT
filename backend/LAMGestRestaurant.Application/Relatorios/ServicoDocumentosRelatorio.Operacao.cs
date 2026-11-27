using System.Globalization;
using LAMGestRestaurant.Application.Impressao;
using LAMGestRestaurant.Domain.Comum;

namespace LAMGestRestaurant.Application.Relatorios;

public partial class ServicoDocumentosRelatorio
{
    private static string Minutos(decimal m) => m.ToString("0.#", CultureInfo.GetCultureInfo("pt-PT")) + " min";
    private static string Percentagem(decimal p) => p.ToString("0.#", CultureInfo.GetCultureInfo("pt-PT")) + " %";

    /// <summary>Contas a pagar: dívida aos fornecedores por antiguidade, compras em aberto e pagamentos do período.</summary>
    private async Task<DocumentoImpressao> ContasPagarAsync(DateOnly desde, DateOnly ate, CancellationToken ct)
    {
        var r = await operacao.ContasPagarAsync(desde, ate, ct);
        var l = await CabecalhoAsync("Contas a pagar (fornecedores)", Periodo(desde, ate));
        l.Add(LinhaImpressao.NovoCartao($"Em dívida hoje ({r.Hoje:dd/MM/yyyy})", Valor(r.TotalEmDivida)));
        l.Add(LinhaImpressao.NovoCartao("Vencido", Valor(r.TotalVencido), alerta: r.TotalVencido > 0));
        l.Add(LinhaImpressao.NovoCartao("Pago no período", Valor(r.PagoNoPeriodo)));
        var inicio = l.Count;
        Grelha(l, "Dívida por fornecedor", null, ["Fornecedor", "Compras", "Não vencido", "1-30 d", "31-60 d", "61-90 d", "+90 d", "Em dívida"],
            r.Dividas.Select(d => Lin(d.EmDivida > d.NaoVencido, d.Fornecedor, Inteiro(d.Compras), Valor(d.NaoVencido), Valor(d.Ate30), Valor(d.De31a60), Valor(d.De61a90),
                Valor(d.Mais90), Valor(d.EmDivida))),
            subtotal: r.Dividas.Count > 0 ? ["Total", "", Valor(r.Dividas.Sum(d => d.NaoVencido)), Valor(r.Dividas.Sum(d => d.Ate30)), Valor(r.Dividas.Sum(d => d.De31a60)),
                Valor(r.Dividas.Sum(d => d.De61a90)), Valor(r.Dividas.Sum(d => d.Mais90)), Valor(r.TotalEmDivida)] : null, vazio: "Nada em dívida aos fornecedores.");
        for (var i = inicio; i < l.Count; i++) l[i] = l[i] with { SoA4 = true };
        inicio = l.Count;
        Grelha(l, "Dívida por fornecedor", null, ["Fornecedor", "Vencido", "Em dívida"],
            r.Dividas.Select(d => Lin(d.EmDivida > d.NaoVencido, d.Fornecedor, Valor(d.EmDivida - d.NaoVencido), Valor(d.EmDivida))),
            subtotal: r.Dividas.Count > 0 ? ["Total", Valor(r.TotalVencido), Valor(r.TotalEmDivida)] : null, vazio: "Nada em dívida aos fornecedores.");
        for (var i = inicio; i < l.Count; i++) l[i] = l[i] with { SoTalao = true };
        Grelha(l, "Compras por pagar", Resumo(r.ComprasEmAberto.Count, "compra", r.TotalEmDivida), ["Fornecedor", "Documento", "Vencimento", "Total", "Em dívida"],
            r.ComprasEmAberto.Select(c => Lin(c.DiasAtraso > 0, c.Fornecedor, c.Documento,
                c.Vencimento is DateOnly v ? $"{v:dd/MM/yyyy}{(c.DiasAtraso > 0 ? $" (+{c.DiasAtraso} d)" : "")}" : "—", Valor(c.Total), Valor(c.EmDivida))),
            texto: 3, vazio: "Nenhuma compra por pagar.");
        Grelha(l, "Pagamentos no período", null, ["Data", "Fornecedor", "Documento", "Método", "Valor"],
            r.Pagamentos.Select(p => Lin(p.Anulado, Dia(p.DataUtc), p.Fornecedor, p.Documento, p.Metodo + (p.Anulado ? " (anulado)" : ""), Valor(p.Valor))),
            subtotal: r.Pagamentos.Count > 0 ? ["Total pago", "", "", "", Valor(r.PagoNoPeriodo)] : null, texto: 4, vazio: "Sem pagamentos no período.");
        l.Add(new("TOTAL EM DÍVIDA", Valor(r.TotalEmDivida), Negrito: true, Grande: true));
        Rodape(l);
        return new DocumentoImpressao("Contas a pagar " + Periodo(desde, ate), l, cabecalho);
    }

    /// <summary>Vendas por hora e por dia da semana (pedidos pagos, pela hora do pagamento).</summary>
    private async Task<DocumentoImpressao> VendasPorHoraAsync(DateOnly desde, DateOnly ate, CancellationToken ct)
    {
        var r = await operacao.VendasPorHoraAsync(desde, ate, ct);
        var l = await CabecalhoAsync("Vendas por hora", Periodo(desde, ate));
        l.Add(LinhaImpressao.NovoCartao($"Vendas ({r.Pedidos})", Valor(r.Valor)));
        l.Add(LinhaImpressao.NovoCartao("Hora de mais vendas", r.HoraPico is int h ? $"{h:00}h-{h + 1:00}h" : "—"));
        l.Add(LinhaImpressao.NovoCartao("Melhor dia (média)", r.DiaPico ?? "—"));
        Grelha(l, "Por hora", null, ["Hora", "Pedidos", "Valor médio", "Valor"],
            r.PorHora.Select(x => Lin($"{x.Hora:00}h-{x.Hora + 1:00}h", Inteiro(x.Pedidos), Valor(x.ValorMedio), Valor(x.Valor))),
            subtotal: r.PorHora.Count > 0 ? ["Total", Inteiro(r.Pedidos), "", Valor(r.Valor)] : null, vazio: "Sem vendas no período.");
        Grelha(l, "Por dia da semana", null, ["Dia", "Pedidos", "Média por dia", "Valor"],
            r.PorDiaSemana.Select(x => Lin(x.Nome, Inteiro(x.Pedidos), Valor(x.MediaPorDia), Valor(x.Valor))));
        Rodape(l);
        return new DocumentoImpressao("Vendas por hora " + Periodo(desde, ate), l, cabecalho);
    }

    /// <summary>Cotações emitidas no período: situação de cada uma e taxa de conversão em factura.</summary>
    private async Task<DocumentoImpressao> CotacoesAsync(DateOnly desde, DateOnly ate, CancellationToken ct)
    {
        var r = await operacao.CotacoesAsync(desde, ate, ct);
        var l = await CabecalhoAsync("Cotações", Periodo(desde, ate));
        l.Add(LinhaImpressao.NovoCartao($"Emitidas ({r.Emitidas})", Valor(r.ValorEmitido)));
        l.Add(LinhaImpressao.NovoCartao($"Convertidas em factura ({r.Convertidas})", Valor(r.ValorConvertido)));
        l.Add(LinhaImpressao.NovoCartao($"Em aberto ({r.Abertas})", Valor(r.ValorAberto)));
        l.Add(LinhaImpressao.NovoCartao("Taxa de conversão", Percentagem(r.TaxaConversao)));
        Grelha(l, "Situação", null, ["Situação", "Quantidade", "Valor"],
        [
            Lin("Convertidas", Inteiro(r.Convertidas), Valor(r.ValorConvertido)), Lin("Em aberto", Inteiro(r.Abertas), Valor(r.ValorAberto)),
            Lin("Expiradas", Inteiro(r.Expiradas), Valor(r.ValorExpirado)), Lin("Anuladas", Inteiro(r.Anuladas), ""),
        ]);
        Grelha(l, "Cotações do período", null, ["Data", "Número", "Cliente", "Situação", "Total"],
            r.Linhas.Select(c => Lin(c.Situacao == SituacaoCotacao.Expirada, Dia(c.EmitidoEmUtc), c.Numero, c.Cliente,
                NomeSituacaoCotacao(c.Situacao) + (c.Factura is null ? "" : $" → {c.Factura}"), Valor(c.Total))),
            texto: 4, vazio: "Sem cotações no período.");
        if (r.Cortado) l.Add(new($"(Lista limitada às primeiras {ServicoRelatoriosOperacao.MaximoLinhas} cotações.)"));
        Rodape(l);
        return new DocumentoImpressao("Cotações " + Periodo(desde, ate), l, cabecalho);
    }

    internal static string NomeSituacaoCotacao(SituacaoCotacao s) => s switch
    {
        SituacaoCotacao.Convertida => "Convertida",
        SituacaoCotacao.Anulada => "Anulada",
        SituacaoCotacao.Expirada => "Expirada",
        _ => "Em aberto",
    };

    /// <summary>Tempos da cozinha: do envio ao «pronto», por estação e por prato, e os itens mais lentos.</summary>
    private async Task<DocumentoImpressao> CozinhaAsync(DateOnly desde, DateOnly ate, CancellationToken ct)
    {
        var r = await operacao.CozinhaAsync(desde, ate, ct: ct);
        var l = await CabecalhoAsync("Tempos da cozinha", Periodo(desde, ate));
        l.Add(LinhaImpressao.NovoCartao($"Itens preparados ({r.Itens})", Minutos(r.MediaMinutos) + " em média"));
        l.Add(LinhaImpressao.NovoCartao("Mediana", Minutos(r.MedianaMinutos)));
        l.Add(LinhaImpressao.NovoCartao($"Acima de {r.LimiteMinutos} min", Inteiro(r.Atrasados), alerta: r.Atrasados > 0));
        Grelha(l, "Por estação", null, ["Estação", "Itens", "Média", "Máximo", "Atrasados"],
            r.PorEstacao.Select(x => Lin(x.Atrasados > 0, x.Nome, Inteiro(x.Itens), Minutos(x.MediaMinutos), Minutos(x.MaximoMinutos), Inteiro(x.Atrasados))),
            vazio: "Sem itens preparados no período.");
        Grelha(l, "Por prato", null, ["Prato", "Itens", "Média", "Máximo", "Atrasados"],
            r.PorPrato.Take(60).Select(x => Lin(x.Atrasados > 0, x.Nome, Inteiro(x.Itens), Minutos(x.MediaMinutos), Minutos(x.MaximoMinutos), Inteiro(x.Atrasados))));
        Grelha(l, "Itens mais lentos", null, ["Enviado", "Pedido", "Prato", "Tempo"],
            r.MaisLentos.Select(x => Lin(DataHora(x.EnviadoEmUtc), x.Pedido + (x.Mesa is null ? "" : $" ({x.Mesa})"), x.Produto, Minutos(x.Minutos))), texto: 3);
        if (r.PorTerminar > 0) l.Add(new($"{r.PorTerminar} itens enviados no período ainda sem «pronto» (não entram nas médias)."));
        Rodape(l);
        return new DocumentoImpressao("Tempos da cozinha " + Periodo(desde, ate), l, cabecalho);
    }

    /// <summary>Delivery e Take Away: pedidos, valor, cancelados, tempo de entrega e por estafeta.</summary>
    private async Task<DocumentoImpressao> DeliveryAsync(DateOnly desde, DateOnly ate, CancellationToken ct)
    {
        var r = await operacao.DeliveryAsync(desde, ate, ct);
        var l = await CabecalhoAsync("Delivery e Take Away", Periodo(desde, ate));
        l.Add(LinhaImpressao.NovoCartao($"Pedidos ({r.Pedidos})", Valor(r.Valor)));
        l.Add(LinhaImpressao.NovoCartao("Taxas de entrega", Valor(r.TaxaEntrega)));
        l.Add(LinhaImpressao.NovoCartao("Cancelados", Inteiro(r.Cancelados), alerta: r.Cancelados > 0));
        l.Add(LinhaImpressao.NovoCartao("Tempo médio até entregar", r.MediaMinutosEntrega is decimal m ? Minutos(m) : "—"));
        Grelha(l, "Por tipo", null, ["Tipo", "Pedidos", "Pagos", "Cancelados", "Valor"],
            r.PorTipo.Select(x => Lin(x.Tipo == TipoPedido.Delivery ? "Delivery" : "Take Away", Inteiro(x.Pedidos), Inteiro(x.Pagos), Inteiro(x.Cancelados), Valor(x.Valor))),
            vazio: "Sem pedidos no período.");
        Grelha(l, "Por estafeta (delivery)", null, ["Estafeta", "Entregas", "Tempo médio", "Taxas", "Valor"],
            r.PorEstafeta.Select(x => Lin(x.Estafeta, Inteiro(x.Entregas), x.MediaMinutos is decimal t ? Minutos(t) : "—", Valor(x.TaxaEntrega), Valor(x.Valor))),
            vazio: "Sem entregas no período.");
        Rodape(l);
        return new DocumentoImpressao("Delivery e Take Away " + Periodo(desde, ate), l, cabecalho);
    }

    /// <summary>Mesas: pedidos pagos, pessoas, valor, tempo médio de ocupação e valor por pessoa, por zona e por mesa.</summary>
    private async Task<DocumentoImpressao> MesasAsync(DateOnly desde, DateOnly ate, CancellationToken ct)
    {
        var r = await operacao.MesasAsync(desde, ate, ct);
        var l = await CabecalhoAsync("Mesas", Periodo(desde, ate));
        l.Add(LinhaImpressao.NovoCartao($"Contas pagas ({r.Pedidos})", Valor(r.Valor)));
        l.Add(LinhaImpressao.NovoCartao("Pessoas", Inteiro(r.Pessoas)));
        l.Add(LinhaImpressao.NovoCartao("Tempo médio por mesa", Minutos(r.MediaMinutos)));
        l.Add(LinhaImpressao.NovoCartao("Valor por pessoa", r.ValorPorPessoa is decimal v ? Valor(v) : "—"));
        string[] colunas = ["", "Contas", "Pessoas", "Tempo médio", "Por pessoa", "Valor"];
        Grelha(l, "Por zona", null, ["Zona", .. colunas[1..]],
            r.PorZona.Select(x => Lin(x.Zona, Inteiro(x.Pedidos), Inteiro(x.Pessoas), Minutos(x.MediaMinutos), x.ValorPorPessoa is decimal p ? Valor(p) : "—", Valor(x.Valor))),
            vazio: "Sem contas de mesa pagas no período.");
        Grelha(l, "Por mesa", null, ["Mesa", .. colunas[1..]],
            r.PorMesa.Select(x => Lin($"{x.Mesa} ({x.Zona})", Inteiro(x.Pedidos), Inteiro(x.Pessoas), Minutos(x.MediaMinutos), x.ValorPorPessoa is decimal p ? Valor(p) : "—", Valor(x.Valor))));
        Rodape(l);
        return new DocumentoImpressao("Mesas " + Periodo(desde, ate), l, cabecalho);
    }

    /// <summary>Pontos de fidelização: ganhos, usados, ajustes e estornos do período; saldo de hoje por cliente.</summary>
    private async Task<DocumentoImpressao> PontosAsync(DateOnly desde, DateOnly ate, CancellationToken ct)
    {
        var r = await operacao.PontosAsync(desde, ate, ct);
        var l = await CabecalhoAsync("Pontos de fidelização", Periodo(desde, ate));
        l.Add(LinhaImpressao.NovoCartao("Ganhos", Inteiro(r.Ganhos)));
        l.Add(LinhaImpressao.NovoCartao("Usados", Inteiro(r.Usados)));
        l.Add(LinhaImpressao.NovoCartao("Ajustes e estornos", Inteiro(r.Ajustes + r.Estornos)));
        l.Add(LinhaImpressao.NovoCartao($"Saldo de hoje ({r.ClientesComSaldo} clientes)", Inteiro(r.SaldoTotal)));
        Grelha(l, "Por cliente", null, ["Cliente", "Ganhos", "Usados", "Ajustes", "Saldo hoje"],
            r.Clientes.Select(c => Lin(c.Cliente, Inteiro(c.Ganhos), Inteiro(c.Usados), Inteiro(c.Ajustes + c.Estornos), Inteiro(c.Saldo))),
            vazio: "Sem movimentos de pontos.");
        Rodape(l);
        return new DocumentoImpressao("Pontos de fidelização " + Periodo(desde, ate), l, cabecalho);
    }

    /// <summary>Reservas do período: compareceram, faltaram, canceladas e por vir; por dia da semana e lista.</summary>
    private async Task<DocumentoImpressao> ReservasAsync(DateOnly desde, DateOnly ate, CancellationToken ct)
    {
        var r = await operacao.ReservasAsync(desde, ate, ct);
        var l = await CabecalhoAsync("Reservas", Periodo(desde, ate));
        l.Add(LinhaImpressao.NovoCartao($"Reservas ({r.Pessoas} pessoas)", Inteiro(r.Reservas)));
        l.Add(LinhaImpressao.NovoCartao("Compareceram", Inteiro(r.Compareceram)));
        l.Add(LinhaImpressao.NovoCartao("Faltaram", Inteiro(r.Faltaram), alerta: r.Faltaram > 0));
        l.Add(LinhaImpressao.NovoCartao("Taxa de comparência", r.TaxaComparencia is decimal t ? Percentagem(t) : "—"));
        Grelha(l, "Por dia da semana", null, ["Dia", "Reservas", "Pessoas"], r.PorDiaSemana.Select(d => Lin(d.Nome, Inteiro(d.Quantidade), Inteiro(d.Valor))));
        Grelha(l, "Reservas do período", null, ["Data", "Cliente", "Mesa", "Situação", "Pessoas"],
            r.Linhas.Select(x => Lin(x.Situacao == SituacaoReserva.Faltou, DataHora(x.InicioUtc), x.Cliente, x.Mesa ?? "—", NomeSituacaoReserva(x.Situacao), Inteiro(x.Pessoas))),
            texto: 4, vazio: "Sem reservas no período.");
        if (r.Cortado) l.Add(new($"(Lista limitada às primeiras {ServicoRelatoriosOperacao.MaximoLinhas} reservas.)"));
        Rodape(l);
        return new DocumentoImpressao("Reservas " + Periodo(desde, ate), l, cabecalho);
    }

    internal static string NomeSituacaoReserva(SituacaoReserva s) => s switch
    {
        SituacaoReserva.Compareceu => "Compareceu",
        SituacaoReserva.Faltou => "Faltou",
        SituacaoReserva.Cancelada => "Cancelada",
        _ => "Por vir",
    };
}
