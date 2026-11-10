using LAMGestRestaurant.Api.Seguranca;
using LAMGestRestaurant.Application.Comum;
using LAMGestRestaurant.Application.Facturacao;
using LAMGestRestaurant.Application.Impressao;
using LAMGestRestaurant.Application.Seguranca;
using LAMGestRestaurant.Application.Vendas;
using LAMGestRestaurant.Domain.Comum;
using LAMGestRestaurant.Domain.Facturacao;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LAMGestRestaurant.Api.Controllers;

/// <summary>
/// Documentos comerciais e fiscais: VD (emitidas pelo POS), facturas, recibos, cotações e notas de crédito.
/// Emitir exige facturacao.emitir; notas de crédito e anulações, facturacao.anular. Criações com Idempotency-Key.
/// </summary>
[ApiController]
[Route("api/documentos")]
public class DocumentosController(ServicoDocumentos documentos) : ControllerBase
{
    private const string CabecalhoIdempotencia = "Idempotency-Key";

    [HttpGet]
    [ExigePermissao(Permissoes.FacturacaoVer)]
    public Task<Pagina<DocumentoResumo>> Listar(TipoDocumento? tipo, EstadoDocumento? estado, int? clienteId, DateOnly? desde, DateOnly? ate,
        string? pesquisa, bool emDivida = false, int pagina = 1, int tamanho = 50, CancellationToken ct = default) =>
        documentos.ListarAsync(new FiltroDocumentos(tipo, estado, clienteId, desde, ate, pesquisa, emDivida, pagina, tamanho), ct);

    [HttpGet("{id:int}")]
    [ExigePermissao(Permissoes.FacturacaoVer)]
    public Task<DocumentoDto> Obter(int id, CancellationToken ct) => documentos.ObterAsync(id, ct);

    /// <summary>Facturas em dívida do cliente (conta corrente), para emitir recibos.</summary>
    [HttpGet("conta-corrente/{clienteId:int}")]
    [ExigePermissao(Permissoes.FacturacaoVer)]
    public Task<ContaCorrente> ContaCorrente(int clienteId, CancellationToken ct) => documentos.ContaCorrenteAsync(clienteId, ct);

    /// <summary>Extrato da conta corrente do cliente no período (saldo anterior, movimentos com saldo e situação de hoje).</summary>
    [HttpGet("conta-corrente/{clienteId:int}/extrato")]
    [ExigeAlgumaPermissao(Permissoes.FacturacaoVer, Permissoes.RelatoriosVendas, Permissoes.RelatoriosFinanceiros)]
    public Task<ExtratoContaCorrente> Extrato(int clienteId, DateOnly desde, DateOnly ate, [FromServices] ServicoContaCorrente contas, CancellationToken ct) =>
        contas.ExtratoAsync(clienteId, desde, ate, ct);

    /// <summary>Histórico de documentos do cliente no período (todos os tipos ou só um).</summary>
    [HttpGet("historico-cliente/{clienteId:int}")]
    [ExigeAlgumaPermissao(Permissoes.FacturacaoVer, Permissoes.RelatoriosVendas, Permissoes.RelatoriosFinanceiros)]
    public Task<HistoricoCliente> Historico(int clienteId, DateOnly desde, DateOnly ate, TipoDocumento? tipo, [FromServices] ServicoContaCorrente contas, CancellationToken ct) =>
        contas.HistoricoAsync(clienteId, desde, ate, tipo, ct);

    /// <summary>Facturas a crédito emitidas no período e dívida actual por cliente (antiguidade).</summary>
    [HttpGet("facturas-credito")]
    [ExigeAlgumaPermissao(Permissoes.FacturacaoVer, Permissoes.RelatoriosVendas, Permissoes.RelatoriosFinanceiros)]
    public Task<RelatorioFacturasCredito> FacturasCredito(DateOnly desde, DateOnly ate, int? clienteId, [FromServices] ServicoContaCorrente contas,
        bool emDivida = false, CancellationToken ct = default) =>
        contas.FacturasCreditoAsync(desde, ate, clienteId, emDivida, ct);

    [HttpPost("facturas")]
    [ExigePermissao(Permissoes.FacturacaoEmitir)]
    public async Task<ActionResult<DocumentoDto>> EmitirFactura(NovoDocumento dados, [FromHeader(Name = CabecalhoIdempotencia)] string? chave) =>
        Resultado(await documentos.EmitirFacturaAsync(dados, chave));

    [HttpPost("cotacoes")]
    [ExigePermissao(Permissoes.FacturacaoEmitir)]
    public async Task<ActionResult<DocumentoDto>> EmitirCotacao(NovoDocumento dados, [FromHeader(Name = CabecalhoIdempotencia)] string? chave) =>
        Resultado(await documentos.EmitirCotacaoAsync(dados, chave));

    [HttpPost("cotacoes/{id:int}/converter")]
    [ExigePermissao(Permissoes.FacturacaoEmitir)]
    public async Task<ActionResult<DocumentoDto>> Converter(int id, ConverterCotacao dados, [FromHeader(Name = CabecalhoIdempotencia)] string? chave) =>
        Resultado(await documentos.ConverterCotacaoAsync(id, dados, chave));

    [HttpPost("recibos")]
    [ExigePermissao(Permissoes.FacturacaoEmitir)]
    public async Task<ActionResult<DocumentoDto>> EmitirRecibo(NovoRecibo dados, [FromHeader(Name = CabecalhoIdempotencia)] string? chave) =>
        Resultado(await documentos.EmitirReciboAsync(dados, chave));

    [HttpPost("notas-credito")]
    [ExigePermissao(Permissoes.FacturacaoAnular)]
    public async Task<ActionResult<DocumentoDto>> EmitirNotaCredito(NovaNotaCredito dados, [FromHeader(Name = CabecalhoIdempotencia)] string? chave) =>
        Resultado(await documentos.EmitirNotaCreditoAsync(dados, chave));

    /// <summary>Anula um recibo ou uma cotação (facturas, VD e notas de crédito corrigem-se com notas de crédito).</summary>
    [HttpPost("{id:int}/anular")]
    [ExigePermissao(Permissoes.FacturacaoAnular)]
    public Task<DocumentoDto> Anular(int id, ComMotivo dados) => documentos.AnularAsync(id, dados);

    /// <summary>Documento composto, para imprimir pelo navegador (80 mm, 58 mm ou A4; logótipo em 80 mm/A4).</summary>
    [HttpGet("{id:int}/impressao")]
    [ExigePermissao(Permissoes.FacturacaoVer)]
    public Task<DocumentoImpressao> Impressao(int id, [FromServices] ServicoImpressao impressao, CancellationToken ct) =>
        impressao.DocumentoFiscalAsync(id, ct);

    /// <summary>Envia o documento para uma impressora do servidor (a indicada, ou a de caixa do terminal), em segundo plano.</summary>
    [HttpPost("{id:int}/imprimir")]
    [ExigePermissao(Permissoes.FacturacaoVer)]
    public async Task<IActionResult> Imprimir(int id, ImprimirDocumentoFiscal dados, [FromServices] ServicoImpressao impressao,
        [FromServices] IAppDbContext db, [FromServices] IFilaImpressao fila, CancellationToken ct)
    {
        var impressora = dados.ImpressoraId is int impressoraId
            ? await db.Impressoras.AsNoTracking().FirstOrDefaultAsync(i => i.Id == impressoraId && i.Activo, ct)
              ?? throw new RegraNegocioException("Impressora inexistente ou inactiva.")
            : await impressao.ImpressoraCaixaAsync(dados.TerminalId, ct)
              ?? throw new RegraNegocioException("Não há impressora de caixa configurada para este terminal (Configurações → Impressoras).");
        fila.Enfileirar(new ImprimirDocumento(impressora.Id, await impressao.DocumentoFiscalAsync(id, ct), await impressao.ChaveCopiasAsync(id, ct)));
        return Accepted(new { impressora = impressora.Nome, pdf = impressora.Modo == ModoImpressora.Pdf });
    }

    private ActionResult<DocumentoDto> Resultado((DocumentoDto Documento, bool Criado) r) =>
        r.Criado ? CreatedAtAction(nameof(Obter), new { id = r.Documento.Id }, r.Documento) : Ok(r.Documento);
}
