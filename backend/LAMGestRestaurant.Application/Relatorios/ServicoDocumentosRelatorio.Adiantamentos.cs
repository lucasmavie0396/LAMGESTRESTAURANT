using LAMGestRestaurant.Application.Impressao;
using LAMGestRestaurant.Domain.Cartoes;

namespace LAMGestRestaurant.Application.Relatorios;

public partial class ServicoDocumentosRelatorio
{
    /// <summary>Adiantamentos por cliente no período (saldo no início, recargas, consumos, saldo no fim) e recargas por método e operador.</summary>
    private async Task<DocumentoImpressao> AdiantamentosAsync(DateOnly desde, DateOnly ate, CancellationToken ct)
    {
        var r = await adiantamentos.RelatorioAsync(desde, ate, ct);
        var l = await CabecalhoAsync("Adiantamentos de clientes", Periodo(desde, ate));
        l.Add(LinhaImpressao.NovoCartao($"Saldo a {desde:dd/MM/yyyy}", Valor(r.SaldoInicial)));
        l.Add(LinhaImpressao.NovoCartao("Recargas", Valor(r.Recargas)));
        l.Add(LinhaImpressao.NovoCartao("Consumos", Valor(r.Consumos)));
        l.Add(LinhaImpressao.NovoCartao($"Saldo em {ate:dd/MM/yyyy}", Valor(r.SaldoFinal)));
        Grelha(l, "Por cliente", Resumo(r.Clientes.Count, "cliente", r.SaldoFinal), ["Cliente", "Saldo inicial", "Recargas", "Consumos", "Outros", "Saldo final"],
            r.Clientes.Select(c => Lin(c.Cliente, Valor(c.SaldoInicial), Valor(c.Recargas), Valor(c.Consumos), c.Outros == 0 ? "" : Valor(c.Outros), Valor(c.SaldoFinal))),
            subtotal: r.Clientes.Count > 0 ? ["Total", Valor(r.SaldoInicial), Valor(r.Recargas), Valor(r.Consumos), Valor(r.Outros), Valor(r.SaldoFinal)] : null,
            vazio: "Nenhum cliente com adiantamentos.");
        if (r.RecargasPorMetodo.Count > 0)
            Grelha(l, "Recargas por método", null, ["Método", "Quantidade", "Valor"],
                r.RecargasPorMetodo.Select(m => Lin(m.Metodo, Inteiro(m.Quantidade), Valor(m.Valor))),
                subtotal: ["Total", Inteiro(r.RecargasPorMetodo.Sum(m => m.Quantidade)), Valor(r.RecargasPorMetodo.Sum(m => m.Valor))]);
        if (r.RecargasPorOperador.Count > 0)
            Grelha(l, "Recargas por operador", null, ["Operador", "Quantidade", "Valor"],
                r.RecargasPorOperador.Select(o => Lin(o.Operador, Inteiro(o.Quantidade), Valor(o.Valor))));
        l.Add(new("DEVIDO AOS CLIENTES", Valor(r.SaldoFinal), Negrito: true, Grande: true));
        Rodape(l);
        return new DocumentoImpressao("Adiantamentos de clientes " + Periodo(desde, ate), l, cabecalho);
    }

    /// <summary>Extrato de adiantamentos do cliente: saldo anterior, cada movimento com o saldo, saldo final, cartões e a dívida em facturas.</summary>
    private async Task<DocumentoImpressao> ExtratoAdiantamentosAsync(int clienteId, DateOnly desde, DateOnly ate, CancellationToken ct)
    {
        var r = await adiantamentos.ExtratoAsync(clienteId, desde, ate, ct);
        var l = await CabecalhoAsync("Extrato de adiantamentos", Periodo(desde, ate));
        Cliente(l, r.ClienteNome, r.ClienteNuit, r.ClienteTelefone);
        l.Add(LinhaImpressao.NovoCartao($"Saldo anterior a {desde:dd/MM/yyyy}", Valor(r.SaldoAnterior)));
        l.Add(LinhaImpressao.NovoCartao("Recargas", Valor(r.Recargas)));
        l.Add(LinhaImpressao.NovoCartao("Consumos", Valor(r.Consumos)));
        l.Add(LinhaImpressao.NovoCartao($"Saldo em {ate:dd/MM/yyyy}", Valor(r.SaldoFinal)));
        var movimentos = new List<LinhaImpressao> { LinhaImpressao.Tabela([Dia(desde.ToDateTime(TimeOnly.MinValue)), "", $"Saldo anterior a {desde:dd/MM/yyyy}", "", "", Valor(r.SaldoAnterior)], negrito: true) };
        movimentos.AddRange(r.Movimentos.Select(m => Lin(Dia(m.DataUtc), m.Cartao,
            string.Join(" · ", new[] { NomeMovimentoCartao(m.Tipo), m.Metodo, m.Pedido }.Where(x => x is not null)),
            m.Valor > 0 ? Valor(m.Valor) : "", m.Valor < 0 ? Valor(-m.Valor) : "", Valor(m.Saldo))));
        Grelha(l, "Movimentos", r.Movimentos.Count == 0 ? "sem movimentos no período" : null, ["Data", "Cartão", "Descrição", "Entrada", "Saída", "Saldo"], movimentos,
            subtotal: [$"Saldo em {ate:dd/MM/yyyy}", "", "", "", "", Valor(r.SaldoFinal)], texto: 3);
        if (r.Cortado) l.Add(new($"(Lista limitada aos primeiros {Cartoes.ServicoAdiantamentos.MaximoLinhas} movimentos.)"));
        l.Add(new($"SALDO EM {ate:dd/MM/yyyy}", Valor(r.SaldoFinal), Negrito: true, Grande: true));
        Grelha(l, "Cartões do cliente (hoje)", null, ["Cartão", "Estado", "Saldo"],
            r.Cartoes.Select(c => Lin(!c.Activo, c.Numero, c.Activo ? "Activo" : "Bloqueado", Valor(c.Saldo))),
            subtotal: r.Cartoes.Count > 0 ? ["Saldo de hoje", "", Valor(r.SaldoActual)] : null, texto: 2, vazio: "O cliente não tem cartões.");
        // Posição global: o que o cliente tem por consumir menos o que deve em facturas a crédito.
        var hoje = await config.HojeAsync(relogio);
        var divida = (await contas.ExtratoAsync(clienteId, hoje, hoje, ct)).EmDividaHoje;
        if (divida > 0)
        {
            l.Add(new("Em dívida (facturas a crédito)", Valor(divida)));
            l.Add(new("POSIÇÃO GLOBAL", Valor(r.SaldoActual - divida), Negrito: true));
        }
        Rodape(l);
        return new DocumentoImpressao($"Adiantamentos {r.ClienteNome} {Periodo(desde, ate)}", l, cabecalho);
    }

    internal static string NomeMovimentoCartao(TipoMovimentoCartao tipo) => tipo switch
    {
        TipoMovimentoCartao.Carregamento => "Carregamento",
        TipoMovimentoCartao.Recarga => "Recarga",
        TipoMovimentoCartao.Pagamento => "Consumo",
        TipoMovimentoCartao.AnulacaoPagamento => "Pagamento anulado",
        TipoMovimentoCartao.Estorno => "Estorno",
        TipoMovimentoCartao.Descarga => "Descarga de saldo",
        TipoMovimentoCartao.TransferenciaSaida => "Transferido para outro cartão",
        TipoMovimentoCartao.TransferenciaEntrada => "Recebido de outro cartão",
        _ => "Mudança de cliente",
    };
}
