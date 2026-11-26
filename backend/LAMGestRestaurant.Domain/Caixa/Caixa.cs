using LAMGestRestaurant.Domain.Comum;
using LAMGestRestaurant.Domain.Vendas;

namespace LAMGestRestaurant.Domain.Caixa;

/// <summary>Computador/posto de caixa registado no sistema.</summary>
public class Terminal : Entidade, IDesactivavel
{
    public string Nome { get; set; } = string.Empty;
    /// <summary>Número do posto indicado no instalador do computador (atalho com ?posto=N). Único quando preenchido.</summary>
    public int? Numero { get; set; }
    public string? Descricao { get; set; }
    public bool Activo { get; set; } = true;
    /// <summary>Agente de impressão do posto: hash SHA-256 da chave (a chave só existe no computador do posto).</summary>
    public string? AgenteTokenHash { get; set; }
    public DateTime? AgenteVistoEmUtc { get; set; }
    /// <summary>Impressoras instaladas no Windows do posto, enviadas pelo agente (JSON).</summary>
    public string? AgenteImpressoras { get; set; }
    public string? AgenteVersao { get; set; }

    /// <summary>Último sinal do navegador deste posto (enviado a cada minuto enquanto o sistema está aberto).</summary>
    public DateTime? VistoEmUtc { get; set; }
    public int? VistoUtilizadorId { get; set; }
    /// <summary>Endereço IP de onde veio o último sinal.</summary>
    public string? VistoEndereco { get; set; }

    /// <summary>Posto escolhido para a ligação dos tablets (app Android) pela rede. No máximo um.</summary>
    public bool LigacaoTablets { get; set; }
    /// <summary>Hash SHA-256 do código de emparelhamento dos tablets (o código só se mostra ao gerá-lo).</summary>
    public string? TabletsCodigoHash { get; set; }
    public DateTime? TabletsCodigoGeradoEmUtc { get; set; }

    /// <summary>Visor do cliente do posto, comandado pelo agente: visor de poste (porta COM) ou segundo monitor.</summary>
    public TipoVisor VisorTipo { get; set; }
    /// <summary>Visor de poste: porta COM (ex.: COM3), protocolo, velocidade e caracteres por linha.</summary>
    public string? VisorPorta { get; set; }
    public ProtocoloVisor VisorProtocolo { get; set; }
    public int VisorVelocidade { get; set; } = 9600;
    public int VisorColunas { get; set; } = 20;
    /// <summary>Segundo monitor: nome do ecrã no Windows (ex.: \\.\DISPLAY2); vazio = o primeiro que não é o principal.</summary>
    public string? VisorEcra { get; set; }
    /// <summary>Chave do endereço da página do visor (segundo monitor ou tablet virado para o cliente), sem sessão.</summary>
    public string? VisorChave { get; set; }
    /// <summary>Portas COM e ecrãs do computador do posto, enviados pelo agente (JSON).</summary>
    public string? AgentePortas { get; set; }
    public string? AgenteEcras { get; set; }

    /// <summary>Ecrã do posto: modo táctil (botões e letra maiores) e tamanho do sistema em % (90, 100, 110 ou 125), aplicados pelo navegador do posto.</summary>
    public bool EcraTactil { get; set; }
    public int EcraEscala { get; set; } = 100;

    /// <summary>Balança ligada ao posto (porta COM), lida pelo agente enquanto o POS pede o peso.</summary>
    public ModoBalanca BalancaModo { get; set; }
    public string? BalancaPorta { get; set; }
    public int BalancaVelocidade { get; set; } = 9600;
    public FormatoSerie BalancaFormato { get; set; }
}

public enum TipoVisor { Nenhum = 0, Poste = 1, Monitor = 2 }

/// <summary>
/// Como o agente lê a balança: contínua (a balança envia o peso sem parar), pedido ENQ (Toledo/Prix e compatíveis)
/// ou pedido «W» (CAS, Dibal e outras).
/// </summary>
public enum ModoBalanca { Nenhuma = 0, Continua = 1, PedidoEnq = 2, PedidoW = 3 }

/// <summary>Bits de dados, paridade e bits de paragem da porta série (8N1 ou 7E1).</summary>
public enum FormatoSerie { Oito_N_1 = 0, Sete_E_1 = 1 }

/// <summary>Comandos do visor de poste: ESC/POS (Epson e compatíveis), CD5220 ou texto simples.</summary>
public enum ProtocoloVisor { EscPos = 0, Cd5220 = 1, Simples = 2 }

public class MetodoPagamento : Entidade, IDesactivavel
{
    public string Codigo { get; set; } = string.Empty;
    public string Nome { get; set; } = string.Empty;
    /// <summary>Só métodos com troco (ex.: Dinheiro) aceitam valor recebido superior ao valor a pagar.</summary>
    public bool PermiteTroco { get; set; }
    /// <summary>Exige referência da transacção (ex.: M-Pesa, Transferência).</summary>
    public bool ExigeReferencia { get; set; }
    /// <summary>Conta para o dinheiro físico esperado na gaveta no fecho.</summary>
    public bool ContaNaGaveta { get; set; }
    public int Ordem { get; set; }
    public bool Activo { get; set; } = true;
}

/// <summary>Regra: no máximo uma sessão aberta por terminal (índice único filtrado).</summary>
public class SessaoCaixa : EntidadeAuditavel, IConcorrente
{
    /// <summary>Dia de operação em que a sessão foi aberta (null nas sessões anteriores ao Abrir/Fechar Dia).</summary>
    public int? DiaOperacaoId { get; set; }
    public DiaOperacao? DiaOperacao { get; set; }
    public int TerminalId { get; set; }
    public Terminal Terminal { get; set; } = null!;
    public int UtilizadorAberturaId { get; set; }
    public DateTime AbertaEmUtc { get; set; }
    public decimal FundoInicial { get; set; }
    public EstadoSessaoCaixa Estado { get; set; } = EstadoSessaoCaixa.Aberta;
    public int? UtilizadorFechoId { get; set; }
    public DateTime? FechadaEmUtc { get; set; }
    public string? ObservacoesAbertura { get; set; }
    public string? ObservacoesFecho { get; set; }
    public List<MovimentoCaixa> Movimentos { get; set; } = [];
    public List<FechoCaixaLinha> LinhasFecho { get; set; } = [];
    public byte[] RowVersion { get; set; } = [];
}

/// <summary>
/// Dia de operação: «Abrir Dia» liberta a abertura dos caixas; «Fechar Dia» exige todos os caixas do dia fechados,
/// calcula o resumo de todos os caixas e congela-o (<see cref="Resumo"/>, JSON) com um número próprio. No máximo um dia aberto
/// (índice único filtrado) e uma só vez cada data. Um dia fechado não se reabre.
/// </summary>
public class DiaOperacao : EntidadeAuditavel, IConcorrente
{
    public DateOnly Data { get; set; }
    public EstadoDia Estado { get; set; } = EstadoDia.Aberto;
    public int UtilizadorAberturaId { get; set; }
    public DateTime AbertoEmUtc { get; set; }
    public string? ObservacoesAbertura { get; set; }
    public int? UtilizadorFechoId { get; set; }
    public DateTime? FechadoEmUtc { get; set; }
    public string? ObservacoesFecho { get; set; }
    /// <summary>Número do documento de fecho (série FD), atribuído ao fechar.</summary>
    public string? Numero { get; set; }
    /// <summary>Anulação (dia aberto por engano, sem movimentos): quando, por quem e porquê.</summary>
    public DateTime? AnuladoEmUtc { get; set; }
    public int? UtilizadorAnulacaoId { get; set; }
    public string? MotivoAnulacao { get; set; }
    /// <summary>Resumo calculado no fecho (JSON): o documento reimprime-se sempre igual.</summary>
    public string? Resumo { get; set; }
    public List<SessaoCaixa> Sessoes { get; set; } = [];
    public byte[] RowVersion { get; set; } = [];
}

/// <summary>Entradas e saídas manuais de caixa (não inclui pagamentos de pedidos).</summary>
public class MovimentoCaixa : Entidade
{
    public int SessaoCaixaId { get; set; }
    public SessaoCaixa SessaoCaixa { get; set; } = null!;
    public TipoMovimentoCaixa Tipo { get; set; }
    public int MetodoPagamentoId { get; set; }
    public MetodoPagamento MetodoPagamento { get; set; } = null!;
    /// <summary>Valor sempre positivo; o sinal resulta do tipo.</summary>
    public decimal Valor { get; set; }
    public string Motivo { get; set; } = string.Empty;
    public int? DespesaId { get; set; }
    public int UtilizadorId { get; set; }
    public DateTime DataUtc { get; set; }
}

/// <summary>Contagem por método de pagamento no fecho da sessão.</summary>
public class FechoCaixaLinha : Entidade
{
    public int SessaoCaixaId { get; set; }
    public SessaoCaixa SessaoCaixa { get; set; } = null!;
    public int MetodoPagamentoId { get; set; }
    public MetodoPagamento MetodoPagamento { get; set; } = null!;
    public decimal ValorEsperado { get; set; }
    public decimal ValorContado { get; set; }
    public decimal Diferenca { get; set; }
}

public class Pagamento : EntidadeAuditavel
{
    public int PedidoId { get; set; }
    public Pedido Pedido { get; set; } = null!;
    public int SessaoCaixaId { get; set; }
    public SessaoCaixa SessaoCaixa { get; set; } = null!;
    public int MetodoPagamentoId { get; set; }
    public MetodoPagamento MetodoPagamento { get; set; } = null!;
    /// <summary>Valor imputado ao pedido.</summary>
    public decimal Valor { get; set; }
    public decimal ValorRecebido { get; set; }
    public decimal Troco { get; set; }
    public string? Referencia { get; set; }
    public EstadoPagamento Estado { get; set; } = EstadoPagamento.Valido;
    public DateTime DataUtc { get; set; }
    public int UtilizadorId { get; set; }
    public DateTime? AnuladoEmUtc { get; set; }
    public int? AnuladoPorId { get; set; }
    public string? MotivoAnulacao { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
}

/// <summary>Recibo interno (não é documento fiscal certificado).</summary>
public class Recibo : Entidade
{
    public string Numero { get; set; } = string.Empty;
    public int PedidoId { get; set; }
    public Pedido Pedido { get; set; } = null!;
    public decimal Total { get; set; }
    public DateTime EmitidoEmUtc { get; set; }
    public int UtilizadorId { get; set; }
    public bool Anulado { get; set; }
    public DateTime? AnuladoEmUtc { get; set; }
    public int? AnuladoPorId { get; set; }
    public string? MotivoAnulacao { get; set; }
}

/// <summary>Numeração sequencial por série e ano. Incrementada sob concorrência optimista.</summary>
public class SerieDocumento : Entidade, IConcorrente
{
    public string Codigo { get; set; } = string.Empty;
    public string Prefixo { get; set; } = string.Empty;
    public int Ano { get; set; }
    public int UltimoNumero { get; set; }
    public byte[] RowVersion { get; set; } = [];
}
