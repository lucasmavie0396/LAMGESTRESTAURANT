using System.Collections.Concurrent;
using LAMGestRestaurant.Application.Comum;

namespace LAMGestRestaurant.Tests.Infraestrutura;

public sealed record PdfEntregue(int UtilizadorId, Guid DocumentoId, string Titulo, string Impressora)
{
    public string Url => $"/api/impressao/pdf/{DocumentoId:N}";
}

public sealed record FalhaImpressao(int UtilizadorId, string Titulo, string Impressora, string Erro);

/// <summary>PDF que o servidor avisou estarem prontos (evento «documentoPdf») e impressões falhadas («impressaoFalhou»).</summary>
public sealed class PdfsTeste
{
    private readonly ConcurrentQueue<PdfEntregue> entregues = new();
    private readonly ConcurrentQueue<FalhaImpressao> falhas = new();

    public void Registar(PdfEntregue pdf) => entregues.Enqueue(pdf);
    public void RegistarFalha(FalhaImpressao falha) => falhas.Enqueue(falha);

    public async Task<FalhaImpressao> EsperarFalhaAsync(string titulo)
    {
        for (var i = 0; i < 100; i++)
        {
            if (falhas.LastOrDefault(f => f.Titulo.Contains(titulo, StringComparison.Ordinal)) is { } falha)
                return falha;
            await Task.Delay(100);
        }
        throw new TimeoutException($"Nenhuma falha de impressão com «{titulo}».");
    }

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

    public Task ImpressaoFalhouAsync(int utilizadorId, string titulo, string impressora, string erro)
    {
        pdfs.RegistarFalha(new FalhaImpressao(utilizadorId, titulo, impressora, erro));
        return real.ImpressaoFalhouAsync(utilizadorId, titulo, impressora, erro);
    }
}
