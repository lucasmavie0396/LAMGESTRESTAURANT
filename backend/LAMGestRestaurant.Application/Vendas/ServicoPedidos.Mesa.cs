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
    /// Pedido de conta: o cliente quer ver o total antes de pagar. Só pedidos abertos com valor; fica no histórico.
    /// A conta não é recibo nem altera o pedido (pode continuar a receber itens e pede-se outra vez).
    /// </summary>
    public async Task<PedidoDto> PedirContaAsync(int id)
    {
        var pedido = await db.Pedidos.AsNoTracking().Include(p => p.Itens).FirstOrDefaultAsync(p => p.Id == id)
                     ?? throw new NaoEncontradoException("Pedido não encontrado.");
        await GarantirAcessoMesaAsync(pedido);
        if (pedido.Estado != EstadoPedido.Aberto)
            throw new RegraNegocioException($"O pedido {pedido.Numero} já está {DescreverEstado(pedido.Estado)}.");
        if (!pedido.Itens.Any(i => !i.Cancelado) || pedido.Total <= 0)
            throw new RegraNegocioException("O pedido ainda não tem produtos.");
        Historico(pedido, "conta", null, null, $"Total {pedido.Total:N2}");
        auditoria.Registar("pedido.conta", "Pedido", id, new { pedido.Numero, pedido.Total });
        await db.SaveChangesAsync();
        return await ObterAsync(id);
    }

    public async Task<PedidoDto> TransferirMesaAsync(int id, TransferirMesa dados)
    {
        if (!actual.TemPermissao(Permissoes.PosTransferirMesa))
            throw new AcessoNegadoException("Não tem permissão para transferir pedidos entre mesas.");
        await using var transacao = await db.Database.BeginTransactionAsync();
        var pedido = await CarregarAbertoAsync(id, dados.RowVersion);
        if (pedido.Tipo != TipoPedido.Mesa || pedido.MesaId is not int origemId)
            throw new RegraNegocioException("Só pedidos de mesa podem ser transferidos.");
        if (origemId == dados.MesaDestinoId)
            throw new RegraNegocioException("A mesa de destino é a mesma.");
        var origem = await db.Mesas.FirstAsync(m => m.Id == origemId);
        var destino = await PrepararMesaAsync(dados.MesaDestinoId, false);

        destino.OcupadaDesdeUtc = origem.OcupadaDesdeUtc ?? Agora;
        origem.Estado = EstadoMesa.Livre;
        origem.OcupadaDesdeUtc = null;
        pedido.MesaId = destino.Id;
        db.TransferenciasMesa.Add(new TransferenciaMesa
        {
            PedidoId = id, MesaOrigemId = origem.Id, MesaDestinoId = destino.Id,
            Motivo = Limpar(dados.Motivo), UtilizadorId = actual.Id!.Value, DataUtc = Agora,
        });
        Historico(pedido, "transferir-mesa", origem.Nome, destino.Nome, Limpar(dados.Motivo));
        auditoria.Registar("pedido.transferir-mesa", "Pedido", id, new { pedido.Numero, De = origem.Nome, Para = destino.Nome });
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (ex is not DbUpdateConcurrencyException)
        {
            throw new ConflitoException($"A mesa {destino.Nome} já tem um pedido aberto.");
        }
        await transacao.CommitAsync();
        await notificador.PedidoAlteradoAsync(id, true);
        await notificador.MesasAlteradasAsync();
        return await ObterAsync(id);
    }

    /// <summary>Passa a mesa a outro responsável (ex.: mudança de turno). Exige pos.mesas-outros; fica no histórico e na auditoria.</summary>
    public async Task<PedidoDto> PassarResponsavelAsync(int id, PassarResponsavel dados)
    {
        if (!VeMesasDeOutros)
            throw new AcessoNegadoException("Não tem permissão para passar mesas a outro responsável.");
        await using var transacao = await db.Database.BeginTransactionAsync();
        var pedido = await CarregarAbertoAsync(id, dados.RowVersion);
        if (pedido.Tipo != TipoPedido.Mesa)
            throw new RegraNegocioException("Só os pedidos de mesa têm responsável.");
        if (pedido.UtilizadorResponsavelId == dados.UtilizadorId)
            throw new RegraNegocioException("Este utilizador já é o responsável da mesa.");
        if (!(await utilizadores.TodosAsync()).Any(u => u.Id == dados.UtilizadorId))
            throw new RegraNegocioException("Utilizador inexistente ou inactivo.");
        var nomes = await utilizadores.NomesAsync(pedido.UtilizadorResponsavelId is int a ? [a, dados.UtilizadorId] : [dados.UtilizadorId]);
        var anterior = pedido.UtilizadorResponsavelId is int ant ? nomes.GetValueOrDefault(ant) : null;
        var novo = nomes.GetValueOrDefault(dados.UtilizadorId);
        pedido.UtilizadorResponsavelId = dados.UtilizadorId;
        Historico(pedido, "responsavel", anterior, novo, null);
        auditoria.Registar("pedido.responsavel", "Pedido", id, new { pedido.Numero, De = anterior, Para = novo });
        await db.SaveChangesAsync();
        await transacao.CommitAsync();
        await notificador.PedidoAlteradoAsync(id, false);
        await notificador.MesasAlteradasAsync();
        return await ObterAsync(id);
    }

    public async Task<PedidoDto> CancelarAsync(int id, ComMotivo dados)
    {
        if (!actual.TemPermissao(Permissoes.PosCancelarPedido))
            throw new AcessoNegadoException("Não tem permissão para cancelar pedidos.");
        await using var transacao = await db.Database.BeginTransactionAsync();
        var pedido = await CarregarAbertoAsync(id, dados.RowVersion);
        if (await db.Pagamentos.AnyAsync(p => p.PedidoId == id && p.Estado == EstadoPagamento.Valido))
            throw new RegraNegocioException("O pedido tem pagamentos registados. Anule-os antes de cancelar o pedido.");

        var motivo = dados.Motivo.Trim();
        var enviados = pedido.Itens.Where(i => !i.Cancelado && i.EstadoCozinha != EstadoItemCozinha.NaoEnviado).Select(i => i.Id).ToList();
        foreach (var item in pedido.Itens.Where(i => !i.Cancelado))
        {
            item.Cancelado = true;
            item.CanceladoEmUtc = Agora;
            item.CanceladoPorId = actual.Id;
            item.MotivoCancelamento = motivo;
        }
        pedido.Estado = EstadoPedido.Cancelado;
        pedido.CanceladoEmUtc = Agora;
        pedido.CanceladoPorId = actual.Id;
        pedido.MotivoCancelamento = motivo;
        if (pedido.EstadoEntrega is not null) pedido.EstadoEntrega = EstadoEntrega.Cancelado;
        await LibertarMesaAsync(pedido);
        Historico(pedido, "cancelar", EstadoPedido.Aberto.ToString(), EstadoPedido.Cancelado.ToString(), motivo);
        auditoria.Registar("pedido.cancelar", "Pedido", id, new { pedido.Numero, Motivo = motivo, pedido.Subtotal });
        await db.SaveChangesAsync();
        await transacao.CommitAsync();
        if (enviados.Count > 0)
            impressao.Enfileirar(new Impressao.ImprimirTaloes(id, enviados, Impressao.TipoTalao.Anulacao, motivo));
        await notificador.PedidoAlteradoAsync(id, true);
        await notificador.MesasAlteradasAsync();
        return await ObterAsync(id);
    }

    /// <summary>Estado logístico de Take Away/Delivery (o cancelamento faz-se pelo cancelamento do pedido).</summary>
    public async Task<PedidoDto> ActualizarEntregaAsync(int id, ActualizarEntrega dados)
    {
        await using var transacao = await db.Database.BeginTransactionAsync();
        var pedido = await db.Pedidos.FirstOrDefaultAsync(p => p.Id == id) ?? throw new NaoEncontradoException("Pedido não encontrado.");
        db.VerificarVersao(pedido, dados.RowVersion);
        if (pedido.EstadoEntrega is not { } actualEstado || pedido.Estado == EstadoPedido.Cancelado)
            throw new RegraNegocioException("Este pedido não tem estado de entrega activo.");
        if (!TransicoesEntrega.TryGetValue(actualEstado, out var permitidos) || !permitidos.Contains(dados.Estado))
            throw new RegraNegocioException($"Não é possível passar de {actualEstado} para {dados.Estado}.");
        if (dados.Estado == EstadoEntrega.SaiuParaEntrega && pedido.Tipo != TipoPedido.Delivery)
            throw new RegraNegocioException("Só pedidos Delivery saem para entrega.");
        if (dados.EstafetaId is int estafeta)
        {
            if (!await db.Funcionarios.AnyAsync(f => f.Id == estafeta && f.Activo))
                throw new RegraNegocioException("Estafeta inexistente ou inactivo.");
            pedido.EstafetaId = estafeta;
        }
        pedido.EstadoEntrega = dados.Estado;
        Historico(pedido, "entrega", actualEstado.ToString(), dados.Estado.ToString(), null);
        await db.SaveChangesAsync();
        await transacao.CommitAsync();
        await notificador.PedidoAlteradoAsync(id, false);
        return await ObterAsync(id);
    }

    internal async Task LibertarMesaAsync(Pedido pedido)
    {
        if (pedido.MesaId is not int mesaId) return;
        var mesa = await db.Mesas.FirstAsync(m => m.Id == mesaId);
        mesa.Estado = EstadoMesa.Livre;
        mesa.OcupadaDesdeUtc = null;
    }

    private async Task<Mesa> PrepararMesaAsync(int? mesaId, bool confirmarReservada)
    {
        if (mesaId is not int id)
            throw new RegraNegocioException("Seleccione a mesa.");
        var mesa = await db.Mesas.FirstOrDefaultAsync(m => m.Id == id) ?? throw new RegraNegocioException("Mesa inexistente.");
        if (!mesa.Activo || mesa.Estado == EstadoMesa.Indisponivel)
            throw new RegraNegocioException($"A mesa {mesa.Nome} não está disponível.");
        if (mesa.Estado == EstadoMesa.Ocupada || await db.Pedidos.AnyAsync(p => p.MesaId == id && p.Estado == EstadoPedido.Aberto))
            throw new ConflitoException($"A mesa {mesa.Nome} já tem um pedido aberto. Abra esse pedido para acrescentar itens.");
        if (mesa.Estado == EstadoMesa.Reservada && !confirmarReservada)
            throw new RegraNegocioException($"A mesa {mesa.Nome} está reservada. Confirme para a ocupar mesmo assim.");
        mesa.Estado = EstadoMesa.Ocupada;
        mesa.OcupadaDesdeUtc = Agora;
        return mesa;
    }

    private async Task<int?> ResolverGarcomAsync(int? garcomId)
    {
        if (garcomId is int id)
        {
            if (!await db.Funcionarios.AnyAsync(f => f.Id == id && f.Activo))
                throw new RegraNegocioException("Garçom inexistente ou inactivo.");
            return id;
        }
        // Por omissão, o funcionário associado ao utilizador que cria o pedido.
        return await db.Funcionarios.Where(f => f.UtilizadorId == actual.Id && f.Activo).Select(f => (int?)f.Id).FirstOrDefaultAsync();
    }

    private async Task NotificarMesaSeAplicavel(Pedido pedido)
    {
        if (pedido.MesaId is not null) await notificador.MesasAlteradasAsync();
    }
}
