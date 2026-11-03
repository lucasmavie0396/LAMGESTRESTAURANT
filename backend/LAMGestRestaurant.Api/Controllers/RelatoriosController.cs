using LAMGestRestaurant.Api.Seguranca;
using LAMGestRestaurant.Application.Comum;
using LAMGestRestaurant.Application.Impressao;
using LAMGestRestaurant.Application.Relatorios;
using LAMGestRestaurant.Application.Seguranca;
using LAMGestRestaurant.Domain.Comum;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LAMGestRestaurant.Api.Controllers;

public record ImprimirRelatorio(TipoDocumentoRelatorio Tipo, DateOnly? Desde, DateOnly? Ate, int? SessaoId, int ImpressoraId);
public record ImpressoraRelatorio(int Id, string Nome, FuncaoImpressora Funcao, int LarguraPapelMm);

[ApiController]
[Route("api")]
public class RelatoriosController(ServicoRelatorios relatorios, ServicoRelatoriosGestao gestao) : ControllerBase
{
    /// <summary>
    /// Relatório em formato de documento (linhas), para o posto imprimir pelo navegador em 80 mm, 58 mm ou A4.
    /// A permissão depende do tipo (verificada no serviço). Stock e fecho de caixa ignoram o período.
    /// </summary>
    [HttpGet("relatorios/documento")]
    public Task<DocumentoImpressao> Documento(TipoDocumentoRelatorio tipo, DateOnly? desde, DateOnly? ate, int? sessaoId,
        [FromServices] ServicoDocumentosRelatorio documentos, CancellationToken ct) =>
        documentos.GerarAsync(tipo, desde ?? Hoje(), ate ?? desde ?? Hoje(), sessaoId, ct);

    /// <summary>Imprime o relatório numa impressora do servidor (no papel dessa impressora), em segundo plano.</summary>
    [HttpPost("relatorios/documento/imprimir")]
    public async Task<IActionResult> ImprimirDocumento(ImprimirRelatorio dados, [FromServices] ServicoDocumentosRelatorio documentos,
        [FromServices] IAppDbContext db, [FromServices] IFilaImpressao fila, CancellationToken ct)
    {
        var impressora = await db.Impressoras.AsNoTracking().FirstOrDefaultAsync(i => i.Id == dados.ImpressoraId && i.Activo, ct)
            ?? throw new RegraNegocioException("Impressora inexistente ou inactiva.");
        var documento = await documentos.GerarAsync(dados.Tipo, dados.Desde ?? Hoje(), dados.Ate ?? dados.Desde ?? Hoje(), dados.SessaoId, ct);
        fila.Enfileirar(new Application.Impressao.ImprimirDocumento(impressora.Id, documento));
        return Accepted(new { impressora = impressora.Nome });
    }

    /// <summary>Impressoras activas para imprimir relatórios (qualquer função).</summary>
    [HttpGet("relatorios/impressoras")]
    [ExigeAlgumaPermissao(Permissoes.RelatoriosVendas, Permissoes.RelatoriosFinanceiros, Permissoes.RelatoriosStock, Permissoes.CaixaOperar, Permissoes.CaixaVerTodas)]
    public async Task<IReadOnlyList<ImpressoraRelatorio>> Impressoras([FromServices] IAppDbContext db, CancellationToken ct) =>
        await db.Impressoras.AsNoTracking().Where(i => i.Activo).OrderBy(i => i.Nome)
            .Select(i => new ImpressoraRelatorio(i.Id, i.Nome, i.Funcao, i.LarguraPapelMm)).ToListAsync(ct);

    private static DateOnly Hoje() => DateOnly.FromDateTime(DateTime.UtcNow);

    /// <summary>Vendas entre duas datas locais (inclusive), no fuso configurado.</summary>
    [HttpGet("relatorios/vendas")]
    [ExigePermissao(Permissoes.RelatoriosVendas)]
    public Task<RelatorioVendas> Vendas(DateOnly desde, DateOnly ate, CancellationToken ct) => relatorios.VendasAsync(desde, ate, ct);

    /// <summary>Resultado estimado, despesas e fechos de caixa do período.</summary>
    [HttpGet("relatorios/financeiro")]
    [ExigePermissao(Permissoes.RelatoriosFinanceiros)]
    public Task<RelatorioFinanceiro> Financeiro(DateOnly desde, DateOnly ate, CancellationToken ct) => gestao.FinanceiroAsync(desde, ate, ct);

    /// <summary>Valor do stock, movimentos, consumo, ajustes/perdas e compras do período.</summary>
    [HttpGet("relatorios/stock")]
    [ExigePermissao(Permissoes.RelatoriosStock)]
    public Task<RelatorioStock> Stock(DateOnly desde, DateOnly ate, CancellationToken ct) => gestao.StockAsync(desde, ate, ct);

    /// <summary>Indicadores do dia; as secções de caixa e stock dependem das permissões do utilizador.</summary>
    [HttpGet("dashboard")]
    [ExigePermissao(Permissoes.DashboardVer)]
    public Task<Dashboard> Dashboard(CancellationToken ct) => relatorios.DashboardAsync(ct);
}
