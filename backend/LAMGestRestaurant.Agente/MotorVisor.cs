using System.Net;
using System.Net.Http.Json;

namespace LAMGestRestaurant.Agente;

/// <summary>Visor do posto enviado pelo servidor (igual ao VisorAgenteDto da API).</summary>
public sealed record VisorRecebido(long Versao, string Tipo, string? Porta, string Protocolo, int Velocidade, int Colunas, string? Ecra,
    string? Endereco, string Linha1, string Linha2);

/// <summary>Onde o visor aparece: visor de poste (porta COM) ou página do visor no segundo monitor.</summary>
public interface ISaidaVisor
{
    /// <summary>Escreve as linhas no visor de poste (só se mudaram ou a porta mudou).</summary>
    void Poste(VisorRecebido visor);
    /// <summary>Garante a página do visor aberta em ecrã inteiro no segundo monitor (volta a abri-la se a fecharem).</summary>
    void Monitor(VisorRecebido visor, Uri pagina);
    /// <summary>Sem visor: fecha a porta e a página.</summary>
    void Desligar();
}

/// <summary>
/// Ciclo do visor, sem interface (testado contra a API): pede o visor ao servidor (pedido longo: responde logo que o POS muda o pedido
/// no ecrã ou a configuração muda) e mostra-o. Sem servidor, tenta de novo de 5 em 5 s.
/// </summary>
public sealed class MotorVisor(HttpClient http, ISaidaVisor saida, Action<string>? registar = null)
{
    public const int EsperaSegundos = 20;
    private long versao;
    private string? ultimoErro;

    public VisorRecebido? Actual { get; private set; }

    public async Task CorrerAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await CicloAsync(EsperaSegundos, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                if (ex.Message != ultimoErro)
                    registar?.Invoke("Visor: " + ex.Message);
                ultimoErro = ex.Message;
                await Task.Delay(ex is AgenteNaoAutorizadoException ? TimeSpan.FromSeconds(60) : TimeSpan.FromSeconds(5), ct);
            }
        }
    }

    public async Task<VisorRecebido?> CicloAsync(int espera, CancellationToken ct)
    {
        using var r = await http.GetAsync($"api/agente/visor?versao={versao}&espera={espera}", ct);
        if (r.StatusCode == HttpStatusCode.Unauthorized)
            throw new AgenteNaoAutorizadoException();
        r.EnsureSuccessStatusCode();
        if (r.StatusCode == HttpStatusCode.NoContent)
            return Actual;
        var visor = await r.Content.ReadFromJsonAsync<VisorRecebido>(MotorAgente.Json, ct) ?? throw new InvalidOperationException("Resposta vazia do servidor.");
        versao = visor.Versao;
        Actual = visor;
        ultimoErro = null;
        Aplicar(visor);
        return visor;
    }

    private void Aplicar(VisorRecebido v)
    {
        try
        {
            switch (v.Tipo)
            {
                case "Poste" when v.Porta is { Length: > 0 }:
                    saida.Poste(v);
                    break;
                case "Monitor" when v.Endereco is { Length: > 0 }:
                    saida.Monitor(v, new Uri(http.BaseAddress!, v.Endereco.TrimStart('/')));
                    break;
                default:
                    saida.Desligar();
                    break;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            registar?.Invoke($"Visor ({v.Tipo}): {ex.Message}");
        }
    }
}
