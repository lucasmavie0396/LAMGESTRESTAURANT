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

    /// <summary>O fecho de caixa impresso inclui os produtos vendidos por categoria.</summary>
    public const string FechoImprimeProdutos = "impressao.fecho-produtos";
    /// <summary>O fecho de caixa impresso mostra só o valor contado (sem recebimentos, esperado nem diferença).</summary>
    public const string FechoContagemCega = "impressao.fecho-contagem-cega";
    /// <summary>Os documentos em papel de 80 mm levam o logótipo.</summary>
    public const string Logotipo80mm = "impressao.logotipo-80mm";

    /// <summary>Prefixo das cópias por documento: o resto da chave é o documento (ex.: impressao.copias.conta).</summary>
    public const string PrefixoCopias = "impressao.copias.";
    public const string CopiasTalao = PrefixoCopias + "talao";
    public const string CopiasAnulacao = PrefixoCopias + "anulacao";
    public const string CopiasConta = PrefixoCopias + "conta";
    public const string CopiasVendaDinheiro = PrefixoCopias + "venda-dinheiro";
    public const string CopiasFactura = PrefixoCopias + "factura";
    public const string CopiasRecibo = PrefixoCopias + "recibo";
    public const string CopiasCotacao = PrefixoCopias + "cotacao";
    public const string CopiasNotaCredito = PrefixoCopias + "nota-credito";
    public const string CopiasFecho = PrefixoCopias + "fecho";
    public const string CopiasRelatorios = PrefixoCopias + "relatorios";
    public static readonly IReadOnlyList<string> Copias =
        [CopiasTalao, CopiasAnulacao, CopiasConta, CopiasVendaDinheiro, CopiasFactura, CopiasRecibo, CopiasCotacao, CopiasNotaCredito, CopiasFecho, CopiasRelatorios];

    public const string EtiquetaLargura = "etiqueta.largura-mm";
    public const string EtiquetaAltura = "etiqueta.altura-mm";
    /// <summary>Campos da etiqueta, pela ordem (nome, categoria, codigo, preco, unidade, codigo-barras).</summary>
    public const string EtiquetaCampos = "etiqueta.campos";

    public const string PosMesa = "pos.mesa";
    public const string PosTakeAway = "pos.take-away";
    public const string PosDelivery = "pos.delivery";
    public const string PosBalcao = "pos.balcao";
    public const string PosCodigoBarras = "pos.codigo-barras";
    public static readonly IReadOnlyList<string> ModosPos = [PosMesa, PosTakeAway, PosDelivery, PosBalcao];
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
    /// <summary>Onde está instalada: vazio = Windows do servidor; preenchido = posto (computador) com o agente de impressão.</summary>
    public int? PostoId { get; set; }
    public Caixa.Terminal? Posto { get; set; }
    public bool Activo { get; set; } = true;
}

/// <summary>
/// Trabalho de impressão para uma impressora instalada num posto: o servidor guarda-o e o agente do posto vai buscá-lo e imprime-o.
/// Fica como histórico (quando foi entregue, concluído ou o erro).
/// </summary>
public class TrabalhoImpressaoPosto : Entidade
{
    public int TerminalId { get; set; }
    public Caixa.Terminal? Terminal { get; set; }
    public int ImpressoraId { get; set; }
    public string NomeWindows { get; set; } = string.Empty;
    public int LarguraPapelMm { get; set; }
    public string Titulo { get; set; } = string.Empty;
    /// <summary>DocumentoImpressao em JSON (o agente desenha-o com a mesma disposição do servidor).</summary>
    public string Documento { get; set; } = string.Empty;
    /// <summary>Leva o logótipo do estabelecimento (o agente pede-o ao servidor).</summary>
    public bool ComLogotipo { get; set; }
    public int? UtilizadorId { get; set; }
    public DateTime CriadoEmUtc { get; set; }
    public DateTime? EntregueEmUtc { get; set; }
    public DateTime? ConcluidoEmUtc { get; set; }
    public string? Erro { get; set; }
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
