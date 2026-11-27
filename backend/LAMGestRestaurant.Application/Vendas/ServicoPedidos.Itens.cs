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
    public async Task<PedidoDto> AdicionarItensAsync(int id, AdicionarItens dados)
    {
        await using var transacao = await db.Database.BeginTransactionAsync();
        var pedido = await CarregarAbertoAsync(id, dados.RowVersion);
        var novos = await AdicionarItensInterno(pedido, dados.Itens);
        CalculoPedido.Recalcular(pedido);
        await db.SaveChangesAsync();
        foreach (var i in novos)
            Historico(pedido, "item.adicionar", null, null, $"{i.Quantidade:0.###} × {i.NomeProduto}{(i.Acompanhamentos is null ? "" : $" ({i.Acompanhamentos})")}", i.Id);
        await db.SaveChangesAsync();
        await transacao.CommitAsync();
        await notificador.PedidoAlteradoAsync(id, false);
        await NotificarMesaSeAplicavel(pedido);
        return await ObterAsync(id);
    }

    /// <summary>
    /// Comentário de um produto do pedido (ex.: «sem batata»). Antes do envio só se grava; depois do envio grava, fica no
    /// histórico e sai um talão de ALTERAÇÃO nos centros de produção desse produto.
    /// </summary>
    public async Task<PedidoDto> ComentarItemAsync(int id, int itemId, Comentario dados)
    {
        await using var transacao = await db.Database.BeginTransactionAsync();
        var pedido = await CarregarAbertoAsync(id, dados.RowVersion);
        var item = ItemDoPedido(pedido, itemId);
        if (item.Cancelado)
            throw new RegraNegocioException("O item está cancelado.");
        var texto = Limpar(dados.Texto);
        if (texto == item.Observacoes)
            throw new RegraNegocioException("O comentário não mudou.");
        var anterior = item.Observacoes;
        item.Observacoes = texto;
        var enviado = item.EstadoCozinha != EstadoItemCozinha.NaoEnviado;
        Historico(pedido, "item.comentario", anterior, texto, $"{item.NomeProduto}{(enviado ? " (já enviado)" : "")}", item.Id);
        await db.SaveChangesAsync();
        await transacao.CommitAsync();
        if (enviado)
            impressao.Enfileirar(new Impressao.ImprimirTaloes(id, [item.Id], Impressao.TipoTalao.Alteracao));
        await notificador.PedidoAlteradoAsync(id, enviado);
        return await ObterAsync(id);
    }

    /// <summary>Comentário do pedido inteiro (ex.: «cliente com pressa»): sai no fim dos talões seguintes.</summary>
    public async Task<PedidoDto> ComentarPedidoAsync(int id, Comentario dados)
    {
        await using var transacao = await db.Database.BeginTransactionAsync();
        var pedido = await CarregarAbertoAsync(id, dados.RowVersion);
        var texto = Limpar(dados.Texto);
        var anterior = pedido.Observacoes;
        pedido.Observacoes = texto;
        Historico(pedido, "comentario", anterior, texto, null);
        await db.SaveChangesAsync();
        await transacao.CommitAsync();
        await notificador.PedidoAlteradoAsync(id, false);
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

    private async Task<List<PedidoItem>> AdicionarItensInterno(Pedido pedido, IReadOnlyList<NovoItem> itens)
    {
        if (itens.Count == 0) return [];
        var ids = itens.Select(i => i.ProdutoId).Distinct().ToList();
        var produtos = await db.Produtos.AsNoTracking().Include(p => p.TaxaImposto)
            .Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id);
        var grupos = await acompanhamentos.DosProdutosAsync(ids);
        // Tabela de preço do pedido (zona da mesa ou retalho): o produto sem preço nessa tabela vende ao preço normal.
        var precosTabela = pedido.TabelaPrecoId is int tabela
            ? await db.PrecosProdutos.AsNoTracking().Where(p => p.TabelaPrecoId == tabela && ids.Contains(p.ProdutoId)).ToDictionaryAsync(p => p.ProdutoId, p => p.Preco)
            : [];
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
                PrecoUnitario = precosTabela.TryGetValue(produto.Id, out var precoTabela) ? precoTabela : produto.PrecoVenda,
                TaxaImpostoPercentagem = produto.TaxaImposto?.Percentagem ?? 0m,
                Quantidade = n.Quantidade,
                Observacoes = Limpar(n.Observacoes),
                EnviaCozinha = produto.EnviaCozinha,
            };
            var escolhidos = Catalogo.ServicoAcompanhamentos.Escolher(produto.Nome, grupos.GetValueOrDefault(produto.Id) ?? [], n.Acompanhamentos);
            if (escolhidos.Count > 0)
            {
                item.OpcoesAcompanhamento = escolhidos;
                item.Acompanhamentos = string.Join(", ", escolhidos.Select(e => e.Nome));
            }
            pedido.Itens.Add(item);
            novos.Add(item);
        }
        return novos;
    }

    /// <summary>
    /// Balcão: passa a venda para o preço de retalho (ou volta ao normal) e recalcula os itens já lançados. Só sem pagamentos,
    /// com a permissão pos.tabela-precos; fica no histórico do pedido e na auditoria.
    /// </summary>
    public async Task<PedidoDto> MudarTabelaPrecoAsync(int id, MudarTabelaPreco dados)
    {
        if (!actual.TemPermissao(Permissoes.PosTabelaPrecos))
            throw new AcessoNegadoException("Não tem permissão para mudar para o preço de retalho.");
        await using var transacao = await db.Database.BeginTransactionAsync();
        var pedido = await CarregarAbertoAsync(id, dados.RowVersion);
        if (pedido.Tipo != TipoPedido.Balcao)
            throw new RegraNegocioException("O preço de retalho só se usa nas vendas ao balcão.");
        if (pedido.TotalPago > 0 || await db.Pagamentos.AnyAsync(p => p.PedidoId == id && p.Estado == EstadoPagamento.Valido))
            throw new RegraNegocioException("A venda já tem pagamentos: o preço já não se pode mudar.");
        int? nova = dados.Retalho ? await TabelaRetalhoAsync() : null;
        if (nova != pedido.TabelaPrecoId)
        {
            var anterior = pedido.TabelaPrecoId;
            pedido.TabelaPrecoId = nova;
            var ids = pedido.Itens.Select(i => i.ProdutoId).Distinct().ToList();
            var normais = await db.Produtos.AsNoTracking().Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.PrecoVenda);
            var daTabela = nova is int t
                ? await db.PrecosProdutos.AsNoTracking().Where(p => p.TabelaPrecoId == t && ids.Contains(p.ProdutoId)).ToDictionaryAsync(p => p.ProdutoId, p => p.Preco)
                : [];
            foreach (var item in pedido.Itens.Where(i => !i.Cancelado))
                item.PrecoUnitario = daTabela.TryGetValue(item.ProdutoId, out var p) ? p : normais.GetValueOrDefault(item.ProdutoId, item.PrecoUnitario);
            CalculoPedido.Recalcular(pedido);
            pedido.AlteradoEmUtc = Agora;
            Historico(pedido, "tabela-precos", anterior?.ToString() ?? "normal", nova?.ToString() ?? "normal", dados.Retalho ? "Retalho" : "Normal");
            auditoria.Registar("pedido.tabela-precos", "Pedido", id, new { pedido.Numero, Retalho = dados.Retalho, pedido.Total });
            await db.SaveChangesAsync();
        }
        await transacao.CommitAsync();
        await notificador.PedidoAlteradoAsync(id, false);
        return await ObterAsync(id);
    }

    /// <summary>Tabela de preço activa da zona (vazio = preço normal).</summary>
    private async Task<int?> TabelaDaZonaAsync(int zonaId) =>
        await db.Zonas.AsNoTracking().Where(z => z.Id == zonaId && z.TabelaPreco != null && z.TabelaPreco.Activo).Select(z => z.TabelaPrecoId).FirstOrDefaultAsync();

    /// <summary>Tabela do retalho (Configurações → POS), activa; exige a permissão pos.tabela-precos.</summary>
    private async Task<int> TabelaRetalhoAsync()
    {
        if (!actual.TemPermissao(Permissoes.PosTabelaPrecos))
            throw new AcessoNegadoException("Não tem permissão para vender ao preço de retalho.");
        var valor = await config.TextoAsync(ChavesConfiguracao.PosTabelaRetalho);
        if (!int.TryParse(valor, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var id)
            || !await db.TabelasPreco.AnyAsync(t => t.Id == id && t.Activo))
            throw new RegraNegocioException("Não há tabela de preço de retalho activa (Configurações → Documentos e POS).");
        return id;
    }

    private static PedidoItem ItemDoPedido(Pedido pedido, int itemId) =>
        pedido.Itens.FirstOrDefault(i => i.Id == itemId) ?? throw new NaoEncontradoException("Item não encontrado neste pedido.");
}
