using System.Collections.Concurrent;
using LAMGestRestaurant.Application.Comum;

namespace LAMGestRestaurant.Tests.Infraestrutura;

public sealed record PdfEntregue(int UtilizadorId, Guid DocumentoId, string Titulo, string Impressora)
{
    public string Url => $"/api/impressao/pdf/{DocumentoId:N}";
}

/// <summary>PDF que o servidor avisou estarem prontos (evento «documentoPdf»).</summary>
public sealed class PdfsTeste
{
    private readonly ConcurrentQueue<PdfEntregue> entregues = new();

    public void Registar(PdfEntregue pdf) => entregues.Enqueue(pdf);

    /// <summary>A geração corre na fila de impressão: espera até aparecer um PDF com este título.</summary>
    public async Task<PdfEntregue> EsperarAsync(string titulo)
    {
        for (var i = 0; i < 100; i++)
        {
            if (entregues.LastOrDefault(p => p.Titulo.Contains(titulo, StringComparison.Ordinal)) is { } pdf)
                return pdf;
            await Task.Delay(100);
        }
        throw new TimeoutException($"Nenhum PDF com «{titulo}».");
    }
}

public sealed class NotificadorComRegisto(INotificador real, PdfsTeste pdfs) : INotificador
{
    public Task PedidoAlteradoAsync(int pedidoId, bool afectaCozinha) => real.PedidoAlteradoAsync(pedidoId, afectaCozinha);
    public Task MesasAlteradasAsync() => real.MesasAlteradasAsync();
    public Task ItensProntosAsync(int pedidoId, string pedidoNumero, string? mesa, IReadOnlyList<string> produtos) =>
        real.ItensProntosAsync(pedidoId, pedidoNumero, mesa, produtos);

    public Task DocumentoPdfAsync(int utilizadorId, Guid documentoId, string titulo, string impressora)
    {
        pdfs.Registar(new PdfEntregue(utilizadorId, documentoId, titulo, impressora));
        return real.DocumentoPdfAsync(utilizadorId, documentoId, titulo, impressora);
    }
}
