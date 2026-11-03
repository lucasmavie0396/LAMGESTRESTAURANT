using LAMGestRestaurant.Application.Comum;
using LAMGestRestaurant.Application.Seguranca;
using LAMGestRestaurant.Domain.Comum;
using LAMGestRestaurant.Domain.Sala;
using LAMGestRestaurant.Domain.Sistema;
using LAMGestRestaurant.Domain.Vendas;
using Microsoft.EntityFrameworkCore;

namespace LAMGestRestaurant.Application.Vendas;

/// <summary>
/// Pedidos do POS. Regras principais:
/// - preços, impostos e totais são sempre calculados aqui (CalculoPedido), nunca vindos do cliente;
/// - criação idempotente pelo cabeçalho Idempotency-Key; restantes alterações exigem a RowVersion lida,
///   pelo que um clique repetido com a versão antiga é recusado (409) em vez de duplicar itens;
/// - uma mesa só tem um pedido aberto (índice único na BD);
/// - itens já enviados à cozinha só se cancelam com permissão e motivo; nada é apagado depois de enviado.
/// </summary>
public class ServicoPedidos(
    IAppDbContext db,
    IUtilizadorActual actual,
    IAuditoria auditoria,
    INotificador notificador,
    ServicoNumeracao numeracao,
    ConfiguracoesSistema config,
    IConsultaUtilizadores utilizadores,
    Impressao.IFilaImpressao impressao,
    TimeProvider relogio)
{
    public const string MotivoRemovidoAntesEnvio = "Removido antes do envio";

    private DateTime Agora => relogio.GetUtcNow().UtcDateTime;

    // ---------- Consulta ----------

    public async Task<PedidoDto> ObterAsync(int id)
    {
        var p = await db.Pedidos.AsNoTracking()
            .Include(x => x.Itens)
            .Include(x => x.Mesa)
            .Include(x => x.Cliente)
            .Include(x => x.Garcom)
            .FirstOrDefaultAsync(x => x.Id == id) ?? throw new NaoEncontradoException("Pedido não encontrado.");
        var pagamentos = await db.Pagamentos.AsNoTracking().Where(x => x.PedidoId == id).OrderBy(x => x.DataUtc)
            .Select(x => new { x.Id, Metodo = x.MetodoPagamento.Nome, x.Valor, x.ValorRecebido, x.Troco, x.Referencia, x.Estado, x.DataUtc, x.UtilizadorId, x.MotivoAnulacao })
            .ToListAsync();
        var recibo = await db.Recibos.AsNoTracking().Where(r => r.PedidoId == id && !r.Anulado).Select(r => r.Numero).FirstOrDefaultAsync();
        var nomes = await utilizadores.NomesAsync(pagamentos.Select(x => x.UtilizadorId).Concat(p.CriadoPorId is int c ? [c] : Array.Empty<int>()));

        return new PedidoDto(
            p.Id, p.Numero, p.Tipo, p.Estado, p.EstadoEntrega, p.MesaId, p.Mesa?.Nome, p.ClienteId, p.Cliente?.Nome, p.GarcomId, p.Garcom?.Nome,
            p.NumeroPessoas, p.ContactoEntrega, p.EnderecoEntrega, p.Observacoes,
            p.Subtotal, p.Desconto, p.Impostos, p.TaxaEntrega, p.Total, p.TotalPago, CalculoPedido.EmDivida(p),
            p.CriadoEmUtc, p.CriadoPorId is int cp ? nomes.GetValueOrDefault(cp) : null, p.FechadoEmUtc, recibo, p.MotivoCancelamento,
            [.. p.Itens.OrderBy(i => i.Id).Select(i => new PedidoItemDto(i.Id, i.ProdutoId, i.NomeProduto, i.PrecoUnitario, i.Quantidade, i.Total,
                i.Observacoes, i.EnviaCozinha, i.EstadoCozinha, i.EnviadoCozinhaEmUtc, i.Cancelado, i.MotivoCancelamento))],
            [.. pagamentos.Select(x => new PagamentoDto(x.Id, x.Metodo, x.Valor, x.ValorRecebido, x.Troco, x.Referencia, x.Estado, x.DataUtc,
                nomes.GetValueOrDefault(x.UtilizadorId), x.MotivoAnulacao))],
            p.RowVersion);
    }

    public async Task<Pagina<PedidoResumo>> ListarAsync(FiltroPedidos f, CancellationToken ct)
    {
        var (pagina, tamanho) = Pagina<PedidoResumo>.Normalizar(f.Pagina, f.Tamanho);
        var q = db.Pedidos.AsNoTracking();
        if (f.Estado is { } estado) q = q.Where(p => p.Estado == estado);
        if (f.Tipo is { } tipo) q = q.Where(p => p.Tipo == tipo);
        if (f.MesaId is int mesa) q = q.Where(p => p.MesaId == mesa);
        if (f.EntregaActiva)
            q = q.Where(p => p.EstadoEntrega != null && p.EstadoEntrega != EstadoEntrega.Entregue && p.EstadoEntrega != EstadoEntrega.Cancelado
                             && p.Estado != EstadoPedido.Cancelado && p.Estado != EstadoPedido.Estornado);
        if (f.DesdeUtc is DateTime desde) q = q.Where(p => p.CriadoEmUtc >= desde);
        if (f.AteUtc is DateTime ate) q = q.Where(p => p.CriadoEmUtc < ate);
        if (!string.IsNullOrWhiteSpace(f.Pesquisa))
        {
            var s = f.Pesquisa.Trim();
            q = q.Where(p => p.Numero.Contains(s) || (p.ContactoEntrega != null && p.ContactoEntrega.Contains(s)) || (p.Mesa != null && p.Mesa.Nome == s));
        }
        var total = await q.CountAsync(ct);
        var itens = await q.OrderByDescending(p => p.CriadoEmUtc).ThenByDescending(p => p.Id)
            .Skip((pagina - 1) * tamanho).Take(tamanho)
            .Select(p => new PedidoResumo(p.Id, p.Numero, p.Tipo, p.Estado, p.EstadoEntrega, p.MesaId, p.Mesa != null ? p.Mesa.Nome : null,
                p.ContactoEntrega, p.EnderecoEntrega, p.Total, p.TotalPago,
                p.Itens.Count(i => !i.Cancelado),
                p.Itens.Count(i => !i.Cancelado && i.EstadoCozinha == EstadoItemCozinha.NaoEnviado),
                p.Itens.Count(i => !i.Cancelado && i.EstadoCozinha == EstadoItemCozinha.Pronto),
                p.CriadoEmUtc))
            .ToListAsync(ct);
        return new Pagina<PedidoResumo>(itens, total, pagina, tamanho);
    }

    // ---------- Criação ----------

    public async Task<(PedidoDto Pedido, bool Criado)> CriarAsync(NovoPedido dados, string? chaveIdempotencia)
    {
        var chave = ValidarChave(chaveIdempotencia);
        if (await db.Pedidos.Where(p => p.IdempotencyKey == chave).Select(p => (int?)p.Id).FirstOrDefaultAsync() is int existente)
            return (await ObterAsync(existente), false);

        var pedido = new Pedido
        {
            Tipo = dados.Tipo,
            NumeroPessoas = dados.NumeroPessoas,
            Observacoes = Limpar(dados.Observacoes),
            IdempotencyKey = chave,
        };

        await using var transacao = await db.Database.BeginTransactionAsync();
        Mesa? mesa = null;
        switch (dados.Tipo)
        {
            case TipoPedido.Mesa:
                mesa = await PrepararMesaAsync(dados.MesaId, dados.ConfirmarMesaReservada);
                pedido.MesaId = mesa.Id;
                break;
            case TipoPedido.Delivery:
                if (string.IsNullOrWhiteSpace(dados.ContactoEntrega) || string.IsNullOrWhiteSpace(dados.EnderecoEntrega))
                    throw new RegraNegocioException("Delivery: indique o contacto e o endereço de entrega.");
                pedido.TaxaEntrega = CalculoPedido.Arredondar(await config.DecimalAsync(ChavesConfiguracao.TaxaEntregaPadrao));
                goto default;
            default:
                if (dados.MesaId is not null)
                    throw new RegraNegocioException("Pedidos Take Away e Delivery não são associados a mesas.");
                pedido.EstadoEntrega = EstadoEntrega.Recebido;
                pedido.ContactoEntrega = Limpar(dados.ContactoEntrega);
                pedido.EnderecoEntrega = Limpar(dados.EnderecoEntrega);
                break;
        }
        if (dados.ClienteId is int clienteId && !await db.Clientes.AnyAsync(c => c.Id == clienteId && c.Activo))
            throw new RegraNegocioException("Cliente inexistente ou inactivo.");
        pedido.ClienteId = dados.ClienteId;
        pedido.GarcomId = await ResolverGarcomAsync(dados.GarcomId);

        pedido.Numero = await numeracao.ProximoAsync(ServicoNumeracao.SeriePedidos);
        await AdicionarItensInterno(pedido, dados.Itens ?? []);
        CalculoPedido.Recalcular(pedido);
        db.Pedidos.Add(pedido);
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException) when (mesa is not null)
        {
            throw new ConflitoException($"A mesa {mesa.Nome} já tem um pedido aberto.");
        }
        Historico(pedido, "criar", null, EstadoPedido.Aberto.ToString(), $"{pedido.Tipo}{(mesa is null ? "" : $" · mesa {mesa.Nome}")}");
        auditoria.Registar("pedido.criar", "Pedido", pedido.Id, new { pedido.Numero, pedido.Tipo, Mesa = mesa?.Nome, pedido.Total });
        await db.SaveChangesAsync();
        await transacao.CommitAsync();

        await notificador.PedidoAlteradoAsync(pedido.Id, false);
        if (mesa is not null) await notificador.MesasAlteradasAsync();
        return (await ObterAsync(pedido.Id), true);
    }

    // ---------- Itens ----------

    public async Task<PedidoDto> AdicionarItensAsync(int id, AdicionarItens dados)
    {
        await using var transacao = await db.Database.BeginTransactionAsync();
        var pedido = await CarregarAbertoAsync(id, dados.RowVersion);
        var novos = await AdicionarItensInterno(pedido, dados.Itens);
        CalculoPedido.Recalcular(pedido);
        await db.SaveChangesAsync();
        foreach (var i in novos)
            Historico(pedido, "item.adicionar", null, null, $"{i.Quantidade:0.###} × {i.NomeProduto}", i.Id);
        await db.SaveChangesAsync();
        await transacao.CommitAsync();
        await notificador.PedidoAlteradoAsync(id, false);
        await NotificarMesaSeAplicavel(pedido);
        return await ObterAsync(id);
    }

    public async Task<PedidoDto> AlterarItemAsync(int id, int itemId, AlterarItem dados)
    {
        if (!CalculoPedido.QuantidadeValida(dados.Quantidade))
            throw new RegraNegocioException("Quantidade inválida (positiva, até 999 e no máximo 3 casas decimais).");
        await using var transacao = await db.Database.BeginTransactionAsync();
        var pedido = await CarregarAbertoAsync(id, dados.RowVersion);
        var item = ItemDoPedido(pedido, itemId);
        if (item.EstadoCozinha != EstadoItemCozinha.NaoEnviado)
            throw new RegraNegocioException("O item já foi enviado. Para o alterar, cancele-o (com motivo) e adicione um novo.");
        var anterior = item.Quantidade;
        item.Quantidade = dados.Quantidade;
        item.Observacoes = Limpar(dados.Observacoes);
        CalculoPedido.Recalcular(pedido);
        Historico(pedido, "item.alterar", null, null, $"{item.NomeProduto}: {anterior:0.###} → {item.Quantidade:0.###}", item.Id);
        await db.SaveChangesAsync();
        await transacao.CommitAsync();
        await notificador.PedidoAlteradoAsync(id, false);
        await NotificarMesaSeAplicavel(pedido);
        return await ObterAsync(id);
    }

    /// <summary>
    /// Retira um item que ainda não foi enviado (sem permissão especial). Não se apaga a linha:
    /// fica cancelada com o motivo «Removido antes do envio», preservando o histórico.
    /// Itens já enviados exigem «Cancelar item» com permissão e motivo.
    /// </summary>
    public async Task<PedidoDto> RemoverItemAsync(int id, int itemId, byte[]? versao)
    {
        await using var transacao = await db.Database.BeginTransactionAsync();
        var pedido = await CarregarAbertoAsync(id, versao);
        var item = ItemDoPedido(pedido, itemId);
        if (item.Cancelado)
            throw new RegraNegocioException("O item já foi retirado.");
        if (item.EstadoCozinha != EstadoItemCozinha.NaoEnviado)
            throw new RegraNegocioException("O item já foi enviado. Use «Cancelar item» com o respectivo motivo.");
        item.Cancelado = true;
        item.CanceladoEmUtc = Agora;
        item.CanceladoPorId = actual.Id;
        item.MotivoCancelamento = MotivoRemovidoAntesEnvio;
        Historico(pedido, "item.remover", null, null, $"{item.Quantidade:0.###} × {item.NomeProduto} (não enviado)", item.Id);
        CalculoPedido.Recalcular(pedido);
        await db.SaveChangesAsync();
        await transacao.CommitAsync();
        await notificador.PedidoAlteradoAsync(id, false);
        await NotificarMesaSeAplicavel(pedido);
        return await ObterAsync(id);
    }

    public async Task<PedidoDto> CancelarItemAsync(int id, int itemId, ComMotivo dados)
    {
        if (!actual.TemPermissao(Permissoes.PosCancelarItem))
            throw new AcessoNegadoException("Não tem permissão para cancelar itens enviados.");
        await using var transacao = await db.Database.BeginTransactionAsync();
        var pedido = await CarregarAbertoAsync(id, dados.RowVersion);
        var item = ItemDoPedido(pedido, itemId);
        if (item.Cancelado)
            throw new RegraNegocioException("O item já está cancelado.");
        item.Cancelado = true;
        item.CanceladoEmUtc = Agora;
        item.CanceladoPorId = actual.Id;
        item.MotivoCancelamento = dados.Motivo.Trim();
        CalculoPedido.Recalcular(pedido);
        if (pedido.TotalPago > pedido.Total)
            throw new RegraNegocioException("O valor já pago excede o novo total. Anule primeiro um pagamento.");
        Historico(pedido, "item.cancelar", item.EstadoCozinha.ToString(), "Cancelado", $"{item.NomeProduto}: {item.MotivoCancelamento}", item.Id);
        auditoria.Registar("pedido.cancelar-item", "Pedido", id, new { pedido.Numero, item.NomeProduto, item.Quantidade, Motivo = item.MotivoCancelamento });
        await db.SaveChangesAsync();
        await transacao.CommitAsync();
        // O centro de produção já recebeu o talão: recebe agora a anulação.
        if (item.EstadoCozinha != EstadoItemCozinha.NaoEnviado)
            impressao.Enfileirar(new Impressao.ImprimirTaloes(id, [item.Id], Impressao.TipoTalao.Anulacao, item.MotivoCancelamento));
        await notificador.PedidoAlteradoAsync(id, item.EnviaCozinha && item.EstadoCozinha != EstadoItemCozinha.NaoEnviado);
        await NotificarMesaSeAplicavel(pedido);
        return await ObterAsync(id);
    }

    public async Task<PedidoDto> AplicarDescontoAsync(int id, AplicarDesconto dados)
    {
        if (!actual.TemPermissao(Permissoes.PosDesconto))
            throw new AcessoNegadoException("Não tem permissão para aplicar descontos.");
        if ((dados.Valor is null) == (dados.Percentagem is null))
            throw new RegraNegocioException("Indique o desconto em valor ou em percentagem (apenas um).");
        await using var transacao = await db.Database.BeginTransactionAsync();
        var pedido = await CarregarAbertoAsync(id, dados.RowVersion);
        var desconto = dados.Valor is decimal v
            ? CalculoPedido.Arredondar(v)
            : CalculoPedido.Arredondar(pedido.Subtotal * dados.Percentagem!.Value / 100m);
        if (desconto > pedido.Subtotal)
            throw new RegraNegocioException("O desconto não pode exceder o subtotal.");
        var anterior = pedido.Desconto;
        pedido.Desconto = desconto;
        CalculoPedido.Recalcular(pedido);
        if (pedido.TotalPago > pedido.Total)
            throw new RegraNegocioException("O valor já pago excede o novo total.");
        Historico(pedido, "desconto", anterior.ToString("0.00"), desconto.ToString("0.00"), dados.Percentagem is decimal pc ? $"{pc:0.##}%" : null);
        auditoria.Registar("pedido.desconto", "Pedido", id, new { pedido.Numero, Anterior = anterior, Novo = desconto, dados.Percentagem });
        await db.SaveChangesAsync();
        await transacao.CommitAsync();
        await notificador.PedidoAlteradoAsync(id, false);
        await NotificarMesaSeAplicavel(pedido);
        return await ObterAsync(id);
    }

    /// <summary>Associa (ou retira, com null) o cliente de um pedido aberto — necessário para a fidelização.</summary>
    public async Task<PedidoDto> AssociarClienteAsync(int id, int? clienteId, byte[]? versao)
    {
        await using var transacao = await db.Database.BeginTransactionAsync();
        var pedido = await CarregarAbertoAsync(id, versao);
        if (clienteId is int c && !await db.Clientes.AnyAsync(x => x.Id == c && x.Activo))
            throw new RegraNegocioException("Cliente inexistente ou inactivo.");
        var anterior = pedido.ClienteId;
        pedido.ClienteId = clienteId;
        Historico(pedido, "cliente", anterior?.ToString(), clienteId?.ToString(), null);
        await db.SaveChangesAsync();
        await transacao.CommitAsync();
        await notificador.PedidoAlteradoAsync(id, false);
        return await ObterAsync(id);
    }

    // ---------- Cozinha, mesas, entrega ----------

    /// <summary>Envia os itens ainda não enviados. Itens que não vão à cozinha (ex.: bebidas do balcão) ficam logo entregues.</summary>
    public async Task<PedidoDto> EnviarCozinhaAsync(int id, byte[]? versao)
    {
        await using var transacao = await db.Database.BeginTransactionAsync();
        var pedido = await CarregarAbertoAsync(id, versao);
        var porEnviar = pedido.Itens.Where(i => !i.Cancelado && i.EstadoCozinha == EstadoItemCozinha.NaoEnviado).ToList();
        if (porEnviar.Count == 0)
            throw new RegraNegocioException("Não há itens novos para enviar.");
        foreach (var item in porEnviar)
        {
            item.EnviadoCozinhaEmUtc = Agora;
            item.EstadoCozinha = item.EnviaCozinha ? EstadoItemCozinha.Pendente : EstadoItemCozinha.Entregue;
        }
        if (pedido.EstadoEntrega == EstadoEntrega.Recebido && porEnviar.Any(i => i.EnviaCozinha))
            pedido.EstadoEntrega = EstadoEntrega.EmPreparacao;
        pedido.AlteradoEmUtc = Agora;
        Historico(pedido, "enviar-cozinha", null, null, string.Join("; ", porEnviar.Select(i => $"{i.Quantidade:0.###} × {i.NomeProduto}")));
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
        if (pedido.Estado != EstadoPedido.Aberto)
            throw new RegraNegocioException("Só se reimprimem talões de pedidos abertos.");
        var itens = pedido.Itens.Where(i => !i.Cancelado && i.EstadoCozinha != EstadoItemCozinha.NaoEnviado).Select(i => i.Id).ToList();
        if (itens.Count == 0)
            throw new RegraNegocioException("O pedido ainda não tem itens enviados.");
        if (!await db.Impressoras.AnyAsync(i => i.Activo && i.Funcao != FuncaoImpressora.Caixa))
            throw new RegraNegocioException("Não há impressoras de produção configuradas (Configurações → Impressoras).");
        impressao.Enfileirar(new Impressao.ImprimirTaloes(id, itens, Impressao.TipoTalao.Reimpressao));
        auditoria.Registar("pedido.reimprimir-taloes", "Pedido", id, new { pedido.Numero, Itens = itens.Count });
        await db.SaveChangesAsync();
        return itens.Count;
    }

    /// <summary>
    /// Pedido de conta: o cliente quer ver o total antes de pagar. Só pedidos abertos com valor; fica no histórico.
    /// A conta não é recibo nem altera o pedido (pode continuar a receber itens e pede-se outra vez).
    /// </summary>
    public async Task<PedidoDto> PedirContaAsync(int id)
    {
        var pedido = await db.Pedidos.AsNoTracking().Include(p => p.Itens).FirstOrDefaultAsync(p => p.Id == id)
                     ?? throw new NaoEncontradoException("Pedido não encontrado.");
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

    private static readonly Dictionary<EstadoEntrega, EstadoEntrega[]> TransicoesEntrega = new()
    {
        [EstadoEntrega.Recebido] = [EstadoEntrega.EmPreparacao, EstadoEntrega.Pronto],
        [EstadoEntrega.EmPreparacao] = [EstadoEntrega.Pronto],
        [EstadoEntrega.Pronto] = [EstadoEntrega.SaiuParaEntrega, EstadoEntrega.Entregue],
        [EstadoEntrega.SaiuParaEntrega] = [EstadoEntrega.Entregue],
    };

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

    // ---------- Auxiliares (também usados pelos pagamentos) ----------

    internal async Task LibertarMesaAsync(Pedido pedido)
    {
        if (pedido.MesaId is not int mesaId) return;
        var mesa = await db.Mesas.FirstAsync(m => m.Id == mesaId);
        mesa.Estado = EstadoMesa.Livre;
        mesa.OcupadaDesdeUtc = null;
    }

    internal void Historico(Pedido pedido, string accao, string? anterior, string? novo, string? detalhes, int? itemId = null) =>
        db.HistoricoPedidos.Add(new PedidoHistorico
        {
            PedidoId = pedido.Id, PedidoItemId = itemId, Accao = accao, EstadoAnterior = anterior, EstadoNovo = novo,
            Detalhes = detalhes is { Length: > 1000 } ? detalhes[..1000] : detalhes,
            UtilizadorId = actual.Id, DataUtc = Agora,
        });

    private async Task<Pedido> CarregarAbertoAsync(int id, byte[]? versao)
    {
        var pedido = await db.Pedidos.Include(p => p.Itens).FirstOrDefaultAsync(p => p.Id == id)
                     ?? throw new NaoEncontradoException("Pedido não encontrado.");
        db.VerificarVersao(pedido, versao);
        if (pedido.Estado != EstadoPedido.Aberto)
            throw new RegraNegocioException($"O pedido {pedido.Numero} já está {DescreverEstado(pedido.Estado)}.");
        // Marca o pedido como alterado para que a rowversion avance mesmo quando só mudam os itens.
        pedido.AlteradoEmUtc = Agora;
        return pedido;
    }

    private async Task<List<PedidoItem>> AdicionarItensInterno(Pedido pedido, IReadOnlyList<NovoItem> itens)
    {
        if (itens.Count == 0) return [];
        var ids = itens.Select(i => i.ProdutoId).Distinct().ToList();
        var produtos = await db.Produtos.AsNoTracking().Include(p => p.TaxaImposto)
            .Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id);
        var novos = new List<PedidoItem>();
        foreach (var n in itens)
        {
            if (!produtos.TryGetValue(n.ProdutoId, out var produto) || !produto.Activo)
                throw new RegraNegocioException("Produto inexistente ou inactivo.");
            if (!produto.Disponivel)
                throw new RegraNegocioException($"{produto.Nome} está esgotado.");
            if (!CalculoPedido.QuantidadeValida(n.Quantidade))
                throw new RegraNegocioException($"Quantidade inválida para {produto.Nome}.");
            var item = new PedidoItem
            {
                ProdutoId = produto.Id,
                NomeProduto = produto.Nome,
                PrecoUnitario = produto.PrecoVenda,
                TaxaImpostoPercentagem = produto.TaxaImposto?.Percentagem ?? 0m,
                Quantidade = n.Quantidade,
                Observacoes = Limpar(n.Observacoes),
                EnviaCozinha = produto.EnviaCozinha,
            };
            pedido.Itens.Add(item);
            novos.Add(item);
        }
        return novos;
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

    private static PedidoItem ItemDoPedido(Pedido pedido, int itemId) =>
        pedido.Itens.FirstOrDefault(i => i.Id == itemId) ?? throw new NaoEncontradoException("Item não encontrado neste pedido.");

    private async Task NotificarMesaSeAplicavel(Pedido pedido)
    {
        if (pedido.MesaId is not null) await notificador.MesasAlteradasAsync();
    }

    internal static string ValidarChave(string? chave)
    {
        if (string.IsNullOrWhiteSpace(chave) || chave.Length is < 8 or > 64)
            throw new RegraNegocioException("Cabeçalho Idempotency-Key em falta ou inválido (8 a 64 caracteres).");
        return chave.Trim();
    }

    private static string? Limpar(string? texto) => string.IsNullOrWhiteSpace(texto) ? null : texto.Trim();

    internal static string DescreverEstado(EstadoPedido estado) => estado switch
    {
        EstadoPedido.Pago => "pago",
        EstadoPedido.Estornado => "estornado",
        EstadoPedido.Cancelado => "cancelado",
        _ => "aberto",
    };
}
