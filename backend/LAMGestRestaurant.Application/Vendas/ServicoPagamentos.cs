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
///   e stock baixado uma única vez; é emitida a venda a dinheiro (VD), cujo número fica também no recibo do pedido.
/// </summary>
public class ServicoPagamentos(
    IAppDbContext db,
    IUtilizadorActual actual,
    IAuditoria auditoria,
    INotificador notificador,
    ServicoPedidos pedidos,
    ServicoBaixaStock stock,
    Caixa.ServicoCaixa caixa,
    Clientes.ServicoFidelizacao fidelizacao,
    ConfiguracoesSistema config,
    IConsultaUtilizadores utilizadores,
    Impressao.IFilaImpressao impressao,
    Facturacao.ServicoDocumentos documentos,
    TimeProvider relogio,
    Cartoes.ServicoCartoes cartoes)
{
    public const int MaximoLinhas = 10;

    /// <summary>Um só pagamento (um método).</summary>
    public Task<(PedidoDto Pedido, bool Criado)> RegistarAsync(int pedidoId, RegistarPagamento dados, string? chaveIdempotencia) =>
        RegistarVariosAsync(pedidoId,
            new RegistarPagamentos(dados.SessaoCaixaId, [new LinhaPagamento(dados.MetodoPagamentoId, dados.Valor, dados.ValorRecebido, dados.Referencia)]),
            chaveIdempotencia);

    /// <summary>
    /// Vários pagamentos (métodos diferentes ou o mesmo várias vezes) confirmados de uma vez, na mesma transacção:
    /// ou ficam todos registados ou nenhum. A soma não pode exceder o saldo em dívida; cada linha segue as regras do seu método.
    /// A Idempotency-Key fica na primeira linha; as seguintes usam «chave#2», «chave#3»… (únicas na BD).
    /// </summary>
    public async Task<(PedidoDto Pedido, bool Criado)> RegistarVariosAsync(int pedidoId, RegistarPagamentos dados, string? chaveIdempotencia)
    {
        var chave = ServicoPedidos.ValidarChave(chaveIdempotencia);
        if (dados.Linhas is not { Count: > 0 and <= MaximoLinhas })
            throw new RegraNegocioException("Indique entre 1 e 10 pagamentos.");
        if (dados.Linhas.Count > 1 && chave.Length > 61)
            throw new RegraNegocioException("Com vários pagamentos, o cabeçalho Idempotency-Key não pode exceder 61 caracteres.");
        if (await PedidoDaChaveAsync(chave) is int existente)
            return existente == pedidoId
                ? (await pedidos.ObterAsync(pedidoId), false)
                : throw new ConflitoException("Esta chave de pagamento já foi usada noutro pedido.");

        var agora = relogio.GetUtcNow().UtcDateTime;
        bool fechou;
        List<int> enviadosAoFechar = [];
        int terminalId;
        Domain.Facturacao.Documento? vendaDinheiro = null;
        await using (var transacao = await db.Database.BeginTransactionAsync())
        {
            var pedido = await db.Pedidos.Include(p => p.Itens).FirstOrDefaultAsync(p => p.Id == pedidoId)
                         ?? throw new NaoEncontradoException("Pedido não encontrado.");
            if (pedido.Estado != EstadoPedido.Aberto)
                throw new RegraNegocioException($"O pedido {pedido.Numero} já está {ServicoPedidos.DescreverEstado(pedido.Estado)}.");
            if (!pedido.Itens.Any(i => !i.Cancelado) || pedido.Total <= 0)
                throw new RegraNegocioException("O pedido não tem valor a pagar.");

            var sessao = await db.SessoesCaixa.FirstOrDefaultAsync(s => s.Id == dados.SessaoCaixaId)
                         ?? throw new RegraNegocioException("Sessão de caixa inexistente.");
            if (sessao.Estado != EstadoSessaoCaixa.Aberta)
                throw new RegraNegocioException("A sessão de caixa está fechada. Abra o caixa para receber pagamentos.");
            if (sessao.UtilizadorAberturaId != actual.Id && !actual.TemPermissao(Permissoes.CaixaVerTodas))
                throw new AcessoNegadoException("A sessão de caixa indicada pertence a outro operador.");
            terminalId = sessao.TerminalId;

            var idsMetodos = dados.Linhas.Select(l => l.MetodoPagamentoId).Distinct().ToList();
            var metodos = await db.MetodosPagamento.Where(m => idsMetodos.Contains(m.Id) && m.Activo).ToDictionaryAsync(m => m.Id);
            if (metodos.Count != idsMetodos.Count)
                throw new RegraNegocioException("Método de pagamento inexistente ou inactivo.");

            var emDivida = CalculoPedido.EmDivida(pedido);
            var linhas = dados.Linhas.Select(l => (Metodo: metodos[l.MetodoPagamentoId], Valor: CalculoPedido.Arredondar(l.Valor),
                Recebido: CalculoPedido.Arredondar(l.ValorRecebido), Referencia: string.IsNullOrWhiteSpace(l.Referencia) ? null : l.Referencia.Trim())).ToList();
            var soma = linhas.Sum(l => l.Valor);
            if (soma > emDivida)
                throw new RegraNegocioException(linhas.Count == 1
                    ? $"O valor excede o saldo em dívida ({emDivida:N2})."
                    : $"A soma dos pagamentos ({soma:N2}) excede o saldo em dívida ({emDivida:N2}).");

            // Fatura a Crédito: cobre o pedido todo, sozinha, e fica na conta corrente do cliente.
            var aCredito = linhas.Any(l => l.Metodo.Codigo == Facturacao.ServicoDocumentos.CodigoFacturaCredito);
            if (aCredito)
            {
                if (linhas.Count > 1 || pedido.TotalPago != 0 || soma != emDivida)
                    throw new RegraNegocioException("A Fatura a Crédito tem de cobrir o pedido todo e não se junta a outros pagamentos.");
                if (dados.ClienteId is int clienteId)
                {
                    pedido.ClienteId = clienteId;
                }
                if (pedido.ClienteId is null)
                    throw new RegraNegocioException("Escolha o cliente da Fatura a Crédito.");
            }

            var pagamentosNovos = new List<(Pagamento Pagamento, MetodoPagamento Metodo)>();
            foreach (var (l, n) in linhas.Select((l, i) => (l, i)))
            {
                var onde = linhas.Count == 1 ? "" : $" (pagamento {n + 1}: {l.Metodo.Nome})";
                if (l.Valor <= 0)
                    throw new RegraNegocioException($"O valor a pagar tem de ser positivo{onde}.");
                if (l.Recebido < l.Valor)
                    throw new RegraNegocioException($"O valor recebido é inferior ao valor a pagar{onde}. Para pagar só uma parte, reduza o valor a pagar.");
                if (l.Recebido > l.Valor && !l.Metodo.PermiteTroco)
                    throw new RegraNegocioException($"{l.Metodo.Nome} não permite troco: o valor recebido tem de ser igual ao valor a pagar.");
                if (l.Metodo.ExigeReferencia && l.Referencia is null)
                    throw new RegraNegocioException($"Indique a referência da transacção {l.Metodo.Nome}.");

                var pagamento = new Pagamento
                {
                    PedidoId = pedido.Id,
                    SessaoCaixaId = sessao.Id,
                    MetodoPagamentoId = l.Metodo.Id,
                    Valor = l.Valor,
                    ValorRecebido = l.Recebido,
                    Troco = l.Recebido - l.Valor,
                    Referencia = l.Referencia,
                    DataUtc = agora,
                    UtilizadorId = actual.Id!.Value,
                    IdempotencyKey = n == 0 ? chave : $"{chave}#{n + 1}",
                };
                db.Pagamentos.Add(pagamento);
                // Cartão Evento: abate no saldo do cartão lido (que fica como referência do pagamento).
                if (l.Metodo.Codigo == Cartoes.ServicoCartoes.CodigoMetodo)
                    pagamento.Referencia = await cartoes.DebitarAsync(l.Referencia, l.Valor, pagamento, pedido.Id, pedido.Numero);
                pagamentosNovos.Add((pagamento, l.Metodo));
                pedido.TotalPago += l.Valor;
                pedidos.Historico(pedido, "pagamento", null, null, $"{l.Metodo.Nome} {l.Valor:N2}" + (pagamento.Troco > 0 ? $" (troco {pagamento.Troco:N2})" : ""));
            }
            pedido.AlteradoEmUtc = agora;

            fechou = pedido.TotalPago == pedido.Total;
            if (fechou)
            {
                pedido.Estado = EstadoPedido.Pago;
                pedido.FechadoEmUtc = agora;
                pedido.SessaoCaixaId = sessao.Id;
                await pedidos.LibertarMesaAsync(pedido);
                // Venda ao balcão: os talões da cozinha/bar saem quando a venda fica paga.
                if (pedido.Tipo == TipoPedido.Balcao)
                    enviadosAoFechar = [.. pedidos.MarcarEnviados(pedido).Select(i => i.Id)];
                pedidos.FinalizarCozinha(pedido);
                await stock.BaixarVendaAsync(pedido);
                await fidelizacao.AcumularAsync(pedido);
                vendaDinheiro = aCredito ? await documentos.EmitirFacturaDoPedidoAsync(pedido, agora) : await documentos.EmitirVendaDinheiroAsync(pedido, agora);
                db.Recibos.Add(new Recibo
                {
                    Numero = vendaDinheiro.Numero,
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
            foreach (var (pagamento, metodo) in pagamentosNovos)
                auditoria.Registar("pagamento.registar", "Pagamento", pagamento.Id,
                    new { Pedido = pedido.Numero, Metodo = metodo.Codigo, pagamento.Valor, Recebido = pagamento.ValorRecebido, pagamento.Troco, SessaoCaixa = sessao.Id });
            if (fechou)
            {
                auditoria.Registar("pedido.fechar", "Pedido", pedido.Id, new { pedido.Numero, pedido.Total, SessaoCaixa = sessao.Id });
                auditoria.Registar("documento.emitir", "Documento", vendaDinheiro!.Id,
                    new { vendaDinheiro.Numero, vendaDinheiro.Tipo, vendaDinheiro.ClienteNome, vendaDinheiro.ClienteNuit, vendaDinheiro.Total, Pedido = pedido.Numero });
            }
            await db.SaveChangesAsync();
            await transacao.CommitAsync();
        }

        // Cada pagamento confirmado abre a gaveta da impressora de pagamentos do terminal (se tiver a opção ligada).
        impressao.Enfileirar(new Impressao.AbrirGaveta(terminalId));
        await notificador.PedidoAlteradoAsync(pedidoId, fechou);
        if (fechou)
        {
            await notificador.MesasAlteradasAsync();
            if (enviadosAoFechar.Count > 0)
                impressao.Enfileirar(new Impressao.ImprimirTaloes(pedidoId, enviadosAoFechar));
            if (!dados.ReciboNoDispositivo && await config.BoolAsync(ChavesConfiguracao.ImprimirReciboAoPagar, padrao: true))
                impressao.Enfileirar(new Impressao.ImprimirRecibo(pedidoId, terminalId));
        }
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
            var pagamento = await db.Pagamentos.Include(p => p.SessaoCaixa).Include(p => p.Pedido).Include(p => p.MetodoPagamento)
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
            if (pagamento.MetodoPagamento.Codigo == Cartoes.ServicoCartoes.CodigoMetodo)
                await cartoes.CreditarAsync(pagamento, Domain.Cartoes.TipoMovimentoCartao.AnulacaoPagamento, $"Pagamento anulado: {pagamento.MotivoAnulacao}");
            pedidos.Historico(pagamento.Pedido, "pagamento.anular", null, null, $"{pagamento.Valor:N2}: {pagamento.MotivoAnulacao}");
            auditoria.Registar("pagamento.anular", "Pagamento", pagamento.Id, new { Pedido = pagamento.Pedido.Numero, pagamento.Valor, Motivo = pagamento.MotivoAnulacao });
            await db.SaveChangesAsync();
            await transacao.CommitAsync();
            pedidoId = pagamento.PedidoId;
        }
        await notificador.PedidoAlteradoAsync(pedidoId, false);
        return await pedidos.ObterAsync(pedidoId);
    }

    /// <summary>
    /// Estorno total de um pedido pago. Nada do histórico é apagado nem alterado nas sessões já fechadas:
    /// - o valor é devolvido com um movimento «Reembolso» na sessão de caixa aberta do operador
    ///   (em dinheiro, não pode exceder o que está na gaveta);
    /// - o recibo fica anulado e é emitida a nota de crédito da VD; o stock é reposto com movimentos de estorno;
    /// - o pedido passa a Estornado e sai das vendas nos relatórios (aparece nos estornos).
    /// Estornos parciais não são suportados: para corrigir parte de um pedido, estorne-o e registe um novo.
    /// </summary>
    public async Task<PedidoDto> EstornarAsync(int pedidoId, EstornarPedido dados)
    {
        if (!actual.TemPermissao(Permissoes.PagamentosAnular))
            throw new AcessoNegadoException("Não tem permissão para estornar pedidos.");
        var motivo = dados.Motivo.Trim();
        await using (var transacao = await db.Database.BeginTransactionAsync())
        {
            var pedido = await db.Pedidos.Include(p => p.Itens).FirstOrDefaultAsync(p => p.Id == pedidoId)
                         ?? throw new NaoEncontradoException("Pedido não encontrado.");
            db.VerificarVersao(pedido, dados.RowVersion);
            if (pedido.Estado != EstadoPedido.Pago)
                throw new RegraNegocioException(pedido.Estado == EstadoPedido.Estornado
                    ? "O pedido já foi estornado."
                    : "Só pedidos pagos podem ser estornados. Para pedidos abertos, anule os pagamentos ou cancele o pedido.");
            if (await db.Pagamentos.AnyAsync(x => x.PedidoId == pedidoId && x.Estado == EstadoPagamento.Valido && x.MetodoPagamento.Codigo == Facturacao.ServicoDocumentos.CodigoFacturaCredito))
                throw new RegraNegocioException("Este pedido foi facturado a crédito: corrija-o com uma nota de crédito em Facturação.");

            var sessao = await db.SessoesCaixa.FirstOrDefaultAsync(s => s.Id == dados.SessaoCaixaId)
                         ?? throw new RegraNegocioException("Sessão de caixa inexistente.");
            caixa.GarantirSessaoOperavel(sessao);
            var metodo = await db.MetodosPagamento.FirstOrDefaultAsync(m => m.Id == dados.MetodoPagamentoId && m.Activo)
                         ?? throw new RegraNegocioException("Método de reembolso inexistente ou inactivo.");
            if (metodo.Codigo == Facturacao.ServicoDocumentos.CodigoFacturaCredito)
                throw new RegraNegocioException("Não se reembolsa com Fatura a Crédito: escolha outro método.");
            var valor = pedido.TotalPago;
            // Reembolso para o Cartão Evento: só se o pedido foi todo pago com cartões; cada cartão recebe o que pagou.
            if (metodo.Codigo == Cartoes.ServicoCartoes.CodigoMetodo)
            {
                var validos = await db.Pagamentos.Include(p => p.MetodoPagamento).Where(p => p.PedidoId == pedidoId && p.Estado == EstadoPagamento.Valido).ToListAsync();
                if (validos.Any(p => p.MetodoPagamento.Codigo != Cartoes.ServicoCartoes.CodigoMetodo))
                    throw new RegraNegocioException("Este pedido não foi todo pago com Cartão Evento: escolha outro método de reembolso.");
                foreach (var p in validos)
                    await cartoes.CreditarAsync(p, Domain.Cartoes.TipoMovimentoCartao.Estorno, $"Estorno {pedido.Numero}: {motivo}");
            }
            if (metodo.ContaNaGaveta && valor > await caixa.EsperadoNoMetodoAsync(sessao, metodo.Id))
                throw new RegraNegocioException($"Não há {metodo.Nome} suficiente na gaveta para reembolsar {valor:N2}.");

            var agora = relogio.GetUtcNow().UtcDateTime;
            db.MovimentosCaixa.Add(new MovimentoCaixa
            {
                SessaoCaixaId = sessao.Id,
                Tipo = TipoMovimentoCaixa.Reembolso,
                MetodoPagamentoId = metodo.Id,
                Valor = valor,
                Motivo = $"Estorno {pedido.Numero}: {motivo}",
                UtilizadorId = actual.Id!.Value,
                DataUtc = agora,
            });
            sessao.AlteradoEmUtc = agora;

            var recibo = await db.Recibos.FirstOrDefaultAsync(r => r.PedidoId == pedidoId && !r.Anulado);
            if (recibo is not null)
            {
                recibo.Anulado = true;
                recibo.AnuladoEmUtc = agora;
                recibo.AnuladoPorId = actual.Id;
                recibo.MotivoAnulacao = motivo;
            }
            var notaCredito = await documentos.CreditarVendaDinheiroAsync(pedido.Id, motivo, agora);
            await stock.EstornarVendaAsync(pedido, motivo);
            await fidelizacao.EstornarAsync(pedido);

            pedido.Estado = EstadoPedido.Estornado;
            pedido.CanceladoEmUtc = agora;
            pedido.CanceladoPorId = actual.Id;
            pedido.MotivoCancelamento = motivo;
            pedidos.Historico(pedido, "estornar", EstadoPedido.Pago.ToString(), EstadoPedido.Estornado.ToString(), $"{metodo.Nome} {valor:N2}: {motivo}");
            auditoria.Registar("pedido.estornar", "Pedido", pedido.Id,
                new { pedido.Numero, Valor = valor, Metodo = metodo.Codigo, SessaoCaixa = sessao.Id, Recibo = recibo?.Numero, NotaCredito = notaCredito?.Numero, Motivo = motivo });
            await db.SaveChangesAsync();
            await transacao.CommitAsync();
        }
        // afectaCozinha: os itens deste pedido saem do ecrã da cozinha.
        await notificador.PedidoAlteradoAsync(pedidoId, true);
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
