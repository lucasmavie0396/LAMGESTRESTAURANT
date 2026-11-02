using LAMGestRestaurant.Application.Comum;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace LAMGestRestaurant.Api.TempoReal;

/// <summary>
/// Canal de tempo real para os terminais da LAN (POS, mesas, cozinha). Só utilizadores autenticados.
/// Os eventos são avisos leves ("algo mudou"); os clientes recarregam os dados pela API,
/// que continua a ser a única fonte de verdade e a aplicar as permissões.
/// </summary>
[Authorize]
public class HubOperacoes : Hub
{
    public const string Rota = "/hubs/operacoes";
}

public class NotificadorSignalR(IHubContext<HubOperacoes> hub, ILogger<NotificadorSignalR> log) : INotificador
{
    public Task PedidoAlteradoAsync(int pedidoId, bool afectaCozinha) =>
        Enviar("pedidoAlterado", new { pedidoId, afectaCozinha });

    public Task MesasAlteradasAsync() => Enviar("mesasAlteradas", new { });

    public Task ItensProntosAsync(int pedidoId, string pedidoNumero, string? mesa, IReadOnlyList<string> produtos) =>
        Enviar("itensProntos", new { pedidoId, pedidoNumero, mesa, produtos });

    private async Task Enviar(string evento, object dados)
    {
        try
        {
            await hub.Clients.All.SendAsync(evento, dados);
        }
        catch (Exception ex)
        {
            // A operação já foi gravada; uma falha de notificação não a pode anular.
            log.LogWarning(ex, "Falha ao enviar o evento {Evento} em tempo real", evento);
        }
    }
}
