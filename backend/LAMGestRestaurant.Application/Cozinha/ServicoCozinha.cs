using LAMGestRestaurant.Application.Comum;
using LAMGestRestaurant.Application.Vendas;
using LAMGestRestaurant.Domain.Comum;
using LAMGestRestaurant.Domain.Vendas;
using Microsoft.EntityFrameworkCore;

namespace LAMGestRestaurant.Application.Cozinha;

public record ItemCozinha(
    int Id,
    string Produto,
    decimal Quantidade,
    string? Observacoes,
    EstadoItemCozinha Estado,
    DateTime EnviadoEmUtc,
    int? EstacaoId,
    string? Estacao);

public record TicketCozinha(
    int PedidoId,
    string Numero,
    TipoPedido Tipo,
    string? Mesa,
    string? Observacoes,
    DateTime PrimeiroEnvioUtc,
    IReadOnlyList<ItemCozinha> Itens);

public record AlterarEstadoCozinha(EstadoItemCozinha EstadoActual, EstadoItemCozinha NovoEstado);

/// <summary>
/// Ecrã da cozinha (KDS). A cozinha só altera estados de preparação; não mexe em preços nem pagamentos.
/// Cada mudança indica o estado que o cozinheiro viu (EstadoActual): se outro posto já o alterou,
/// a operação é recusada (409) em vez de saltar estados.
/// </summary>
public class ServicoCozinha(IAppDbContext db, INotificador notificador, ServicoPedidos pedidos, TimeProvider relogio)
{
    private static readonly Dictionary<EstadoItemCozinha, EstadoItemCozinha[]> Transicoes = new()
    {
        [EstadoItemCozinha.Pendente] = [EstadoItemCozinha.EmPreparacao, EstadoItemCozinha.Pronto],
        [EstadoItemCozinha.EmPreparacao] = [EstadoItemCozinha.Pronto],
        [EstadoItemCozinha.Pronto] = [EstadoItemCozinha.Entregue],
    };

    public async Task<IReadOnlyList<TicketCozinha>> ListarAsync(int? estacaoId, CancellationToken ct)
    {
        var activos = new[] { EstadoItemCozinha.Pendente, EstadoItemCozinha.EmPreparacao, EstadoItemCozinha.Pronto };
        var linhas = await db.PedidoItens.AsNoTracking()
            // Só pedidos por pagar: a conta paga finaliza o pedido no ecrã.
            .Where(i => i.EnviaCozinha && !i.Cancelado && activos.Contains(i.EstadoCozinha)
                        && i.Pedido.Estado == EstadoPedido.Aberto
                        && (estacaoId == null || i.Produto.Categoria.EstacaoPreparacaoId == estacaoId))
            .Select(i => new
            {
                i.PedidoId, i.Pedido.Numero, i.Pedido.Tipo, Mesa = i.Pedido.Mesa != null ? i.Pedido.Mesa.Nome : null, PedidoObs = i.Pedido.Observacoes,
                Item = new ItemCozinha(i.Id, i.NomeProduto, i.Quantidade, i.Observacoes, i.EstadoCozinha, i.EnviadoCozinhaEmUtc!.Value,
                    i.Produto.Categoria.EstacaoPreparacaoId,
                    i.Produto.Categoria.EstacaoPreparacao != null ? i.Produto.Categoria.EstacaoPreparacao.Nome : null),
            })
            .ToListAsync(ct);

        return [.. linhas
            .GroupBy(l => new { l.PedidoId, l.Numero, l.Tipo, l.Mesa, l.PedidoObs })
            .Select(g => new TicketCozinha(g.Key.PedidoId, g.Key.Numero, g.Key.Tipo, g.Key.Mesa, g.Key.PedidoObs,
                g.Min(l => l.Item.EnviadoEmUtc), [.. g.Select(l => l.Item).OrderBy(i => i.EnviadoEmUtc).ThenBy(i => i.Id)]))
            .OrderBy(t => t.PrimeiroEnvioUtc)];
    }

    public async Task AlterarEstadoAsync(int itemId, AlterarEstadoCozinha dados) =>
        await AlterarAsync(db.PedidoItens.Where(i => i.Id == itemId), dados, unico: true);

    /// <summary>Avança todos os itens do pedido que estão no estado indicado (ex.: «tudo pronto»).</summary>
    public async Task AlterarEstadoPedidoAsync(int pedidoId, AlterarEstadoCozinha dados) =>
        await AlterarAsync(db.PedidoItens.Where(i => i.PedidoId == pedidoId && i.EnviaCozinha && !i.Cancelado && i.EstadoCozinha == dados.EstadoActual), dados, unico: false);

    private async Task AlterarAsync(IQueryable<PedidoItem> consulta, AlterarEstadoCozinha dados, bool unico)
    {
        if (!Transicoes.TryGetValue(dados.EstadoActual, out var permitidos) || !permitidos.Contains(dados.NovoEstado))
            throw new RegraNegocioException($"Transição inválida: {dados.EstadoActual} → {dados.NovoEstado}.");

        var agora = relogio.GetUtcNow().UtcDateTime;
        await using var transacao = await db.Database.BeginTransactionAsync();
        var itens = await consulta.Include(i => i.Pedido).ThenInclude(p => p.Itens).Include(i => i.Pedido.Mesa).ToListAsync();
        if (itens.Count == 0)
            throw unico ? new NaoEncontradoException("Item não encontrado.") : new ConflitoException("Não há itens nesse estado. Actualize o ecrã.");

        foreach (var item in itens)
        {
            if (!item.EnviaCozinha || item.Cancelado || item.Pedido.Estado is EstadoPedido.Cancelado or EstadoPedido.Estornado)
                throw new RegraNegocioException($"{item.NomeProduto} foi cancelado ou o pedido foi estornado.");
            if (item.EstadoCozinha != dados.EstadoActual)
                throw new ConflitoException($"{item.NomeProduto} já foi actualizado noutro posto. Actualize o ecrã.");
            item.EstadoCozinha = dados.NovoEstado;
            if (dados.NovoEstado == EstadoItemCozinha.Pronto) item.ProntoEmUtc = agora;
            item.Pedido.AlteradoEmUtc = agora;
            pedidos.Historico(item.Pedido, "cozinha", dados.EstadoActual.ToString(), dados.NovoEstado.ToString(), item.NomeProduto, item.Id);
        }

        // Take Away/Delivery: quando todos os itens da cozinha estão prontos, o pedido fica «Pronto».
        var pedido = itens[0].Pedido;
        if (pedido.EstadoEntrega is EstadoEntrega.Recebido or EstadoEntrega.EmPreparacao
            && pedido.Itens.Where(i => i.EnviaCozinha && !i.Cancelado && i.EstadoCozinha != EstadoItemCozinha.NaoEnviado)
                .All(i => i.EstadoCozinha is EstadoItemCozinha.Pronto or EstadoItemCozinha.Entregue))
            pedido.EstadoEntrega = EstadoEntrega.Pronto;

        await db.SaveChangesAsync();
        await transacao.CommitAsync();

        foreach (var grupo in itens.GroupBy(i => i.Pedido))
        {
            await notificador.PedidoAlteradoAsync(grupo.Key.Id, true);
            if (dados.NovoEstado == EstadoItemCozinha.Pronto)
                await notificador.ItensProntosAsync(grupo.Key.Id, grupo.Key.Numero, grupo.Key.Mesa?.Nome, [.. grupo.Select(i => $"{i.Quantidade:0.###} × {i.NomeProduto}")]);
        }
    }
}
