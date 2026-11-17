using LAMGestRestaurant.Api.Seguranca;
using LAMGestRestaurant.Application.Caixa;
using LAMGestRestaurant.Application.Seguranca;
using Microsoft.AspNetCore.Mvc;

namespace LAMGestRestaurant.Api.Controllers;

/// <summary>Balança no POS: etiquetas com peso ou preço e o peso da balança ligada ao posto.</summary>
[ApiController]
[Route("api/balanca")]
[ExigePermissao(Permissoes.PosUsar)]
public class BalancaController(ServicoBalanca balanca) : ControllerBase
{
    private static readonly TimeSpan EsperaMaxima = TimeSpan.FromSeconds(20);

    /// <summary>Produto e quantidade de uma etiqueta da balança (404 se o código não for uma etiqueta).</summary>
    [HttpGet("etiqueta/{codigo}")]
    public Task<ProdutoEtiquetaDto> Etiqueta(string codigo, CancellationToken ct) => balanca.EtiquetaAsync(codigo, ct);

    /// <summary>Peso da balança do posto; com a versão que o POS já tem, espera até haver uma leitura nova (pedido longo).</summary>
    [HttpGet("{terminalId:int}")]
    public async Task<IActionResult> Peso(int terminalId, long versao = 0, int espera = 0, CancellationToken ct = default)
    {
        try
        {
            return Ok(await balanca.LeituraParaPosAsync(terminalId, versao, TimeSpan.FromSeconds(Math.Clamp(espera, 0, (int)EsperaMaxima.TotalSeconds)), ct));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return NoContent();
        }
    }
}
