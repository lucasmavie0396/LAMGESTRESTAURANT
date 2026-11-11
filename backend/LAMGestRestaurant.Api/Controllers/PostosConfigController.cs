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
    /// Endereços deste servidor na rede local, com a porta em que ele está à escuta: é o que os tablets vão usar.
    /// Só endereços privados (nunca públicos, que dependeriam da internet); as placas reais vêm antes das virtuais.
    /// </summary>
    [HttpGet("rede")]
    public RedeServidor Rede()
    {
        var porta = HttpContext.Connection.LocalPort > 0 ? HttpContext.Connection.LocalPort : HttpContext.Request.Host.Port ?? (HttpContext.Request.IsHttps ? 443 : 80);
        var esquema = HttpContext.Request.Scheme;
        var enderecos = PlacasDeRede.Listar()
            .Where(p => RedeLocal.Privado(p.Subrede.Endereco) || RedeLocal.Automatico(p.Subrede.Endereco))
            .OrderBy(p => RedeLocal.Automatico(p.Subrede.Endereco)).ThenBy(p => p.Virtual)
            .Select(p => $"{esquema}://{p.Subrede.Endereco}:{porta}")
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
    public Task<PostoEmparelhado> Emparelhar(Emparelhar dados, CancellationToken ct) =>
        postos.EmparelharAsync(dados.Codigo, HttpContext.Connection.RemoteIpAddress, [.. PlacasDeRede.Listar().Select(p => p.Subrede)], ct);
}

/// <summary>Placas de rede IPv4 ligadas neste servidor (funciona com ou sem internet).</summary>
internal static class PlacasDeRede
{
    private static readonly string[] Virtuais = ["virtual", "hyper-v", "vethernet", "vmware", "wsl", "tap-", "vpn"];

    public static IReadOnlyList<(Subrede Subrede, bool Virtual)> Listar() =>
        [.. NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel))
            .SelectMany(n => n.GetIPProperties().UnicastAddresses
                .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork && !System.Net.IPAddress.IsLoopback(a.Address))
                .Select(a => (new Subrede(a.Address, a.IPv4Mask), Virtuais.Any(v => (n.Name + " " + n.Description).Contains(v, StringComparison.OrdinalIgnoreCase)))))];
}
