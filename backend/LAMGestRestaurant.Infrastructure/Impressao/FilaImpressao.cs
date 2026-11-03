using System.Threading.Channels;
using LAMGestRestaurant.Application.Impressao;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LAMGestRestaurant.Infrastructure.Impressao;

/// <summary>
/// Fila de impressão em segundo plano: os pedidos ficam gravados antes de imprimir e uma impressora desligada
/// ou sem papel nunca atrasa nem falha uma venda. Os erros ficam no log e no estado da impressora (Configurações).
/// </summary>
public sealed class FilaImpressao(IServiceScopeFactory ambitos, IDispositivoImpressao dispositivo, EstadoImpressoras estados, ILogger<FilaImpressao> log)
    : BackgroundService, IFilaImpressao
{
    private readonly Channel<PedidoImpressao> canal = Channel.CreateUnbounded<PedidoImpressao>(new UnboundedChannelOptions { SingleReader = true });

    public void Enfileirar(PedidoImpressao pedido) => canal.Writer.TryWrite(pedido);

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await foreach (var pedido in canal.Reader.ReadAllAsync(ct))
        {
            IReadOnlyList<TrabalhoImpressao> trabalhos;
            try
            {
                using var ambito = ambitos.CreateScope();
                var servico = ambito.ServiceProvider.GetRequiredService<ServicoImpressao>();
                trabalhos = pedido switch
                {
                    ImprimirTaloes t => await servico.TaloesAsync(t.PedidoId, t.Itens, t.Tipo, t.Motivo, ct),
                    ImprimirDocumento d => await servico.DocumentoAsync(d.ImpressoraId, d.Documento, ct) is { } documento ? [documento] : [],
                    ImprimirRecibo r => await servico.ReciboAsync(r.PedidoId, r.TerminalId, ct) is { } recibo ? [recibo] : [],
                    ImprimirTeste t => await servico.TesteAsync(t.ImpressoraId, ct) is { } teste ? [teste] : [],
                    _ => [],
                };
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogError(ex, "Não foi possível preparar a impressão {Pedido}", pedido);
                continue;
            }

            foreach (var t in trabalhos)
            {
                try
                {
                    await Task.Run(() => dispositivo.Imprimir(t.NomeWindows, t.LarguraPapelMm, t.Documento, t.Logotipo), ct);
                    estados.Registar(t.ImpressoraId, null);
                    log.LogInformation("Impresso «{Documento}» em {Impressora}", t.Documento.Titulo, t.NomeWindows);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    estados.Registar(t.ImpressoraId, ex.Message);
                    log.LogError(ex, "Falhou a impressão de «{Documento}» em {Impressora}", t.Documento.Titulo, t.NomeWindows);
                }
            }
        }
    }
}
