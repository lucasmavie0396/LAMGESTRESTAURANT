using System.Collections.Concurrent;
using LAMGestRestaurant.Domain.Comum;

namespace LAMGestRestaurant.Application.Impressao;

public enum Alinhamento { Esquerda, Centro, Direita }

/// <summary>
/// Linha de um documento a imprimir. <see cref="Direita"/> é texto alinhado à direita na mesma linha (ex.: valores).
/// Com <see cref="Celulas"/> é uma linha de tabela: a 1.ª célula é a descrição (à esquerda) e as restantes são valores (à direita).
/// Em A4 as células ficam em colunas; nos talões a descrição ocupa uma linha e os valores vão em colunas por baixo.
/// Linhas de tabela seguidas formam uma tabela (as colunas alinham entre si). <see cref="Cabecalho"/> marca os títulos das colunas.
/// </summary>
public sealed record LinhaImpressao(
    string Texto,
    string? Direita = null,
    Alinhamento Alinhamento = Alinhamento.Esquerda,
    bool Negrito = false,
    bool Grande = false,
    bool Separador = false,
    IReadOnlyList<string>? Celulas = null,
    bool Cabecalho = false)
{
    public static LinhaImpressao Traco { get; } = new("", Separador: true);

    public static LinhaImpressao Tabela(IReadOnlyList<string> celulas, bool cabecalho = false, bool negrito = false) =>
        new(celulas[0], Celulas: celulas, Negrito: negrito || cabecalho, Cabecalho: cabecalho);
}

/// <summary>
/// Documento independente da impressora: o dispositivo decide a disposição conforme a largura do papel.
/// <see cref="Cabecalho"/>: identificação do estabelecimento (nome, endereço, contactos, NUIT). Em A4 fica à direita do logótipo;
/// nos talões fica centrada por baixo dele. O logótipo carregado nas configurações sai em todos os documentos e papéis.
/// </summary>
public sealed record DocumentoImpressao(string Titulo, IReadOnlyList<LinhaImpressao> Linhas, IReadOnlyList<LinhaImpressao>? Cabecalho = null);

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

/// <summary>Converte um documento em PDF com a largura do papel (talão de 58/80 mm numa só página, ou A4).</summary>
public interface IGeradorPdf
{
    byte[] Gerar(int larguraPapelMm, DocumentoImpressao documento, byte[]? logotipo = null);
}

/// <summary>Documento pronto a enviar para uma impressora configurada.</summary>
public sealed record TrabalhoImpressao(int ImpressoraId, string NomeWindows, int LarguraPapelMm, DocumentoImpressao Documento, byte[]? Logotipo = null,
    ModoImpressora Modo = ModoImpressora.Directo, string NomeImpressora = "");

/// <summary>PDF gerado por uma impressora em modo PDF, à espera de ser aberto por quem o pediu.</summary>
public sealed record DocumentoPdf(Guid Id, string Titulo, string Impressora, int? UtilizadorId, byte[] Dados, DateTime CriadoEmUtc);

/// <summary>
/// PDFs das impressoras em modo PDF, guardados em memória durante 2 horas (no máximo os 200 mais recentes).
/// Só quem pediu a impressão os pode abrir.
/// </summary>
public sealed class DocumentosPdf(TimeProvider relogio)
{
    private static readonly TimeSpan Validade = TimeSpan.FromHours(2);
    private const int Maximo = 200;
    private readonly ConcurrentDictionary<Guid, DocumentoPdf> documentos = new();

    public DocumentoPdf Guardar(string titulo, string impressora, int? utilizadorId, byte[] dados)
    {
        var agora = relogio.GetUtcNow().UtcDateTime;
        foreach (var antigo in documentos.Values.Where(d => agora - d.CriadoEmUtc > Validade)
                     .Concat(documentos.Values.OrderByDescending(d => d.CriadoEmUtc).Skip(Maximo - 1)))
            documentos.TryRemove(antigo.Id, out _);
        var documento = new DocumentoPdf(Guid.NewGuid(), titulo, impressora, utilizadorId, dados, agora);
        documentos[documento.Id] = documento;
        return documento;
    }

    public DocumentoPdf? Obter(Guid id, int? utilizadorId) =>
        documentos.TryGetValue(id, out var d) && d.UtilizadorId == utilizadorId && relogio.GetUtcNow().UtcDateTime - d.CriadoEmUtc <= Validade
            ? d
            : null;
}

public sealed record EstadoImpressora(DateTime EmUtc, string? Erro);

/// <summary>Resultado da última impressão de cada impressora (em memória; mostrado nas Configurações).</summary>
public sealed class EstadoImpressoras
{
    private readonly ConcurrentDictionary<int, EstadoImpressora> estados = new();

    public void Registar(int impressoraId, string? erro) => estados[impressoraId] = new EstadoImpressora(DateTime.UtcNow, erro);

    public EstadoImpressora? Obter(int impressoraId) => estados.GetValueOrDefault(impressoraId);
}
