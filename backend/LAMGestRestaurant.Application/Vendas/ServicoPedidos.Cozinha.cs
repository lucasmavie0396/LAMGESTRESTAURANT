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
    /// <summary>
    /// Conta paga: tudo o que estava no ecrã da cozinha fica finalizado (entregue) e sai do ecrã.
    /// Take Away/Delivery ainda por preparar passam a «Pronto», para seguirem na lista de entregas.
    /// </summary>
    internal void FinalizarCozinha(Pedido pedido)
    {
        var activos = pedido.Itens.Where(i => !i.Cancelado && i.EstadoCozinha is EstadoItemCozinha.Pendente or EstadoItemCozinha.EmPreparacao or EstadoItemCozinha.Pronto).ToList();
        foreach (var item in activos)
        {
            Historico(pedido, "cozinha", item.EstadoCozinha.ToString(), EstadoItemCozinha.Entregue.ToString(), $"{item.NomeProduto} (conta paga)", item.Id);
            item.EstadoCozinha = EstadoItemCozinha.Entregue;
        }
        if (activos.Count > 0 && pedido.EstadoEntrega is EstadoEntrega.Recebido or EstadoEntrega.EmPreparacao)
            pedido.EstadoEntrega = EstadoEntrega.Pronto;
    }

    /// <summary>Marca como enviados os itens ainda não enviados (quem chama grava e pede os talões depois de gravar).</summary>
    internal List<PedidoItem> MarcarEnviados(Pedido pedido)
    {
        var porEnviar = pedido.Itens.Where(i => !i.Cancelado && i.EstadoCozinha == EstadoItemCozinha.NaoEnviado).ToList();
        if (porEnviar.Count == 0)
            return porEnviar;
        foreach (var item in porEnviar)
        {
            item.EnviadoCozinhaEmUtc = Agora;
            item.EstadoCozinha = item.EnviaCozinha ? EstadoItemCozinha.Pendente : EstadoItemCozinha.Entregue;
        }
        if (pedido.EstadoEntrega == EstadoEntrega.Recebido && porEnviar.Any(i => i.EnviaCozinha))
            pedido.EstadoEntrega = EstadoEntrega.EmPreparacao;
        pedido.AlteradoEmUtc = Agora;
        Historico(pedido, "enviar-cozinha", null, null, string.Join("; ", porEnviar.Select(i => $"{i.Quantidade:0.###} × {i.NomeProduto}")));
        return porEnviar;
    }

    /// <summary>Envia os itens ainda não enviados. Itens que não vão à cozinha (ex.: bebidas do balcão) ficam logo entregues.</summary>
    public async Task<PedidoDto> EnviarCozinhaAsync(int id, byte[]? versao)
    {
        await using var transacao = await db.Database.BeginTransactionAsync();
        var pedido = await CarregarAbertoAsync(id, versao);
        var porEnviar = MarcarEnviados(pedido);
        if (porEnviar.Count == 0)
            throw new RegraNegocioException("Não há itens novos para enviar.");
        await db.SaveChangesAsync();
        await transacao.CommitAsync();
        // Talões nas impressoras de cozinha/bar, depois de gravado (uma falha de impressão não desfaz o envio).
        impressao.Enfileirar(new Impressao.ImprimirTaloes(id, [.. porEnviar.Select(i => i.Id)]));
        await notificador.PedidoAlteradoAsync(id, porEnviar.Any(i => i.EnviaCozinha));
        return await ObterAsync(id);
    }

    /// <summary>
    /// Volta a imprimir nos centros de produção os itens já enviados e não cancelados (ex.: talão perdido ou papel acabado).
    /// Devolve o número de itens enviados para impressão.
    /// </summary>
    public async Task<int> ReimprimirTaloesAsync(int id)
    {
        var pedido = await db.Pedidos.AsNoTracking().Include(p => p.Itens).FirstOrDefaultAsync(p => p.Id == id)
                     ?? throw new NaoEncontradoException("Pedido não encontrado.");
        await GarantirAcessoMesaAsync(pedido);
        if (pedido.Estado != EstadoPedido.Aberto)
            throw new RegraNegocioException("Só se reimprimem talões de pedidos abertos.");
        var itens = pedido.Itens.Where(i => !i.Cancelado && i.EstadoCozinha != EstadoItemCozinha.NaoEnviado).Select(i => i.Id).ToList();
        if (itens.Count == 0)
            throw new RegraNegocioException("O pedido ainda não tem itens enviados.");
        if (!await db.Impressoras.AnyAsync(i => i.Activo && i.Funcao != FuncaoImpressora.Caixa && i.Funcao != FuncaoImpressora.Etiquetas))
            throw new RegraNegocioException("Não há impressoras de produção configuradas (Configurações → Impressoras).");
        impressao.Enfileirar(new Impressao.ImprimirTaloes(id, itens, Impressao.TipoTalao.Reimpressao));
        auditoria.Registar("pedido.reimprimir-taloes", "Pedido", id, new { pedido.Numero, Itens = itens.Count });
        await db.SaveChangesAsync();
        return itens.Count;
    }
}
