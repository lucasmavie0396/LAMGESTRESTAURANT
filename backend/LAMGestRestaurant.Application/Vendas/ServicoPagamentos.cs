using LAMGestRestaurant.Application.Comum;
using LAMGestRestaurant.Application.Seguranca;
using LAMGestRestaurant.Application.Stock;
using LAMGestRestaurant.Domain.Caixa;
using LAMGestRestaurant.Domain.Comum;
using LAMGestRestaurant.Domain.Sistema;
using LAMGestRestaurant.Domain.Vendas;
using Microsoft.EntityFrameworkCore;

namespace LAMGestRestaurant.Application.Vendas;

/// <summary>
/// Pagamentos de pedidos. Regras:
/// - idempotência: a mesma Idempotency-Key nunca gera dois pagamentos (verificação + índice único na BD);
/// - pagamentos parciais permitidos (vários métodos ou divisão por valor), nunca acima do saldo em dívida;
/// - valor recebido ≥ valor pago; troco só em métodos que o permitem; referência obrigatória onde exigida;
/// - o pagamento fica sempre associado a uma sessão de caixa aberta do operador;
/// - quando o saldo chega a zero o pedido fecha na mesma transacção: mesa libertada, recibo emitido
///   e stock baixado uma única vez.
/// </summary>
public class ServicoPagamentos(
    IAppDbContext db,
    IUtilizadorActual actual,
    IAuditoria auditoria,
    INotificador notificador,
    ServicoPedidos pedidos,
    ServicoBaixaStock stock,
    ServicoNumeracao numeracao,
    ConfiguracoesSistema config,
    IConsultaUtilizadores utilizadores,
    TimeProvider relogio)
{
    public async Task<(PedidoDto Pedido, bool Criado)> RegistarAsync(int pedidoId, RegistarPagamento dados, string? chaveIdempotencia)
    {
        var chave = ServicoPedidos.ValidarChave(chaveIdempotencia);
        if (await PedidoDaChaveAsync(chave) is int existente)
            return existente == pedidoId
                ? (await pedidos.ObterAsync(pedidoId), false)
                : throw new ConflitoException("Esta chave de pagamento já foi usada noutro pedido.");

        var agora = relogio.GetUtcNow().UtcDateTime;
        bool fechou;
        await using (var transacao = await db.Database.BeginTransactionAsync())
        {
            var pedido = await db.Pedidos.Include(p => p.Itens).FirstOrDefaultAsync(p => p.Id == pedidoId)
                         ?? throw new NaoEncontradoException("Pedido não encontrado.");
            if (pedido.Estado != EstadoPedido.Aberto)
                throw new RegraNegocioException($"O pedido {pedido.Numero} já está {(pedido.Estado == EstadoPedido.Pago ? "pago" : "cancelado")}.");
            if (!pedido.Itens.Any(i => !i.Cancelado) || pedido.Total <= 0)
                throw new RegraNegocioException("O pedido não tem valor a pagar.");

            var sessao = await db.SessoesCaixa.FirstOrDefaultAsync(s => s.Id == dados.SessaoCaixaId)
                         ?? throw new RegraNegocioException("Sessão de caixa inexistente.");
            if (sessao.Estado != EstadoSessaoCaixa.Aberta)
                throw new RegraNegocioException("A sessão de caixa está fechada. Abra o caixa para receber pagamentos.");
            if (sessao.UtilizadorAberturaId != actual.Id && !actual.TemPermissao(Permissoes.CaixaVerTodas))
                throw new AcessoNegadoException("A sessão de caixa indicada pertence a outro operador.");

            var metodo = await db.MetodosPagamento.FirstOrDefaultAsync(m => m.Id == dados.MetodoPagamentoId && m.Activo)
                         ?? throw new RegraNegocioException("Método de pagamento inexistente ou inactivo.");

            var emDivida = CalculoPedido.EmDivida(pedido);
            var valor = CalculoPedido.Arredondar(dados.Valor);
            var recebido = CalculoPedido.Arredondar(dados.ValorRecebido);
            if (valor > emDivida)
                throw new RegraNegocioException($"O valor excede o saldo em dívida ({emDivida:N2}).");
            if (recebido < valor)
                throw new RegraNegocioException("O valor recebido é inferior ao valor a pagar. Para pagar só uma parte, reduza o valor a pagar.");
            if (recebido > valor && !metodo.PermiteTroco)
                throw new RegraNegocioException($"{metodo.Nome} não permite troco: o valor recebido tem de ser igual ao valor a pagar.");
            var referencia = string.IsNullOrWhiteSpace(dados.Referencia) ? null : dados.Referencia.Trim();
            if (metodo.ExigeReferencia && referencia is null)
                throw new RegraNegocioException($"Indique a referência da transacção {metodo.Nome}.");

            var pagamento = new Pagamento
            {
                PedidoId = pedido.Id,
                SessaoCaixaId = sessao.Id,
                MetodoPagamentoId = metodo.Id,
                Valor = valor,
                ValorRecebido = recebido,
                Troco = recebido - valor,
                Referencia = referencia,
                DataUtc = agora,
                UtilizadorId = actual.Id!.Value,
                IdempotencyKey = chave,
            };
            db.Pagamentos.Add(pagamento);
            pedido.TotalPago += valor;
            pedido.AlteradoEmUtc = agora;
            pedidos.Historico(pedido, "pagamento", null, null, $"{metodo.Nome} {valor:N2}" + (pagamento.Troco > 0 ? $" (troco {pagamento.Troco:N2})" : ""));

            fechou = pedido.TotalPago == pedido.Total;
            if (fechou)
            {
                pedido.Estado = EstadoPedido.Pago;
                pedido.FechadoEmUtc = agora;
                pedido.SessaoCaixaId = sessao.Id;
                await pedidos.LibertarMesaAsync(pedido);
                await stock.BaixarVendaAsync(pedido);
                db.Recibos.Add(new Recibo
                {
                    Numero = await numeracao.ProximoAsync(ServicoNumeracao.SerieRecibos),
                    PedidoId = pedido.Id,
                    Total = pedido.Total,
                    EmitidoEmUtc = agora,
                    UtilizadorId = actual.Id!.Value,
                });
                pedidos.Historico(pedido, "fechar", EstadoPedido.Aberto.ToString(), EstadoPedido.Pago.ToString(), null);
            }

            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateException ex) when (ex is not DbUpdateConcurrencyException)
            {
                // Corrida com um reenvio do mesmo pagamento: o índice único da chave rejeitou o segundo.
                await transacao.RollbackAsync();
                if (await PedidoDaChaveAsync(chave) == pedidoId)
                    return (await pedidos.ObterAsync(pedidoId), false);
                throw;
            }
            auditoria.Registar("pagamento.registar", "Pagamento", pagamento.Id,
                new { Pedido = pedido.Numero, Metodo = metodo.Codigo, Valor = valor, Recebido = recebido, pagamento.Troco, SessaoCaixa = sessao.Id });
            if (fechou)
                auditoria.Registar("pedido.fechar", "Pedido", pedido.Id, new { pedido.Numero, pedido.Total, SessaoCaixa = sessao.Id });
            await db.SaveChangesAsync();
            await transacao.CommitAsync();
        }

        await notificador.PedidoAlteradoAsync(pedidoId, fechou);
        if (fechou) await notificador.MesasAlteradasAsync();
        return (await pedidos.ObterAsync(pedidoId), true);
    }

    /// <summary>
    /// Anula um pagamento parcial de um pedido ainda aberto, numa sessão de caixa aberta.
    /// Estornos de pedidos já fechados ainda não estão implementados (ver docs/TAREFAS.md).
    /// </summary>
    public async Task<PedidoDto> AnularAsync(int pagamentoId, string motivo)
    {
        if (!actual.TemPermissao(Permissoes.PagamentosAnular))
            throw new AcessoNegadoException("Não tem permissão para anular pagamentos.");
        int pedidoId;
        await using (var transacao = await db.Database.BeginTransactionAsync())
        {
            var pagamento = await db.Pagamentos.Include(p => p.SessaoCaixa).Include(p => p.Pedido)
                                .FirstOrDefaultAsync(p => p.Id == pagamentoId) ?? throw new NaoEncontradoException("Pagamento não encontrado.");
            if (pagamento.Estado == EstadoPagamento.Anulado)
                throw new RegraNegocioException("O pagamento já está anulado.");
            if (pagamento.Pedido.Estado != EstadoPedido.Aberto)
                throw new RegraNegocioException("O pedido já foi fechado. A anulação de pagamentos de pedidos fechados (estorno) ainda não está disponível.");
            if (pagamento.SessaoCaixa.Estado != EstadoSessaoCaixa.Aberta)
                throw new RegraNegocioException("A sessão de caixa deste pagamento já foi fechada.");

            var agora = relogio.GetUtcNow().UtcDateTime;
            pagamento.Estado = EstadoPagamento.Anulado;
            pagamento.AnuladoEmUtc = agora;
            pagamento.AnuladoPorId = actual.Id;
            pagamento.MotivoAnulacao = motivo.Trim();
            pagamento.Pedido.TotalPago -= pagamento.Valor;
            pagamento.Pedido.AlteradoEmUtc = agora;
            pedidos.Historico(pagamento.Pedido, "pagamento.anular", null, null, $"{pagamento.Valor:N2}: {pagamento.MotivoAnulacao}");
            auditoria.Registar("pagamento.anular", "Pagamento", pagamento.Id, new { Pedido = pagamento.Pedido.Numero, pagamento.Valor, Motivo = pagamento.MotivoAnulacao });
            await db.SaveChangesAsync();
            await transacao.CommitAsync();
            pedidoId = pagamento.PedidoId;
        }
        await notificador.PedidoAlteradoAsync(pedidoId, false);
        return await pedidos.ObterAsync(pedidoId);
    }

    /// <summary>Dados do recibo interno (não é factura fiscal certificada).</summary>
    public async Task<ReciboDto> ObterReciboAsync(int pedidoId)
    {
        var recibo = await db.Recibos.AsNoTracking().FirstOrDefaultAsync(r => r.PedidoId == pedidoId && !r.Anulado)
                     ?? throw new NaoEncontradoException("Este pedido ainda não tem recibo (só é emitido quando o pedido fica pago).");
        var pedido = await pedidos.ObterAsync(pedidoId);
        var operador = (await utilizadores.NomesAsync([recibo.UtilizadorId])).GetValueOrDefault(recibo.UtilizadorId);

        return new ReciboDto(
            recibo.Numero, pedido.Numero, recibo.EmitidoEmUtc,
            await config.TextoAsync(ChavesConfiguracao.NomeEstabelecimento, "LAMGest Restaurant"),
            Vazio(await config.TextoAsync(ChavesConfiguracao.Endereco)),
            Vazio(await config.TextoAsync(ChavesConfiguracao.Telefone)),
            Vazio(await config.TextoAsync(ChavesConfiguracao.Nuit)),
            pedido.MesaNome, pedido.Tipo, operador,
            [.. pedido.Itens.Where(i => !i.Cancelado).Select(i => new ReciboLinha(i.NomeProduto, i.Quantidade, i.PrecoUnitario, i.Total))],
            pedido.Subtotal, pedido.Desconto, pedido.Impostos, pedido.TaxaEntrega, pedido.Total,
            [.. pedido.Pagamentos.Where(p => p.Estado == EstadoPagamento.Valido)],
            await config.TextoAsync(ChavesConfiguracao.SimboloMoeda, "MT"),
            Vazio(await config.TextoAsync(ChavesConfiguracao.RodapeRecibo)));
    }

    private Task<int?> PedidoDaChaveAsync(string chave) =>
        db.Pagamentos.Where(p => p.IdempotencyKey == chave).Select(p => (int?)p.PedidoId).FirstOrDefaultAsync();

    private static string? Vazio(string texto) => string.IsNullOrWhiteSpace(texto) ? null : texto;
}
