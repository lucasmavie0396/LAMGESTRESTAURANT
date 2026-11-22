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
public partial class ServicoPedidos(
    IAppDbContext db,
    IUtilizadorActual actual,
    IAuditoria auditoria,
    INotificador notificador,
    ServicoNumeracao numeracao,
    ConfiguracoesSistema config,
    IConsultaUtilizadores utilizadores,
    Impressao.IFilaImpressao impressao,
    Catalogo.ServicoAcompanhamentos acompanhamentos,
    TimeProvider relogio)
{
    // Dependências em campos: o serviço está dividido em ficheiros parciais (ServicoPedidos.*.cs) por assunto.
    private readonly IAppDbContext db = db;
    private readonly IUtilizadorActual actual = actual;
    private readonly IAuditoria auditoria = auditoria;
    private readonly INotificador notificador = notificador;
    private readonly ServicoNumeracao numeracao = numeracao;
    private readonly ConfiguracoesSistema config = config;
    private readonly IConsultaUtilizadores utilizadores = utilizadores;
    private readonly Impressao.IFilaImpressao impressao = impressao;
    private readonly Catalogo.ServicoAcompanhamentos acompanhamentos = acompanhamentos;
    private readonly TimeProvider relogio = relogio;

    public const string MotivoRemovidoAntesEnvio = "Removido antes do envio";

    private DateTime Agora => relogio.GetUtcNow().UtcDateTime;

    /// <summary>Pode ver e lançar nas mesas de outros utilizadores.</summary>
    public bool VeMesasDeOutros => actual.TemPermissao(Permissoes.PosMesasOutros);

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
            UtilizadorResponsavelId = actual.Id,
        };

        var chaveModo = dados.Tipo switch
        {
            TipoPedido.Mesa => ChavesConfiguracao.PosMesa,
            TipoPedido.TakeAway => ChavesConfiguracao.PosTakeAway,
            TipoPedido.Delivery => ChavesConfiguracao.PosDelivery,
            _ => ChavesConfiguracao.PosBalcao,
        };
        if (!await config.BoolAsync(chaveModo, padrao: true))
            throw new RegraNegocioException("Este tipo de venda está desactivado nas configurações (Documentos e POS).");

        await using var transacao = await db.Database.BeginTransactionAsync();
        Mesa? mesa = null;
        switch (dados.Tipo)
        {
            case TipoPedido.Balcao:
                // Venda rápida: sem mesa nem dados de entrega (não aparece na lista de entregas).
                if (dados.MesaId is not null)
                    throw new RegraNegocioException("Vendas ao balcão não são associadas a mesas.");
                break;
            case TipoPedido.Mesa:
                mesa = await PrepararMesaAsync(dados.MesaId, dados.ConfirmarMesaReservada);
                pedido.MesaId = mesa.Id;
                break;
            case TipoPedido.Delivery:
                if (string.IsNullOrWhiteSpace(dados.ContactoEntrega) || string.IsNullOrWhiteSpace(dados.EnderecoEntrega))
                    throw new RegraNegocioException("Delivery: indique o contacto e o endereço de entrega.");
                if (string.IsNullOrWhiteSpace(dados.NomeEntrega) && dados.ClienteId is null)
                    throw new RegraNegocioException("Delivery: indique o nome do cliente.");
                pedido.TaxaEntrega = CalculoPedido.Arredondar(await config.DecimalAsync(ChavesConfiguracao.TaxaEntregaPadrao));
                goto default;
            default:
                if (dados.MesaId is not null)
                    throw new RegraNegocioException("Pedidos Take Away e Delivery não são associados a mesas.");
                pedido.EstadoEntrega = EstadoEntrega.Recebido;
                pedido.NomeEntrega = Limpar(dados.NomeEntrega);
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

    private static readonly Dictionary<EstadoEntrega, EstadoEntrega[]> TransicoesEntrega = new()
    {
        [EstadoEntrega.Recebido] = [EstadoEntrega.EmPreparacao, EstadoEntrega.Pronto],
        [EstadoEntrega.EmPreparacao] = [EstadoEntrega.Pronto],
        [EstadoEntrega.Pronto] = [EstadoEntrega.SaiuParaEntrega, EstadoEntrega.Entregue],
        [EstadoEntrega.SaiuParaEntrega] = [EstadoEntrega.Entregue],
    };

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
        await GarantirAcessoMesaAsync(pedido);
        db.VerificarVersao(pedido, versao);
        if (pedido.Estado != EstadoPedido.Aberto)
            throw new RegraNegocioException($"O pedido {pedido.Numero} já está {DescreverEstado(pedido.Estado)}.");
        // Marca o pedido como alterado para que a rowversion avance mesmo quando só mudam os itens.
        pedido.AlteradoEmUtc = Agora;
        return pedido;
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
