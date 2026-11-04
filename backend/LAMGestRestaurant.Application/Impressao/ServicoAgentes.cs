using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LAMGestRestaurant.Application.Comum;
using LAMGestRestaurant.Domain.Caixa;
using LAMGestRestaurant.Domain.Sistema;
using Microsoft.EntityFrameworkCore;

namespace LAMGestRestaurant.Application.Impressao;

/// <summary>Estado enviado pelo agente de impressão do posto (a cada minuto).</summary>
public sealed record EstadoAgente(string? Versao, IReadOnlyList<string>? Impressoras);

/// <summary>Trabalho entregue ao agente: o documento é desenhado no posto com a mesma disposição do servidor.</summary>
public sealed record TrabalhoAgenteDto(int Id, string NomeWindows, int LarguraPapelMm, DocumentoImpressao Documento, bool ComLogotipo);

public sealed record ResultadoTrabalho(bool Sucesso, string? Erro);

/// <summary>Chave do agente (só é mostrada uma vez: o servidor guarda apenas o hash).</summary>
public sealed record AgenteRegistado(int Posto, string Nome, string Chave);

/// <summary>Onde as impressoras podem estar: o servidor (PostoId vazio) ou um posto com agente.</summary>
public sealed record LocalImpressoras(int? PostoId, int? Numero, string Nome, bool TemAgente, bool Ligado, DateTime? VistoEmUtc, string? Versao, IReadOnlyList<string> Impressoras);

/// <summary>Acorda os agentes que estão à espera de trabalhos (pedido longo), assim que há um trabalho novo para o seu posto.</summary>
public sealed class AvisosPostos
{
    private readonly ConcurrentDictionary<int, TaskCompletionSource> espera = new();

    public void Avisar(int terminalId)
    {
        if (espera.TryRemove(terminalId, out var sinal))
            sinal.TrySetResult();
    }

    /// <summary>Pedir o sinal antes de consultar a BD: um trabalho criado entre a consulta e a espera acorda-a na mesma.</summary>
    public Task Sinal(int terminalId) =>
        espera.GetOrAdd(terminalId, _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)).Task;

    public static async Task EsperarAsync(Task sinal, TimeSpan tempo, CancellationToken ct)
    {
        try
        {
            await sinal.WaitAsync(tempo, ct);
        }
        catch (TimeoutException)
        {
        }
    }
}

/// <summary>
/// Agentes de impressão dos postos: cada computador imprime nas impressoras instaladas no seu Windows.
/// O servidor guarda os trabalhos do posto; o agente vai buscá-los (pedido longo), imprime-os sem diálogo e devolve o resultado.
/// Autenticação do agente: número do posto + chave gerada no registo (guardada só em hash SHA-256).
/// </summary>
public class ServicoAgentes(IAppDbContext db, IAuditoria auditoria, AvisosPostos avisos, EstadoImpressoras estados, INotificador notificador, TimeProvider relogio)
{
    /// <summary>Sem notícias do agente há mais do que isto: o posto aparece desligado.</summary>
    public static readonly TimeSpan Ligado = TimeSpan.FromMinutes(3);
    /// <summary>Entregue e sem resultado há mais do que isto (ex.: o agente fechou a meio): volta a ser entregue.</summary>
    public static readonly TimeSpan Reentrega = TimeSpan.FromMinutes(2);
    /// <summary>Talões e recibos antigos já não servem: ao fim deste tempo sem imprimir, o trabalho falha.</summary>
    public static readonly TimeSpan Validade = TimeSpan.FromMinutes(30);
    private const int MaximoImpressoras = 4000;

    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private DateTime Agora => relogio.GetUtcNow().UtcDateTime;

    public static string Hash(string chave) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(chave)));

    /// <summary>Gera (ou substitui) a chave do agente do posto. A chave anterior deixa de valer.</summary>
    public async Task<AgenteRegistado> RegistarAsync(int numero, CancellationToken ct = default)
    {
        var terminal = await db.Terminais.FirstOrDefaultAsync(t => t.Numero == numero && t.Activo, ct)
            ?? throw new NaoEncontradoException($"Não existe o posto n.º {numero} (ou está inactivo). Crie-o em Caixa → Terminais.");
        var chave = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        terminal.AgenteTokenHash = Hash(chave);
        auditoria.Registar("posto.agente.registar", "Terminal", terminal.Id, new { terminal.Nome, terminal.Numero });
        await db.SaveChangesAsync(ct);
        return new AgenteRegistado(numero, terminal.Nome, chave);
    }

    /// <summary>Retira a chave do agente (o computador deixa de poder imprimir pelo posto).</summary>
    public async Task RevogarAsync(int terminalId, CancellationToken ct = default)
    {
        var terminal = await db.Terminais.FindAsync([terminalId], ct) ?? throw new NaoEncontradoException("Terminal não encontrado.");
        terminal.AgenteTokenHash = null;
        auditoria.Registar("posto.agente.revogar", "Terminal", terminal.Id, new { terminal.Nome, terminal.Numero });
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Id do terminal do posto se a chave for a registada; senão null.</summary>
    public async Task<int?> AutenticarAsync(int numero, string? chave, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(chave))
            return null;
        var terminal = await db.Terminais.AsNoTracking().Where(t => t.Numero == numero && t.Activo && t.AgenteTokenHash != null)
            .Select(t => new { t.Id, t.AgenteTokenHash }).FirstOrDefaultAsync(ct);
        if (terminal is null)
            return null;
        var esperado = Encoding.ASCII.GetBytes(terminal.AgenteTokenHash!);
        var recebido = Encoding.ASCII.GetBytes(Hash(chave.Trim()));
        return CryptographicOperations.FixedTimeEquals(esperado, recebido) ? terminal.Id : null;
    }

    public async Task ReportarAsync(int terminalId, EstadoAgente estado, CancellationToken ct = default)
    {
        var terminal = await db.Terminais.FindAsync([terminalId], ct) ?? throw new NaoEncontradoException("Terminal não encontrado.");
        var nomes = new List<string>();
        foreach (var nome in (estado.Impressoras ?? []).Select(n => n.Trim()).Where(n => n.Length is > 0 and <= 260).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            nomes.Add(nome);
            if (JsonSerializer.Serialize(nomes, Json).Length > MaximoImpressoras)
            {
                nomes.RemoveAt(nomes.Count - 1);
                break;
            }
        }
        terminal.AgenteImpressoras = JsonSerializer.Serialize(nomes, Json);
        terminal.AgenteVersao = estado.Versao is { Length: > 0 } v ? v[..Math.Min(v.Length, 40)] : null;
        terminal.AgenteVistoEmUtc = Agora;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Trabalhos por imprimir do posto. Sem nenhum, espera até <paramref name="espera"/> que chegue um (pedido longo):
    /// a impressão sai logo, sem o agente estar sempre a perguntar.
    /// </summary>
    public async Task<IReadOnlyList<TrabalhoAgenteDto>> ProximosAsync(int terminalId, TimeSpan espera, CancellationToken ct = default)
    {
        var limite = Agora + espera;
        while (true)
        {
            var sinal = avisos.Sinal(terminalId);
            var trabalhos = await PorEntregarAsync(terminalId, ct);
            if (trabalhos.Count > 0 || Agora >= limite)
                return trabalhos;
            await AvisosPostos.EsperarAsync(sinal, limite - Agora, ct);
        }
    }

    private async Task<IReadOnlyList<TrabalhoAgenteDto>> PorEntregarAsync(int terminalId, CancellationToken ct)
    {
        var agora = Agora;
        var terminal = await db.Terminais.FindAsync([terminalId], ct);
        if (terminal is not null)
            terminal.AgenteVistoEmUtc = agora;

        var pendentes = await db.TrabalhosImpressaoPosto
            .Where(t => t.TerminalId == terminalId && t.ConcluidoEmUtc == null && (t.EntregueEmUtc == null || t.EntregueEmUtc < agora - Reentrega))
            .OrderBy(t => t.Id).Take(20).ToListAsync(ct);
        var entregar = new List<TrabalhoAgenteDto>();
        foreach (var t in pendentes)
        {
            if (agora - t.CriadoEmUtc > Validade)
            {
                await ConcluirAsync(t, "O posto não imprimiu a tempo (agente de impressão desligado?).");
                continue;
            }
            t.EntregueEmUtc = agora;
            entregar.Add(new TrabalhoAgenteDto(t.Id, t.NomeWindows, t.LarguraPapelMm,
                JsonSerializer.Deserialize<DocumentoImpressao>(t.Documento, Json)!, t.ComLogotipo));
        }
        await db.SaveChangesAsync(ct);
        return entregar;
    }

    /// <summary>Resultado devolvido pelo agente. Repetido (ex.: rede caiu a meio) não faz nada.</summary>
    public async Task RegistarResultadoAsync(int terminalId, int trabalhoId, ResultadoTrabalho resultado, CancellationToken ct = default)
    {
        var trabalho = await db.TrabalhosImpressaoPosto.FirstOrDefaultAsync(t => t.Id == trabalhoId && t.TerminalId == terminalId, ct)
            ?? throw new NaoEncontradoException("Trabalho de impressão não encontrado.");
        if (trabalho.ConcluidoEmUtc is not null)
            return;
        var erro = resultado.Sucesso ? null : string.IsNullOrWhiteSpace(resultado.Erro) ? "Erro desconhecido no posto." : resultado.Erro.Trim();
        await ConcluirAsync(trabalho, erro is { Length: > 1000 } ? erro[..1000] : erro);
        await db.SaveChangesAsync(ct);
    }

    private async Task ConcluirAsync(TrabalhoImpressaoPosto trabalho, string? erro)
    {
        trabalho.ConcluidoEmUtc = Agora;
        trabalho.Erro = erro;
        estados.Registar(trabalho.ImpressoraId, erro);
        if (erro is null || trabalho.UtilizadorId is not int utilizador)
            return;
        var impressora = await db.Impressoras.AsNoTracking().Where(i => i.Id == trabalho.ImpressoraId).Select(i => i.Nome).FirstOrDefaultAsync();
        try
        {
            await notificador.ImpressaoFalhouAsync(utilizador, trabalho.Titulo, impressora ?? trabalho.NomeWindows, erro);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Avisar é acessório: a falha fica no estado da impressora.
        }
    }

    /// <summary>Guarda o trabalho para o agente do posto e acorda-o.</summary>
    public async Task EnfileirarAsync(TrabalhoImpressao trabalho, int? utilizadorId, CancellationToken ct = default)
    {
        var posto = trabalho.PostoId ?? throw new InvalidOperationException("Impressora sem posto.");
        db.TrabalhosImpressaoPosto.Add(new TrabalhoImpressaoPosto
        {
            TerminalId = posto,
            ImpressoraId = trabalho.ImpressoraId,
            NomeWindows = trabalho.NomeWindows,
            LarguraPapelMm = trabalho.LarguraPapelMm,
            Titulo = trabalho.Documento.Titulo.Length > 200 ? trabalho.Documento.Titulo[..200] : trabalho.Documento.Titulo,
            Documento = JsonSerializer.Serialize(trabalho.Documento, Json),
            ComLogotipo = trabalho.Logotipo is not null,
            UtilizadorId = utilizadorId,
            CriadoEmUtc = Agora,
        });
        await db.SaveChangesAsync(ct);
        avisos.Avisar(posto);
    }

    /// <summary>Servidor e postos (com número) com as impressoras instaladas em cada um, para escolher onde fica cada impressora.</summary>
    public async Task<IReadOnlyList<LocalImpressoras>> LocaisAsync(IReadOnlyList<string> doServidor, CancellationToken ct = default)
    {
        var agora = Agora;
        var postos = await db.Terminais.AsNoTracking().Where(t => t.Activo && t.Numero != null).OrderBy(t => t.Numero)
            .Select(t => new { t.Id, t.Numero, t.Nome, TemAgente = t.AgenteTokenHash != null, t.AgenteVistoEmUtc, t.AgenteVersao, t.AgenteImpressoras })
            .ToListAsync(ct);
        return
        [
            new LocalImpressoras(null, null, "Servidor", true, true, agora, null, doServidor),
            .. postos.Select(p => new LocalImpressoras(p.Id, p.Numero, p.Nome, p.TemAgente,
                p.TemAgente && p.AgenteVistoEmUtc is DateTime visto && agora - visto <= Ligado, p.AgenteVistoEmUtc, p.AgenteVersao,
                Impressoras(p.AgenteImpressoras))),
        ];
    }

    internal static IReadOnlyList<string> Impressoras(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return [];
        try
        {
            return JsonSerializer.Deserialize<List<string>>(json, Json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
