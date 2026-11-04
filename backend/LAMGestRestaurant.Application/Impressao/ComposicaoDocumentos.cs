using System.Globalization;
using LAMGestRestaurant.Application.Facturacao;
using LAMGestRestaurant.Application.Vendas;
using LAMGestRestaurant.Domain.Comum;
using LAMGestRestaurant.Domain.Facturacao;

namespace LAMGestRestaurant.Application.Impressao;

/// <summary>
/// Documentos comerciais e fiscais impressos (VD, factura, recibo, cotação, nota de crédito), com os elementos exigidos
/// pelo Código do IVA: tipo e número sequencial, data, emitente e adquirente com NUIT, quantidade, descrição e preço,
/// IVA por taxa (base e imposto), motivo da isenção nas linhas a 0 % e a indicação de processamento por computador.
/// </summary>
public static class ComposicaoDocumentos
{
    /// <summary>Texto antigo do rodapé do recibo interno: deixou de fazer sentido nos documentos fiscais.</summary>
    public const string RodapeInternoAntigo = "Documento interno - não serve de factura.";
    public const string Processado = "Processado por computador · LAMGest Restaurant";

    public static DocumentoImpressao Compor(DocumentoDto d, TimeZoneInfo fuso, string moeda, string? rodape, IReadOnlyList<PagamentoDto>? pagamentos = null)
    {
        string V(decimal v) => ComposicaoImpressao.Valor(v, moeda);
        string Data(DateTime utc) => TimeZoneInfo.ConvertTimeFromUtc(utc, fuso).ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);
        static string Dia(DateOnly dia) => dia.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
        static string Taxa(decimal p) => $"{p.ToString("0.##", CultureInfo.InvariantCulture).Replace('.', ',')} %";

        var nome = Documento.Nome(d.Tipo);
        var cabecalho = ComposicaoImpressao.Estabelecimento(d.EmitenteNome, d.EmitenteEndereco, d.EmitenteTelefone, d.EmitenteNuit ?? "—");
        var l = new List<LinhaImpressao>();
        l.Add(new(nome.ToUpperInvariant(), Alinhamento: Alinhamento.Centro, Negrito: true, Grande: true));
        l.Add(new(d.Numero, Alinhamento: Alinhamento.Centro, Negrito: true));
        if (d.Estado == EstadoDocumento.Anulado)
            l.Add(new($"ANULADO{(d.AnuladoEmUtc is DateTime a ? " em " + Data(a) : "")}: {d.MotivoAnulacao}", Alinhamento: Alinhamento.Centro, Negrito: true));
        l.Add(new("Data", Data(d.EmitidoEmUtc)));
        if (d.Vencimento is DateOnly vencimento) l.Add(new("Vencimento", Dia(vencimento)));
        if (d.ValidaAte is DateOnly validade) l.Add(new("Válida até", Dia(validade)));
        if (d.PedidoNumero is not null) l.Add(new("Pedido", d.PedidoNumero));
        if (d.DocumentoOrigemNumero is not null && d.DocumentoOrigemTipo is TipoDocumento origem)
            l.Add(new(d.Tipo == TipoDocumento.Factura ? "Cotação de origem" : $"Referente à {Documento.Nome(origem)}", d.DocumentoOrigemNumero));
        if (d.Motivo is not null) l.Add(new($"Motivo: {d.Motivo}"));
        if (d.Utilizador is not null) l.Add(new($"Operador: {d.Utilizador}"));
        l.Add(LinhaImpressao.Traco);

        l.Add(new($"Cliente: {d.ClienteNome}", Negrito: true));
        l.Add(new($"NUIT: {d.ClienteNuit ?? "—"}"));
        if (d.ClienteEndereco is not null) l.Add(new(d.ClienteEndereco));
        l.Add(LinhaImpressao.Traco);

        if (d.Tipo == TipoDocumento.Recibo)
        {
            l.Add(LinhaImpressao.Tabela(["Factura liquidada", $"Valor ({moeda})"], cabecalho: true));
            foreach (var q in d.Liquidacoes)
                l.Add(LinhaImpressao.Tabela([q.FacturaNumero, ComposicaoImpressao.Numero(q.Valor)]));
            l.Add(LinhaImpressao.Traco);
            l.Add(new("TOTAL RECEBIDO", V(d.Total), Negrito: true, Grande: true));
            if (d.MetodoPagamento is not null)
                l.Add(new(d.Referencia is null ? d.MetodoPagamento : $"{d.MetodoPagamento} ({d.Referencia})", V(d.Total)));
        }
        else
        {
            l.Add(LinhaImpressao.Tabela(["Descrição", "Qtd.", "Preço", "IVA", $"Total ({moeda})"], cabecalho: true));
            foreach (var i in d.Linhas)
            {
                var descricao = i.Desconto > 0 ? $"{i.Descricao} (desc. {ComposicaoImpressao.Numero(i.Desconto)})" : i.Descricao;
                l.Add(LinhaImpressao.Tabela([descricao, ComposicaoImpressao.Quantidade(i.Quantidade), ComposicaoImpressao.Numero(i.PrecoUnitario),
                    Taxa(i.PercentagemIva) + (i.PercentagemIva <= 0 ? "*" : ""), ComposicaoImpressao.Numero(i.Total)]));
            }
            l.Add(LinhaImpressao.Traco);
            l.Add(new("Subtotal", V(d.Subtotal)));
            if (d.Desconto > 0) l.Add(new("Descontos", "-" + V(d.Desconto)));
            l.Add(new("Total sem IVA", V(d.BaseTributavel)));
            l.Add(new("IVA", V(d.Iva)));
            l.Add(new(d.Tipo == TipoDocumento.NotaCredito ? "TOTAL CREDITADO" : "TOTAL", V(d.Total), Negrito: true, Grande: true));
            l.Add(LinhaImpressao.Traco);
            l.Add(LinhaImpressao.Tabela(["Resumo do IVA", "Base", "IVA"], cabecalho: true));
            foreach (var r in d.ResumoIva)
                l.Add(LinhaImpressao.Tabela([$"IVA {Taxa(r.Percentagem)}", ComposicaoImpressao.Numero(r.Base), ComposicaoImpressao.Numero(r.Iva)]));
            foreach (var motivo in d.Linhas.Where(i => i.PercentagemIva <= 0 && i.MotivoIsencao is not null).Select(i => i.MotivoIsencao!).Distinct())
                l.Add(new($"* {motivo}"));

            if (d.Tipo == TipoDocumento.VendaDinheiro && pagamentos is { Count: > 0 })
            {
                l.Add(LinhaImpressao.Traco);
                foreach (var p in pagamentos)
                {
                    l.Add(new(p.Referencia is null ? p.Metodo : $"{p.Metodo} ({p.Referencia})", V(p.Valor)));
                    if (p.Troco > 0)
                        l.Add(new($"   Recebido {V(p.ValorRecebido)}", $"Troco {V(p.Troco)}"));
                }
            }
            if (d.Tipo == TipoDocumento.Factura && d.Estado == EstadoDocumento.Emitido && (d.ValorPago > 0 || d.ValorCreditado > 0))
            {
                l.Add(LinhaImpressao.Traco);
                if (d.ValorPago > 0) l.Add(new("Já pago", V(d.ValorPago)));
                if (d.ValorCreditado > 0) l.Add(new("Creditado", V(d.ValorCreditado)));
                l.Add(new("EM DÍVIDA", V(d.EmDivida), Negrito: true));
            }
        }
        if (d.Observacoes is not null)
        {
            l.Add(LinhaImpressao.Traco);
            l.Add(new(d.Observacoes));
        }

        l.Add(LinhaImpressao.Traco);
        if (d.Tipo == TipoDocumento.Cotacao)
            l.Add(new("Este documento não serve de factura.", Alinhamento: Alinhamento.Centro, Negrito: true));
        if (rodape is not null && rodape != RodapeInternoAntigo && d.Tipo is TipoDocumento.VendaDinheiro or TipoDocumento.Recibo or TipoDocumento.Factura)
            l.Add(new(rodape, Alinhamento: Alinhamento.Centro));
        l.Add(new(Processado, Alinhamento: Alinhamento.Centro));
        return new DocumentoImpressao($"{nome} {d.Numero}", l, cabecalho);
    }
}
