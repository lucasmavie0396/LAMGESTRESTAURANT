using System.Net.NetworkInformation;
using System.Net.Sockets;
using LAMGestRestaurant.Api.Seguranca;
using LAMGestRestaurant.Application.Caixa;
using LAMGestRestaurant.Application.Seguranca;
using Microsoft.AspNetCore.Mvc;

namespace LAMGestRestaurant.Api.Controllers;

/// <summary>Endereços do servidor na rede local, para os tablets se ligarem.</summary>
/// <summary><see cref="EnderecosVpn"/>: endereços deste servidor numa VPN privada (Tailscale), para o acesso dos tablets fora do restaurante.</summary>
public sealed record RedeServidor(IReadOnlyList<string> Enderecos, int Porta, IReadOnlyList<string>? EnderecosVpn = null);

/// <summary>APK da app de venda no servidor (<see cref="Disponivel"/>) e os endereços completos de descarga, em HTTP.</summary>
public sealed record AppVendaServidor(bool Disponivel, long? Tamanho, DateTime? ActualizadaEmUtc, IReadOnlyList<string> Enderecos);

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
        var porta = PortaActual();
        var esquema = HttpContext.Request.Scheme;
        var vpn = PlacasDeRede.Listar().Where(PlacasDeRede.Vpn).Select(p => $"{esquema}://{p.Subrede.Endereco}:{porta}").Distinct().ToList();
        return new RedeServidor(EnderecosLocais(esquema, porta), porta, vpn);
    }

    /// <summary>
    /// APK da app de venda neste servidor e os endereços de descarga (sempre em HTTP: um telemóvel novo ainda não confia
    /// no certificado local). Sem APK, o quadro pede para actualizar o servidor com o Setup novo.
    /// </summary>
    [HttpGet("app-venda")]
    public AppVendaServidor AppVendaInfo([FromServices] IConfiguration configuracao, [FromServices] Microsoft.AspNetCore.Hosting.Server.IServer servidor)
    {
        var ficheiro = new FileInfo(AppVenda.Ficheiro(configuracao));
        var portaHttp = servidor.Features.Get<Microsoft.AspNetCore.Hosting.Server.Features.IServerAddressesFeature>()?.Addresses
            .Select(a => Uri.TryCreate(a.Replace("://*", "://localhost").Replace("://+", "://localhost"), UriKind.Absolute, out var u) ? u : null)
            .FirstOrDefault(u => u?.Scheme == Uri.UriSchemeHttp)?.Port
            ?? (HttpContext.Request.IsHttps ? 80 : PortaActual());
        var enderecos = EnderecosLocais(Uri.UriSchemeHttp, portaHttp).Select(e => e + AppVenda.Rota).ToList();
        return ficheiro.Exists
            ? new AppVendaServidor(true, ficheiro.Length, ficheiro.LastWriteTimeUtc, enderecos)
            : new AppVendaServidor(false, null, null, enderecos);
    }

    private int PortaActual() =>
        HttpContext.Connection.LocalPort > 0 ? HttpContext.Connection.LocalPort : HttpContext.Request.Host.Port ?? (HttpContext.Request.IsHttps ? 443 : 80);

    private static List<string> EnderecosLocais(string esquema, int porta) =>
        [.. PlacasDeRede.Listar()
            .Where(p => RedeLocal.Privado(p.Subrede.Endereco) || RedeLocal.Automatico(p.Subrede.Endereco))
            .OrderBy(p => RedeLocal.Automatico(p.Subrede.Endereco)).ThenBy(p => p.Virtual)
            .Select(p => $"{esquema}://{p.Subrede.Endereco}:{porta}")
            .Distinct()];

    /// <summary>Escolhe (activo) ou retira o posto de ligação dos tablets; ao escolher devolve o código de emparelhamento.</summary>
    [HttpPost("{id:int}/tablets")]
    public async Task<IActionResult> Tablets(int id, DefinirTablets dados, CancellationToken ct) =>
        await postos.DefinirTabletsAsync(id, dados.Activo, ct) is { } codigo ? Ok(codigo) : NoContent();

    /// <summary>Gera um código de emparelhamento novo (o anterior deixa de valer).</summary>
    [HttpPost("{id:int}/tablets/codigo")]
    public Task<CodigoTablets> NovoCodigo(int id, CancellationToken ct) => postos.NovoCodigoTabletsAsync(id, ct);

    /// <summary>Ecrã do posto: modo táctil e tamanho do sistema (o navegador do posto aplica-o no minuto seguinte ou ao recarregar).</summary>
    [HttpPut("{id:int}/ecra")]
    public async Task<IActionResult> Ecra(int id, EcraPostoDto dados, CancellationToken ct)
    {
        await postos.ConfigurarEcraAsync(id, dados, ct);
        return NoContent();
    }

    /// <summary>Visor do cliente do posto: visor de poste (porta COM) ou segundo monitor.</summary>
    [HttpPut("{id:int}/visor")]
    public async Task<IActionResult> Visor(int id, ConfigurarVisor dados, [FromServices] ServicoVisor visor, CancellationToken ct)
    {
        await visor.ConfigurarAsync(id, dados, ct);
        return NoContent();
    }

    /// <summary>Balança ligada ao posto (porta COM), lida pelo agente.</summary>
    [HttpPut("{id:int}/balanca")]
    public async Task<IActionResult> Balanca(int id, ConfigurarBalanca dados, [FromServices] ServicoBalanca balanca, CancellationToken ct)
    {
        await balanca.ConfigurarAsync(id, dados, ct);
        return NoContent();
    }

    /// <summary>Mostra uma mensagem de teste no visor do posto.</summary>
    [HttpPost("{id:int}/visor/testar")]
    public async Task<IActionResult> TestarVisor(int id, [FromServices] ServicoVisor visor, CancellationToken ct)
    {
        await visor.TestarAsync(id, ct);
        return NoContent();
    }

    /// <summary>Endereço novo da página do visor (o anterior deixa de abrir).</summary>
    [HttpPost("{id:int}/visor/endereco")]
    public async Task<IActionResult> NovoEnderecoVisor(int id, [FromServices] ServicoVisor visor, CancellationToken ct)
    {
        await visor.NovoEnderecoAsync(id, ct);
        return NoContent();
    }

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

    /// <summary>
    /// Endereço do servidor fora do restaurante (VPN), que a app guarda quando abre no Wi-Fi do restaurante (antes de entrar).
    /// Null = acesso externo desligado. Só um endereço privado da VPN: não dá acesso a nada sem a VPN.
    /// </summary>
    [HttpGet("ligacao")]
    [Microsoft.AspNetCore.Authorization.AllowAnonymous]
    public async Task<LigacaoTablets> Ligacao([FromServices] Application.Comum.ConfiguracoesSistema config) =>
        new(await config.TextoAsync(Domain.Sistema.ChavesConfiguracao.EnderecoExternoTablets) is { Length: > 0 } e ? e : null);
}

public sealed record LigacaoTablets(string? EnderecoExterno);

/// <summary>Placas de rede IPv4 ligadas neste servidor (funciona com ou sem internet).</summary>
public static class PlacasDeRede
{
    private static readonly string[] Virtuais = ["virtual", "hyper-v", "vethernet", "vmware", "wsl", "tap-", "vpn"];

    /// <summary><c>Radmin</c>: placa da Radmin VPN (endereços 26.x, fora das gamas privadas, só dentro da rede Radmin).</summary>
    public static IReadOnlyList<(Subrede Subrede, bool Virtual, bool Radmin)> Listar() =>
        [.. NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel))
            .SelectMany(n => n.GetIPProperties().UnicastAddresses
                .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork && !System.Net.IPAddress.IsLoopback(a.Address))
                .Select(a => (new Subrede(a.Address, a.IPv4Mask), Virtuais.Any(v => (n.Name + " " + n.Description).Contains(v, StringComparison.OrdinalIgnoreCase)),
                    (n.Name + " " + n.Description).Contains("Radmin", StringComparison.OrdinalIgnoreCase))))];

    /// <summary>Endereço de uma VPN para os tablets fora do restaurante: Tailscale (100.64.0.0/10) ou a placa da Radmin VPN.</summary>
    public static bool Vpn((Subrede Subrede, bool Virtual, bool Radmin) placa) => RedeLocal.Vpn(placa.Subrede.Endereco) || placa.Radmin;
}
