using System.Globalization;
using LAMGestRestaurant.Application.Caixa;
using LAMGestRestaurant.Application.Comum;
using LAMGestRestaurant.Application.Impressao;
using LAMGestRestaurant.Application.Seguranca;
using LAMGestRestaurant.Domain.Comum;
using LAMGestRestaurant.Domain.Sistema;
using Microsoft.EntityFrameworkCore;

namespace LAMGestRestaurant.Application.Relatorios;

/// <summary>Ordem dos artigos na folha de contagem.</summary>
public enum OrdemFolhaContagem { Alfabetica, Codigo, Categoria, Tipo, Localizacao }

/// <summary>Opções da folha de contagem: ordem, saldo do sistema, só algumas categorias, uma página por grupo (A4), sem os de saldo zero.</summary>
public sealed record OpcoesFolhaContagem(OrdemFolhaContagem Ordem = OrdemFolhaContagem.Alfabetica, bool ComSaldo = false, IReadOnlyList<int>? Categorias = null,
    bool PaginaPorGrupo = false, bool EsconderZero = false);

public enum TipoDocumentoRelatorio
{
    Pagamentos, Vendas, Produtos, Despesas, Financeiro, Stock, FechoCaixa, ListaVendas, Clientes, Impostos, Anulacoes, CartoesEvento,
    FacturasCredito, HistoricoCliente, ExtratoCliente, DescargasCartoes, FechoDia, FolhaContagem,
    Acompanhamentos, Existencias, Transferencias,
}

/// <summary>
/// Relatórios em formato de documento imprimível (talão 80/58 mm ou A4), com os mesmos números dos ecrãs.
/// O mesmo documento serve a impressão pelo servidor e pela janela do navegador.
/// </summary>
public partial class ServicoDocumentosRelatorio(
    IAppDbContext db,
    IUtilizadorActual actual,
    ConfiguracoesSistema config,
    ServicoRelatorios relatorios,
    ServicoRelatoriosGestao gestao,
    ServicoCaixa caixa,
    ServicoRelatoriosDetalhe detalhe,
    Cartoes.ServicoRelatoriosCartoes cartoes,
    Facturacao.ServicoContaCorrente contas,
    ServicoDia dias,
    Stock.ServicoStock stock)
{
    // Dependências em campos: o serviço está dividido em ficheiros parciais (ServicoDocumentosRelatorio.*.cs) por assunto.
    private readonly IAppDbContext db = db;
    private readonly IUtilizadorActual actual = actual;
    private readonly ConfiguracoesSistema config = config;
    private readonly ServicoRelatorios relatorios = relatorios;
    private readonly ServicoRelatoriosGestao gestao = gestao;
    private readonly ServicoCaixa caixa = caixa;
    private readonly ServicoRelatoriosDetalhe detalhe = detalhe;
    private readonly Cartoes.ServicoRelatoriosCartoes cartoes = cartoes;
    private readonly Facturacao.ServicoContaCorrente contas = contas;
    private readonly ServicoDia dias = dias;
    private readonly Stock.ServicoStock stock = stock;

    private string moeda = "MT";
    private TimeZoneInfo fuso = TimeZoneInfo.Utc;
    private IReadOnlyList<LinhaImpressao>? cabecalho;

    /// <summary>Contas correntes dos clientes: quem consulta a facturação ou os relatórios de vendas/financeiros.</summary>
    public static readonly string[] PermissoesContas = [Permissoes.FacturacaoVer, Permissoes.RelatoriosVendas, Permissoes.RelatoriosFinanceiros];

    public static string Permissao(TipoDocumentoRelatorio tipo) => tipo switch
    {
        TipoDocumentoRelatorio.Pagamentos or TipoDocumentoRelatorio.Vendas or TipoDocumentoRelatorio.Produtos or TipoDocumentoRelatorio.ListaVendas
            or TipoDocumentoRelatorio.Clientes or TipoDocumentoRelatorio.Impostos or TipoDocumentoRelatorio.Anulacoes or TipoDocumentoRelatorio.Acompanhamentos => Permissoes.RelatoriosVendas,
        TipoDocumentoRelatorio.Despesas or TipoDocumentoRelatorio.Financeiro or TipoDocumentoRelatorio.CartoesEvento or TipoDocumentoRelatorio.DescargasCartoes => Permissoes.RelatoriosFinanceiros,
        TipoDocumentoRelatorio.Stock or TipoDocumentoRelatorio.Existencias or TipoDocumentoRelatorio.Transferencias => Permissoes.RelatoriosStock,
        TipoDocumentoRelatorio.FacturasCredito or TipoDocumentoRelatorio.HistoricoCliente or TipoDocumentoRelatorio.ExtratoCliente => Permissoes.FacturacaoVer,
        TipoDocumentoRelatorio.FechoDia => Permissoes.CaixaDia,
        TipoDocumentoRelatorio.FolhaContagem => Permissoes.StockVer,
        _ => Permissoes.CaixaOperar,
    };

    private static bool DeCliente(TipoDocumentoRelatorio tipo) => tipo is TipoDocumentoRelatorio.HistoricoCliente or TipoDocumentoRelatorio.ExtratoCliente;

    public async Task<DocumentoImpressao> GerarAsync(TipoDocumentoRelatorio tipo, DateOnly desde, DateOnly ate, int? sessaoId, CancellationToken ct = default,
        int? clienteId = null, int? diaId = null, int? inventarioId = null, OpcoesFolhaContagem? folha = null, int? armazemId = null)
    {
        var autorizado = tipo switch
        {
            TipoDocumentoRelatorio.FechoCaixa => actual.TemPermissao(Permissoes.CaixaOperar) || actual.TemPermissao(Permissoes.CaixaVerTodas),
            TipoDocumentoRelatorio.FechoDia => actual.TemPermissao(Permissoes.CaixaDia) || actual.TemPermissao(Permissoes.CaixaVerTodas),
            TipoDocumentoRelatorio.FacturasCredito or TipoDocumentoRelatorio.HistoricoCliente or TipoDocumentoRelatorio.ExtratoCliente =>
                PermissoesContas.Any(actual.TemPermissao),
            _ => actual.TemPermissao(Permissao(tipo)),
        };
        if (!autorizado)
            throw new AcessoNegadoException("Não tem permissão para este relatório.");
        // O histórico e o extrato do cliente podem abranger vários anos (validados no serviço das contas).
        if (tipo is not (TipoDocumentoRelatorio.FechoCaixa or TipoDocumentoRelatorio.FechoDia or TipoDocumentoRelatorio.FolhaContagem or TipoDocumentoRelatorio.Existencias) && !DeCliente(tipo))
            ServicoRelatorios.ValidarPeriodo(desde, ate);
        if (DeCliente(tipo) && clienteId is null)
            throw new RegraNegocioException("Escolha o cliente.");
        moeda = await config.TextoAsync(ChavesConfiguracao.SimboloMoeda, "MT");
        fuso = await config.FusoAsync();

        var documento = tipo switch
        {
            TipoDocumentoRelatorio.Pagamentos => await PagamentosAsync(desde, ate, ct),
            TipoDocumentoRelatorio.Vendas => await VendasAsync(desde, ate, ct),
            TipoDocumentoRelatorio.Produtos => await ProdutosAsync(desde, ate, ct),
            TipoDocumentoRelatorio.Despesas => await DespesasAsync(desde, ate, ct),
            TipoDocumentoRelatorio.Financeiro => await FinanceiroAsync(desde, ate, ct),
            TipoDocumentoRelatorio.Stock => await StockAsync(ct),
            TipoDocumentoRelatorio.Acompanhamentos => await AcompanhamentosAsync(desde, ate, ct),
            TipoDocumentoRelatorio.Existencias => await ExistenciasAsync(armazemId, folha?.Categorias is [var categoria] ? categoria : null, ct),
            TipoDocumentoRelatorio.Transferencias => await TransferenciasAsync(desde, ate, armazemId, ct),
            TipoDocumentoRelatorio.ListaVendas => await ListaVendasAsync(desde, ate, ct),
            TipoDocumentoRelatorio.Clientes => await ClientesAsync(desde, ate, ct),
            TipoDocumentoRelatorio.Impostos => await ImpostosAsync(desde, ate, ct),
            TipoDocumentoRelatorio.Anulacoes => await AnulacoesAsync(desde, ate, ct),
            TipoDocumentoRelatorio.CartoesEvento => await CartoesEventoAsync(desde, ate, sessaoId, ct),
            TipoDocumentoRelatorio.DescargasCartoes => await DescargasCartoesAsync(desde, ate, sessaoId, ct),
            TipoDocumentoRelatorio.FacturasCredito => await FacturasCreditoAsync(desde, ate, clienteId, ct),
            TipoDocumentoRelatorio.HistoricoCliente => await HistoricoClienteAsync(clienteId!.Value, desde, ate, ct),
            TipoDocumentoRelatorio.ExtratoCliente => await ExtratoClienteAsync(clienteId!.Value, desde, ate, ct),
            TipoDocumentoRelatorio.FechoDia => await FechoDiaAsync(diaId ?? throw new RegraNegocioException("Indique o dia."), ct),
            TipoDocumentoRelatorio.FolhaContagem => await FolhaContagemAsync(inventarioId ?? throw new RegraNegocioException("Indique o inventário."), folha ?? new OpcoesFolhaContagem()),
            _ => await FechoCaixaAsync(sessaoId ?? throw new RegraNegocioException("Indique a sessão de caixa."), ct),
        };
        // Totais grandes («texto … valor») saem num quadro em A4.
        return documento with
        {
            Linhas = [.. documento.Linhas.Select(x => x.Grande && x.Direita is not null && x.Celulas is null ? x with { Total = true } : x)],
        };
    }

    // ---------------- Documentos ----------------

    private static string Num(decimal v) => ComposicaoImpressao.Valor(v, "").Trim();

    private static string Pct(decimal parte, decimal total) =>
        total == 0 ? "—" : decimal.Round(parte * 100m / total, 1, MidpointRounding.AwayFromZero).ToString("0.0", CultureInfo.GetCultureInfo("pt-PT"));

    // ---------------- Contas correntes dos clientes ----------------

    private string Dia(DateTime utc) => TimeZoneInfo.ConvertTimeFromUtc(utc, fuso).ToString("dd/MM/yy", CultureInfo.InvariantCulture);

    // ---------------- Auxiliares ----------------

    private async Task<List<LinhaImpressao>> CabecalhoAsync(string titulo, string periodo)
    {
        var nome = await config.TextoAsync(ChavesConfiguracao.NomeEstabelecimento, "LAMGest Restaurant");
        var nuit = await config.TextoAsync(ChavesConfiguracao.Nuit);
        cabecalho = ComposicaoImpressao.Estabelecimento(string.IsNullOrWhiteSpace(nome) ? "LAMGest Restaurant" : nome, null, null, string.IsNullOrWhiteSpace(nuit) ? null : nuit);
        // Título (A4: grande à esquerda com o período à direita; talões: centrados em duas linhas).
        return [new(titulo.ToUpperInvariant(), periodo, Alinhamento.Centro, Negrito: true, Titulo: true), LinhaImpressao.Traco with { SoTalao = true }];
    }

    private void Rodape(List<LinhaImpressao> l)
    {
        l.Add(LinhaImpressao.Traco);
        l.Add(new($"Emitido em {DataHora(DateTime.UtcNow)}{(actual.Nome is { } n ? $" por {n}" : "")}", Alinhamento: Alinhamento.Centro));
        l.Add(new("Documento interno", Alinhamento: Alinhamento.Centro));
    }

    /// <summary>
    /// Grelha: faixa com o título e o resumo no topo, títulos das colunas, linhas e (opcional) linha de subtotal.
    /// <paramref name="texto"/>: quantas colunas iniciais são texto. Sem linhas, só a faixa e a mensagem <paramref name="vazio"/>.
    /// Grelhas de 2 colunas («descrição → valor»): nos talões não levam a linha de títulos (só a faixa).
    /// </summary>
    private static void Grelha(List<LinhaImpressao> l, string titulo, string? resumo, string[] colunas, IEnumerable<LinhaImpressao> linhas,
        string[]? subtotal = null, int texto = 1, string vazio = "—")
    {
        l.Add(LinhaImpressao.NovaFaixa(titulo, resumo));
        var lista = linhas.ToList();
        if (lista.Count == 0)
        {
            l.Add(new(vazio));
            return;
        }
        l.Add(LinhaImpressao.Tabela(colunas, cabecalho: true, colunasTexto: texto) with { SoA4 = colunas.Length == 2 });
        l.AddRange(lista.Select(x => x with { ColunasTexto = texto }));
        if (subtotal is not null) l.Add(LinhaImpressao.Tabela(subtotal, subtotal: true, colunasTexto: texto));
    }

    private static LinhaImpressao Lin(params string[] celulas) => LinhaImpressao.Tabela(celulas);

    private static LinhaImpressao Lin(bool alerta, params string[] celulas) => LinhaImpressao.Tabela(celulas, alerta: alerta);

    /// <summary>Resumo da faixa: «3 artigos · 1 234,00 MT».</summary>
    private string Resumo(int quantidade, string nome, decimal? valor)
    {
        var plural = quantidade == 1 ? nome : nome.EndsWith('m') ? nome[..^1] + "ns" : nome + "s";
        return $"{quantidade} {plural}{(valor is decimal v ? $" · {Valor(v)}" : "")}";
    }

    /// <summary>Legenda dos alertas: em A4 as linhas a vermelho; nos talões o «(!)».</summary>
    private static void Legenda(List<LinhaImpressao> l, string significado)
    {
        l.Add(new($"Linhas a vermelho: {significado}.", SoA4: true));
        l.Add(new($"(!) {significado}.", SoTalao: true));
    }

    private static void Assinaturas(List<LinhaImpressao> l, params string[] quem)
    {
        foreach (var q in quem)
        {
            l.Add(new(""));
            l.Add(new($"{q}: {new string('_', Math.Max(10, 30 - q.Length))}"));
        }
    }

    private static string Inteiro(int n) => n.ToString(CultureInfo.InvariantCulture);

    private static string Inteiro(decimal n) => ComposicaoImpressao.Quantidade(n);

    private string Valor(decimal v) => ComposicaoImpressao.Valor(v, moeda);

    private string DataHora(DateTime utc) => TimeZoneInfo.ConvertTimeFromUtc(utc, fuso).ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);

    private string Hora(DateTime utc, bool comDia) =>
        TimeZoneInfo.ConvertTimeFromUtc(utc, fuso).ToString(comDia ? "dd/MM HH:mm" : "HH:mm", CultureInfo.InvariantCulture);

    private static string Periodo(DateOnly desde, DateOnly ate) =>
        desde == ate ? $"{desde:dd/MM/yyyy}" : $"{desde:dd/MM/yyyy} a {ate:dd/MM/yyyy}";
}
