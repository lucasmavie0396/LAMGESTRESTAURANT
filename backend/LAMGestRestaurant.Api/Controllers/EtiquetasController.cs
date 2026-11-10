using LAMGestRestaurant.Api.Seguranca;
using LAMGestRestaurant.Application.Comum;
using LAMGestRestaurant.Application.Impressao;
using LAMGestRestaurant.Application.Seguranca;
using Microsoft.AspNetCore.Mvc;

namespace LAMGestRestaurant.Api.Controllers;

/// <summary>Etiquetas de produto: pré-visualização (com o tamanho e campos ainda por guardar) e impressão.</summary>
[ApiController]
[Route("api/etiquetas")]
public class EtiquetasController(ServicoEtiquetas etiquetas) : ControllerBase
{
    [HttpGet("previsao")]
    [ExigeAlgumaPermissao(Permissoes.ProdutosVer, Permissoes.ConfiguracoesGerir)]
    public async Task<IActionResult> Previsao(int? produtoId, decimal? largura, decimal? altura, string? campos, CancellationToken ct) =>
        await etiquetas.PrevisaoAsync(produtoId, largura, altura, campos, ct) is { } png
            ? File(png, "image/png")
            : throw new RegraNegocioException("A pré-visualização só está disponível num servidor Windows.");

    [HttpPost("imprimir")]
    [ExigePermissao(Permissoes.ProdutosVer)]
    public async Task<IActionResult> Imprimir(ImprimirEtiquetas dados, CancellationToken ct) =>
        Accepted(new { impressora = await etiquetas.ImprimirAsync(dados, ct) });
}
