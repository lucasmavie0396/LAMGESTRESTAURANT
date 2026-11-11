using System.Globalization;
using LAMGestRestaurant.Application.Comum;
using LAMGestRestaurant.Application.Impressao;
using LAMGestRestaurant.Domain.Cartoes;
using LAMGestRestaurant.Domain.Comum;
using LAMGestRestaurant.Domain.Sistema;
using Microsoft.EntityFrameworkCore;

namespace LAMGestRestaurant.Application.Cartoes;

public sealed partial class ServicoCartoes
{
    /// <summary>Recibo (talão de 80 mm) do último carregamento ou recarga do cartão, e o terminal onde o dinheiro entrou.</summary>
    public async Task<(DocumentoImpressao Documento, int? TerminalId)> ReciboCarregamentoAsync(int id, CancellationToken ct)
    {
        var m = await db.MovimentosCartao.AsNoTracking()
                    .Where(x => x.CartaoEventoId == id && x.Valor > 0 && (x.Tipo == TipoMovimentoCartao.Carregamento || x.Tipo == TipoMovimentoCartao.Recarga))
                    .OrderByDescending(x => x.Id)
                    .Select(x => new
                    {
                        x.Id, x.Tipo, x.Valor, x.SaldoApos, x.DataUtc, x.UtilizadorId, x.CartaoEvento!.Numero, x.CartaoEvento.Titular,
                        Metodo = x.MetodoPagamento != null ? x.MetodoPagamento.Nome : null,
                        Terminal = db.SessoesCaixa.Where(s => s.Id == x.SessaoCaixaId).Select(s => (int?)s.TerminalId).FirstOrDefault(),
                    })
                    .FirstOrDefaultAsync(ct)
                ?? throw new RegraNegocioException("Este cartão ainda não teve nenhum carregamento.");

        var moeda = await config.TextoAsync(ChavesConfiguracao.SimboloMoeda, "MT");
        var fuso = await config.FusoAsync();
        var operador = (await utilizadores.NomesAsync([m.UtilizadorId], ct)).GetValueOrDefault(m.UtilizadorId);
        string V(decimal v) => ComposicaoImpressao.Valor(v, moeda);
        static string? Vazio(string t) => string.IsNullOrWhiteSpace(t) ? null : t;

        var nome = await config.TextoAsync(ChavesConfiguracao.NomeEstabelecimento, "LAMGest Restaurant");
        var cabecalho = ComposicaoImpressao.Estabelecimento(Vazio(nome) ?? "LAMGest Restaurant", Vazio(await config.TextoAsync(ChavesConfiguracao.Endereco)),
            Vazio(await config.TextoAsync(ChavesConfiguracao.Telefone)), Vazio(await config.TextoAsync(ChavesConfiguracao.Nuit)));
        var tipo = m.Tipo == TipoMovimentoCartao.Recarga ? "Recarga" : "Carregamento inicial";
        var l = new List<LinhaImpressao>
        {
            new("RECIBO DE CARTÃO EVENTO", Alinhamento: Alinhamento.Centro, Negrito: true, Grande: true),
            new($"N.º {m.Id}", Alinhamento: Alinhamento.Centro),
            new("Data", TimeZoneInfo.ConvertTimeFromUtc(m.DataUtc, fuso).ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture)),
        };
        if (operador is not null) l.Add(new($"Operador: {operador}"));
        l.Add(LinhaImpressao.Traco);
        l.Add(new("Cartão", m.Numero, Negrito: true));
        if (m.Titular is not null) l.Add(new("Titular", m.Titular));
        l.Add(LinhaImpressao.Traco);
        l.Add(new(tipo, V(m.Valor), Negrito: true, Grande: true));
        if (m.Metodo is not null) l.Add(new("Pago com", m.Metodo));
        l.Add(new("Saldo do cartão", V(m.SaldoApos), Negrito: true));
        l.Add(LinhaImpressao.Traco);
        l.Add(new("Guarde este recibo.", Alinhamento: Alinhamento.Centro));
        return (new DocumentoImpressao($"Recibo do cartão {m.Numero}", l, cabecalho), m.Terminal);
    }
}
