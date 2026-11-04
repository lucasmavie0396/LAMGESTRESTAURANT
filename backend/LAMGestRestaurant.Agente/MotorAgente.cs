using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using LAMGestRestaurant.Application.Impressao;

namespace LAMGestRestaurant.Agente;

/// <summary>Ficheiro C:\ProgramData\LAMGestRestaurant\agente.json, escrito pelo instalador do posto.</summary>
public sealed record ConfiguracaoAgente(string Servidor, int Posto, string Chave)
{
    public static string Ficheiro => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "LAMGestRestaurant", "agente.json");

    public static ConfiguracaoAgente? Ler(string ficheiro)
    {
        if (!File.Exists(ficheiro))
            return null;
        var c = JsonSerializer.Deserialize<ConfiguracaoAgente>(File.ReadAllText(ficheiro), MotorAgente.Json);
        return c is { Servidor.Length: > 0, Posto: > 0, Chave.Length: > 0 } ? c : null;
    }
}

public enum EstadoLigacao { ALigar, Ligado, SemServidor, NaoAutorizado }

/// <summary>Trabalho recebido do servidor (igual ao TrabalhoAgenteDto da API).</summary>
public sealed record TrabalhoRecebido(int Id, string NomeWindows, int LarguraPapelMm, DocumentoImpressao Documento, bool ComLogotipo);

/// <summary>
/// Ciclo do agente, sem interface (testado contra a API): envia as impressoras do computador a cada minuto, pede os trabalhos
/// (pedido longo: o servidor responde logo que haja um), imprime-os e devolve o resultado. Sem servidor, tenta de novo de 5 em 5 s.
/// </summary>
public sealed class MotorAgente(HttpClient http, string versao,
    Func<IReadOnlyList<string>> instaladas, Action<TrabalhoRecebido, byte[]?> imprimir, Action<string>? registar = null)
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
    public static readonly TimeSpan IntervaloEstado = TimeSpan.FromSeconds(60);
    public const int EsperaSegundos = 20;

    private DateTime estadoEnviadoEm = DateTime.MinValue;
    private (DateTime Em, byte[]? Dados)? logotipo;

    public EstadoLigacao Estado { get; private set; } = EstadoLigacao.ALigar;
    public string? UltimoErro { get; private set; }
    public int Impressos { get; private set; }
    public event Action? Mudou;

    /// <summary>Cliente com os cabeçalhos do agente e tempo suficiente para o pedido longo.</summary>
    public static HttpClient CriarCliente(string servidor, int posto, string chave, HttpMessageHandler? manipulador = null)
    {
        var http = manipulador is null ? new HttpClient() : new HttpClient(manipulador);
        http.BaseAddress = new Uri(servidor.TrimEnd('/') + "/");
        http.Timeout = TimeSpan.FromSeconds(EsperaSegundos + 30);
        http.DefaultRequestHeaders.Add("X-Posto", posto.ToString(System.Globalization.CultureInfo.InvariantCulture));
        http.DefaultRequestHeaders.Add("X-Agente-Token", chave);
        return http;
    }

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
                await FalhouAsync(ex, ct);
            }
        }
    }

    /// <summary>Uma volta: estado (se for altura), trabalhos e resultados. Devolve quantos trabalhos tratou.</summary>
    public async Task<int> CicloAsync(int espera, CancellationToken ct)
    {
        if (DateTime.UtcNow - estadoEnviadoEm >= IntervaloEstado)
            await EnviarEstadoAsync(ct);

        using var resposta = await http.GetAsync($"api/agente/trabalhos?espera={espera}", ct);
        Verificar(resposta);
        var trabalhos = await resposta.Content.ReadFromJsonAsync<List<TrabalhoRecebido>>(Json, ct) ?? [];
        Definir(EstadoLigacao.Ligado, null);
        foreach (var t in trabalhos)
        {
            string? erro = null;
            try
            {
                var logo = t.ComLogotipo ? await LogotipoAsync(ct) : null;
                await Task.Run(() => imprimir(t, logo), ct);
                Impressos++;
                registar?.Invoke($"Impresso «{t.Documento.Titulo}» em {t.NomeWindows}");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                erro = ex.Message;
                registar?.Invoke($"Falhou «{t.Documento.Titulo}» em {t.NomeWindows}: {ex.Message}");
            }
            using var r = await http.PostAsJsonAsync($"api/agente/trabalhos/{t.Id}/resultado", new { sucesso = erro is null, erro }, Json, ct);
            Verificar(r);
        }
        Mudou?.Invoke();
        return trabalhos.Count;
    }

    public async Task EnviarEstadoAsync(CancellationToken ct)
    {
        IReadOnlyList<string> nomes;
        try
        {
            nomes = instaladas();
        }
        catch (Exception ex)
        {
            registar?.Invoke("Não foi possível ler as impressoras do Windows: " + ex.Message);
            nomes = [];
        }
        using var r = await http.PostAsJsonAsync("api/agente/estado", new { versao, impressoras = nomes }, Json, ct);
        Verificar(r);
        estadoEnviadoEm = DateTime.UtcNow;
    }

    /// <summary>Logótipo do estabelecimento, guardado 5 minutos (muda raramente).</summary>
    private async Task<byte[]?> LogotipoAsync(CancellationToken ct)
    {
        if (logotipo is { } l && DateTime.UtcNow - l.Em < TimeSpan.FromMinutes(5))
            return l.Dados;
        try
        {
            using var r = await http.GetAsync("api/agente/logotipo", ct);
            Verificar(r);
            var dados = r.StatusCode == HttpStatusCode.NoContent ? null : await r.Content.ReadAsByteArrayAsync(ct);
            logotipo = (DateTime.UtcNow, dados);
            return dados;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            registar?.Invoke("Sem logótipo: " + ex.Message);
            return logotipo?.Dados; // imprime na mesma, sem logótipo (ou com o anterior)
        }
    }

    private static void Verificar(HttpResponseMessage r)
    {
        if (r.StatusCode == HttpStatusCode.Unauthorized)
            throw new AgenteNaoAutorizadoException();
        r.EnsureSuccessStatusCode();
    }

    private async Task FalhouAsync(Exception ex, CancellationToken ct)
    {
        var naoAutorizado = ex is AgenteNaoAutorizadoException;
        var mensagem = naoAutorizado
            ? "O servidor recusou a chave deste posto. Volte a instalar o agente com o LAMGest-Setup (modo Posto)."
            : "Sem ligação ao servidor: " + ex.Message;
        if (mensagem != UltimoErro)
            registar?.Invoke(mensagem);
        Definir(naoAutorizado ? EstadoLigacao.NaoAutorizado : EstadoLigacao.SemServidor, mensagem);
        estadoEnviadoEm = DateTime.MinValue; // ao voltar a ligar, reenvia as impressoras
        await Task.Delay(naoAutorizado ? TimeSpan.FromSeconds(60) : TimeSpan.FromSeconds(5), ct);
    }

    private void Definir(EstadoLigacao estado, string? erro)
    {
        var mudou = estado != Estado || erro != UltimoErro;
        Estado = estado;
        UltimoErro = erro;
        if (mudou)
            Mudou?.Invoke();
    }
}

public sealed class AgenteNaoAutorizadoException() : Exception("Agente não autorizado.");
