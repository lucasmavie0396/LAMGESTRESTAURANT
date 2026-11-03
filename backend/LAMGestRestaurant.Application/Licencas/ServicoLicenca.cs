using System.Security.Cryptography;
using LAMGestRestaurant.Application.Comum;
using LAMGestRestaurant.Domain.Sistema;
using LAMGestRestaurant.Licenciamento;
using Microsoft.EntityFrameworkCore;

namespace LAMGestRestaurant.Application.Licencas;

/// <summary>
/// Valida: tudo normal. AExpirar: faltam 30 dias ou menos (aviso). Tolerancia: expirou há 7 dias ou menos (aviso, ainda trabalha).
/// Expirada, Inexistente, Invalida: vendas e gravações bloqueadas (a consulta e os relatórios continuam).
/// </summary>
public enum EstadoLicenca { Valida, AExpirar, Tolerancia, Expirada, Inexistente, Invalida }

public record LicencaDto(Guid LicencaId, string NomeComercial, string Nuit, DateOnly EmitidaEm, DateOnly ValidaAte, DateTime ActivadaEmUtc, string? ActivadaPor);

/// <summary>Estado actual. Público (também no ecrã de entrada): não inclui o texto da licença.</summary>
public record EstadoLicencaDto(EstadoLicenca Estado, bool Bloqueada, string Mensagem, string? NomeComercial, string? Nuit, DateOnly? ValidaAte, int? DiasRestantes);

public record ActivarLicenca(string Texto);

/// <summary>Chave pública que verifica as licenças (a do fabricante; nos testes, uma chave de teste).</summary>
public sealed class ChaveLicencas(ECDsa chave)
{
    public ECDsa Chave { get; } = chave;
}

/// <summary>Último estado calculado (em memória), para o bloqueio não ir à base de dados em cada pedido.</summary>
public sealed class CacheEstadoLicenca
{
    public static readonly TimeSpan Validade = TimeSpan.FromMinutes(5);
    private volatile Tuple<EstadoLicencaDto, DateTime>? actual;

    public EstadoLicencaDto? Obter(DateTime agoraUtc) =>
        actual is { } a && agoraUtc - a.Item2 < Validade && agoraUtc >= a.Item2 ? a.Item1 : null;

    public void Guardar(EstadoLicencaDto estado, DateTime agoraUtc) => actual = Tuple.Create(estado, agoraUtc);

    public void Invalidar() => actual = null;
}

/// <summary>
/// Licença do sistema. A licença é assinada pelo gerador do fabricante (chave privada) e verificada aqui com a chave pública;
/// fica ligada ao NUIT do estabelecimento (Configurações). Verificada no arranque e, depois, de 5 em 5 minutos.
/// </summary>
public class ServicoLicenca(IAppDbContext db, ChaveLicencas chave, CacheEstadoLicenca cache, IAuditoria auditoria, IUtilizadorActual actual,
    IConsultaUtilizadores utilizadores, ConfiguracoesSistema config, TimeProvider relogio)
{
    public const int DiasAviso = 30;
    public const int DiasTolerancia = 7;
    /// <summary>Folga para acertos de relógio (fusos, sincronização) antes de considerar o relógio atrasado.</summary>
    private static readonly TimeSpan FolgaRelogio = TimeSpan.FromHours(36);

    /// <summary>Estado em cache (até 5 minutos) ou calculado agora.</summary>
    public async Task<EstadoLicencaDto> EstadoAsync(CancellationToken ct = default)
    {
        var agora = relogio.GetUtcNow().UtcDateTime;
        if (cache.Obter(agora) is { } e)
            return e;
        var estado = await CalcularAsync(agora, ct);
        cache.Guardar(estado, agora);
        return estado;
    }

    private async Task<EstadoLicencaDto> CalcularAsync(DateTime agora, CancellationToken ct)
    {
        var licenca = await db.Licencas.OrderByDescending(l => l.ActivadaEmUtc).ThenByDescending(l => l.Id).FirstOrDefaultAsync(ct);
        if (licenca is null)
            return new(EstadoLicenca.Inexistente, true, "Sistema sem licença. Peça a licença à LAM company e active-a em Configurações → Licença.", null, null, null, null);

        EstadoLicencaDto Invalida(string motivo) =>
            new(EstadoLicenca.Invalida, true, motivo, licenca.NomeComercial, licenca.Nuit, licenca.ValidaAte, null);

        // O texto guardado volta a ser verificado: alterar a validade na base de dados não serve de nada.
        var dados = FormatoLicenca.Ler(licenca.Texto, chave.Chave, out _);
        if (dados is null || dados.Id != licenca.LicencaId || dados.ValidaAte != licenca.ValidaAte || dados.Nuit != licenca.Nuit)
            return Invalida("A licença guardada não é válida para este sistema. Active de novo a licença.");
        var nuit = (await config.TextoAsync(ChavesConfiguracao.Nuit)).Trim();
        if (nuit != dados.Nuit)
            return Invalida($"O NUIT do estabelecimento ({(nuit.Length == 0 ? "vazio" : nuit)}) não é o da licença ({dados.Nuit}).");
        if (agora < licenca.UltimaVerificacaoUtc - FolgaRelogio)
            return Invalida("O relógio deste servidor está atrasado em relação à última utilização. Acerte a data e a hora do Windows.");
        if (agora - licenca.UltimaVerificacaoUtc > TimeSpan.FromHours(1))
        {
            licenca.UltimaVerificacaoUtc = agora;
            await db.SaveChangesAsync(ct);
        }

        var hoje = await config.HojeAsync(relogio);
        var dias = dados.ValidaAte.DayNumber - hoje.DayNumber;
        var (estado, mensagem) = dias switch
        {
            >= 0 and <= DiasAviso => (EstadoLicenca.AExpirar, dias == 0 ? "A licença expira hoje." : $"A licença expira dentro de {dias} dias ({dados.ValidaAte:dd/MM/yyyy}). Renove-a com a LAM company."),
            > DiasAviso => (EstadoLicenca.Valida, $"Licença válida até {dados.ValidaAte:dd/MM/yyyy}."),
            >= -DiasTolerancia => (EstadoLicenca.Tolerancia,
                $"A licença expirou em {dados.ValidaAte:dd/MM/yyyy}. O sistema bloqueia dentro de {DiasTolerancia + dias + 1} dias: renove-a com a LAM company."),
            _ => (EstadoLicenca.Expirada, $"A licença expirou em {dados.ValidaAte:dd/MM/yyyy}. As vendas e gravações estão bloqueadas até activar uma licença renovada."),
        };
        return new(estado, estado == EstadoLicenca.Expirada, mensagem, dados.Nome, dados.Nuit, dados.ValidaAte, dias);
    }

    /// <summary>
    /// Activa uma licença (nova ou renovação). O nome comercial e o NUIT do estabelecimento passam a ser os da licença.
    /// Recusa licenças já expiradas, já activadas, ou que encurtariam a validade da licença actual do mesmo estabelecimento.
    /// </summary>
    public async Task<EstadoLicencaDto> ActivarAsync(string texto)
    {
        var dados = FormatoLicenca.Ler(texto, chave.Chave, out var erro) ?? throw new RegraNegocioException(erro!);
        var hoje = await config.HojeAsync(relogio);
        if (dados.ValidaAte < hoje)
            throw new RegraNegocioException($"Esta licença já expirou em {dados.ValidaAte:dd/MM/yyyy}.");
        var actualLicenca = await db.Licencas.OrderByDescending(l => l.ActivadaEmUtc).ThenByDescending(l => l.Id).FirstOrDefaultAsync();
        var agora = relogio.GetUtcNow().UtcDateTime;
        if (actualLicenca?.LicencaId == dados.Id)
        {
            // A licença em vigor outra vez: repõe o nome e o NUIT do estabelecimento (ex.: alterados por engano).
            await GravarConfiguracaoAsync(ChavesConfiguracao.NomeEstabelecimento, dados.Nome, agora);
            await GravarConfiguracaoAsync(ChavesConfiguracao.Nuit, dados.Nuit, agora);
            auditoria.Registar("licenca.reaplicar", "Licenca", actualLicenca.Id, new { dados.Id, dados.Nuit });
            await db.SaveChangesAsync();
            cache.Invalidar();
            return await EstadoAsync();
        }
        if (await db.Licencas.AnyAsync(l => l.LicencaId == dados.Id))
            throw new ConflitoException("Esta licença já foi activada antes e foi substituída por outra.");
        if (actualLicenca is not null && actualLicenca.Nuit == dados.Nuit && actualLicenca.ValidaAte > dados.ValidaAte)
            throw new RegraNegocioException($"A licença actual é válida até {actualLicenca.ValidaAte:dd/MM/yyyy}; esta terminaria antes ({dados.ValidaAte:dd/MM/yyyy}).");

        await using var transacao = await db.Database.BeginTransactionAsync();
        var licenca = db.Licencas.Add(new LicencaSistema
        {
            LicencaId = dados.Id, NomeComercial = dados.Nome, Nuit = dados.Nuit, EmitidaEm = dados.EmitidaEm, ValidaAte = dados.ValidaAte,
            Texto = FormatoLicenca.Normalizar(texto), ActivadaEmUtc = agora, ActivadaPorId = actual.Id,
            UltimaVerificacaoUtc = Max(agora, actualLicenca?.UltimaVerificacaoUtc ?? agora),
        }).Entity;
        await GravarConfiguracaoAsync(ChavesConfiguracao.NomeEstabelecimento, dados.Nome, agora);
        await GravarConfiguracaoAsync(ChavesConfiguracao.Nuit, dados.Nuit, agora);
        await db.SaveChangesAsync();
        auditoria.Registar("licenca.activar", "Licenca", licenca.Id, new { dados.Id, dados.Nome, dados.Nuit, ValidaAte = dados.ValidaAte.ToString("yyyy-MM-dd") });
        await db.SaveChangesAsync();
        await transacao.CommitAsync();
        cache.Invalidar();
        return await EstadoAsync();
    }

    public async Task<IReadOnlyList<LicencaDto>> HistoricoAsync(CancellationToken ct = default)
    {
        var lista = await db.Licencas.AsNoTracking().OrderByDescending(l => l.ActivadaEmUtc).ThenByDescending(l => l.Id).ToListAsync(ct);
        var nomes = await utilizadores.NomesAsync(lista.Where(l => l.ActivadaPorId is not null).Select(l => l.ActivadaPorId!.Value).Distinct(), ct);
        return [.. lista.Select(l => new LicencaDto(l.LicencaId, l.NomeComercial, l.Nuit, l.EmitidaEm, l.ValidaAte, l.ActivadaEmUtc,
            l.ActivadaPorId is int u ? nomes.GetValueOrDefault(u) : null))];
    }

    /// <summary>NUIT da licença em vigor (para as Configurações não o deixarem mudar), ou null sem licença.</summary>
    public Task<string?> NuitLicenciadoAsync() =>
        db.Licencas.OrderByDescending(l => l.ActivadaEmUtc).ThenByDescending(l => l.Id).Select(l => l.Nuit).FirstOrDefaultAsync();

    private async Task GravarConfiguracaoAsync(string chaveConfig, string valor, DateTime agora)
    {
        var c = await db.Configuracoes.FirstOrDefaultAsync(x => x.Chave == chaveConfig) ?? db.Configuracoes.Add(new Configuracao { Chave = chaveConfig }).Entity;
        c.Valor = valor;
        c.AlteradoEmUtc = agora;
        c.AlteradoPorId = actual.Id;
    }

    private static DateTime Max(DateTime a, DateTime b) => a > b ? a : b;
}
