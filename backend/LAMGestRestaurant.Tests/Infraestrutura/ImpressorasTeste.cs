using System.Collections.Concurrent;
using LAMGestRestaurant.Application.Impressao;

namespace LAMGestRestaurant.Tests.Infraestrutura;

public sealed record Impresso(string Impressora, int LarguraPapelMm, DocumentoImpressao Documento, byte[]? Logotipo = null)
{
    public string Texto => TextoDocumento.De(Documento);
}

/// <summary>
/// Impressoras «instaladas» no servidor de testes. A «Avariada» falha sempre; a «Pendurada» fica presa (como um controlador
/// que não devolve) até o teste a soltar.
/// </summary>
public sealed class ImpressorasTeste : IDispositivoImpressao
{
    public const string Caixa = "Térmica Caixa", Cozinha = "Térmica Cozinha", Bar = "Térmica Bar", A4 = "Laser A4", Avariada = "Avariada", Pendurada = "Pendurada";

    private readonly ConcurrentQueue<Impresso> impressos = new();

    public ManualResetEventSlim SoltarPendurada { get; } = new(false);

    public IReadOnlyList<string> Instaladas() => [A4, Avariada, Bar, Caixa, Cozinha, Pendurada];

    public void Imprimir(string nomeWindows, int larguraPapelMm, DocumentoImpressao documento, byte[]? logotipo = null)
    {
        if (nomeWindows == Avariada)
            throw new InvalidOperationException("Sem papel");
        if (nomeWindows == Pendurada)
            SoltarPendurada.Wait(TimeSpan.FromMinutes(2));
        impressos.Enqueue(new Impresso(nomeWindows, larguraPapelMm, documento, logotipo));
    }

    public IReadOnlyList<Impresso> Com(string texto) => [.. impressos.Where(i => i.Texto.Contains(texto, StringComparison.Ordinal))];

    /// <summary>A impressão corre em segundo plano: espera até aparecerem <paramref name="quantos"/> documentos com o texto.</summary>
    public async Task<IReadOnlyList<Impresso>> EsperarAsync(string texto, int quantos = 1)
    {
        for (var i = 0; i < 100; i++)
        {
            var encontrados = Com(texto);
            if (encontrados.Count >= quantos)
                return encontrados;
            await Task.Delay(100);
        }
        throw new TimeoutException($"Esperava {quantos} impressão(ões) com «{texto}»; houve {Com(texto).Count}.");
    }
}
