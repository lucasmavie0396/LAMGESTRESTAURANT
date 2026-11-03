using LAMGestRestaurant.Api.Seguranca;
using LAMGestRestaurant.Application.Relatorios;
using LAMGestRestaurant.Application.Seguranca;
using Microsoft.AspNetCore.Mvc;

namespace LAMGestRestaurant.Api.Controllers;

[ApiController]
[Route("api")]
public class RelatoriosController(ServicoRelatorios relatorios, ServicoRelatoriosGestao gestao) : ControllerBase
{
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
