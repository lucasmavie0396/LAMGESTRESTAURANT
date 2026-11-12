using LAMGestRestaurant.Api.Seguranca;
using LAMGestRestaurant.Application.Comum;
using LAMGestRestaurant.Application.Impressao;
using LAMGestRestaurant.Application.Relatorios;
using LAMGestRestaurant.Application.Seguranca;
using LAMGestRestaurant.Domain.Comum;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LAMGestRestaurant.Api.Controllers;

/// <summary><see cref="LarguraPapelMm"/>: 58, 80 ou 210 (A4); null = o papel configurado na impressora.</summary>
public record ImprimirRelatorio(TipoDocumentoRelatorio Tipo, DateOnly? Desde, DateOnly? Ate, int? SessaoId, int ImpressoraId,
    int? ClienteId = null, int? LarguraPapelMm = null, int? DiaId = null);
public record ImpressoraRelatorio(int Id, string Nome, FuncaoImpressora Funcao, int LarguraPapelMm, ModoImpressora Modo);

/// <summary>Fecho a imprimir: a sessão de caixa ou o dia; <see cref="TerminalId"/>: terminal deste posto (fecho do dia).</summary>
public record ImprimirFecho(int? SessaoId, int? DiaId, int? TerminalId);

/// <summary>
/// Resultado da impressão automática do fecho: <see cref="Automatico"/> falso = a opção está desligada; com <see cref="Impressora"/>
/// foi para essa impressora (<see cref="Pdf"/>: em modo PDF); sem impressora, <see cref="Documento"/> para imprimir pelo navegador.
/// </summary>
public record FechoAutomatico(bool Automatico, string? Impressora, bool Pdf, DocumentoImpressao? Documento);

[ApiController]
[Route("api")]
public class RelatoriosController(ServicoRelatorios relatorios, ServicoRelatoriosGestao gestao) : ControllerBase
{
    /// <summary>
    /// Relatório em formato de documento (linhas), para o posto imprimir pelo navegador em 80 mm, 58 mm ou A4.
    /// A permissão depende do tipo (verificada no serviço). Stock e fecho de caixa ignoram o período;
    /// o histórico e o extrato do cliente pedem o cliente.
    /// </summary>
    [HttpGet("relatorios/documento")]
    public Task<DocumentoImpressao> Documento(TipoDocumentoRelatorio tipo, DateOnly? desde, DateOnly? ate, int? sessaoId, int? clienteId, int? diaId,
        [FromServices] ServicoDocumentosRelatorio documentos, CancellationToken ct) =>
        documentos.GerarAsync(tipo, desde ?? Hoje(), ate ?? desde ?? Hoje(), sessaoId, ct, clienteId, diaId);

    /// <summary>Imprime o relatório numa impressora do servidor, no papel escolhido (ou no dessa impressora), em segundo plano.</summary>
    [HttpPost("relatorios/documento/imprimir")]
    public async Task<IActionResult> ImprimirDocumento(ImprimirRelatorio dados, [FromServices] ServicoDocumentosRelatorio documentos,
        [FromServices] IAppDbContext db, [FromServices] IFilaImpressao fila, CancellationToken ct)
    {
        if (dados.LarguraPapelMm is int largura && largura is not (Domain.Sistema.Impressora.Largura58 or Domain.Sistema.Impressora.Largura80 or Domain.Sistema.Impressora.LarguraA4))
            throw new RegraNegocioException("Papel inválido: escolha 58 mm, 80 mm ou A4.");
        var impressora = await db.Impressoras.AsNoTracking().FirstOrDefaultAsync(i => i.Id == dados.ImpressoraId && i.Activo, ct)
            ?? throw new RegraNegocioException("Impressora inexistente ou inactiva.");
        var documento = await documentos.GerarAsync(dados.Tipo, dados.Desde ?? Hoje(), dados.Ate ?? dados.Desde ?? Hoje(), dados.SessaoId, ct, dados.ClienteId, dados.DiaId);
        fila.Enfileirar(new Application.Impressao.ImprimirDocumento(impressora.Id, documento,
            dados.Tipo is TipoDocumentoRelatorio.FechoCaixa or TipoDocumentoRelatorio.FechoDia ? Domain.Sistema.ChavesConfiguracao.CopiasFecho : Domain.Sistema.ChavesConfiguracao.CopiasRelatorios,
            dados.LarguraPapelMm));
        return Accepted(new { impressora = impressora.Nome, pdf = impressora.Modo == ModoImpressora.Pdf, larguraPapelMm = dados.LarguraPapelMm ?? impressora.LarguraPapelMm });
    }

    /// <summary>
    /// Impressão automática do fecho de caixa (<see cref="ImprimirFecho.SessaoId"/>) ou do fecho do dia (<see cref="ImprimirFecho.DiaId"/>),
    /// chamada ao confirmar o fecho. Com «Imprimir o fecho automaticamente» desligado não imprime. Vai para a impressora de caixa do
    /// terminal (o da sessão; no dia, o indicado) no papel dela e com as cópias do fecho; sem impressora de caixa no servidor,
    /// devolve o documento para o posto imprimir pelo navegador.
    /// </summary>
    [HttpPost("relatorios/fecho/imprimir-automatico")]
    [ExigeAlgumaPermissao(Permissoes.CaixaOperar, Permissoes.CaixaVerTodas, Permissoes.CaixaDia)]
    public async Task<FechoAutomatico> ImprimirFechoAutomatico(ImprimirFecho dados, [FromServices] ServicoDocumentosRelatorio documentos,
        [FromServices] ServicoImpressao impressao, [FromServices] ConfiguracoesSistema config, [FromServices] IAppDbContext db,
        [FromServices] IFilaImpressao fila, CancellationToken ct)
    {
        if ((dados.SessaoId is null) == (dados.DiaId is null))
            throw new RegraNegocioException("Indique a sessão de caixa ou o dia.");
        if (!await config.BoolAsync(Domain.Sistema.ChavesConfiguracao.FechoAutomatico, padrao: true))
            return new FechoAutomatico(false, null, false, null);
        var tipo = dados.SessaoId is not null ? TipoDocumentoRelatorio.FechoCaixa : TipoDocumentoRelatorio.FechoDia;
        // A geração aplica as permissões do documento (dono da sessão ou caixa.ver-todas; caixa.dia no dia).
        var documento = await documentos.GerarAsync(tipo, Hoje(), Hoje(), dados.SessaoId, ct, null, dados.DiaId);
        var terminal = dados.SessaoId is int s
            ? await db.SessoesCaixa.AsNoTracking().Where(x => x.Id == s).Select(x => (int?)x.TerminalId).FirstOrDefaultAsync(ct)
            : dados.TerminalId;
        var impressora = await impressao.ImpressoraCaixaAsync(terminal, ct);
        if (impressora is null)
            return new FechoAutomatico(true, null, false, documento);
        fila.Enfileirar(new Application.Impressao.ImprimirDocumento(impressora.Id, documento, Domain.Sistema.ChavesConfiguracao.CopiasFecho));
        return new FechoAutomatico(true, impressora.Nome, impressora.Modo == ModoImpressora.Pdf, null);
    }

    /// <summary>Impressoras activas para imprimir relatórios e documentos (qualquer função).</summary>
    [HttpGet("relatorios/impressoras")]
    [ExigeAlgumaPermissao(Permissoes.RelatoriosVendas, Permissoes.RelatoriosFinanceiros, Permissoes.RelatoriosStock, Permissoes.CaixaOperar, Permissoes.CaixaVerTodas, Permissoes.CaixaDia, Permissoes.FacturacaoVer)]
    public async Task<IReadOnlyList<ImpressoraRelatorio>> Impressoras([FromServices] IAppDbContext db, CancellationToken ct) =>
        await db.Impressoras.AsNoTracking().Where(i => i.Activo && i.Funcao != FuncaoImpressora.Etiquetas).OrderBy(i => i.Nome)
            .Select(i => new ImpressoraRelatorio(i.Id, i.Nome, i.Funcao, i.LarguraPapelMm, i.Modo)).ToListAsync(ct);

    private static DateOnly Hoje() => DateOnly.FromDateTime(DateTime.UtcNow);

    /// <summary>Vendas entre duas datas locais (inclusive), no fuso configurado.</summary>
    [HttpGet("relatorios/vendas")]
    [ExigePermissao(Permissoes.RelatoriosVendas)]
    public Task<RelatorioVendas> Vendas(DateOnly desde, DateOnly ate, CancellationToken ct) => relatorios.VendasAsync(desde, ate, ct);

    /// <summary>Vendas pedido a pedido, com filtros e totais de tudo o que foi filtrado (não só da página).</summary>
    [HttpGet("relatorios/lista-vendas")]
    [ExigePermissao(Permissoes.RelatoriosVendas)]
    public Task<ListaVendas> ListaVendas([FromQuery] FiltroListaVendas filtro, [FromServices] ServicoRelatoriosDetalhe detalhe, CancellationToken ct) =>
        detalhe.ListaVendasAsync(filtro, ct);

    /// <summary>Vendas por cliente associado no POS.</summary>
    [HttpGet("relatorios/clientes")]
    [ExigePermissao(Permissoes.RelatoriosVendas)]
    public Task<RelatorioClientes> Clientes(DateOnly desde, DateOnly ate, string? pesquisa, [FromServices] ServicoRelatoriosDetalhe detalhe, CancellationToken ct) =>
        detalhe.ClientesAsync(desde, ate, pesquisa, ct);

    /// <summary>IVA incluído por taxa e por dia/mês, com descontos e taxas de entrega.</summary>
    [HttpGet("relatorios/impostos")]
    [ExigePermissao(Permissoes.RelatoriosVendas)]
    public Task<RelatorioImpostos> Impostos(DateOnly desde, DateOnly ate, string? agrupar, [FromServices] ServicoRelatoriosDetalhe detalhe, CancellationToken ct) =>
        detalhe.ImpostosAsync(desde, ate, agrupar, ct);

    /// <summary>Pedidos cancelados, estornos, itens cancelados após envio e pagamentos anulados.</summary>
    [HttpGet("relatorios/anulacoes")]
    [ExigePermissao(Permissoes.RelatoriosVendas)]
    public Task<RelatorioAnulacoes> Anulacoes(DateOnly desde, DateOnly ate, [FromServices] ServicoRelatoriosDetalhe detalhe, CancellationToken ct) =>
        detalhe.AnulacoesAsync(desde, ate, ct);

    /// <summary>Resultado estimado, despesas e fechos de caixa do período.</summary>
    [HttpGet("relatorios/financeiro")]
    [ExigePermissao(Permissoes.RelatoriosFinanceiros)]
    public Task<RelatorioFinanceiro> Financeiro(DateOnly desde, DateOnly ate, CancellationToken ct) => gestao.FinanceiroAsync(desde, ate, ct);

    /// <summary>Cartões Evento por datas ou, com <paramref name="sessaoCaixaId"/>, por caixa.</summary>
    [HttpGet("relatorios/cartoes")]
    [ExigePermissao(Permissoes.RelatoriosFinanceiros)]
    public Task<Application.Cartoes.RelatorioCartoes> Cartoes(DateOnly? desde, DateOnly? ate, int? sessaoCaixaId, [FromServices] Application.Cartoes.ServicoRelatoriosCartoes servico, CancellationToken ct) =>
        servico.GerarAsync(desde ?? Hoje(), ate ?? desde ?? Hoje(), ct, sessaoCaixaId);

    /// <summary>Descargas de saldo dos cartões, por datas ou por caixa.</summary>
    [HttpGet("relatorios/cartoes/descargas")]
    [ExigePermissao(Permissoes.RelatoriosFinanceiros)]
    public Task<Application.Cartoes.RelatorioDescargas> DescargasCartoes(DateOnly? desde, DateOnly? ate, int? sessaoCaixaId, [FromServices] Application.Cartoes.ServicoRelatoriosCartoes servico, CancellationToken ct) =>
        servico.DescargasAsync(desde ?? Hoje(), ate ?? desde ?? Hoje(), sessaoCaixaId, ct);

    /// <summary>Caixas com movimentos de cartões no período (para o relatório por caixa).</summary>
    [HttpGet("relatorios/cartoes/sessoes")]
    [ExigePermissao(Permissoes.RelatoriosFinanceiros)]
    public Task<IReadOnlyList<Application.Cartoes.SessaoCartoesDto>> SessoesCartoes(DateOnly? desde, DateOnly? ate, [FromServices] Application.Cartoes.ServicoRelatoriosCartoes servico, CancellationToken ct) =>
        servico.SessoesAsync(desde ?? Hoje(), ate ?? desde ?? Hoje(), ct);

    [HttpGet("relatorios/stock")]
    [ExigePermissao(Permissoes.RelatoriosStock)]
    public Task<RelatorioStock> Stock(DateOnly desde, DateOnly ate, CancellationToken ct) => gestao.StockAsync(desde, ate, ct);

    /// <summary>Indicadores do dia; as secções de caixa e stock dependem das permissões do utilizador.</summary>
    [HttpGet("dashboard")]
    [ExigePermissao(Permissoes.DashboardVer)]
    public Task<Dashboard> Dashboard(CancellationToken ct) => relatorios.DashboardAsync(ct);
}
