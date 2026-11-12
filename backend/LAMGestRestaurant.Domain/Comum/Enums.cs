namespace LAMGestRestaurant.Domain.Comum;

// Todos os enums são guardados como texto na BD (ver AppDbContext.ConfigureConventions).

public enum TipoProduto { Restaurante, Bar, Outro }

public enum EstadoMesa { Livre, Ocupada, Reservada, Indisponivel }

public enum TipoPedido { Mesa, TakeAway, Delivery, Balcao }

/// <summary>
/// Estado financeiro/de ciclo de vida do pedido. Estornado = estava pago e foi reembolsado
/// (os campos Cancelado* do pedido registam quem, quando e porquê).
/// </summary>
public enum EstadoPedido { Aberto, Pago, Cancelado, Estornado }

/// <summary>Estado de cada item na cozinha (KDS).</summary>
public enum EstadoItemCozinha { NaoEnviado, Pendente, EmPreparacao, Pronto, Entregue }

/// <summary>Estado logístico de pedidos Take Away / Delivery.</summary>
public enum EstadoEntrega { Recebido, EmPreparacao, Pronto, SaiuParaEntrega, Entregue, Cancelado }

public enum EstadoPagamento { Valido, Anulado }

public enum EstadoSessaoCaixa { Aberta, Fechada }

public enum EstadoDia { Aberto, Fechado }

/// <summary>Reembolso = devolução ao cliente no estorno de um pedido pago (conta como saída).</summary>
public enum TipoMovimentoCaixa { Entrada, Saida, Despesa, Reembolso }

public enum DimensaoUnidade { Unidade, Massa, Volume }

public enum TipoArtigo { Ingrediente, Revenda, Consumivel }

public enum TipoMovimentoStock
{
    EntradaCompra,
    EstornoCompra,
    SaidaVenda,
    EstornoVenda,
    Ajuste,
    Perda,
    TransferenciaSaida,
    TransferenciaEntrada,
    Inventario,
    /// <summary>Primeira entrada de stock lançada na criação do produto.</summary>
    StockInicial
}

public enum EstadoInventario { Aberto, Confirmado, Cancelado }

public enum EstadoCompra { Rascunho, Confirmada, Anulada }

public enum TipoCliente { Particular, Empresa }

public enum TipoMovimentoPontos { Acumulacao, Resgate, Ajuste, Estorno }

public enum EstadoReserva { Pendente, Confirmada, Chegada, Concluida, Cancelada }

public enum EstadoDespesa { Confirmada, Anulada }

public enum FuncaoFuncionario { Garcom, Cozinheiro, Caixa, Estafeta, Barman, Gerente, Outro }

/// <summary>Producao = outro centro de produção (pastelaria, grelhados…): só recebe o que categorias, produtos ou estações lhe atribuem.</summary>
public enum FuncaoImpressora { Caixa, Bar, Cozinha, Producao, Etiquetas }

public enum LigacaoImpressora { Rede, Windows }

/// <summary>Directo: o servidor imprime sem diálogo. Pdf: o documento abre em PDF no ecrã de quem o pediu (imprimir ou guardar).</summary>
public enum ModoImpressora { Directo, Pdf }
