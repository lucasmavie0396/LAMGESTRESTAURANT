using LAMGestRestaurant.Api.Seguranca;
using LAMGestRestaurant.Application.Cartoes;
using LAMGestRestaurant.Application.Comum;
using LAMGestRestaurant.Application.Seguranca;
using Microsoft.AspNetCore.Mvc;

namespace LAMGestRestaurant.Api.Controllers;

/// <summary>Cartões Evento (pré-pagos): registo, carregamento, consulta de saldo, extrato, bloqueio e etiquetas.</summary>
[ApiController]
[Route("api/cartoes")]
public class CartoesController(ServicoCartoes cartoes) : ControllerBase
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
    public Task<Pagina<MovimentoCartaoDto>> Movimentos(int id, int pagina = 1, int tamanho = 25, CancellationToken ct = default) =>
        cartoes.MovimentosAsync(id, pagina, tamanho, ct);

    [HttpPost]
    [ExigePermissao(Permissoes.CartoesGerir)]
    public async Task<ActionResult<CartaoDto>> Registar(RegistarCartao dados, [FromHeader(Name = CabecalhoIdempotencia)] string? chave)
    {
        var (cartao, criado) = await cartoes.RegistarAsync(dados, chave);
        return criado ? Created($"api/cartoes/{cartao.Id}", cartao) : Ok(cartao);
    }

    [HttpPost("{id:int}/recargas")]
    [ExigePermissao(Permissoes.CartoesGerir)]
    public async Task<ActionResult<CartaoDto>> Recarregar(int id, RecarregarCartao dados, [FromHeader(Name = CabecalhoIdempotencia)] string? chave)
    {
        var (cartao, criado) = await cartoes.RecarregarAsync(id, dados, chave);
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
    public async Task<IActionResult> Etiqueta(int id, CancellationToken ct) =>
        await cartoes.PrevisaoEtiquetaAsync(id, ct) is { } png ? File(png, "image/png")
            : throw new RegraNegocioException("A pré-visualização só está disponível num servidor Windows.");
}
