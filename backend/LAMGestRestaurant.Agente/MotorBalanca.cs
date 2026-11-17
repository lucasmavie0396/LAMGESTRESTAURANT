using System.Net;
using System.Net.Http.Json;

namespace LAMGestRestaurant.Agente;

/// <summary>Balança do posto enviada pelo servidor (igual ao BalancaAgenteDto da API).</summary>
public sealed record BalancaRecebida(long Versao, string Modo, string? Porta, int Velocidade, string Formato, bool Activa);

/// <summary>Porta série da balança (no Windows, <see cref="PortaBalancaWindows"/>; nos testes, uma balança simulada).</summary>
public interface IPortaBalanca : IDisposable
{
    void Escrever(byte[] dados);
    /// <summary>Texto recebido desde a última leitura; espera até <paramref name="espera"/> pelo primeiro byte (vazio se não chegar nada).</summary>
    string Ler(TimeSpan espera);
}

/// <summary>
/// Ciclo da balança, sem interface (testado contra a API): espera (pedido longo) que o POS peça o peso e, enquanto o pedir,
/// lê a balança e envia o peso ao servidor várias vezes por segundo. Fora disso a porta COM fica fechada.
/// </summary>
public sealed class MotorBalanca(HttpClient http, Func<BalancaRecebida, IPortaBalanca> abrir, Action<string>? registar = null)
{
    public const int EsperaSegundos = 20;
    public static readonly TimeSpan Intervalo = TimeSpan.FromMilliseconds(250);
    public static readonly TimeSpan EsperaResposta = TimeSpan.FromMilliseconds(400);
    /// <summary>Sem nenhum peso durante este tempo, o POS é avisado de que a balança não responde.</summary>
    public static readonly TimeSpan SemPeso = TimeSpan.FromSeconds(2);
    /// <summary>Peso igual: volta a enviar ao fim deste tempo (o servidor só aceita leituras recentes).</summary>
    public static readonly TimeSpan Repetir = TimeSpan.FromSeconds(1);

    private long versao;
    private string? ultimoErro;

    public BalancaRecebida? Actual { get; private set; }

    public async Task CorrerAsync(CancellationToken ct)
    {
        var espera = EsperaSegundos;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var b = await CicloAsync(espera, ct);
                espera = EsperaSegundos;
                if (b is { Activa: true } && b.Modo != "Nenhuma")
                {
                    await LerAsync(b, ct);
                    espera = 0; // o POS deixou de pedir ou a porta falhou: pergunta logo outra vez
                    await Task.Delay(TimeSpan.FromSeconds(1), ct);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                if (ex.Message != ultimoErro)
                    registar?.Invoke("Balança: " + ex.Message);
                ultimoErro = ex.Message;
                espera = 0;
                await Task.Delay(ex is AgenteNaoAutorizadoException ? TimeSpan.FromSeconds(60) : TimeSpan.FromSeconds(5), ct);
            }
        }
    }

    public async Task<BalancaRecebida?> CicloAsync(int espera, CancellationToken ct)
    {
        using var r = await http.GetAsync($"api/agente/balanca?versao={versao}&espera={espera}", ct);
        if (r.StatusCode == HttpStatusCode.Unauthorized)
            throw new AgenteNaoAutorizadoException();
        r.EnsureSuccessStatusCode();
        if (r.StatusCode == HttpStatusCode.NoContent)
            return Actual;
        var b = await r.Content.ReadFromJsonAsync<BalancaRecebida>(MotorAgente.Json, ct) ?? throw new InvalidOperationException("Resposta vazia do servidor.");
        versao = b.Versao;
        Actual = b;
        ultimoErro = null;
        return b;
    }

    /// <summary>Lê a balança e envia o peso enquanto o POS o pedir (ou até <paramref name="maximo"/> leituras, nos testes).</summary>
    public async Task LerAsync(BalancaRecebida b, CancellationToken ct, int? maximo = null)
    {
        IPortaBalanca porta;
        try
        {
            porta = abrir(b);
        }
        catch (Exception ex)
        {
            await EnviarAsync(new LeituraEnviada(null, false, $"Não foi possível abrir a porta {b.Porta} da balança (ligada? usada por outro programa?)."), ct);
            registar?.Invoke($"Balança: porta {b.Porta}: {ex.Message}");
            return;
        }
        using (porta)
        {
            registar?.Invoke($"Balança: a ler em {b.Porta} ({b.Velocidade} bps, {b.Formato}, {b.Modo}).");
            var pedido = ProtocoloBalanca.Pedido(b.Modo);
            var estabilizador = new Estabilizador();
            var resto = "";
            var ultimoPeso = DateTime.UtcNow;
            LeituraEnviada? enviada = null;
            var enviadaEm = DateTime.MinValue;
            for (var n = 0; maximo is null || n < maximo; n++)
            {
                ct.ThrowIfCancellationRequested();
                PesoLido? lido;
                try
                {
                    lido = Ler(porta, pedido, ref resto);
                }
                catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
                {
                    await EnviarAsync(new LeituraEnviada(null, false, "A ligação à balança falhou (cabo desligado?)."), ct);
                    registar?.Invoke("Balança: " + ex.Message);
                    return;
                }

                LeituraEnviada leitura;
                if (lido is not null)
                {
                    ultimoPeso = DateTime.UtcNow;
                    leitura = new LeituraEnviada(lido.Peso, estabilizador.Estavel(lido), lido.Erro);
                }
                else if (DateTime.UtcNow - ultimoPeso >= SemPeso)
                {
                    estabilizador.Limpar();
                    leitura = new LeituraEnviada(null, false, $"A balança não envia o peso: confirme a porta {b.Porta}, a velocidade e o modo de leitura.");
                }
                else
                {
                    if (pedido is not null)
                        await Task.Delay(Intervalo, ct);
                    continue;
                }

                if (leitura != enviada || DateTime.UtcNow - enviadaEm >= Repetir)
                {
                    if (!await EnviarAsync(leitura, ct))
                        return; // o POS já não pede o peso
                    enviada = leitura;
                    enviadaEm = DateTime.UtcNow;
                }
                if (pedido is not null)
                    await Task.Delay(Intervalo, ct);
            }
        }
    }

    /// <summary>Modo a pedido: envia o pedido e lê a resposta inteira. Contínuo: só as tramas completas (o resto espera pela seguinte).</summary>
    private static PesoLido? Ler(IPortaBalanca porta, byte[]? pedido, ref string resto)
    {
        IReadOnlyList<string> tramas;
        if (pedido is not null)
        {
            porta.Escrever(pedido);
            var resposta = porta.Ler(EsperaResposta);
            tramas = [.. ProtocoloBalanca.Tramas(resposta, out var fim), ProtocoloBalanca.Limpar(fim)];
            resto = "";
        }
        else
        {
            tramas = ProtocoloBalanca.Tramas(resto + porta.Ler(Intervalo), out resto);
            if (resto.Length > 200)
                resto = ""; // lixo sem fim de linha (velocidade errada?)
        }
        return tramas.Reverse().Select(ProtocoloBalanca.Interpretar).FirstOrDefault(p => p is not null);
    }

    private sealed record LeituraEnviada(decimal? Peso, bool Estavel, string? Erro);
    private sealed record Resposta(bool Activa);

    private async Task<bool> EnviarAsync(LeituraEnviada leitura, CancellationToken ct)
    {
        using var r = await http.PostAsJsonAsync("api/agente/balanca/leitura", leitura, MotorAgente.Json, ct);
        if (r.StatusCode == HttpStatusCode.Unauthorized)
            throw new AgenteNaoAutorizadoException();
        r.EnsureSuccessStatusCode();
        return (await r.Content.ReadFromJsonAsync<Resposta>(MotorAgente.Json, ct))?.Activa ?? false;
    }
}
