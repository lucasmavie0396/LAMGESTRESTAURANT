using System.ComponentModel.DataAnnotations;
using LAMGestRestaurant.Api.Seguranca;
using LAMGestRestaurant.Application.Catalogo;
using LAMGestRestaurant.Application.Comum;
using LAMGestRestaurant.Application.Seguranca;
using LAMGestRestaurant.Domain.Comum;
using Microsoft.AspNetCore.Mvc;

namespace LAMGestRestaurant.Api.Controllers;

public record AlterarActivo(bool Activo, byte[]? RowVersion);
public record AlterarDisponibilidade(bool Disponivel);
public record NovaEstacao([Required(ErrorMessage = "Indique o nome.")] string Nome);

[ApiController]
[Route("api/categorias")]
public class CategoriasController(ServicoCatalogo servico) : ControllerBase
{
    [HttpGet]
    [ExigeAlgumaPermissao(Permissoes.ProdutosVer, Permissoes.PosUsar)]
    public Task<IReadOnlyList<CategoriaDto>> Listar(bool incluirInactivas = false, CancellationToken ct = default) =>
        servico.ListarCategoriasAsync(incluirInactivas, ct);

    [HttpPost]
    [ExigePermissao(Permissoes.ProdutosGerir)]
    public async Task<ActionResult<CategoriaDto>> Criar(GuardarCategoria dados)
    {
        var criada = await servico.CriarCategoriaAsync(dados);
        return Created($"api/categorias/{criada.Id}", criada);
    }

    [HttpPut("{id:int}")]
    [ExigePermissao(Permissoes.ProdutosGerir)]
    public Task<CategoriaDto> Editar(int id, GuardarCategoria dados) => servico.EditarCategoriaAsync(id, dados);

    [HttpPost("{id:int}/activo")]
    [ExigePermissao(Permissoes.ProdutosGerir)]
    public async Task<IActionResult> DefinirActiva(int id, AlterarActivo dados)
    {
        await servico.DefinirCategoriaActivaAsync(id, dados.Activo);
        return NoContent();
    }
}

[ApiController]
[Route("api/produtos")]
public class ProdutosController(ServicoCatalogo servico) : ControllerBase
{
    [HttpGet]
    [ExigeAlgumaPermissao(Permissoes.ProdutosVer, Permissoes.PosUsar)]
    public Task<Pagina<ProdutoDto>> Listar(
        string? pesquisa, int? categoriaId, TipoProduto? tipo, bool? activo, bool? disponivel,
        string? ordenar, bool descendente = false, int pagina = 1, int tamanho = 25, CancellationToken ct = default) =>
        servico.ListarProdutosAsync(new FiltroProdutos(pesquisa, categoriaId, tipo, activo, disponivel, ordenar, descendente, pagina, tamanho), ct);

    [HttpGet("{id:int}")]
    [ExigeAlgumaPermissao(Permissoes.ProdutosVer, Permissoes.PosUsar)]
    public Task<ProdutoDto> Obter(int id) => servico.ObterProdutoAsync(id);

    [HttpGet("sugerir-codigo")]
    [ExigePermissao(Permissoes.ProdutosGerir)]
    public async Task<object> SugerirCodigo() => new { codigo = await servico.SugerirCodigoAsync() };

    /// <summary>Gera um código de barras EAN-13 interno livre (não grava; fica no formulário até guardar o produto).</summary>
    [HttpGet("gerar-codigo-barras")]
    [ExigePermissao(Permissoes.ProdutosGerir)]
    public async Task<object> GerarCodigoBarras(CancellationToken ct) => new { codigoBarras = await servico.GerarCodigoBarrasAsync(ct) };

    /// <summary>Verifica o modelo Excel linha a linha, sem gravar nada.</summary>
    [HttpPost("importacao/analisar")]
    [ExigePermissao(Permissoes.ProdutosGerir)]
    [RequestSizeLimit(ServicoImportacaoProdutos.BytesMaximos + 64 * 1024)]
    public async Task<AnaliseImportacao> AnalisarImportacao(IFormFile ficheiro, [FromServices] ServicoImportacaoProdutos importacao, CancellationToken ct)
    {
        await using var conteudo = ficheiro.OpenReadStream();
        return await importacao.AnalisarAsync(conteudo, ct);
    }

    /// <summary>Importa os produtos do modelo Excel: só grava se não houver erros, tudo ou nada.</summary>
    [HttpPost("importacao")]
    [ExigePermissao(Permissoes.ProdutosGerir)]
    [RequestSizeLimit(ServicoImportacaoProdutos.BytesMaximos + 64 * 1024)]
    public async Task<ResultadoImportacao> Importar(IFormFile ficheiro, [FromServices] ServicoImportacaoProdutos importacao, CancellationToken ct)
    {
        await using var conteudo = ficheiro.OpenReadStream();
        return await importacao.ImportarAsync(conteudo, ct);
    }

    [HttpPost]
    [ExigePermissao(Permissoes.ProdutosGerir)]
    public async Task<ActionResult<ProdutoDto>> Criar(GuardarProduto dados)
    {
        var criado = await servico.CriarProdutoAsync(dados);
        return CreatedAtAction(nameof(Obter), new { id = criado.Id }, criado);
    }

    /// <summary>Alterar o preço exige ainda produtos.alterar-preco (verificado no serviço).</summary>
    [HttpPut("{id:int}")]
    [ExigePermissao(Permissoes.ProdutosGerir)]
    public Task<ProdutoDto> Editar(int id, GuardarProduto dados) => servico.EditarProdutoAsync(id, dados);

    [HttpPost("{id:int}/activo")]
    [ExigePermissao(Permissoes.ProdutosGerir)]
    public Task<ProdutoDto> DefinirActivo(int id, AlterarActivo dados) => servico.DefinirProdutoActivoAsync(id, dados.Activo, dados.RowVersion);

    /// <summary>Esgotado/disponível: gestão de produtos ou cozinha.</summary>
    [HttpPost("{id:int}/disponibilidade")]
    [ExigeAlgumaPermissao(Permissoes.ProdutosGerir, Permissoes.CozinhaActualizar)]
    public Task<ProdutoDto> DefinirDisponibilidade(int id, AlterarDisponibilidade dados) => servico.DefinirDisponibilidadeAsync(id, dados.Disponivel);
}

[ApiController]
[Route("api/catalogo")]
public class ReferenciasCatalogoController(ServicoCatalogo servico) : ControllerBase
{
    [HttpGet("taxas")]
    [ExigeAlgumaPermissao(Permissoes.ProdutosVer, Permissoes.PosUsar)]
    public Task<IReadOnlyList<TaxaImpostoDto>> Taxas(CancellationToken ct) => servico.ListarTaxasAsync(ct);

    /// <summary>Impressoras de talões (Bar/Cozinha) para escolher na categoria e no produto.</summary>
    [HttpGet("impressoras")]
    [ExigePermissao(Permissoes.ProdutosVer)]
    public Task<IReadOnlyList<ImpressoraTalao>> Impressoras(CancellationToken ct) => servico.ListarImpressorasTalaoAsync(ct);

    [HttpGet("estacoes")]
    [ExigeAlgumaPermissao(Permissoes.ProdutosVer, Permissoes.CozinhaVer, Permissoes.StockVer)]
    public Task<IReadOnlyList<EstacaoPreparacaoDto>> Estacoes(CancellationToken ct) => servico.ListarEstacoesAsync(ct);

    [HttpPost("estacoes")]
    [ExigePermissao(Permissoes.ProdutosGerir)]
    public Task<EstacaoPreparacaoDto> CriarEstacao(NovaEstacao dados) => servico.CriarEstacaoAsync(dados.Nome);

    /// <summary>Armazém de onde sai o stock vendido por este centro de produção.</summary>
    [HttpPut("estacoes/{id:int}/armazem")]
    [ExigePermissao(Permissoes.StockGerir)]
    public Task<EstacaoPreparacaoDto> DefinirArmazemEstacao(int id, DefinirArmazemEstacao dados) => servico.DefinirArmazemEstacaoAsync(id, dados.ArmazemId);
}
