using System.Net.NetworkInformation;
using System.Net.Sockets;
using LAMGestRestaurant.Api.Seguranca;
using LAMGestRestaurant.Application.Caixa;
using LAMGestRestaurant.Application.Seguranca;
using Microsoft.AspNetCore.Mvc;

namespace LAMGestRestaurant.Api.Controllers;

/// <summary>Endereços do servidor na rede local, para os tablets se ligarem.</summary>
public sealed record RedeServidor(IReadOnlyList<string> Enderecos, int Porta);

/// <summary>Configurações → Postos: estado de todos os postos e escolha do posto de ligação dos tablets.</summary>
[ApiController]
[Route("api/configuracoes/postos")]
[ExigePermissao(Permissoes.ConfiguracoesGerir)]
public class PostosConfigController(ServicoPostos postos) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<PostoEstadoDto>> Listar(CancellationToken ct) => postos.ListarAsync(ct);

    /// <summary>
    /// Endereços IPv4 deste servidor nas placas de rede ligadas (sem o 127.0.0.1), com a porta por onde o pedido chegou:
    /// é o endereço que os tablets vão usar na rede local.
    /// </summary>
    [HttpGet("rede")]
    public RedeServidor Rede()
    {
        var porta = HttpContext.Request.Host.Port ?? (HttpContext.Request.IsHttps ? 443 : 80);
        var esquema = HttpContext.Request.Scheme;
        var enderecos = NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .SelectMany(n => n.GetIPProperties().UnicastAddresses)
            .Select(a => a.Address)
            .Where(a => a.AddressFamily == AddressFamily.InterNetwork && !System.Net.IPAddress.IsLoopback(a) && !a.ToString().StartsWith("169.254.", StringComparison.Ordinal))
            .Select(a => $"{esquema}://{a}:{porta}")
            .Distinct()
            .ToList();
        return new RedeServidor(enderecos, porta);
    }

    /// <summary>Escolhe (activo) ou retira o posto de ligação dos tablets; ao escolher devolve o código de emparelhamento.</summary>
    [HttpPost("{id:int}/tablets")]
    public async Task<IActionResult> Tablets(int id, DefinirTablets dados, CancellationToken ct) =>
        await postos.DefinirTabletsAsync(id, dados.Activo, ct) is { } codigo ? Ok(codigo) : NoContent();

    /// <summary>Gera um código de emparelhamento novo (o anterior deixa de valer).</summary>
    [HttpPost("{id:int}/tablets/codigo")]
    public Task<CodigoTablets> NovoCodigo(int id, CancellationToken ct) => postos.NovoCodigoTabletsAsync(id, ct);

    /// <summary>QR code (SVG em JSON) do texto de emparelhamento, para o tablet ler com a câmara. Pedido por POST: o código não vai no endereço.</summary>
    [HttpPost("qr")]
    public IActionResult Qr(TextoQr dados)
    {
        if (string.IsNullOrWhiteSpace(dados.Texto) || dados.Texto.Length > 500)
            throw new Application.Comum.RegraNegocioException("Texto do QR code inválido.");
        var matriz = Infrastructure.Impressao.Qr.Matriz(dados.Texto);
        var lado = matriz.Count;
        var svg = new System.Text.StringBuilder(
            $"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {lado} {lado}\" shape-rendering=\"crispEdges\"><rect width=\"{lado}\" height=\"{lado}\" fill=\"#fff\"/><path fill=\"#000\" d=\"");
        for (var y = 0; y < lado; y++)
            for (var x = 0; x < lado; x++)
                if (matriz[y][x]) svg.Append($"M{x} {y}h1v1h-1z");
        svg.Append("\"/></svg>");
        return Ok(new { svg = svg.ToString() });
    }
}

public sealed record TextoQr(string Texto);

/// <summary>Emparelhamento da app de venda (Android). Sem sessão: o operador só entra depois, com o seu utilizador.</summary>
[ApiController]
[Route("api/tablets")]
public class TabletsController(ServicoPostos postos) : ControllerBase
{
    [HttpPost("emparelhar")]
    [Microsoft.AspNetCore.Authorization.AllowAnonymous]
    public Task<PostoEmparelhado> Emparelhar(Emparelhar dados, CancellationToken ct) => postos.EmparelharAsync(dados.Codigo, ct);
}
