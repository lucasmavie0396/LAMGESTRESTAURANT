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

/// <summary>Documento independente da impressora: o dispositivo decide a disposição conforme a largura do papel.</summary>
public sealed record DocumentoImpressao(string Titulo, IReadOnlyList<LinhaImpressao> Linhas);

/// <summary>Pedidos de impressão processados em segundo plano (uma impressora parada nunca bloqueia uma venda).</summary>
public abstract record PedidoImpressao;
public sealed record ImprimirTaloes(int PedidoId, IReadOnlyList<int> Itens) : PedidoImpressao;
public sealed record ImprimirRecibo(int PedidoId, int? TerminalId) : PedidoImpressao;
public sealed record ImprimirTeste(int ImpressoraId) : PedidoImpressao;

public interface IFilaImpressao
{
    void Enfileirar(PedidoImpressao pedido);
}

/// <summary>Impressoras do Windows do servidor.</summary>
public interface IDispositivoImpressao
{
    IReadOnlyList<string> Instaladas();
    void Imprimir(string nomeWindows, int larguraPapelMm, DocumentoImpressao documento);
}

/// <summary>Documento pronto a enviar para uma impressora configurada.</summary>
public sealed record TrabalhoImpressao(int ImpressoraId, string NomeWindows, int LarguraPapelMm, DocumentoImpressao Documento);

public sealed record EstadoImpressora(DateTime EmUtc, string? Erro);

/// <summary>Resultado da última impressão de cada impressora (em memória; mostrado nas Configurações).</summary>
public sealed class EstadoImpressoras
{
    private readonly ConcurrentDictionary<int, EstadoImpressora> estados = new();

    public void Registar(int impressoraId, string? erro) => estados[impressoraId] = new EstadoImpressora(DateTime.UtcNow, erro);

    public EstadoImpressora? Obter(int impressoraId) => estados.GetValueOrDefault(impressoraId);
}
