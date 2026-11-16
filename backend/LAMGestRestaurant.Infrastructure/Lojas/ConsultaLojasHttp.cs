using System.Net.Http.Json;
using LAMGestRestaurant.Application.Comum;
using LAMGestRestaurant.Application.Sistema;

namespace LAMGestRestaurant.Infrastructure.Lojas;

/// <summary>Consulta outro servidor LAMGest pela VPN (pedidos sem sessão a /api/loja e /api/lojas), com pouco tempo de espera.</summary>
public class ConsultaLojasHttp(HttpClient http) : IConsultaLojas
{
    public static readonly TimeSpan Espera = TimeSpan.FromSeconds(6);

    public Task<IdentidadeLoja> IdentidadeAsync(string endereco, CancellationToken ct) => LerAsync<IdentidadeLoja>(endereco, "/api/loja", ct);

    public Task<ListaLojas> ListaAsync(string endereco, CancellationToken ct) => LerAsync<ListaLojas>(endereco, "/api/lojas", ct);

    private async Task<T> LerAsync<T>(string endereco, string caminho, CancellationToken ct)
    {
        using var limite = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limite.CancelAfter(Espera);
        HttpResponseMessage resposta;
        try
        {
            resposta = await http.GetAsync(endereco + caminho, limite.Token);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            throw new RegraNegocioException($"Não foi possível contactar a loja em {endereco}. Confirme o endereço e que a VPN está ligada nos dois servidores.");
        }
        using (resposta)
        {
            if (!resposta.IsSuccessStatusCode)
                throw new RegraNegocioException($"O servidor em {endereco} não é um LAMGest actualizado (resposta {(int)resposta.StatusCode}). Actualize-o com o Setup mais recente.");
            try
            {
                return await resposta.Content.ReadFromJsonAsync<T>(limite.Token) ?? throw new FormatException();
            }
            catch (Exception e) when (e is System.Text.Json.JsonException or FormatException or NotSupportedException)
            {
                throw new RegraNegocioException($"O endereço {endereco} não é um servidor LAMGest.");
            }
        }
    }
}
