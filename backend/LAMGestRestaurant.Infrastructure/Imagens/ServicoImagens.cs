using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text.Json;
using LAMGestRestaurant.Application.Comum;
using LAMGestRestaurant.Domain.Catalogo;
using LAMGestRestaurant.Infrastructure.Dados;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace LAMGestRestaurant.Infrastructure.Imagens;

public sealed record ImagemEncontrada(string Titulo, string Miniatura, string Url, string? Autor, string Licenca, string? Pagina, int? Largura, int? Altura);
public sealed record ImagemGuardada(string Url, int Largura, int Altura, string? Autor, string? Licenca);

/// <summary>
/// Imagens dos produtos. Pesquisa no Openverse (catálogo aberto de imagens com licença livre, sem chave), importa de um
/// endereço ou de um ficheiro, reduz para no máximo 640 px em JPEG e guarda na base (entra nas cópias de segurança).
/// O servidor só descarrega de endereços públicos da internet: nunca da rede local (protecção contra SSRF).
/// </summary>
public sealed class ServicoImagens(AppDbContext db, IUtilizadorActual actual, IAuditoria auditoria, ILogger<ServicoImagens> log)
{
    public const int LadoMaximo = 640;
    public const int BytesMaximos = 8 * 1024 * 1024;
    private const string Openverse = "api.openverse.org";

    private static readonly HttpClient Http = CriarCliente();

    public static string Endereco(Guid id) => $"/api/imagens/{id:N}";

    public async Task<IReadOnlyList<ImagemEncontrada>> PesquisarAsync(string termo, CancellationToken ct)
    {
        termo = termo.Trim();
        if (termo.Length is < 2 or > 100)
            throw new RegraNegocioException("Escreva entre 2 e 100 caracteres para pesquisar.");
        JsonElement raiz;
        try
        {
            // Sem chave, o Openverse só aceita até 20 resultados por página (mais do que isso responde 401).
            using var resposta = await Http.GetAsync($"https://{Openverse}/v1/images/?q={Uri.EscapeDataString(termo)}&page_size=20&mature=false", ct);
            resposta.EnsureSuccessStatusCode();
            raiz = await resposta.Content.ReadFromJsonAsync<JsonElement>(ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            log.LogWarning(ex, "Pesquisa de imagens falhou");
            throw new RegraNegocioException("Não foi possível pesquisar imagens: o servidor não tem acesso à internet ou o serviço não respondeu.");
        }
        return [.. raiz.GetProperty("results").EnumerateArray()
            .Where(r => r.TryGetProperty("url", out _) && r.TryGetProperty("thumbnail", out _))
            .Select(r => new ImagemEncontrada(
                Texto(r, "title") ?? termo,
                Texto(r, "thumbnail")!,
                Texto(r, "url")!,
                Texto(r, "creator"),
                $"CC {Texto(r, "license")?.ToUpperInvariant()} {Texto(r, "license_version")}".Trim(),
                Texto(r, "foreign_landing_url"),
                Numero(r, "width"),
                Numero(r, "height")))];
    }

    /// <summary>Miniaturas do Openverse passam pelo servidor: os postos sem internet também as vêem. Só aceita esse domínio.</summary>
    public async Task<(byte[] Dados, string Tipo)> MiniaturaAsync(string url, CancellationToken ct)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || uri.Host != Openverse
            || !uri.AbsolutePath.StartsWith("/v1/images/", StringComparison.Ordinal))
            throw new RegraNegocioException("Miniatura não permitida.");
        var (dados, tipo) = await DescarregarAsync(uri, ct);
        return (dados, tipo ?? "image/jpeg");
    }

    public async Task<ImagemGuardada> ImportarAsync(string url, string? autor, string? licenca, CancellationToken ct)
    {
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            throw new RegraNegocioException("Endereço inválido: use um endereço http:// ou https:// de uma imagem.");
        var (dados, _) = await DescarregarAsync(uri, ct);
        return await GuardarAsync(dados, uri.ToString(), autor, licenca, ct);
    }

    public async Task<ImagemGuardada> CarregarAsync(Stream ficheiro, string nome, CancellationToken ct)
    {
        var dados = await LerComLimiteAsync(ficheiro, ct);
        return await GuardarAsync(dados, $"ficheiro: {Path.GetFileName(nome)}", null, null, ct);
    }

    public async Task<(byte[] Dados, string Tipo)?> ObterAsync(Guid id, CancellationToken ct) =>
        await db.ImagensProduto.AsNoTracking().Where(i => i.Id == id).Select(i => new { i.Dados, i.TipoConteudo }).FirstOrDefaultAsync(ct)
            is { } i ? (i.Dados, i.TipoConteudo) : null;

    private async Task<ImagemGuardada> GuardarAsync(byte[] original, string origem, string? autor, string? licenca, CancellationToken ct)
    {
        if (!OperatingSystem.IsWindows())
            throw new RegraNegocioException("O tratamento de imagens só está disponível num servidor Windows.");
        var (jpeg, largura, altura) = Reduzir(original);
        var imagem = new ImagemProduto
        {
            Id = Guid.NewGuid(),
            TipoConteudo = "image/jpeg",
            Dados = jpeg,
            Largura = largura,
            Altura = altura,
            Origem = Cortar(origem, 1000),
            Autor = Cortar(autor, 300),
            Licenca = Cortar(licenca, 100),
            CriadoEmUtc = DateTime.UtcNow,
            CriadoPorId = actual.Id,
        };
        db.ImagensProduto.Add(imagem);
        auditoria.Registar("imagem.guardar", "ImagemProduto", null, new { imagem.Id, imagem.Origem, imagem.Autor, imagem.Licenca, Bytes = jpeg.Length });
        await db.SaveChangesAsync(ct);
        return new ImagemGuardada(Endereco(imagem.Id), largura, altura, imagem.Autor, imagem.Licenca);
    }

    /// <summary>Descodifica (valida que é imagem), corrige a rotação da câmara, reduz e grava em JPEG sem metadados.</summary>
    [SupportedOSPlatform("windows")]
    internal static (byte[] Jpeg, int Largura, int Altura) Reduzir(byte[] original)
    {
        Image imagem;
        try
        {
            imagem = Image.FromStream(new MemoryStream(original), useEmbeddedColorManagement: false, validateImageData: true);
        }
        catch (Exception ex) when (ex is ArgumentException or OutOfMemoryException or ExternalException)
        {
            throw new RegraNegocioException("O ficheiro não é uma imagem suportada (use JPG, PNG, GIF ou BMP).");
        }
        using (imagem)
        {
            const int orientacaoExif = 0x0112;
            if (imagem.PropertyIdList.Contains(orientacaoExif) && imagem.GetPropertyItem(orientacaoExif)?.Value is [var o, ..])
                imagem.RotateFlip(o switch
                {
                    3 => RotateFlipType.Rotate180FlipNone,
                    6 => RotateFlipType.Rotate90FlipNone,
                    8 => RotateFlipType.Rotate270FlipNone,
                    _ => RotateFlipType.RotateNoneFlipNone,
                });
            var escala = Math.Min(1d, (double)LadoMaximo / Math.Max(imagem.Width, imagem.Height));
            var largura = Math.Max(1, (int)Math.Round(imagem.Width * escala));
            var altura = Math.Max(1, (int)Math.Round(imagem.Height * escala));
            using var destino = new Bitmap(largura, altura);
            using (var g = Graphics.FromImage(destino))
            {
                g.Clear(Color.White); // fundo branco para PNG com transparência
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.SmoothingMode = SmoothingMode.HighQuality;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.DrawImage(imagem, 0, 0, largura, altura);
            }
            var codificador = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);
            using var parametros = new EncoderParameters(1);
            parametros.Param[0] = new EncoderParameter(Encoder.Quality, 85L);
            using var saida = new MemoryStream();
            destino.Save(saida, codificador, parametros);
            return (saida.ToArray(), largura, altura);
        }
    }

    private static async Task<(byte[] Dados, string? Tipo)> DescarregarAsync(Uri uri, CancellationToken ct)
    {
        try
        {
            using var resposta = await Http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!resposta.IsSuccessStatusCode)
                throw new RegraNegocioException($"O endereço respondeu com o erro {(int)resposta.StatusCode}.");
            if (resposta.Content.Headers.ContentLength > BytesMaximos)
                throw new RegraNegocioException("A imagem é demasiado grande (máximo 8 MB).");
            var tipo = resposta.Content.Headers.ContentType?.MediaType;
            if (tipo is not null && !tipo.StartsWith("image/", StringComparison.OrdinalIgnoreCase) && tipo != "application/octet-stream")
                throw new RegraNegocioException("O endereço não aponta para uma imagem (copie o endereço da imagem, não o da página).");
            await using var corpo = await resposta.Content.ReadAsStreamAsync(ct);
            return (await LerComLimiteAsync(corpo, ct), tipo);
        }
        catch (HttpRequestException ex)
        {
            throw new RegraNegocioException(ex.InnerException is EnderecoNaoPermitidoException ? ex.InnerException.Message : "Não foi possível descarregar a imagem desse endereço.");
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new RegraNegocioException("O endereço demorou demasiado a responder.");
        }
    }

    private static async Task<byte[]> LerComLimiteAsync(Stream origem, CancellationToken ct)
    {
        using var destino = new MemoryStream();
        var tampao = new byte[81920];
        int lidos;
        while ((lidos = await origem.ReadAsync(tampao, ct)) > 0)
        {
            if (destino.Length + lidos > BytesMaximos)
                throw new RegraNegocioException("A imagem é demasiado grande (máximo 8 MB).");
            destino.Write(tampao, 0, lidos);
        }
        if (destino.Length == 0)
            throw new RegraNegocioException("O ficheiro está vazio.");
        return destino.ToArray();
    }

    private sealed class EnderecoNaoPermitidoException() : Exception("Só são permitidos endereços públicos da internet (não da rede local).");

    /// <summary>A verificação é feita no momento da ligação (cada redireccionamento também), contra o IP efectivamente usado.</summary>
    private static HttpClient CriarCliente()
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 3,
            ConnectTimeout = TimeSpan.FromSeconds(8),
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            ConnectCallback = async (contexto, ct) =>
            {
                var enderecos = await Dns.GetHostAddressesAsync(contexto.DnsEndPoint.Host, ct);
                if (enderecos.Length == 0 || enderecos.Any(e => !EnderecoPublico(e)))
                    throw new EnderecoNaoPermitidoException();
                // IPv4 primeiro (muitas redes locais não têm rota IPv6); tenta cada endereço até um responder.
                SocketException? ultimo = null;
                foreach (var ip in enderecos.OrderBy(e => e.AddressFamily == AddressFamily.InterNetwork ? 0 : 1))
                {
                    var socket = new Socket(ip.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                    try
                    {
                        await socket.ConnectAsync(new IPEndPoint(ip, contexto.DnsEndPoint.Port), ct);
                        return new NetworkStream(socket, ownsSocket: true);
                    }
                    catch (SocketException ex)
                    {
                        socket.Dispose();
                        ultimo = ex;
                    }
                    catch
                    {
                        socket.Dispose();
                        throw;
                    }
                }
                throw ultimo!;
            },
        };
        var cliente = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(20) };
        cliente.DefaultRequestHeaders.UserAgent.ParseAdd("LAMGestRestaurant/1.0");
        return cliente;
    }

    public static bool EnderecoPublico(IPAddress ip)
    {
        if (ip.IsIPv4MappedToIPv6)
            ip = ip.MapToIPv4();
        if (IPAddress.IsLoopback(ip) || ip.Equals(IPAddress.Any) || ip.Equals(IPAddress.IPv6Any))
            return false;
        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = ip.GetAddressBytes();
            return !(b[0] == 0 || b[0] == 10 || b[0] == 127 || b[0] >= 224
                     || (b[0] == 100 && b[1] >= 64 && b[1] <= 127)   // CGNAT
                     || (b[0] == 169 && b[1] == 254)                  // link-local
                     || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
                     || (b[0] == 192 && b[1] == 168)
                     || (b[0] == 192 && b[1] == 0 && b[2] == 0)
                     || (b[0] == 198 && b[1] is 18 or 19));
        }
        var v6 = ip.GetAddressBytes();
        return !(ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || ip.IsIPv6Multicast || (v6[0] & 0xFE) == 0xFC);
    }

    private static string? Texto(JsonElement e, string nome) =>
        e.TryGetProperty(nome, out var v) && v.ValueKind == JsonValueKind.String && v.GetString() is { Length: > 0 } s ? s : null;

    private static int? Numero(JsonElement e, string nome) =>
        e.TryGetProperty(nome, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : null;

    private static string? Cortar(string? s, int max) => string.IsNullOrWhiteSpace(s) ? null : s.Trim()[..Math.Min(s.Trim().Length, max)];
}
