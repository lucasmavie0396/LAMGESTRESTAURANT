using LAMGestRestaurant.Application.Comum;
using LAMGestRestaurant.Application.Seguranca;
using LAMGestRestaurant.Domain.Comum;
using LAMGestRestaurant.Domain.Sala;
using LAMGestRestaurant.Domain.Sistema;
using LAMGestRestaurant.Domain.Vendas;
using Microsoft.EntityFrameworkCore;

namespace LAMGestRestaurant.Application.Vendas;

public partial class ServicoPedidos
{
    public async Task<PedidoDto> ObterAsync(int id)
    {
        var p = await db.Pedidos.AsNoTracking()
            .Include(x => x.Itens).ThenInclude(i => i.Produto).ThenInclude(p => p.UnidadeMedida)
            .Include(x => x.Mesa)
            .Include(x => x.Cliente)
            .Include(x => x.Garcom)
            .Include(x => x.TabelaPreco)
            .FirstOrDefaultAsync(x => x.Id == id) ?? throw new NaoEncontradoException("Pedido não encontrado.");
        var pagamentos = await db.Pagamentos.AsNoTracking().Where(x => x.PedidoId == id).OrderBy(x => x.DataUtc)
            .Select(x => new { x.Id, Metodo = x.MetodoPagamento.Nome, x.Valor, x.ValorRecebido, x.Troco, x.Referencia, x.Estado, x.DataUtc, x.UtilizadorId, x.MotivoAnulacao })
            .ToListAsync();
        var recibo = await db.Recibos.AsNoTracking().Where(r => r.PedidoId == id && !r.Anulado).Select(r => r.Numero).FirstOrDefaultAsync();
        var nomes = await utilizadores.NomesAsync(pagamentos.Select(x => x.UtilizadorId).Concat(p.CriadoPorId is int c ? [c] : Array.Empty<int>())
            .Concat(p.UtilizadorResponsavelId is int r ? [r] : Array.Empty<int>()));

        return new PedidoDto(
            p.Id, p.Numero, p.Tipo, p.Estado, p.EstadoEntrega, p.MesaId, p.Mesa?.Nome, p.ClienteId, p.Cliente?.Nome, p.GarcomId, p.Garcom?.Nome,
            p.NumeroPessoas, p.ContactoEntrega, p.EnderecoEntrega, p.Observacoes,
            p.Subtotal, p.Desconto, p.Impostos, p.TaxaEntrega, p.Total, p.TotalPago, CalculoPedido.EmDivida(p),
            p.CriadoEmUtc, p.CriadoPorId is int cp ? nomes.GetValueOrDefault(cp) : null, p.FechadoEmUtc, recibo, p.MotivoCancelamento,
            [.. p.Itens.OrderBy(i => i.Id).Select(i => new PedidoItemDto(i.Id, i.ProdutoId, i.NomeProduto, i.PrecoUnitario, i.Quantidade, i.Total,
                i.Observacoes, i.EnviaCozinha, i.EstadoCozinha, i.EnviadoCozinhaEmUtc, i.Cancelado, i.MotivoCancelamento, i.Produto?.UnidadeMedida?.Codigo ?? "un", i.Acompanhamentos))],
            [.. pagamentos.Select(x => new PagamentoDto(x.Id, x.Metodo, x.Valor, x.ValorRecebido, x.Troco, x.Referencia, x.Estado, x.DataUtc,
                nomes.GetValueOrDefault(x.UtilizadorId), x.MotivoAnulacao))],
            p.RowVersion, p.NomeEntrega, p.UtilizadorResponsavelId,
            p.UtilizadorResponsavelId is int resp ? nomes.GetValueOrDefault(resp) : null, p.TabelaPrecoId, p.TabelaPreco?.Nome);
    }

    /// <summary>Pedido para o POS: nas mesas de outro responsável exige pos.mesas-outros.</summary>
    public async Task<PedidoDto> ObterParaPosAsync(int id)
    {
        var pedido = await db.Pedidos.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id) ?? throw new NaoEncontradoException("Pedido não encontrado.");
        await GarantirAcessoMesaAsync(pedido);
        return await ObterAsync(id);
    }

    /// <summary>
    /// Mesa (pedido aberto) de outro utilizador: só com pos.mesas-outros (aplica-se a ver, lançar, enviar, pedir a conta e transferir).
    /// Os pagamentos dependem só da permissão de registar pagamentos. Pedidos sem mesa não têm esta restrição.
    /// </summary>
    private async Task GarantirAcessoMesaAsync(Pedido pedido)
    {
        if (pedido.Tipo != TipoPedido.Mesa || pedido.Estado != EstadoPedido.Aberto || pedido.UtilizadorResponsavelId is not int responsavel
            || responsavel == actual.Id || VeMesasDeOutros)
            return;
        var nome = (await utilizadores.NomesAsync([responsavel])).GetValueOrDefault(responsavel) ?? "outro utilizador";
        var mesa = pedido.MesaId is int m ? await db.Mesas.Where(x => x.Id == m).Select(x => x.Nome).FirstOrDefaultAsync() : null;
        throw new AcessoNegadoException($"A mesa {mesa} foi aberta por {nome}. Não tem permissão para ver nem lançar em mesas de outros utilizadores.");
    }

    public async Task<Pagina<PedidoResumo>> ListarAsync(FiltroPedidos f, CancellationToken ct)
    {
        var (pagina, tamanho) = Pagina<PedidoResumo>.Normalizar(f.Pagina, f.Tamanho);
        var q = db.Pedidos.AsNoTracking();
        if (f.Estado is { } estado) q = q.Where(p => p.Estado == estado);
        if (f.Tipo is { } tipo) q = q.Where(p => p.Tipo == tipo);
        if (f.MesaId is int mesa) q = q.Where(p => p.MesaId == mesa);
        // Sem pos.mesas-outros, as mesas abertas de outros responsáveis não aparecem.
        if (!VeMesasDeOutros)
            q = q.Where(p => p.Tipo != TipoPedido.Mesa || p.Estado != EstadoPedido.Aberto || p.UtilizadorResponsavelId == null || p.UtilizadorResponsavelId == actual.Id);
        if (f.EntregaActiva)
            q = q.Where(p => p.EstadoEntrega != null && p.EstadoEntrega != EstadoEntrega.Entregue && p.EstadoEntrega != EstadoEntrega.Cancelado
                             && p.Estado != EstadoPedido.Cancelado && p.Estado != EstadoPedido.Estornado);
        if (f.DesdeUtc is DateTime desde) q = q.Where(p => p.CriadoEmUtc >= desde);
        if (f.AteUtc is DateTime ate) q = q.Where(p => p.CriadoEmUtc < ate);
        if (!string.IsNullOrWhiteSpace(f.Pesquisa))
        {
            var s = f.Pesquisa.Trim();
            q = q.Where(p => p.Numero.Contains(s) || (p.ContactoEntrega != null && p.ContactoEntrega.Contains(s)) || (p.NomeEntrega != null && p.NomeEntrega.Contains(s))
                             || (p.Mesa != null && p.Mesa.Nome == s));
        }
        var total = await q.CountAsync(ct);
        var itens = await q.OrderByDescending(p => p.CriadoEmUtc).ThenByDescending(p => p.Id)
            .Skip((pagina - 1) * tamanho).Take(tamanho)
            .Select(p => new PedidoResumo(p.Id, p.Numero, p.Tipo, p.Estado, p.EstadoEntrega, p.MesaId, p.Mesa != null ? p.Mesa.Nome : null,
                p.ContactoEntrega, p.EnderecoEntrega, p.Total, p.TotalPago,
                p.Itens.Count(i => !i.Cancelado),
                p.Itens.Count(i => !i.Cancelado && i.EstadoCozinha == EstadoItemCozinha.NaoEnviado),
                p.Itens.Count(i => !i.Cancelado && i.EstadoCozinha == EstadoItemCozinha.Pronto),
                p.CriadoEmUtc, p.NomeEntrega))
            .ToListAsync(ct);
        return new Pagina<PedidoResumo>(itens, total, pagina, tamanho);
    }
}
