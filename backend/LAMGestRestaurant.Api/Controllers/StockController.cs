using LAMGestRestaurant.Api.Seguranca;
using LAMGestRestaurant.Application.Comum;
using LAMGestRestaurant.Application.Compras;
using LAMGestRestaurant.Application.Seguranca;
using LAMGestRestaurant.Application.Stock;
using LAMGestRestaurant.Domain.Comum;
using Microsoft.AspNetCore.Mvc;

namespace LAMGestRestaurant.Api.Controllers;

public record ComVersaoSimples(byte[]? RowVersion);
public record AnularComMotivo(
    [System.ComponentModel.DataAnnotations.Required(ErrorMessage = "Indique o motivo.")]
    [System.ComponentModel.DataAnnotations.StringLength(1000, MinimumLength = 3, ErrorMessage = "O motivo deve ter entre 3 e 1000 caracteres.")]
    string Motivo,
    byte[]? RowVersion);

[ApiController]
[Route("api/stock")]
public class StockController(ServicoStock stock) : ControllerBase
{
    [HttpGet("unidades")]
    [ExigeAlgumaPermissao(Permissoes.StockVer, Permissoes.ReceitasGerir, Permissoes.ComprasVer)]
    public Task<IReadOnlyList<UnidadeDto>> Unidades(CancellationToken ct) => stock.UnidadesAsync(ct);

    [HttpGet("armazens")]
    [ExigeAlgumaPermissao(Permissoes.StockVer, Permissoes.ComprasVer)]
    public Task<IReadOnlyList<ArmazemDto>> Armazens(CancellationToken ct) => stock.ArmazensAsync(ct);

    [HttpPost("armazens")]
    [ExigePermissao(Permissoes.StockGerir)]
    public Task<ArmazemDto> CriarArmazem(GuardarArmazem dados) => stock.CriarArmazemAsync(dados);

    [HttpGet("artigos")]
    [ExigeAlgumaPermissao(Permissoes.StockVer, Permissoes.ReceitasGerir, Permissoes.ComprasVer)]
    public Task<Pagina<ArtigoDto>> Artigos(string? pesquisa, TipoArtigo? tipo, bool? activo, bool abaixoMinimo = false,
        int pagina = 1, int tamanho = 25, CancellationToken ct = default) =>
        stock.ListarArtigosAsync(new FiltroArtigos(pesquisa, tipo, activo, abaixoMinimo, pagina, tamanho), ct);

    [HttpGet("artigos/{id:int}")]
    [ExigeAlgumaPermissao(Permissoes.StockVer, Permissoes.ReceitasGerir, Permissoes.ComprasVer)]
    public Task<ArtigoDto> Artigo(int id) => stock.ObterArtigoAsync(id);

    [HttpPost("artigos")]
    [ExigePermissao(Permissoes.StockGerir)]
    public Task<ArtigoDto> CriarArtigo(GuardarArtigo dados) => stock.CriarArtigoAsync(dados);

    [HttpPut("artigos/{id:int}")]
    [ExigePermissao(Permissoes.StockGerir)]
    public Task<ArtigoDto> EditarArtigo(int id, GuardarArtigo dados) => stock.EditarArtigoAsync(id, dados);

    [HttpPost("artigos/{id:int}/activo")]
    [ExigePermissao(Permissoes.StockGerir)]
    public async Task<IActionResult> ArtigoActivo(int id, AlterarActivo dados)
    {
        await stock.DefinirArtigoActivoAsync(id, dados.Activo);
        return NoContent();
    }

    [HttpGet("movimentos")]
    [ExigePermissao(Permissoes.StockVer)]
    public Task<Pagina<MovimentoStockDto>> Movimentos(int? artigoId, int? armazemId, TipoMovimentoStock? tipo, DateTime? desdeUtc, DateTime? ateUtc,
        int pagina = 1, int tamanho = 50, CancellationToken ct = default) =>
        stock.ListarMovimentosAsync(new FiltroMovimentos(artigoId, armazemId, tipo, desdeUtc, ateUtc, pagina, tamanho), ct);

    [HttpPost("ajustes")]
    [ExigePermissao(Permissoes.StockAjustar)]
    public Task<ArtigoDto> Ajustar(AjustarStock dados) => stock.AjustarAsync(dados);

    [HttpPost("transferencias")]
    [ExigePermissao(Permissoes.StockGerir)]
    public Task<ArtigoDto> Transferir(TransferirStock dados) => stock.TransferirAsync(dados);

    [HttpGet("inventarios")]
    [ExigePermissao(Permissoes.StockVer)]
    public Task<IReadOnlyList<InventarioResumo>> Inventarios(CancellationToken ct) => stock.ListarInventariosAsync(ct);

    [HttpGet("inventarios/{id:int}")]
    [ExigePermissao(Permissoes.StockVer)]
    public Task<InventarioDto> Inventario(int id) => stock.ObterInventarioAsync(id);

    [HttpPost("inventarios")]
    [ExigePermissao(Permissoes.StockAjustar)]
    public Task<InventarioDto> CriarInventario(NovoInventario dados) => stock.CriarInventarioAsync(dados);

    [HttpPut("inventarios/{id:int}/contagem")]
    [ExigePermissao(Permissoes.StockAjustar)]
    public Task<InventarioDto> Contagem(int id, GuardarContagem dados) => stock.GuardarContagemAsync(id, dados);

    [HttpPost("inventarios/{id:int}/confirmar")]
    [ExigePermissao(Permissoes.StockAjustar)]
    public Task<InventarioDto> ConfirmarInventario(int id, ComVersaoSimples dados) => stock.ConfirmarInventarioAsync(id, dados.RowVersion);

    [HttpPost("inventarios/{id:int}/cancelar")]
    [ExigePermissao(Permissoes.StockAjustar)]
    public async Task<IActionResult> CancelarInventario(int id, ComVersaoSimples dados)
    {
        await stock.CancelarInventarioAsync(id, dados.RowVersion);
        return NoContent();
    }
}

[ApiController]
[Route("api/receitas")]
[ExigePermissao(Permissoes.ReceitasGerir)]
public class ReceitasController(ServicoReceitas receitas) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<ProdutoFicha>> Listar(string? pesquisa, CancellationToken ct) => receitas.ListarAsync(pesquisa, ct);

    [HttpGet("produtos/{produtoId:int}")]
    public Task<IReadOnlyList<ReceitaDto>> Versoes(int produtoId, CancellationToken ct) => receitas.VersoesAsync(produtoId, ct);

    [HttpPut("produtos/{produtoId:int}")]
    public Task<ReceitaDto> Guardar(int produtoId, GuardarReceita dados) => receitas.GuardarAsync(produtoId, dados);

    [HttpPost("produtos/{produtoId:int}/retirar")]
    public async Task<IActionResult> Retirar(int produtoId)
    {
        await receitas.RetirarAsync(produtoId);
        return NoContent();
    }
}

[ApiController]
[Route("api/fornecedores")]
public class FornecedoresController(ServicoCompras compras) : ControllerBase
{
    [HttpGet]
    [ExigePermissao(Permissoes.ComprasVer)]
    public Task<IReadOnlyList<FornecedorDto>> Listar(string? pesquisa, bool incluirInactivos = false, CancellationToken ct = default) =>
        compras.ListarFornecedoresAsync(pesquisa, incluirInactivos, ct);

    [HttpPost]
    [ExigePermissao(Permissoes.ComprasGerir)]
    public Task<FornecedorDto> Criar(GuardarFornecedor dados) => compras.GuardarFornecedorAsync(null, dados);

    [HttpPut("{id:int}")]
    [ExigePermissao(Permissoes.ComprasGerir)]
    public Task<FornecedorDto> Editar(int id, GuardarFornecedor dados) => compras.GuardarFornecedorAsync(id, dados);

    [HttpPost("{id:int}/activo")]
    [ExigePermissao(Permissoes.ComprasGerir)]
    public async Task<IActionResult> Activo(int id, AlterarActivo dados)
    {
        await compras.DefinirFornecedorActivoAsync(id, dados.Activo);
        return NoContent();
    }
}

[ApiController]
[Route("api/compras")]
public class ComprasController(ServicoCompras compras) : ControllerBase
{
    [HttpGet]
    [ExigePermissao(Permissoes.ComprasVer)]
    public Task<Pagina<CompraResumo>> Listar(int? fornecedorId, EstadoCompra? estado, DateOnly? desde, DateOnly? ate, string? pesquisa,
        int pagina = 1, int tamanho = 25, CancellationToken ct = default) =>
        compras.ListarAsync(new FiltroCompras(fornecedorId, estado, desde, ate, pesquisa, pagina, tamanho), ct);

    [HttpGet("{id:int}")]
    [ExigePermissao(Permissoes.ComprasVer)]
    public Task<CompraDto> Obter(int id) => compras.ObterAsync(id);

    [HttpPost]
    [ExigePermissao(Permissoes.ComprasGerir)]
    public Task<CompraDto> Criar(GuardarCompra dados) => compras.CriarRascunhoAsync(dados);

    [HttpPut("{id:int}")]
    [ExigePermissao(Permissoes.ComprasGerir)]
    public Task<CompraDto> Editar(int id, GuardarCompra dados) => compras.EditarRascunhoAsync(id, dados);

    [HttpPost("{id:int}/eliminar")]
    [ExigePermissao(Permissoes.ComprasGerir)]
    public async Task<IActionResult> Eliminar(int id, ComVersaoSimples dados)
    {
        await compras.EliminarRascunhoAsync(id, dados.RowVersion);
        return NoContent();
    }

    [HttpPost("{id:int}/confirmar")]
    [ExigePermissao(Permissoes.ComprasGerir)]
    public Task<CompraDto> Confirmar(int id, ComVersaoSimples dados) => compras.ConfirmarAsync(id, dados.RowVersion);

    [HttpPost("{id:int}/anular")]
    [ExigePermissao(Permissoes.ComprasAnular)]
    public Task<CompraDto> Anular(int id, AnularComMotivo dados) => compras.AnularAsync(id, dados.Motivo, dados.RowVersion);

    [HttpGet("resumo")]
    [ExigeAlgumaPermissao(Permissoes.ComprasVer, Permissoes.RelatoriosStock)]
    public Task<ResumoCompras> Resumo(DateOnly desde, DateOnly ate, CancellationToken ct) => compras.ResumoAsync(desde, ate, ct);
}
