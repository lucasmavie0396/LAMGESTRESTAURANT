using LAMGestRestaurant.Api.Seguranca;
using LAMGestRestaurant.Application.Comum;
using LAMGestRestaurant.Application.Cozinha;
using LAMGestRestaurant.Application.Seguranca;
using LAMGestRestaurant.Application.Vendas;
using LAMGestRestaurant.Domain.Comum;
using Microsoft.AspNetCore.Mvc;

namespace LAMGestRestaurant.Api.Controllers;

public record MotivoSimples(
    [System.ComponentModel.DataAnnotations.Required(ErrorMessage = "Indique o motivo.")]
    [System.ComponentModel.DataAnnotations.StringLength(1000, MinimumLength = 3, ErrorMessage = "O motivo deve ter entre 3 e 1000 caracteres.")]
    string Motivo);

[ApiController]
[Route("api/pedidos")]
public class PedidosController(ServicoPedidos pedidos, ServicoPagamentos pagamentos) : ControllerBase
{
    private const string CabecalhoIdempotencia = "Idempotency-Key";

    [HttpGet]
    [ExigeAlgumaPermissao(Permissoes.PosUsar, Permissoes.PagamentosRegistar, Permissoes.DeliveryGerir, Permissoes.RelatoriosVendas)]
    public Task<Pagina<PedidoResumo>> Listar(
        EstadoPedido? estado, TipoPedido? tipo, int? mesaId, DateTime? desdeUtc, DateTime? ateUtc, string? pesquisa,
        bool entregaActiva = false, int pagina = 1, int tamanho = 50, CancellationToken ct = default) =>
        pedidos.ListarAsync(new FiltroPedidos(estado, tipo, mesaId, desdeUtc, ateUtc, pesquisa, entregaActiva, pagina, tamanho), ct);

    [HttpGet("{id:int}")]
    [ExigeAlgumaPermissao(Permissoes.PosUsar, Permissoes.PagamentosRegistar, Permissoes.DeliveryGerir, Permissoes.RelatoriosVendas)]
    public Task<PedidoDto> Obter(int id) => pedidos.ObterAsync(id);

    /// <summary>Cria o pedido. Reenvios com o mesmo Idempotency-Key devolvem o pedido já criado (200).</summary>
    [HttpPost]
    [ExigePermissao(Permissoes.PosUsar)]
    public async Task<ActionResult<PedidoDto>> Criar(NovoPedido dados, [FromHeader(Name = CabecalhoIdempotencia)] string? chave)
    {
        var (pedido, criado) = await pedidos.CriarAsync(dados, chave);
        return criado ? CreatedAtAction(nameof(Obter), new { id = pedido.Id }, pedido) : Ok(pedido);
    }

    [HttpPost("{id:int}/itens")]
    [ExigePermissao(Permissoes.PosUsar)]
    public Task<PedidoDto> AdicionarItens(int id, AdicionarItens dados) => pedidos.AdicionarItensAsync(id, dados);

    [HttpPut("{id:int}/itens/{itemId:int}")]
    [ExigePermissao(Permissoes.PosUsar)]
    public Task<PedidoDto> AlterarItem(int id, int itemId, AlterarItem dados) => pedidos.AlterarItemAsync(id, itemId, dados);

    [HttpPost("{id:int}/itens/{itemId:int}/remover")]
    [ExigePermissao(Permissoes.PosUsar)]
    public Task<PedidoDto> RemoverItem(int id, int itemId, ComVersao dados) => pedidos.RemoverItemAsync(id, itemId, dados.RowVersion);

    [HttpPost("{id:int}/itens/{itemId:int}/cancelar")]
    [ExigePermissao(Permissoes.PosCancelarItem)]
    public Task<PedidoDto> CancelarItem(int id, int itemId, ComMotivo dados) => pedidos.CancelarItemAsync(id, itemId, dados);

    [HttpPost("{id:int}/desconto")]
    [ExigePermissao(Permissoes.PosDesconto)]
    public Task<PedidoDto> Desconto(int id, AplicarDesconto dados) => pedidos.AplicarDescontoAsync(id, dados);

    [HttpPost("{id:int}/enviar-cozinha")]
    [ExigePermissao(Permissoes.PosUsar)]
    public Task<PedidoDto> EnviarCozinha(int id, ComVersao dados) => pedidos.EnviarCozinhaAsync(id, dados.RowVersion);

    [HttpPost("{id:int}/transferir")]
    [ExigePermissao(Permissoes.PosTransferirMesa)]
    public Task<PedidoDto> Transferir(int id, TransferirMesa dados) => pedidos.TransferirMesaAsync(id, dados);

    [HttpPost("{id:int}/cancelar")]
    [ExigePermissao(Permissoes.PosCancelarPedido)]
    public Task<PedidoDto> Cancelar(int id, ComMotivo dados) => pedidos.CancelarAsync(id, dados);

    [HttpPost("{id:int}/entrega")]
    [ExigeAlgumaPermissao(Permissoes.DeliveryGerir, Permissoes.PosUsar)]
    public Task<PedidoDto> Entrega(int id, ActualizarEntrega dados) => pedidos.ActualizarEntregaAsync(id, dados);

    /// <summary>Regista um pagamento. Reenvios com o mesmo Idempotency-Key não duplicam o pagamento.</summary>
    [HttpPost("{id:int}/pagamentos")]
    [ExigePermissao(Permissoes.PagamentosRegistar)]
    public async Task<ActionResult<PedidoDto>> Pagar(int id, RegistarPagamento dados, [FromHeader(Name = CabecalhoIdempotencia)] string? chave)
    {
        var (pedido, criado) = await pagamentos.RegistarAsync(id, dados, chave);
        return criado ? StatusCode(StatusCodes.Status201Created, pedido) : Ok(pedido);
    }

    [HttpPost("{id:int}/estornar")]
    [ExigePermissao(Permissoes.PagamentosAnular)]
    public Task<PedidoDto> Estornar(int id, EstornarPedido dados) => pagamentos.EstornarAsync(id, dados);

    [HttpGet("{id:int}/recibo")]
    [ExigeAlgumaPermissao(Permissoes.PagamentosRegistar, Permissoes.PosUsar, Permissoes.RelatoriosVendas)]
    public Task<ReciboDto> Recibo(int id) => pagamentos.ObterReciboAsync(id);
}

[ApiController]
[Route("api/pagamentos")]
public class PagamentosController(ServicoPagamentos pagamentos) : ControllerBase
{
    [HttpPost("{id:int}/anular")]
    [ExigePermissao(Permissoes.PagamentosAnular)]
    public Task<PedidoDto> Anular(int id, MotivoSimples dados) => pagamentos.AnularAsync(id, dados.Motivo);
}

[ApiController]
[Route("api/cozinha")]
public class CozinhaController(ServicoCozinha cozinha) : ControllerBase
{
    [HttpGet]
    [ExigePermissao(Permissoes.CozinhaVer)]
    public Task<IReadOnlyList<TicketCozinha>> Listar(int? estacaoId, CancellationToken ct) => cozinha.ListarAsync(estacaoId, ct);

    [HttpPost("itens/{itemId:int}/estado")]
    [ExigePermissao(Permissoes.CozinhaActualizar)]
    public async Task<IActionResult> EstadoItem(int itemId, AlterarEstadoCozinha dados)
    {
        await cozinha.AlterarEstadoAsync(itemId, dados);
        return NoContent();
    }

    [HttpPost("pedidos/{pedidoId:int}/estado")]
    [ExigePermissao(Permissoes.CozinhaActualizar)]
    public async Task<IActionResult> EstadoPedido(int pedidoId, AlterarEstadoCozinha dados)
    {
        await cozinha.AlterarEstadoPedidoAsync(pedidoId, dados);
        return NoContent();
    }
}
