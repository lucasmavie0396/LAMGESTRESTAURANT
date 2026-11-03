using System.ComponentModel.DataAnnotations;
using LAMGestRestaurant.Api.Seguranca;
using LAMGestRestaurant.Application.Seguranca;
using LAMGestRestaurant.Infrastructure.Imagens;
using Microsoft.AspNetCore.Mvc;

namespace LAMGestRestaurant.Api.Controllers;

public record ImportarImagem(
    [Required(ErrorMessage = "Indique o endereço da imagem.")][StringLength(2000)] string Url,
    [StringLength(300)] string? Autor,
    [StringLength(100)] string? Licenca);

/// <summary>Imagens dos produtos: pesquisa na internet, importação, carregamento e leitura.</summary>
[ApiController]
[Route("api/imagens")]
public class ImagensController(ServicoImagens imagens) : ControllerBase
{
    /// <summary>Pesquisa imagens com licença livre (Openverse).</summary>
    [HttpGet("pesquisa")]
    [ExigePermissao(Permissoes.ProdutosGerir)]
    public Task<IReadOnlyList<ImagemEncontrada>> Pesquisar([FromQuery] string q, CancellationToken ct) => imagens.PesquisarAsync(q ?? "", ct);

    [HttpGet("miniatura")]
    [ExigePermissao(Permissoes.ProdutosGerir)]
    public async Task<IActionResult> Miniatura([FromQuery] string url, CancellationToken ct)
    {
        var (dados, tipo) = await imagens.MiniaturaAsync(url, ct);
        Response.Headers.CacheControl = "private, max-age=86400";
        return File(dados, tipo);
    }

    [HttpPost("importar")]
    [ExigePermissao(Permissoes.ProdutosGerir)]
    public Task<ImagemGuardada> Importar(ImportarImagem dados, CancellationToken ct) => imagens.ImportarAsync(dados.Url, dados.Autor, dados.Licenca, ct);

    /// <summary>Imagens de produtos e o logótipo do estabelecimento (Configurações).</summary>
    [HttpPost("carregar")]
    [ExigeAlgumaPermissao(Permissoes.ProdutosGerir, Permissoes.ConfiguracoesGerir)]
    [RequestSizeLimit(ServicoImagens.BytesMaximos + 64 * 1024)]
    public async Task<ImagemGuardada> Carregar(IFormFile ficheiro, CancellationToken ct)
    {
        await using var conteudo = ficheiro.OpenReadStream();
        return await imagens.CarregarAsync(conteudo, ficheiro.FileName, ct);
    }

    /// <summary>Qualquer utilizador com sessão (o POS mostra as imagens). O conteúdo de um id nunca muda.</summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Obter(Guid id, CancellationToken ct)
    {
        if (await imagens.ObterAsync(id, ct) is not { } imagem)
            return NotFound();
        Response.Headers.CacheControl = "private, max-age=31536000, immutable";
        return File(imagem.Dados, imagem.Tipo);
    }
}
