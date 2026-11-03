using System.Globalization;
using LAMGestRestaurant.Application.Vendas;
using LAMGestRestaurant.Domain.Comum;

namespace LAMGestRestaurant.Application.Impressao;

public sealed record ItemTalao(decimal Quantidade, string Produto, string? Observacoes);

public sealed record DadosTalao(
    string Destino,
    string PedidoNumero,
    TipoPedido Tipo,
    string? Mesa,
    string? Garcom,
    string? Operador,
    DateTime EnviadoLocal,
    string? ObservacoesPedido,
    IReadOnlyList<ItemTalao> Itens);

/// <summary>Conteúdo dos documentos impressos (recibo, talão de preparação, teste). Sem dependências de impressora.</summary>
public static class ComposicaoImpressao
{
    // Vírgula decimal e espaço nos milhares, como na interface (independente da cultura do servidor).
    private static readonly NumberFormatInfo Numeros = new() { NumberDecimalSeparator = ",", NumberGroupSeparator = " ", NumberGroupSizes = [3] };

    public static string Valor(decimal v, string moeda) => $"{v.ToString("N2", Numeros)} {moeda}";
    public static string Quantidade(decimal q) => q.ToString("0.###", Numeros);

    public static DocumentoImpressao Recibo(ReciboDto r, TimeZoneInfo fuso)
    {
        var l = new List<LinhaImpressao>
        {
            new(r.Estabelecimento, Alinhamento: Alinhamento.Centro, Negrito: true, Grande: true),
        };
        if (r.Endereco is not null) l.Add(new(r.Endereco, Alinhamento: Alinhamento.Centro));
        if (r.Telefone is not null) l.Add(new(r.Telefone, Alinhamento: Alinhamento.Centro));
        if (r.Nuit is not null) l.Add(new($"NUIT {r.Nuit}", Alinhamento: Alinhamento.Centro));
        l.Add(LinhaImpressao.Traco);
        l.Add(new($"Recibo {r.Numero}", Negrito: true));
        l.Add(new($"Pedido {r.PedidoNumero} · {DescreverTipo(r.Tipo, r.Mesa)}"));
        l.Add(new(TimeZoneInfo.ConvertTimeFromUtc(r.EmitidoEmUtc, fuso).ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture)));
        if (r.Operador is not null) l.Add(new($"Operador: {r.Operador}"));
        l.Add(LinhaImpressao.Traco);
        foreach (var i in r.Linhas)
        {
            l.Add(new($"{Quantidade(i.Quantidade)} x {i.Produto}", Valor(i.Total, r.Moeda)));
            if (i.Quantidade != 1)
                l.Add(new($"   {Valor(i.PrecoUnitario, r.Moeda)} / un."));
        }
        l.Add(LinhaImpressao.Traco);
        l.Add(new("Subtotal", Valor(r.Subtotal, r.Moeda)));
        if (r.Desconto > 0) l.Add(new("Desconto", "-" + Valor(r.Desconto, r.Moeda)));
        if (r.TaxaEntrega > 0) l.Add(new("Taxa de entrega", Valor(r.TaxaEntrega, r.Moeda)));
        l.Add(new("TOTAL", Valor(r.Total, r.Moeda), Negrito: true, Grande: true));
        l.Add(new("Impostos incluídos", Valor(r.Impostos, r.Moeda)));
        l.Add(LinhaImpressao.Traco);
        foreach (var p in r.Pagamentos)
        {
            l.Add(new(p.Referencia is null ? p.Metodo : $"{p.Metodo} ({p.Referencia})", Valor(p.Valor, r.Moeda)));
            if (p.Troco > 0)
                l.Add(new($"   Recebido {Valor(p.ValorRecebido, r.Moeda)}", $"Troco {Valor(p.Troco, r.Moeda)}"));
        }
        if (r.Rodape is not null)
        {
            l.Add(LinhaImpressao.Traco);
            l.Add(new(r.Rodape, Alinhamento: Alinhamento.Centro));
        }
        return new DocumentoImpressao($"Recibo {r.Numero}", l);
    }

    public static DocumentoImpressao Talao(DadosTalao t)
    {
        var l = new List<LinhaImpressao>
        {
            new(t.Destino.ToUpperInvariant(), Alinhamento: Alinhamento.Centro, Negrito: true, Grande: true),
            new(DescreverTipo(t.Tipo, t.Mesa), Alinhamento: Alinhamento.Centro, Negrito: true, Grande: true),
            new($"{t.PedidoNumero} · {t.EnviadoLocal.ToString("dd/MM HH:mm", CultureInfo.InvariantCulture)}", Alinhamento: Alinhamento.Centro),
        };
        if (t.Garcom is not null) l.Add(new($"Garçom: {t.Garcom}", Alinhamento: Alinhamento.Centro));
        else if (t.Operador is not null) l.Add(new($"Por: {t.Operador}", Alinhamento: Alinhamento.Centro));
        l.Add(LinhaImpressao.Traco);
        foreach (var i in t.Itens)
        {
            l.Add(new($"{Quantidade(i.Quantidade)} x {i.Produto}", Negrito: true, Grande: true));
            if (!string.IsNullOrWhiteSpace(i.Observacoes))
                l.Add(new($"   >> {i.Observacoes}", Negrito: true));
        }
        if (!string.IsNullOrWhiteSpace(t.ObservacoesPedido))
        {
            l.Add(LinhaImpressao.Traco);
            l.Add(new($"Obs.: {t.ObservacoesPedido}"));
        }
        l.Add(LinhaImpressao.Traco);
        return new DocumentoImpressao($"{t.Destino} {t.PedidoNumero}", l);
    }

    public static DocumentoImpressao Teste(string impressora, string nomeWindows, int larguraMm, DateTime agoraLocal) => new(
        "Teste LAMGest",
        [
            new("LAMGest Restaurant", Alinhamento: Alinhamento.Centro, Negrito: true, Grande: true),
            new("Impressão de teste", Alinhamento: Alinhamento.Centro),
            LinhaImpressao.Traco,
            new("Impressora", impressora),
            new("Windows", nomeWindows),
            new("Papel", DescreverPapel(larguraMm)),
            new("Data", agoraLocal.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture)),
            LinhaImpressao.Traco,
            new("1 x Produto com um nome bastante comprido para testar a quebra de linha", Valor(1234.5m, "MT")),
            new("TOTAL", Valor(1234.5m, "MT"), Negrito: true, Grande: true),
            LinhaImpressao.Traco,
            new("Se lê este texto, a impressora está bem configurada.", Alinhamento: Alinhamento.Centro),
        ]);

    public static string DescreverPapel(int larguraMm) => larguraMm >= Domain.Sistema.Impressora.LarguraA4 ? "A4" : $"{larguraMm} mm";

    private static string DescreverTipo(TipoPedido tipo, string? mesa) => tipo switch
    {
        TipoPedido.Mesa => $"Mesa {mesa}",
        TipoPedido.TakeAway => "Take Away",
        _ => "Delivery",
    };
}
