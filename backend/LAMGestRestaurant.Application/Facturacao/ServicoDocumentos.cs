using System.Globalization;
using LAMGestRestaurant.Application.Comum;
using LAMGestRestaurant.Application.Seguranca;
using LAMGestRestaurant.Application.Vendas;
using LAMGestRestaurant.Domain.Caixa;
using LAMGestRestaurant.Domain.Comum;
using LAMGestRestaurant.Domain.Facturacao;
using LAMGestRestaurant.Domain.Sistema;
using LAMGestRestaurant.Domain.Vendas;
using Microsoft.EntityFrameworkCore;

namespace LAMGestRestaurant.Application.Facturacao;

/// <summary>
/// Documentos comerciais e fiscais. Regras:
/// - numeração sequencial por tipo e ano, sem saltos (série atribuída dentro da transacção);
/// - emitidos nunca se eliminam nem se alteram: facturas e VD corrigem-se com notas de crédito;
///   recibos e cotações anulam-se com motivo;
/// - VD: emitida ao fechar o pedido no POS; a nota de crédito da VD é emitida pelo estorno do pedido;
/// - factura: a crédito, a um cliente da ficha; fica em dívida até ser liquidada por recibos (parciais ou totais);
///   notas de crédito manuais só sobre o valor ainda em dívida (para creditar o que já foi pago, anula-se o recibo);
/// - recibo em dinheiro (gaveta) entra na sessão de caixa aberta do operador como entrada;
/// - criação idempotente (cabeçalho Idempotency-Key).
/// </summary>
public class ServicoDocumentos(
    IAppDbContext db,
    IUtilizadorActual actual,
    IAuditoria auditoria,
    ServicoNumeracao numeracao,
    ConfiguracoesSistema config,
    IConsultaUtilizadores utilizadores,
    Caixa.ServicoCaixa caixa,
    TimeProvider relogio)
{
    public const string CodigoFacturaCredito = "FACTURA_CREDITO";
    public const string ConsumidorFinal = "Consumidor final";
    public const int MaximoLinhas = 200;

    // ---------------------------------------------------------------- POS (dentro da transacção do chamador)

    /// <summary>VD do pedido que acabou de ficar pago. O desconto do pedido é repartido pelas linhas; a taxa de entrega é uma linha sem IVA.</summary>
    internal Task<Documento> EmitirVendaDinheiroAsync(Pedido pedido, DateTime agora) => DoPedidoAsync(TipoDocumento.VendaDinheiro, pedido, agora);

    /// <summary>Pedido fechado com «Fatura a Crédito»: factura em dívida na conta corrente do cliente, com o prazo configurado.</summary>
    internal async Task<Documento> EmitirFacturaDoPedidoAsync(Pedido pedido, DateTime agora)
    {
        var factura = await DoPedidoAsync(TipoDocumento.Factura, pedido, agora);
        factura.Vencimento = (await config.HojeAsync(relogio)).AddDays(await InteiroAsync(ChavesConfiguracao.PrazoFacturaDias, 30));
        return factura;
    }

    private async Task<Documento> DoPedidoAsync(TipoDocumento tipo, Pedido pedido, DateTime agora)
    {
        var documento = await NovoAsync(tipo, agora);
        documento.PedidoId = pedido.Id;
        await PreencherClienteAsync(documento, pedido.ClienteId, null, null, null, exigirFicha: tipo == TipoDocumento.Factura);
        var isencao = await MotivoIsencaoAsync();

        var itens = pedido.Itens.Where(i => !i.Cancelado).OrderBy(i => i.Id).ToList();
        var idsProdutos = itens.Select(i => i.ProdutoId).Distinct().ToList();
        var codigos = await db.Produtos.Where(p => idsProdutos.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Codigo);
        var partes = CalculoDocumento.Repartir(pedido.Desconto, [.. itens.Select(i => i.Total)]);
        foreach (var (item, n) in itens.Select((item, n) => (item, n)))
        {
            var linha = new DocumentoLinha
            {
                Ordem = n + 1,
                ProdutoId = item.ProdutoId,
                Codigo = codigos.GetValueOrDefault(item.ProdutoId),
                Descricao = item.NomeProduto,
                Quantidade = item.Quantidade,
                PrecoUnitario = item.PrecoUnitario,
                Desconto = item.Desconto + partes[n],
                PercentagemIva = item.TaxaImpostoPercentagem,
                MotivoIsencao = item.TaxaImpostoPercentagem <= 0 ? isencao : null,
            };
            CalculoDocumento.CalcularLinha(linha);
            documento.Linhas.Add(linha);
        }
        if (pedido.TaxaEntrega > 0)
        {
            var entrega = new DocumentoLinha
            {
                Ordem = documento.Linhas.Count + 1, Descricao = "Taxa de entrega", Quantidade = 1, PrecoUnitario = pedido.TaxaEntrega,
                PercentagemIva = 0, MotivoIsencao = isencao,
            };
            CalculoDocumento.CalcularLinha(entrega);
            documento.Linhas.Add(entrega);
        }
        CalculoDocumento.Totalizar(documento);
        if (documento.Total != pedido.Total)
            throw new InvalidOperationException($"A VD do pedido {pedido.Numero} ({documento.Total:N2}) não confere com o total do pedido ({pedido.Total:N2}).");
        db.Documentos.Add(documento);
        return documento;
    }

    /// <summary>Nota de crédito total da VD do pedido estornado (pedidos antigos, só com recibo interno, não têm VD: devolve null).</summary>
    internal async Task<Documento?> CreditarVendaDinheiroAsync(int pedidoId, string motivo, DateTime agora)
    {
        var vd = await db.Documentos.Include(d => d.Linhas)
            .FirstOrDefaultAsync(d => d.PedidoId == pedidoId && d.Tipo == TipoDocumento.VendaDinheiro && d.Estado == EstadoDocumento.Emitido);
        if (vd is null)
            return null;
        var creditado = await CreditadoPorLinhaAsync(vd.Id);
        var linhas = vd.Linhas.Select(l => (l, l.Quantidade - creditado.GetValueOrDefault(l.Id).Quantidade)).Where(x => x.Item2 > 0).ToList();
        if (linhas.Count == 0)
            return null;
        return await ConstruirNotaCreditoAsync(vd, linhas, creditado, motivo, agora);
    }

    /// <summary>VD do pedido (para imprimir o documento fiscal em vez do recibo interno), ou null.</summary>
    public Task<int?> VendaDinheiroDoPedidoAsync(int pedidoId, CancellationToken ct = default) =>
        db.Documentos.Where(d => d.PedidoId == pedidoId && (d.Tipo == TipoDocumento.VendaDinheiro || d.Tipo == TipoDocumento.Factura))
            .OrderByDescending(d => d.Id).Select(d => (int?)d.Id).FirstOrDefaultAsync(ct);

    // ---------------------------------------------------------------- Facturas e cotações

    public Task<(DocumentoDto Documento, bool Criado)> EmitirFacturaAsync(NovoDocumento dados, string? chave) =>
        EmitirAsync(TipoDocumento.Factura, dados, chave);

    public Task<(DocumentoDto Documento, bool Criado)> EmitirCotacaoAsync(NovoDocumento dados, string? chave) =>
        EmitirAsync(TipoDocumento.Cotacao, dados, chave);

    private async Task<(DocumentoDto, bool)> EmitirAsync(TipoDocumento tipo, NovoDocumento dados, string? chaveIdempotencia)
    {
        var chave = ServicoPedidos.ValidarChave(chaveIdempotencia);
        if (await PorChaveAsync(chave, tipo) is { } existente)
            return (existente, false);
        if (tipo == TipoDocumento.Factura && dados.ClienteId is null)
            throw new RegraNegocioException("A factura a crédito precisa de um cliente da ficha de clientes (para a conta corrente).");
        if (dados.Linhas is not { Count: > 0 and <= MaximoLinhas })
            throw new RegraNegocioException($"Indique entre 1 e {MaximoLinhas} linhas.");

        var hoje = await config.HojeAsync(relogio);
        int id;
        await using (var transacao = await db.Database.BeginTransactionAsync())
        {
            var documento = await NovoAsync(tipo, relogio.GetUtcNow().UtcDateTime);
            documento.IdempotencyKey = chave;
            await PreencherClienteAsync(documento, dados.ClienteId, dados.ClienteNome, dados.ClienteNuit, dados.ClienteEndereco, exigirFicha: tipo == TipoDocumento.Factura);
            if (tipo == TipoDocumento.Factura)
            {
                documento.Vencimento = dados.Data ?? hoje.AddDays(await InteiroAsync(ChavesConfiguracao.PrazoFacturaDias, 30));
                if (documento.Vencimento < hoje)
                    throw new RegraNegocioException("O vencimento não pode ser anterior a hoje.");
            }
            else
            {
                documento.ValidaAte = dados.Data ?? hoje.AddDays(await InteiroAsync(ChavesConfiguracao.ValidadeCotacaoDias, 15));
                if (documento.ValidaAte < hoje)
                    throw new RegraNegocioException("A validade não pode ser anterior a hoje.");
            }
            documento.Observacoes = Limpar(dados.Observacoes);
            await AdicionarLinhasAsync(documento, dados.Linhas);
            CalculoDocumento.Totalizar(documento);
            if (documento.Total <= 0)
                throw new RegraNegocioException("O documento não tem valor.");
            db.Documentos.Add(documento);
            await GravarAsync(documento, transacao, chave);
            id = documento.Id;
        }
        return (await ObterAsync(id), true);
    }

    /// <summary>Converte uma cotação em factura (mesmas linhas e preços); a cotação fica «Convertida».</summary>
    public async Task<(DocumentoDto Documento, bool Criado)> ConverterCotacaoAsync(int cotacaoId, ConverterCotacao dados, string? chaveIdempotencia)
    {
        var chave = ServicoPedidos.ValidarChave(chaveIdempotencia);
        if (await PorChaveAsync(chave, TipoDocumento.Factura) is { } existente)
            return (existente, false);
        var hoje = await config.HojeAsync(relogio);
        int id;
        await using (var transacao = await db.Database.BeginTransactionAsync())
        {
            var cotacao = await db.Documentos.Include(d => d.Linhas).FirstOrDefaultAsync(d => d.Id == cotacaoId && d.Tipo == TipoDocumento.Cotacao)
                          ?? throw new NaoEncontradoException("Cotação não encontrada.");
            db.VerificarVersao(cotacao, dados.RowVersion);
            if (cotacao.Estado != EstadoDocumento.Emitido)
                throw new RegraNegocioException(cotacao.Estado == EstadoDocumento.Convertido ? "A cotação já foi convertida em factura." : "A cotação está anulada.");
            var clienteId = dados.ClienteId ?? cotacao.ClienteId
                            ?? throw new RegraNegocioException("Escolha o cliente da ficha de clientes para a factura.");

            var factura = await NovoAsync(TipoDocumento.Factura, relogio.GetUtcNow().UtcDateTime);
            factura.IdempotencyKey = chave;
            factura.DocumentoOrigemId = cotacao.Id;
            await PreencherClienteAsync(factura, clienteId, null, null, null, exigirFicha: true);
            factura.Vencimento = dados.Vencimento ?? hoje.AddDays(await InteiroAsync(ChavesConfiguracao.PrazoFacturaDias, 30));
            if (factura.Vencimento < hoje)
                throw new RegraNegocioException("O vencimento não pode ser anterior a hoje.");
            factura.Observacoes = cotacao.Observacoes;
            foreach (var l in cotacao.Linhas.OrderBy(l => l.Ordem))
                factura.Linhas.Add(new DocumentoLinha
                {
                    Ordem = l.Ordem, ProdutoId = l.ProdutoId, Codigo = l.Codigo, Descricao = l.Descricao, Quantidade = l.Quantidade,
                    PrecoUnitario = l.PrecoUnitario, Desconto = l.Desconto, PercentagemIva = l.PercentagemIva, MotivoIsencao = l.MotivoIsencao,
                    Base = l.Base, Iva = l.Iva, Total = l.Total,
                });
            CalculoDocumento.Totalizar(factura);
            cotacao.Estado = EstadoDocumento.Convertido;
            db.Documentos.Add(factura);
            await GravarAsync(factura, transacao, chave, new { Cotacao = cotacao.Numero });
            id = factura.Id;
        }
        return (await ObterAsync(id), true);
    }

    /// <summary>
    /// Anula uma cotação ou um recibo (com motivo). Anular um recibo devolve o valor à dívida das facturas e,
    /// se a sessão de caixa ainda estiver aberta, regista a saída do valor; numa sessão já fechada o acerto é manual.
    /// Facturas, VD e notas de crédito não se anulam: corrigem-se com notas de crédito.
    /// </summary>
    public async Task<DocumentoDto> AnularAsync(int id, ComMotivo dados)
    {
        if (!actual.TemPermissao(Permissoes.FacturacaoAnular))
            throw new AcessoNegadoException("Não tem permissão para anular documentos.");
        var motivo = dados.Motivo.Trim();
        await using (var transacao = await db.Database.BeginTransactionAsync())
        {
            var documento = await db.Documentos.Include(d => d.Liquidacoes).ThenInclude(l => l.Factura).Include(d => d.MetodoPagamento)
                                .FirstOrDefaultAsync(d => d.Id == id) ?? throw new NaoEncontradoException("Documento não encontrado.");
            db.VerificarVersao(documento, dados.RowVersion);
            if (documento.Tipo is not (TipoDocumento.Cotacao or TipoDocumento.Recibo))
                throw new RegraNegocioException(documento.Tipo == TipoDocumento.VendaDinheiro
                    ? "Uma venda a dinheiro não se anula: estorne o pedido no POS (é emitida a nota de crédito)."
                    : documento.Tipo == TipoDocumento.NotaCredito
                        ? "Uma nota de crédito não se anula."
                        : "Uma factura não se anula: emita uma nota de crédito.");
            if (documento.Estado != EstadoDocumento.Emitido)
                throw new RegraNegocioException(documento.Estado == EstadoDocumento.Convertido ? "A cotação já foi convertida em factura." : "O documento já está anulado.");

            var agora = relogio.GetUtcNow().UtcDateTime;
            string? caixaAcerto = null;
            if (documento.Tipo == TipoDocumento.Recibo)
            {
                foreach (var l in documento.Liquidacoes)
                    l.Factura.ValorPago -= l.Valor;
                if (documento.SessaoCaixaId is int sessaoId)
                {
                    var sessao = await db.SessoesCaixa.FirstAsync(s => s.Id == sessaoId);
                    if (sessao.Estado == EstadoSessaoCaixa.Aberta)
                    {
                        db.MovimentosCaixa.Add(new MovimentoCaixa
                        {
                            SessaoCaixaId = sessao.Id, Tipo = TipoMovimentoCaixa.Saida, MetodoPagamentoId = documento.MetodoPagamentoId!.Value,
                            Valor = documento.Total, Motivo = $"Anulação do recibo {documento.Numero}: {motivo}", UtilizadorId = actual.Id!.Value, DataUtc = agora,
                        });
                        sessao.AlteradoEmUtc = agora;
                    }
                    else
                        caixaAcerto = "A sessão de caixa do recibo já está fechada: o acerto do dinheiro é manual.";
                }
            }
            documento.Estado = EstadoDocumento.Anulado;
            documento.AnuladoEmUtc = agora;
            documento.AnuladoPorId = actual.Id;
            documento.MotivoAnulacao = motivo;
            auditoria.Registar("documento.anular", "Documento", documento.Id, new { documento.Numero, documento.Tipo, documento.Total, Motivo = motivo, Caixa = caixaAcerto });
            await db.SaveChangesAsync();
            await transacao.CommitAsync();
        }
        return await ObterAsync(id);
    }

    // ---------------------------------------------------------------- Recibos

    public async Task<(DocumentoDto Documento, bool Criado)> EmitirReciboAsync(NovoRecibo dados, string? chaveIdempotencia)
    {
        var chave = ServicoPedidos.ValidarChave(chaveIdempotencia);
        if (await PorChaveAsync(chave, TipoDocumento.Recibo) is { } existente)
            return (existente, false);
        if (dados.Liquidacoes is not { Count: > 0 and <= MaximoLinhas })
            throw new RegraNegocioException("Indique as facturas a pagar.");
        if (dados.Liquidacoes.Select(l => l.FacturaId).Distinct().Count() != dados.Liquidacoes.Count)
            throw new RegraNegocioException("A mesma factura aparece mais de uma vez.");

        int id;
        await using (var transacao = await db.Database.BeginTransactionAsync())
        {
            var metodo = await db.MetodosPagamento.FirstOrDefaultAsync(m => m.Id == dados.MetodoPagamentoId && m.Activo)
                         ?? throw new RegraNegocioException("Forma de pagamento inexistente ou inactiva.");
            if (metodo.Codigo is CodigoFacturaCredito or Cartoes.ServicoCartoes.CodigoMetodo)
                throw new RegraNegocioException($"Uma factura não se paga com {metodo.Nome}.");
            var referencia = Limpar(dados.Referencia);
            if (metodo.ExigeReferencia && referencia is null)
                throw new RegraNegocioException($"Indique a referência da transacção {metodo.Nome}.");
            SessaoCaixa? sessao = null;
            if (dados.SessaoCaixaId is int sessaoId)
            {
                sessao = await db.SessoesCaixa.FirstOrDefaultAsync(s => s.Id == sessaoId) ?? throw new RegraNegocioException("Sessão de caixa inexistente.");
                caixa.GarantirSessaoOperavel(sessao);
            }
            else if (metodo.ContaNaGaveta)
                throw new RegraNegocioException($"Para receber em {metodo.Nome} abra o caixa: o valor entra na sessão de caixa.");

            var ids = dados.Liquidacoes.Select(l => l.FacturaId).ToList();
            var facturas = await db.Documentos.Where(d => ids.Contains(d.Id)).ToDictionaryAsync(d => d.Id);
            var agora = relogio.GetUtcNow().UtcDateTime;
            var recibo = await NovoAsync(TipoDocumento.Recibo, agora);
            recibo.IdempotencyKey = chave;
            await PreencherClienteAsync(recibo, dados.ClienteId, null, null, null, exigirFicha: true);
            recibo.MetodoPagamentoId = metodo.Id;
            recibo.Referencia = referencia;
            recibo.SessaoCaixaId = sessao?.Id;
            recibo.Observacoes = Limpar(dados.Observacoes);
            foreach (var l in dados.Liquidacoes)
            {
                var factura = facturas.GetValueOrDefault(l.FacturaId);
                if (factura is null || factura.Tipo != TipoDocumento.Factura || factura.ClienteId != dados.ClienteId)
                    throw new RegraNegocioException("Factura inexistente ou de outro cliente.");
                if (factura.Estado != EstadoDocumento.Emitido)
                    throw new RegraNegocioException($"A factura {factura.Numero} não está em vigor.");
                var valor = CalculoPedido.Arredondar(l.Valor);
                if (valor <= 0 || valor > factura.EmDivida)
                    throw new RegraNegocioException($"O valor a pagar da factura {factura.Numero} tem de estar entre 0,01 e {factura.EmDivida:N2} (em dívida).");
                factura.ValorPago += valor;
                recibo.Liquidacoes.Add(new LiquidacaoFactura { FacturaId = factura.Id, Valor = valor });
            }
            recibo.Total = recibo.Subtotal = recibo.Liquidacoes.Sum(l => l.Valor);
            db.Documentos.Add(recibo);
            if (sessao is not null)
            {
                db.MovimentosCaixa.Add(new MovimentoCaixa
                {
                    SessaoCaixaId = sessao.Id, Tipo = TipoMovimentoCaixa.Entrada, MetodoPagamentoId = metodo.Id, Valor = recibo.Total,
                    Motivo = $"Recibo {recibo.Numero} ({recibo.ClienteNome})", UtilizadorId = actual.Id!.Value, DataUtc = agora,
                });
                sessao.AlteradoEmUtc = agora;
            }
            await GravarAsync(recibo, transacao, chave, new { Metodo = metodo.Codigo, SessaoCaixa = sessao?.Id, Facturas = recibo.Liquidacoes.Select(l => facturas[l.FacturaId].Numero) });
            id = recibo.Id;
        }
        return (await ObterAsync(id), true);
    }

    // ---------------------------------------------------------------- Notas de crédito

    /// <summary>Nota de crédito (total ou por linhas/quantidades) sobre uma factura, limitada ao valor ainda em dívida.</summary>
    public async Task<(DocumentoDto Documento, bool Criado)> EmitirNotaCreditoAsync(NovaNotaCredito dados, string? chaveIdempotencia)
    {
        if (!actual.TemPermissao(Permissoes.FacturacaoAnular))
            throw new AcessoNegadoException("Não tem permissão para emitir notas de crédito.");
        var chave = ServicoPedidos.ValidarChave(chaveIdempotencia);
        if (await PorChaveAsync(chave, TipoDocumento.NotaCredito) is { } existente)
            return (existente, false);
        int id;
        await using (var transacao = await db.Database.BeginTransactionAsync())
        {
            var origem = await db.Documentos.Include(d => d.Linhas).FirstOrDefaultAsync(d => d.Id == dados.DocumentoOrigemId)
                         ?? throw new NaoEncontradoException("Documento não encontrado.");
            if (origem.Tipo == TipoDocumento.VendaDinheiro)
                throw new RegraNegocioException("Para corrigir uma venda a dinheiro, estorne o pedido no POS: a nota de crédito é emitida automaticamente.");
            if (origem.Tipo != TipoDocumento.Factura || origem.Estado != EstadoDocumento.Emitido)
                throw new RegraNegocioException("Só se emitem notas de crédito sobre facturas em vigor.");

            var creditado = await CreditadoPorLinhaAsync(origem.Id);
            decimal Creditavel(DocumentoLinha l) => l.Quantidade - creditado.GetValueOrDefault(l.Id).Quantidade;
            List<(DocumentoLinha, decimal)> linhas;
            if (dados.Linhas is not { Count: > 0 })
                linhas = [.. origem.Linhas.Select(l => (l, Creditavel(l))).Where(x => x.Item2 > 0)];
            else
            {
                if (dados.Linhas.Select(l => l.LinhaOrigemId).Distinct().Count() != dados.Linhas.Count)
                    throw new RegraNegocioException("A mesma linha aparece mais de uma vez.");
                linhas = [];
                foreach (var pedida in dados.Linhas)
                {
                    var l = origem.Linhas.FirstOrDefault(x => x.Id == pedida.LinhaOrigemId) ?? throw new RegraNegocioException("Linha inexistente nesta factura.");
                    if (!QuantidadeValida(pedida.Quantidade))
                        throw new RegraNegocioException("Quantidade inválida (até 3 casas decimais).");
                    if (pedida.Quantidade > Creditavel(l))
                        throw new RegraNegocioException($"«{l.Descricao}»: só pode creditar até {Creditavel(l).ToString("0.###", CultureInfo.GetCultureInfo("pt-PT"))}.");
                    linhas.Add((l, pedida.Quantidade));
                }
            }
            if (linhas.Count == 0)
                throw new RegraNegocioException("Esta factura já está totalmente creditada.");

            var nota = await ConstruirNotaCreditoAsync(origem, linhas, creditado, dados.Motivo.Trim(), relogio.GetUtcNow().UtcDateTime);
            if (origem.ValorPago + origem.ValorCreditado > origem.Total)
                throw new RegraNegocioException($"A nota de crédito ({nota.Total:N2}) excede o valor em dívida da factura ({origem.Total - origem.ValorPago - origem.ValorCreditado + nota.Total:N2}). Anule primeiro o recibo do valor já pago.");
            nota.IdempotencyKey = chave;
            await GravarAsync(nota, transacao, chave, new { Origem = origem.Numero, nota.Motivo });
            id = nota.Id;
        }
        return (await ObterAsync(id), true);
    }

    private async Task<Documento> ConstruirNotaCreditoAsync(Documento origem, IReadOnlyList<(DocumentoLinha Linha, decimal Quantidade)> linhas,
        IReadOnlyDictionary<int, (decimal Quantidade, decimal Total, decimal Desconto)> creditado, string motivo, DateTime agora)
    {
        var nota = await NovoAsync(TipoDocumento.NotaCredito, agora);
        nota.DocumentoOrigemId = origem.Id;
        nota.ClienteId = origem.ClienteId;
        nota.ClienteNome = origem.ClienteNome;
        nota.ClienteNuit = origem.ClienteNuit;
        nota.ClienteEndereco = origem.ClienteEndereco;
        nota.Motivo = motivo;
        foreach (var ((l, quantidade), n) in linhas.Select((x, n) => (x, n)))
        {
            var ja = creditado.GetValueOrDefault(l.Id);
            // O resto da linha leva o que falta (sem diferenças de arredondamento acumuladas); uma parte leva a proporção.
            var tudo = quantidade == l.Quantidade - ja.Quantidade;
            var desconto = tudo ? l.Desconto - ja.Desconto : CalculoPedido.Arredondar(l.Desconto * quantidade / l.Quantidade);
            var total = tudo ? l.Total - ja.Total : CalculoPedido.Arredondar(l.PrecoUnitario * quantidade) - desconto;
            var iva = CalculoPedido.ImpostoIncluido(total, l.PercentagemIva);
            nota.Linhas.Add(new DocumentoLinha
            {
                Ordem = n + 1, ProdutoId = l.ProdutoId, Codigo = l.Codigo, Descricao = l.Descricao, Quantidade = quantidade, PrecoUnitario = l.PrecoUnitario,
                Desconto = desconto, PercentagemIva = l.PercentagemIva, MotivoIsencao = l.MotivoIsencao, Total = total, Iva = iva, Base = total - iva,
                LinhaOrigemId = l.Id,
            });
        }
        CalculoDocumento.Totalizar(nota);
        origem.ValorCreditado += nota.Total;
        db.Documentos.Add(nota);
        return nota;
    }

    /// <summary>Quantidade, total e desconto já creditados por linha do documento (notas de crédito em vigor).</summary>
    private async Task<IReadOnlyDictionary<int, (decimal Quantidade, decimal Total, decimal Desconto)>> CreditadoPorLinhaAsync(int documentoId) =>
        (await db.DocumentoLinhas.AsNoTracking()
            .Where(l => l.LinhaOrigemId != null && l.Documento.DocumentoOrigemId == documentoId
                        && l.Documento.Tipo == TipoDocumento.NotaCredito && l.Documento.Estado == EstadoDocumento.Emitido)
            .GroupBy(l => l.LinhaOrigemId!.Value)
            .Select(g => new { g.Key, Quantidade = g.Sum(l => l.Quantidade), Total = g.Sum(l => l.Total), Desconto = g.Sum(l => l.Desconto) })
            .ToListAsync())
        .ToDictionary(x => x.Key, x => (x.Quantidade, x.Total, x.Desconto));

    // ---------------------------------------------------------------- Consultas

    public async Task<Pagina<DocumentoResumo>> ListarAsync(FiltroDocumentos filtro, CancellationToken ct = default)
    {
        var (pagina, tamanho) = Pagina<DocumentoResumo>.Normalizar(filtro.Pagina, filtro.TamanhoPagina);
        var q = db.Documentos.AsNoTracking().AsQueryable();
        if (filtro.Tipo is TipoDocumento tipo) q = q.Where(d => d.Tipo == tipo);
        if (filtro.Estado is EstadoDocumento estado) q = q.Where(d => d.Estado == estado);
        if (filtro.ClienteId is int clienteId) q = q.Where(d => d.ClienteId == clienteId);
        if (filtro.Desde is DateOnly desde)
        {
            var (inicio, fim) = await config.IntervaloUtcAsync(desde, filtro.Ate ?? desde);
            q = q.Where(d => d.EmitidoEmUtc >= inicio && d.EmitidoEmUtc < fim);
        }
        else if (filtro.Ate is DateOnly ate)
        {
            var (_, fim) = await config.IntervaloUtcAsync(ate, ate);
            q = q.Where(d => d.EmitidoEmUtc < fim);
        }
        if (!string.IsNullOrWhiteSpace(filtro.Pesquisa))
        {
            var p = filtro.Pesquisa.Trim();
            q = q.Where(d => d.Numero.Contains(p) || d.ClienteNome.Contains(p) || d.ClienteNuit == p || (d.Pedido != null && d.Pedido.Numero.Contains(p)));
        }
        if (filtro.ApenasEmDivida)
            q = q.Where(d => d.Tipo == TipoDocumento.Factura && d.Estado == EstadoDocumento.Emitido && d.Total - d.ValorPago - d.ValorCreditado > 0);

        var total = await q.CountAsync(ct);
        var itens = await q.OrderByDescending(d => d.EmitidoEmUtc).ThenByDescending(d => d.Id).Skip((pagina - 1) * tamanho).Take(tamanho)
            .Select(d => new DocumentoResumo(d.Id, d.Tipo, d.Numero, d.Estado, d.EmitidoEmUtc, d.ClienteNome, d.ClienteNuit, d.Total,
                d.Tipo == TipoDocumento.Factura && d.Estado == EstadoDocumento.Emitido ? d.Total - d.ValorPago - d.ValorCreditado : 0m,
                d.Vencimento, d.ValidaAte,
                d.DocumentoOrigem != null ? d.DocumentoOrigem.Numero : d.Pedido != null ? d.Pedido.Numero : null))
            .ToListAsync(ct);
        return new Pagina<DocumentoResumo>(itens, total, pagina, tamanho);
    }

    public async Task<DocumentoDto> ObterAsync(int id, CancellationToken ct = default)
    {
        var d = await db.Documentos.AsNoTracking()
                    .Include(x => x.Linhas).Include(x => x.Liquidacoes).ThenInclude(l => l.Factura)
                    .Include(x => x.MetodoPagamento).Include(x => x.DocumentoOrigem).Include(x => x.Pedido)
                    .AsSplitQuery().FirstOrDefaultAsync(x => x.Id == id, ct)
                ?? throw new NaoEncontradoException("Documento não encontrado.");

        var relacionados = await db.Documentos.AsNoTracking().Where(x => x.DocumentoOrigemId == id)
            .Select(x => new DocumentoRelacionado(x.Id, x.Tipo, x.Numero, x.Estado, x.EmitidoEmUtc, x.Total)).ToListAsync(ct);
        if (d.Tipo == TipoDocumento.Factura)
            relacionados.AddRange(await db.LiquidacoesFactura.AsNoTracking().Where(l => l.FacturaId == id)
                .Select(l => new DocumentoRelacionado(l.Recibo.Id, l.Recibo.Tipo, l.Recibo.Numero, l.Recibo.Estado, l.Recibo.EmitidoEmUtc, l.Valor)).ToListAsync(ct));
        var creditado = d.Tipo is TipoDocumento.Factura or TipoDocumento.VendaDinheiro
            ? await CreditadoPorLinhaAsync(id)
            : new Dictionary<int, (decimal Quantidade, decimal Total, decimal Desconto)>();
        var nomes = await utilizadores.NomesAsync(new[] { d.UtilizadorId, d.AnuladoPorId ?? 0 }.Where(x => x > 0), ct);

        return new DocumentoDto(
            d.Id, d.Tipo, d.Numero, d.Estado, d.EmitidoEmUtc, nomes.GetValueOrDefault(d.UtilizadorId), d.Vencimento, d.ValidaAte,
            d.EmitenteNome, d.EmitenteNuit, d.EmitenteEndereco, d.EmitenteTelefone,
            d.ClienteId, d.ClienteNome, d.ClienteNuit, d.ClienteEndereco,
            d.PedidoId, d.Pedido?.Numero, d.DocumentoOrigemId, d.DocumentoOrigem?.Numero, d.DocumentoOrigem?.Tipo,
            d.Subtotal, d.Desconto, d.BaseTributavel, d.Iva, d.Total, d.ValorPago, d.ValorCreditado,
            d.Tipo == TipoDocumento.Factura && d.Estado == EstadoDocumento.Emitido ? d.EmDivida : 0m,
            d.MetodoPagamento?.Nome, d.Referencia, d.Motivo, d.Observacoes,
            d.AnuladoEmUtc, d.AnuladoPorId is int a ? nomes.GetValueOrDefault(a) : null, d.MotivoAnulacao,
            [.. d.Linhas.OrderBy(l => l.Ordem).Select(l => new DocumentoLinhaDto(l.Id, l.ProdutoId, l.Codigo, l.Descricao, l.Quantidade, l.PrecoUnitario,
                l.Desconto, l.PercentagemIva, l.MotivoIsencao, l.Base, l.Iva, l.Total,
                d.Estado == EstadoDocumento.Emitido && d.Tipo is TipoDocumento.Factura or TipoDocumento.VendaDinheiro
                    ? l.Quantidade - creditado.GetValueOrDefault(l.Id).Quantidade : 0m))],
            [.. CalculoDocumento.ResumoIva(d.Linhas).Select(r => new ResumoIvaDto(r.Percentagem, r.Base, r.Iva, r.Total))],
            [.. d.Liquidacoes.Select(l => new LiquidacaoDto(l.FacturaId, l.Factura.Numero, l.Valor))],
            [.. relacionados.OrderBy(r => r.EmitidoEmUtc)],
            d.RowVersion);
    }

    /// <summary>Facturas em dívida do cliente (para emitir recibos), das mais antigas para as mais recentes.</summary>
    public async Task<ContaCorrente> ContaCorrenteAsync(int clienteId, CancellationToken ct = default)
    {
        var cliente = await db.Clientes.AsNoTracking().FirstOrDefaultAsync(c => c.Id == clienteId, ct) ?? throw new NaoEncontradoException("Cliente não encontrado.");
        var hoje = await config.HojeAsync(relogio);
        var facturas = await db.Documentos.AsNoTracking()
            .Where(d => d.ClienteId == clienteId && d.Tipo == TipoDocumento.Factura && d.Estado == EstadoDocumento.Emitido && d.Total - d.ValorPago - d.ValorCreditado > 0)
            .OrderBy(d => d.EmitidoEmUtc).ThenBy(d => d.Id)
            .Select(d => new FacturaEmDivida(d.Id, d.Numero, d.EmitidoEmUtc, d.Vencimento, d.Total, d.ValorPago, d.ValorCreditado,
                d.Total - d.ValorPago - d.ValorCreditado, d.Vencimento != null && d.Vencimento < hoje))
            .ToListAsync(ct);
        return new ContaCorrente(cliente.Id, cliente.Nome, cliente.Nuit, facturas.Sum(f => f.EmDivida), facturas.Where(f => f.Vencida).Sum(f => f.EmDivida), facturas);
    }

    // ---------------------------------------------------------------- Auxiliares

    private async Task<Documento> NovoAsync(TipoDocumento tipo, DateTime agora) => new()
    {
        Tipo = tipo,
        Numero = await numeracao.ProximoAsync(Documento.Prefixo(tipo)),
        EmitidoEmUtc = agora,
        UtilizadorId = actual.Id!.Value,
        EmitenteNome = await config.TextoAsync(ChavesConfiguracao.NomeEstabelecimento, "LAMGest Restaurant") is { Length: > 0 } nome ? nome : "LAMGest Restaurant",
        EmitenteNuit = Limpar(await config.TextoAsync(ChavesConfiguracao.Nuit)),
        EmitenteEndereco = Limpar(await config.TextoAsync(ChavesConfiguracao.Endereco)),
        EmitenteTelefone = Limpar(await config.TextoAsync(ChavesConfiguracao.Telefone)),
    };

    /// <summary>Cliente da ficha (dados copiados) ou só nome/NUIT escritos; sem nada, «Consumidor final».</summary>
    private async Task PreencherClienteAsync(Documento documento, int? clienteId, string? nome, string? nuit, string? endereco, bool exigirFicha)
    {
        if (clienteId is int id)
        {
            var cliente = await db.Clientes.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id) ?? throw new RegraNegocioException("Cliente inexistente.");
            if (!cliente.Activo && documento.Tipo != TipoDocumento.VendaDinheiro)
                throw new RegraNegocioException($"O cliente {cliente.Nome} está desactivado.");
            documento.ClienteId = cliente.Id;
            documento.ClienteNome = cliente.Nome;
            documento.ClienteNuit = Limpar(cliente.Nuit);
            documento.ClienteEndereco = Limpar(cliente.Endereco);
            return;
        }
        if (exigirFicha)
            throw new RegraNegocioException("Escolha o cliente da ficha de clientes.");
        nuit = Limpar(nuit);
        if (nuit is not null && !(nuit.Length == 9 && nuit.All(char.IsAsciiDigit)))
            throw new RegraNegocioException("O NUIT do cliente tem 9 algarismos.");
        documento.ClienteNome = Limpar(nome) ?? ConsumidorFinal;
        documento.ClienteNuit = nuit;
        documento.ClienteEndereco = Limpar(endereco);
    }

    private async Task AdicionarLinhasAsync(Documento documento, IReadOnlyList<NovaLinhaDocumento> linhas)
    {
        var idsProdutos = linhas.Where(l => l.ProdutoId is not null).Select(l => l.ProdutoId!.Value).Distinct().ToList();
        var produtos = await db.Produtos.AsNoTracking().Include(p => p.TaxaImposto).Where(p => idsProdutos.Contains(p.Id)).ToDictionaryAsync(p => p.Id);
        var idsTaxas = linhas.Where(l => l.TaxaImpostoId is not null).Select(l => l.TaxaImpostoId!.Value).Distinct().ToList();
        var taxas = await db.TaxasImposto.AsNoTracking().Where(t => idsTaxas.Contains(t.Id) && t.Activo).ToDictionaryAsync(t => t.Id);
        var isencao = await MotivoIsencaoAsync();

        foreach (var (l, n) in linhas.Select((l, n) => (l, n)))
        {
            var onde = $"Linha {n + 1}";
            if (!QuantidadeValida(l.Quantidade))
                throw new RegraNegocioException($"{onde}: quantidade inválida (positiva, até 3 casas decimais).");
            var linha = new DocumentoLinha { Ordem = n + 1, Quantidade = l.Quantidade, Desconto = CalculoPedido.Arredondar(l.Desconto ?? 0m) };
            if (l.ProdutoId is int produtoId)
            {
                var p = produtos.GetValueOrDefault(produtoId) ?? throw new RegraNegocioException($"{onde}: produto inexistente.");
                if (!p.Activo)
                    throw new RegraNegocioException($"{onde}: o produto {p.Nome} está desactivado.");
                linha.ProdutoId = p.Id;
                linha.Codigo = p.Codigo;
                linha.Descricao = Limpar(l.Descricao) ?? p.Nome;
                linha.PrecoUnitario = CalculoPedido.Arredondar(l.PrecoUnitario ?? p.PrecoVenda);
                linha.PercentagemIva = p.TaxaImposto?.Percentagem ?? 0m;
            }
            else
            {
                linha.Descricao = Limpar(l.Descricao) ?? throw new RegraNegocioException($"{onde}: indique a descrição.");
                linha.PrecoUnitario = CalculoPedido.Arredondar(l.PrecoUnitario ?? throw new RegraNegocioException($"{onde}: indique o preço."));
                linha.PercentagemIva = l.TaxaImpostoId is int taxaId
                    ? (taxas.GetValueOrDefault(taxaId) ?? throw new RegraNegocioException($"{onde}: taxa de IVA inexistente ou inactiva.")).Percentagem
                    : 0m;
            }
            if (linha.Desconto < 0 || linha.Desconto > CalculoPedido.Arredondar(linha.PrecoUnitario * linha.Quantidade))
                throw new RegraNegocioException($"{onde}: o desconto não pode exceder o valor da linha.");
            linha.MotivoIsencao = linha.PercentagemIva <= 0 ? isencao : null;
            CalculoDocumento.CalcularLinha(linha);
            documento.Linhas.Add(linha);
        }
    }

    /// <summary>Grava o documento (dentro da transacção aberta pelo chamador) e regista a auditoria; um reenvio simultâneo com a mesma chave perde para o primeiro.</summary>
    private async Task GravarAsync(Documento documento, Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transacao, string chave, object? extra = null)
    {
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (ex is not DbUpdateConcurrencyException)
        {
            await transacao.RollbackAsync();
            if (await db.Documentos.AsNoTracking().AnyAsync(d => d.IdempotencyKey == chave))
                throw new ConflitoException("Este documento já foi emitido (pedido repetido). Actualize a lista.");
            throw;
        }
        auditoria.Registar("documento.emitir", "Documento", documento.Id,
            new { documento.Numero, documento.Tipo, documento.ClienteNome, documento.ClienteNuit, documento.Total, Detalhes = extra });
        await db.SaveChangesAsync();
        await transacao.CommitAsync();
    }

    private async Task<DocumentoDto?> PorChaveAsync(string chave, TipoDocumento tipo)
    {
        var existente = await db.Documentos.AsNoTracking().Where(d => d.IdempotencyKey == chave).Select(d => new { d.Id, d.Tipo }).FirstOrDefaultAsync();
        if (existente is null)
            return null;
        return existente.Tipo == tipo ? await ObterAsync(existente.Id) : throw new ConflitoException("Esta chave já foi usada noutro documento.");
    }

    private async Task<string> MotivoIsencaoAsync() =>
        Limpar(await config.TextoAsync(ChavesConfiguracao.MotivoIsencao)) ?? "Isento nos termos do artigo 9 do Código do IVA";

    private async Task<int> InteiroAsync(string chave, int padrao) =>
        int.TryParse(await config.TextoAsync(chave), NumberStyles.None, CultureInfo.InvariantCulture, out var v) ? v : padrao;

    /// <summary>Positiva, até 999 999 e no máximo 3 casas decimais (documentos podem ter quantidades maiores do que um pedido).</summary>
    private static bool QuantidadeValida(decimal q) => q > 0 && q <= 999_999m && decimal.Round(q, 3) == q;

    private static string? Limpar(string? texto) => string.IsNullOrWhiteSpace(texto) ? null : texto.Trim();
}
