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
    int? ClienteId = null, int? LarguraPapelMm = null);
public record ImpressoraRelatorio(int Id, string Nome, FuncaoImpressora Funcao, int LarguraPapelMm, ModoImpressora Modo);

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
    public Task<DocumentoImpressao> Documento(TipoDocumentoRelatorio tipo, DateOnly? desde, DateOnly? ate, int? sessaoId, int? clienteId,
        [FromServices] ServicoDocumentosRelatorio documentos, CancellationToken ct) =>
        documentos.GerarAsync(tipo, desde ?? Hoje(), ate ?? desde ?? Hoje(), sessaoId, ct, clienteId);

    /// <summary>Imprime o relatório numa impressora do servidor, no papel escolhido (ou no dessa impressora), em segundo plano.</summary>
    [HttpPost("relatorios/documento/imprimir")]
    public async Task<IActionResult> ImprimirDocumento(ImprimirRelatorio dados, [FromServices] ServicoDocumentosRelatorio documentos,
        [FromServices] IAppDbContext db, [FromServices] IFilaImpressao fila, CancellationToken ct)
    {
        if (dados.LarguraPapelMm is int largura && largura is not (Domain.Sistema.Impressora.Largura58 or Domain.Sistema.Impressora.Largura80 or Domain.Sistema.Impressora.LarguraA4))
            throw new RegraNegocioException("Papel inválido: escolha 58 mm, 80 mm ou A4.");
        var impressora = await db.Impressoras.AsNoTracking().FirstOrDefaultAsync(i => i.Id == dados.ImpressoraId && i.Activo, ct)
            ?? throw new RegraNegocioException("Impressora inexistente ou inactiva.");
        var documento = await documentos.GerarAsync(dados.Tipo, dados.Desde ?? Hoje(), dados.Ate ?? dados.Desde ?? Hoje(), dados.SessaoId, ct, dados.ClienteId);
        fila.Enfileirar(new Application.Impressao.ImprimirDocumento(impressora.Id, documento,
            dados.Tipo == TipoDocumentoRelatorio.FechoCaixa ? Domain.Sistema.ChavesConfiguracao.CopiasFecho : Domain.Sistema.ChavesConfiguracao.CopiasRelatorios,
            dados.LarguraPapelMm));
        return Accepted(new { impressora = impressora.Nome, pdf = impressora.Modo == ModoImpressora.Pdf, larguraPapelMm = dados.LarguraPapelMm ?? impressora.LarguraPapelMm });
    }

    /// <summary>Impressoras activas para imprimir relatórios e documentos (qualquer função).</summary>
    [HttpGet("relatorios/impressoras")]
    [ExigeAlgumaPermissao(Permissoes.RelatoriosVendas, Permissoes.RelatoriosFinanceiros, Permissoes.RelatoriosStock, Permissoes.CaixaOperar, Permissoes.CaixaVerTodas, Permissoes.FacturacaoVer)]
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

    /// <summary>Valor do stock, movimentos, consumo, ajustes/perdas e compras do período.</summary>
    [HttpGet("relatorios/cartoes")]
    [ExigePermissao(Permissoes.RelatoriosFinanceiros)]
    public Task<Application.Cartoes.RelatorioCartoes> Cartoes(DateOnly? desde, DateOnly? ate, [FromServices] Application.Cartoes.ServicoRelatoriosCartoes servico, CancellationToken ct) =>
        servico.GerarAsync(desde ?? Hoje(), ate ?? desde ?? Hoje(), ct);

    [HttpGet("relatorios/stock")]
    [ExigePermissao(Permissoes.RelatoriosStock)]
    public Task<RelatorioStock> Stock(DateOnly desde, DateOnly ate, CancellationToken ct) => gestao.StockAsync(desde, ate, ct);

    /// <summary>Indicadores do dia; as secções de caixa e stock dependem das permissões do utilizador.</summary>
    [HttpGet("dashboard")]
    [ExigePermissao(Permissoes.DashboardVer)]
    public Task<Dashboard> Dashboard(CancellationToken ct) => relatorios.DashboardAsync(ct);
}
