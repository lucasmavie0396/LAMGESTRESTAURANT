using LAMGestRestaurant.Api.Seguranca;
using LAMGestRestaurant.Application.Relatorios;
using LAMGestRestaurant.Application.Seguranca;
using Microsoft.AspNetCore.Mvc;

namespace LAMGestRestaurant.Api.Controllers;

[ApiController]
[Route("api")]
public class RelatoriosController(ServicoRelatorios relatorios) : ControllerBase
{
    /// <summary>Vendas entre duas datas locais (inclusive), no fuso configurado.</summary>
    [HttpGet("relatorios/vendas")]
    [ExigePermissao(Permissoes.RelatoriosVendas)]
    public Task<RelatorioVendas> Vendas(DateOnly desde, DateOnly ate, CancellationToken ct) => relatorios.VendasAsync(desde, ate, ct);

    /// <summary>Indicadores do dia; as secções de caixa e stock dependem das permissões do utilizador.</summary>
    [HttpGet("dashboard")]
    [ExigePermissao(Permissoes.DashboardVer)]
    public Task<Dashboard> Dashboard(CancellationToken ct) => relatorios.DashboardAsync(ct);
}
