using System.Runtime.Versioning;
using LAMGestRestaurant.Application.Impressao;

namespace LAMGestRestaurant.Infrastructure.Impressao;

/// <summary>
/// Impressoras do Windows do servidor (locais ou partilhadas de outro computador, instaladas para todos os utilizadores).
/// O desenho é o da biblioteca partilhada com o agente de impressão dos postos (<see cref="ImpressaoWindows"/>).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class ImpressoraWindows : IDispositivoImpressao
{
    public IReadOnlyList<string> Instaladas() => ImpressaoWindows.Instaladas();

    public void Imprimir(string nomeWindows, int larguraPapelMm, DocumentoImpressao documento, byte[]? logotipo = null) =>
        ImpressaoWindows.Imprimir(nomeWindows, larguraPapelMm, documento, logotipo);

    /// <summary>Diagnóstico: imprime para ficheiro (ex.: «Microsoft Print to PDF» gera o PDF sem diálogo).</summary>
    public void ImprimirParaFicheiro(string nomeWindows, int larguraPapelMm, DocumentoImpressao documento, string ficheiro, byte[]? logotipo = null) =>
        ImpressaoWindows.Imprimir(nomeWindows, larguraPapelMm, documento, logotipo, ficheiro);
}

/// <summary>Fora do Windows não há impressoras (a aplicação foi desenhada para servidor Windows).</summary>
public sealed class SemImpressoras : IDispositivoImpressao
{
    public IReadOnlyList<string> Instaladas() => [];

    public void Imprimir(string nomeWindows, int larguraPapelMm, DocumentoImpressao documento, byte[]? logotipo = null) =>
        throw new PlatformNotSupportedException("A impressão directa só está disponível num servidor Windows.");
}
