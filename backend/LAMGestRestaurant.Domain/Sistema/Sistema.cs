using LAMGestRestaurant.Domain.Catalogo;
using LAMGestRestaurant.Domain.Comum;

namespace LAMGestRestaurant.Domain.Sistema;

/// <summary>Configuração chave/valor. As chaves conhecidas estão em ChavesConfiguracao.</summary>
public class Configuracao : Entidade
{
    public string Chave { get; set; } = string.Empty;
    public string Valor { get; set; } = string.Empty;
    public string? Descricao { get; set; }
    public DateTime? AlteradoEmUtc { get; set; }
    public int? AlteradoPorId { get; set; }
}

public static class ChavesConfiguracao
{
    public const string NomeEstabelecimento = "estabelecimento.nome";
    public const string Endereco = "estabelecimento.endereco";
    public const string Telefone = "estabelecimento.telefone";
    public const string Nuit = "estabelecimento.nuit";
    public const string LogotipoUrl = "estabelecimento.logotipo";
    public const string Moeda = "geral.moeda";
    public const string SimboloMoeda = "geral.simbolo-moeda";
    public const string FusoHorario = "geral.fuso-horario";
    public const string TaxaEntregaPadrao = "delivery.taxa-padrao";
    public const string PermitirStockNegativo = "stock.permitir-negativo";
    public const string FidelizacaoActiva = "fidelizacao.activa";
    public const string PontosPorMoeda = "fidelizacao.valor-por-ponto";
    public const string RodapeRecibo = "recibo.rodape";
    public const string ImprimirReciboAoPagar = "impressao.recibo-automatico";
    /// <summary>Texto legal impresso nas linhas com IVA a 0 % (ex.: artigo do Código do IVA que justifica a isenção).</summary>
    public const string MotivoIsencao = "facturacao.motivo-isencao";
    /// <summary>Prazo de pagamento por omissão das facturas, em dias.</summary>
    public const string PrazoFacturaDias = "facturacao.prazo-dias";
    /// <summary>Validade por omissão das cotações, em dias.</summary>
    public const string ValidadeCotacaoDias = "facturacao.validade-cotacao-dias";
}

/// <summary>
/// Impressora instalada no Windows do servidor (local ou partilhada), usada pelo servidor para imprimir sem diálogo:
/// Caixa → recibos; Bar e Cozinha → talões de preparação enviados pelo POS.
/// </summary>
public class Impressora : Entidade, IDesactivavel
{
    public const int Largura58 = 58, Largura80 = 80, LarguraA4 = 210;

    public string Nome { get; set; } = string.Empty;
    public FuncaoImpressora Funcao { get; set; }
    /// <summary>Reservado para impressão directa por rede (ESC/POS); hoje só Windows.</summary>
    public LigacaoImpressora Ligacao { get; set; } = LigacaoImpressora.Windows;
    /// <summary>Nome exacto da impressora no Windows do servidor.</summary>
    public string NomeWindows { get; set; } = string.Empty;
    /// <summary>58 ou 80 (talão térmico) ou 210 (A4).</summary>
    public int LarguraPapelMm { get; set; } = Largura80;
    public ModoImpressora Modo { get; set; } = ModoImpressora.Directo;
    /// <summary>Cozinha/Bar: só os itens desta estação (vazio = todos os itens da função).</summary>
    public int? EstacaoPreparacaoId { get; set; }
    public EstacaoPreparacao? EstacaoPreparacao { get; set; }
    /// <summary>Caixa: recibos dos pagamentos feitos neste terminal (vazio = impressora de recibos por omissão).</summary>
    public int? TerminalId { get; set; }
    public Caixa.Terminal? Terminal { get; set; }
    public bool Activo { get; set; } = true;
}

/// <summary>
/// Licença activada no sistema (histórico: cada activação é uma linha nova; vale a mais recente).
/// O texto assinado é guardado tal como foi emitido e volta a ser verificado sempre que o estado é calculado.
/// </summary>
public class LicencaSistema : Entidade
{
    /// <summary>Id da emissão (único: a mesma licença não se activa duas vezes).</summary>
    public Guid LicencaId { get; set; }
    public string NomeComercial { get; set; } = string.Empty;
    public string Nuit { get; set; } = string.Empty;
    public DateOnly EmitidaEm { get; set; }
    public DateOnly ValidaAte { get; set; }
    public string Texto { get; set; } = string.Empty;
    public DateTime ActivadaEmUtc { get; set; }
    public int? ActivadaPorId { get; set; }
    /// <summary>Hora mais recente vista pelo sistema com esta licença: um relógio atrasado (para prolongar a licença) é detectado.</summary>
    public DateTime UltimaVerificacaoUtc { get; set; }
}

/// <summary>Registo de auditoria só de inserção.</summary>
public class RegistoAuditoria
{
    public long Id { get; set; }
    public DateTime DataUtc { get; set; }
    public int? UtilizadorId { get; set; }
    public string? NomeUtilizador { get; set; }
    public string Accao { get; set; } = string.Empty;
    public string? Entidade { get; set; }
    public string? EntidadeId { get; set; }
    /// <summary>JSON com os detalhes estritamente necessários (sem palavras-passe nem dados sensíveis).</summary>
    public string? Detalhes { get; set; }
    public string? EnderecoIp { get; set; }
}
