using System.Collections.Concurrent;

namespace LAMGestRestaurant.Application.Impressao;

public enum Alinhamento { Esquerda, Centro, Direita }

/// <summary>Linha de um documento a imprimir. <see cref="Direita"/> é texto alinhado à direita na mesma linha (ex.: valores).</summary>
public sealed record LinhaImpressao(
    string Texto,
    string? Direita = null,
    Alinhamento Alinhamento = Alinhamento.Esquerda,
    bool Negrito = false,
    bool Grande = false,
    bool Separador = false)
{
    public static LinhaImpressao Traco { get; } = new("", Separador: true);
}

/// <summary>Onde o logótipo do estabelecimento aparece: recibos em A4 e 80 mm; relatórios só em A4; talões nunca.</summary>
public enum UsoLogotipo { Nenhum, SoA4, A4E80mm }

/// <summary>Documento independente da impressora: o dispositivo decide a disposição conforme a largura do papel.</summary>
public sealed record DocumentoImpressao(string Titulo, IReadOnlyList<LinhaImpressao> Linhas, UsoLogotipo Logotipo = UsoLogotipo.Nenhum)
{
    /// <summary>O logótipo entra neste papel? (58 mm nunca: é demasiado estreito.)</summary>
    public bool LevaLogotipo(int larguraPapelMm) => Logotipo switch
    {
        UsoLogotipo.A4E80mm => larguraPapelMm >= Domain.Sistema.Impressora.Largura80,
        UsoLogotipo.SoA4 => larguraPapelMm >= Domain.Sistema.Impressora.LarguraA4,
        _ => false,
    };
}

public enum TipoTalao { Pedido, Anulacao, Reimpressao }

/// <summary>Pedidos de impressão processados em segundo plano (uma impressora parada nunca bloqueia uma venda).</summary>
public abstract record PedidoImpressao;
public sealed record ImprimirTaloes(int PedidoId, IReadOnlyList<int> Itens, TipoTalao Tipo = TipoTalao.Pedido, string? Motivo = null) : PedidoImpressao;
public sealed record ImprimirRecibo(int PedidoId, int? TerminalId) : PedidoImpressao;
public sealed record ImprimirTeste(int ImpressoraId) : PedidoImpressao;
/// <summary>Documento já composto (relatórios, fecho de caixa) para uma impressora escolhida.</summary>
public sealed record ImprimirDocumento(int ImpressoraId, DocumentoImpressao Documento) : PedidoImpressao;

public interface IFilaImpressao
{
    void Enfileirar(PedidoImpressao pedido);
}

/// <summary>Impressoras do Windows do servidor.</summary>
public interface IDispositivoImpressao
{
    IReadOnlyList<string> Instaladas();
    /// <summary><paramref name="logotipo"/>: imagem a pôr no topo (já decidido se se aplica a este papel), ou null.</summary>
    void Imprimir(string nomeWindows, int larguraPapelMm, DocumentoImpressao documento, byte[]? logotipo = null);
}

/// <summary>Documento pronto a enviar para uma impressora configurada.</summary>
public sealed record TrabalhoImpressao(int ImpressoraId, string NomeWindows, int LarguraPapelMm, DocumentoImpressao Documento, byte[]? Logotipo = null);

public sealed record EstadoImpressora(DateTime EmUtc, string? Erro);

/// <summary>Resultado da última impressão de cada impressora (em memória; mostrado nas Configurações).</summary>
public sealed class EstadoImpressoras
{
    private readonly ConcurrentDictionary<int, EstadoImpressora> estados = new();

    public void Registar(int impressoraId, string? erro) => estados[impressoraId] = new EstadoImpressora(DateTime.UtcNow, erro);

    public EstadoImpressora? Obter(int impressoraId) => estados.GetValueOrDefault(impressoraId);
}
