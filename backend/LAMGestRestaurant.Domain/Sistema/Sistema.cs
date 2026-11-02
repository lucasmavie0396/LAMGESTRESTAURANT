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
}

public class Impressora : Entidade, IDesactivavel
{
    public string Nome { get; set; } = string.Empty;
    public TipoImpressora Tipo { get; set; }
    public LigacaoImpressora Ligacao { get; set; }
    /// <summary>IP:porta (Rede, ESC/POS na porta 9100) ou nome da impressora no Windows.</summary>
    public string Endereco { get; set; } = string.Empty;
    public int LarguraPapelMm { get; set; } = 80;
    public int? EstacaoPreparacaoId { get; set; }
    public EstacaoPreparacao? EstacaoPreparacao { get; set; }
    public bool Activo { get; set; } = true;
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
