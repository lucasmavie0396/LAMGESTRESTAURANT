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

    /// <summary>Só para as ligações desse utilizador (identificado pelo NameIdentifier do cookie de sessão).</summary>
    public Task DocumentoPdfAsync(int utilizadorId, Guid documentoId, string titulo, string impressora) =>
        Enviar("documentoPdf", new { url = $"/api/impressao/pdf/{documentoId:N}", titulo, impressora }, utilizadorId);

    public Task ImpressaoFalhouAsync(int utilizadorId, string titulo, string impressora, string erro) =>
        Enviar("impressaoFalhou", new { titulo, impressora, erro }, utilizadorId);

    private async Task Enviar(string evento, object dados, int? utilizadorId = null)
    {
        try
        {
            var destino = utilizadorId is int id ? hub.Clients.User(id.ToString(System.Globalization.CultureInfo.InvariantCulture)) : hub.Clients.All;
            await destino.SendAsync(evento, dados);
        }
        catch (Exception ex)
        {
            // A operação já foi gravada; uma falha de notificação não a pode anular.
            log.LogWarning(ex, "Falha ao enviar o evento {Evento} em tempo real", evento);
        }
    }
}
