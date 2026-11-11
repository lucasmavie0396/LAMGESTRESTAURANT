using LAMGestRestaurant.Api.Seguranca;
using LAMGestRestaurant.Application.Cartoes;
using LAMGestRestaurant.Application.Comum;
using LAMGestRestaurant.Application.Impressao;
using LAMGestRestaurant.Application.Seguranca;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LAMGestRestaurant.Api.Controllers;

/// <summary>Cartões Evento (pré-pagos): registo, carregamento, consulta de saldo, extrato, bloqueio e etiquetas.</summary>
[ApiController]
[Route("api/cartoes")]
public class CartoesController(ServicoCartoes cartoes, IFilaImpressao fila, IAppDbContext db) : ControllerBase
{
    private const string CabecalhoIdempotencia = "Idempotency-Key";

    [HttpGet]
    [ExigePermissao(Permissoes.CartoesVer)]
    public Task<Pagina<CartaoDto>> Listar(string? pesquisa, bool? activo, int pagina = 1, int tamanho = 25, CancellationToken ct = default) =>
        cartoes.ListarAsync(pesquisa, activo, pagina, tamanho, ct);

    /// <summary>Saldo pelo que o leitor escreveu (cartão magnético, código de barras ou QR code). Também no pagamento do POS.</summary>
    [HttpGet("consulta")]
    [ExigeAlgumaPermissao(Permissoes.CartoesVer, Permissoes.PagamentosRegistar)]
    public Task<CartaoDto> Consultar(string? numero, CancellationToken ct) => cartoes.ConsultarAsync(numero, ct);

    [HttpGet("{id:int}")]
    [ExigePermissao(Permissoes.CartoesVer)]
    public Task<CartaoDto> Obter(int id, CancellationToken ct) => cartoes.ObterAsync(id, ct);

    [HttpGet("{id:int}/movimentos")]
    [ExigePermissao(Permissoes.CartoesVer)]
    public Task<ExtratoCartaoDto> Movimentos(int id, DateOnly? desde, DateOnly? ate, int? sessaoCaixaId, int pagina = 1, int tamanho = 25, CancellationToken ct = default) =>
        cartoes.MovimentosAsync(id, pagina, tamanho, ct, desde, ate, sessaoCaixaId);

    /// <summary>Caixas (sessões) onde o cartão teve movimentos, para filtrar o extrato.</summary>
    [HttpGet("{id:int}/sessoes")]
    [ExigePermissao(Permissoes.CartoesVer)]
    public Task<IReadOnlyList<SessaoCartoesDto>> Sessoes(int id, CancellationToken ct) => cartoes.SessoesDoCartaoAsync(id, ct);

    [HttpPost]
    [ExigePermissao(Permissoes.CartoesGerir)]
    public async Task<ActionResult<CartaoDto>> Registar(RegistarCartao dados, [FromHeader(Name = CabecalhoIdempotencia)] string? chave)
    {
        var (cartao, criado) = await cartoes.RegistarAsync(dados, chave);
        if (criado && dados.ValorInicial > 0) await GavetaAsync(cartao.Id);
        return criado ? Created($"api/cartoes/{cartao.Id}", cartao) : Ok(cartao);
    }

    /// <summary>
    /// Recibo de 80 mm do último carregamento ou recarga, na impressora de pagamentos (caixa) do terminal onde o dinheiro entrou.
    /// Sem essa impressora, devolve o documento para o posto o imprimir.
    /// </summary>
    [HttpPost("{id:int}/recibo")]
    [ExigePermissao(Permissoes.CartoesGerir)]
    public async Task<IActionResult> Recibo(int id, ImprimirReciboNoTerminal dados, [FromServices] ServicoImpressao impressao, [FromServices] IFilaImpressao fila, CancellationToken ct)
    {
        var (documento, terminal) = await cartoes.ReciboCarregamentoAsync(id, ct);
        var impressora = dados.SemImpressora ? null : await impressao.ImpressoraCaixaAsync(terminal ?? dados.TerminalId, ct);
        if (impressora is not null)
            fila.Enfileirar(new ImprimirDocumento(impressora.Id, documento, Domain.Sistema.ChavesConfiguracao.CopiasRecibo, Domain.Sistema.Impressora.Largura80));
        return Ok(new { impressora = impressora?.Nome, pdf = impressora?.Modo == Domain.Comum.ModoImpressora.Pdf, documento });
    }

    /// <summary>Saldo restante de cada cartão e o acumulado: os indicados em <paramref name="id"/> ou, sem nenhum, todos os que têm saldo.</summary>
    [HttpGet("descargas/saldos")]
    [ExigePermissao(Permissoes.CartoesGerir)]
    public Task<SaldosDescarga> SaldosDescarga([FromQuery] int[]? id, CancellationToken ct) => cartoes.SaldosParaDescargaAsync(id, ct);

    /// <summary>Descarrega o saldo dos cartões indicados, tudo ou nada. Reenvios com o mesmo Idempotency-Key não duplicam.</summary>
    [HttpPost("descargas")]
    [ExigePermissao(Permissoes.CartoesGerir)]
    public async Task<ActionResult<DescargaDto>> Descarregar(NovaDescarga dados, [FromHeader(Name = CabecalhoIdempotencia)] string? chave)
    {
        var (descarga, criada) = await cartoes.DescarregarAsync(dados, chave);
        if (criada && descarga.SessaoCaixaId is int sessao) fila.Enfileirar(new AbrirGaveta(null, sessao)); // dinheiro devolvido
        return criada ? Created($"api/cartoes/descargas/{descarga.Id}", descarga) : Ok(descarga);
    }

    [HttpGet("descargas/{id:int}")]
    [ExigePermissao(Permissoes.CartoesVer)]
    public Task<DescargaDto> Descarga(int id, CancellationToken ct) => cartoes.ObterDescargaAsync(id, ct);

    [HttpPost("{id:int}/recargas")]
    [ExigePermissao(Permissoes.CartoesGerir)]
    public async Task<ActionResult<CartaoDto>> Recarregar(int id, RecarregarCartao dados, [FromHeader(Name = CabecalhoIdempotencia)] string? chave)
    {
        var (cartao, criado) = await cartoes.RecarregarAsync(id, dados, chave);
        if (criado) await GavetaAsync(cartao.Id);
        return criado ? Created($"api/cartoes/{cartao.Id}", cartao) : Ok(cartao);
    }

    [HttpPost("{id:int}/activo")]
    [ExigePermissao(Permissoes.CartoesGerir)]
    public Task<CartaoDto> DefinirActivo(int id, AlterarCartao dados) => cartoes.DefinirActivoAsync(id, dados);

    [HttpPost("etiquetas")]
    [ExigePermissao(Permissoes.CartoesGerir)]
    public async Task<IActionResult> Etiquetas(EtiquetasCartoes dados, CancellationToken ct) =>
        Accepted(new { impressora = await cartoes.ImprimirEtiquetasAsync(dados, ct) });

    [HttpGet("{id:int}/etiqueta")]
    [ExigePermissao(Permissoes.CartoesVer)]
    public async Task<IActionResult> Etiqueta(int id, CancellationToken ct, CodigoEtiquetaCartao codigo = CodigoEtiquetaCartao.CodigoBarras) =>
        await cartoes.PrevisaoEtiquetaAsync(id, codigo, ct) is { } png ? File(png, "image/png")
            : throw new RegraNegocioException("A pré-visualização só está disponível num servidor Windows.");

    /// <summary>Dinheiro recebido no carregamento ou recarga: abre a gaveta do caixa onde entrou.</summary>
    private async Task GavetaAsync(int cartaoId)
    {
        var sessao = await db.MovimentosCartao.AsNoTracking().Where(m => m.CartaoEventoId == cartaoId && m.SessaoCaixaId != null)
            .OrderByDescending(m => m.Id).Select(m => m.SessaoCaixaId).FirstOrDefaultAsync();
        if (sessao is int s)
            fila.Enfileirar(new AbrirGaveta(null, s));
    }
}
