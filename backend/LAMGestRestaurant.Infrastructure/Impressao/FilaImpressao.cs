using System.Globalization;
using System.Security.Claims;
using System.Threading.Channels;
using LAMGestRestaurant.Application.Comum;
using LAMGestRestaurant.Application.Impressao;
using LAMGestRestaurant.Domain.Comum;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LAMGestRestaurant.Infrastructure.Impressao;

/// <summary>
/// Fila de impressão em segundo plano: os pedidos ficam gravados antes de imprimir e uma impressora desligada
/// ou sem papel nunca atrasa nem falha uma venda. Os erros ficam no log e no estado da impressora (Configurações).
/// Impressoras em modo PDF: o documento é gerado em PDF e enviado (tempo real) a quem pediu a impressão.
/// </summary>
public sealed class FilaImpressao(
    IServiceScopeFactory ambitos,
    IDispositivoImpressao dispositivo,
    IGeradorPdf geradorPdf,
    DocumentosPdf documentosPdf,
    EstadoImpressoras estados,
    IHttpContextAccessor acessor,
    IConfiguration configuracao,
    ILogger<FilaImpressao> log)
    : BackgroundService, IFilaImpressao
{
    private sealed record Entrada(PedidoImpressao Pedido, int? UtilizadorId);

    private readonly Channel<Entrada> canal = Channel.CreateUnbounded<Entrada>(new UnboundedChannelOptions { SingleReader = true });
    private readonly Dictionary<string, Task> filas = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Tempo máximo de uma impressão (Impressao:TempoMaximoSegundos; por omissão 60 s).</summary>
    private TimeSpan TempoMaximo { get; } = TimeSpan.FromSeconds(configuracao.GetValue("Impressao:TempoMaximoSegundos", 60));

    /// <summary>Guarda também quem fez o pedido (para lhe entregar os PDF).</summary>
    public void Enfileirar(PedidoImpressao pedido)
    {
        var id = acessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier);
        canal.Writer.TryWrite(new Entrada(pedido, int.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : null));
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await foreach (var (pedido, utilizadorId) in canal.Reader.ReadAllAsync(ct))
        {
            IReadOnlyList<TrabalhoImpressao> trabalhos;
            try
            {
                using var ambito = ambitos.CreateScope();
                var servico = ambito.ServiceProvider.GetRequiredService<ServicoImpressao>();
                trabalhos = pedido switch
                {
                    ImprimirTaloes t => await servico.TaloesAsync(t.PedidoId, t.Itens, t.Tipo, t.Motivo, ct),
                    ImprimirDocumento d => await servico.DocumentoAsync(d.ImpressoraId, d.Documento, d.ChaveCopias, ct, d.LarguraPapelMm) is { } documento ? [documento] : [],
                    ImprimirRecibo r => await servico.ReciboAsync(r.PedidoId, r.TerminalId, ct) is { } recibo ? [recibo] : [],
                    ImprimirTeste t => await servico.TesteAsync(t.ImpressoraId, ct) is { } teste ? [teste] : [],
                    _ => [],
                };
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogError(ex, "Não foi possível preparar a impressão {Pedido}", pedido);
                await AvisarFalhaAsync(utilizadorId, "Impressão", "", ex.Message);
                continue;
            }

            foreach (var t in trabalhos)
            {
                try
                {
                    if (t.Modo == ModoImpressora.Pdf)
                    {
                        await EntregarPdfAsync(t, utilizadorId);
                        estados.Registar(t.ImpressoraId, null);
                    }
                    else if (t.PostoId is not null)
                    {
                        // Impressora do posto: o agente desse computador imprime-a e devolve o resultado (estado registado aí).
                        using var ambito = ambitos.CreateScope();
                        await ambito.ServiceProvider.GetRequiredService<ServicoAgentes>().EnfileirarAsync(t, utilizadorId, ct);
                        log.LogInformation("«{Documento}» enviado ao posto {Posto} ({Impressora})", t.Documento.Titulo, t.PostoId, t.NomeWindows);
                    }
                    else
                        Encadear(t, utilizadorId, ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    await FalhouAsync(t, utilizadorId, ex.Message, ex);
                }
            }
        }
    }

    /// <summary>
    /// Cada impressora do Windows tem a sua própria fila: uma impressora parada ou com o controlador preso não atrasa as outras.
    /// Na mesma impressora os documentos saem pela ordem em que foram pedidos.
    /// </summary>
    private void Encadear(TrabalhoImpressao t, int? utilizadorId, CancellationToken ct)
    {
        lock (filas)
        {
            var anterior = filas.GetValueOrDefault(t.NomeWindows) ?? Task.CompletedTask;
            filas[t.NomeWindows] = anterior.ContinueWith(_ => ImprimirAsync(t, utilizadorId, ct), CancellationToken.None,
                TaskContinuationOptions.None, TaskScheduler.Default).Unwrap();
        }
    }

    /// <summary>
    /// Imprime com tempo máximo: se o controlador não devolver (impressora desligada, fila do Windows em erro, janela do controlador
    /// que o serviço não pode mostrar), a impressão é dada como falhada e os documentos seguintes continuam a sair.
    /// </summary>
    private async Task ImprimirAsync(TrabalhoImpressao t, int? utilizadorId, CancellationToken ct)
    {
        try
        {
            for (var copia = 1; copia <= Math.Max(1, t.Copias); copia++)
                await Task.Run(() => dispositivo.Imprimir(t.NomeWindows, t.LarguraPapelMm, t.Documento, t.Logotipo), CancellationToken.None)
                    .WaitAsync(TempoMaximo, ct);
            estados.Registar(t.ImpressoraId, null);
            log.LogInformation("Impresso «{Documento}» em {Impressora}", t.Documento.Titulo, t.NomeWindows);
        }
        catch (TimeoutException)
        {
            await FalhouAsync(t, utilizadorId,
                $"A impressora não respondeu em {TempoMaximo.TotalSeconds:0} s (desligada, sem papel ou com documentos presos na fila do Windows).", null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await FalhouAsync(t, utilizadorId, ex.Message, ex);
        }
    }

    private async Task FalhouAsync(TrabalhoImpressao t, int? utilizadorId, string erro, Exception? ex)
    {
        estados.Registar(t.ImpressoraId, erro);
        log.LogError(ex, "Falhou a impressão de «{Documento}» em {Impressora}: {Erro}", t.Documento.Titulo, t.NomeWindows, erro);
        await AvisarFalhaAsync(utilizadorId, t.Documento.Titulo, t.NomeImpressora.Length > 0 ? t.NomeImpressora : t.NomeWindows, erro);
    }

    /// <summary>Mostra o erro no posto de quem pediu a impressão (uma falha a avisar nunca pára a fila).</summary>
    private async Task AvisarFalhaAsync(int? utilizadorId, string titulo, string impressora, string erro)
    {
        if (utilizadorId is not int id)
            return;
        try
        {
            using var ambito = ambitos.CreateScope();
            await ambito.ServiceProvider.GetRequiredService<INotificador>().ImpressaoFalhouAsync(id, titulo, impressora, erro);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogWarning(ex, "Não foi possível avisar o utilizador {Utilizador} da falha de impressão", id);
        }
    }

    private async Task EntregarPdfAsync(TrabalhoImpressao t, int? utilizadorId)
    {
        var pdf = documentosPdf.Guardar(t.Documento.Titulo, t.NomeImpressora, utilizadorId,
            geradorPdf.Gerar(t.LarguraPapelMm, t.Documento, t.Logotipo));
        log.LogInformation("PDF «{Documento}» gerado para {Impressora}", t.Documento.Titulo, t.NomeImpressora);
        if (utilizadorId is not int id)
            return; // sem utilizador (ex.: tarefa interna): não há a quem mostrar
        using var ambito = ambitos.CreateScope();
        await ambito.ServiceProvider.GetRequiredService<INotificador>().DocumentoPdfAsync(id, pdf.Id, pdf.Titulo, pdf.Impressora);
    }
}
