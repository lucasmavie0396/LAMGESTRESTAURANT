using System.Security.Cryptography;
using LAMGestRestaurant.Application.Comum;
using LAMGestRestaurant.Application.Impressao;
using LAMGestRestaurant.Domain.Comum;
using Microsoft.EntityFrameworkCore;

namespace LAMGestRestaurant.Application.Caixa;

/// <summary>Estado de um posto: online (sinal do navegador), caixa aberto, agente de impressão e impressoras, ligação dos tablets.</summary>
public sealed record PostoEstadoDto(
    int Id, int? Numero, string Nome, string? Descricao, bool Activo,
    bool Online, DateTime? VistoEmUtc, string? VistoPor, string? VistoEndereco,
    IReadOnlyList<string> CaixasAbertos,
    bool AgenteRegistado, bool AgenteLigado, DateTime? AgenteVistoEmUtc, string? AgenteVersao, IReadOnlyList<string> ImpressorasWindows,
    IReadOnlyList<string> ImpressorasConfiguradas,
    bool LigacaoTablets, DateTime? TabletsCodigoGeradoEmUtc);

/// <summary>Código de emparelhamento dos tablets: só é mostrado quando é gerado.</summary>
public sealed record CodigoTablets(int TerminalId, string Codigo, DateTime GeradoEmUtc);

public sealed record DefinirTablets(bool Activo);

public sealed record Emparelhar(string? Codigo);

/// <summary>Posto a que a app de venda ficou ligada.</summary>
public sealed record PostoEmparelhado(int TerminalId, int? Numero, string Nome, string? Endereco = null);

/// <summary>
/// Postos (computadores) registados: o navegador de cada posto envia um sinal a cada minuto (online = sinal há menos de
/// <see cref="Online"/>); o caixa aberto e o agente de impressão completam o estado.
/// Um posto pode ser escolhido para a ligação dos tablets (app Android, mais tarde): fica com um código de emparelhamento
/// (guardado só em hash) que os tablets usarão, juntamente com o endereço do servidor na rede.
/// </summary>
public class ServicoPostos(IAppDbContext db, IUtilizadorActual actual, IAuditoria auditoria, IConsultaUtilizadores utilizadores, TimeProvider relogio)
{
    public static readonly TimeSpan Online = TimeSpan.FromMinutes(2);
    /// <summary>Sinais mais próximos do que isto (do mesmo utilizador e endereço) não se gravam.</summary>
    private static readonly TimeSpan IntervaloMinimo = TimeSpan.FromSeconds(20);
    private const string Alfabeto = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    private DateTime Agora => relogio.GetUtcNow().UtcDateTime;

    /// <summary>Sinal do navegador do posto (o sistema está aberto neste posto).</summary>
    public async Task PresencaAsync(int terminalId, string? endereco, CancellationToken ct = default)
    {
        var t = await db.Terminais.FirstOrDefaultAsync(x => x.Id == terminalId, ct) ?? throw new NaoEncontradoException("Posto não encontrado.");
        if (!t.Activo)
            throw new RegraNegocioException($"O posto {t.Nome} está desactivado.");
        var agora = Agora;
        endereco = endereco is { Length: > 64 } ? endereco[..64] : endereco;
        if (t.VistoEmUtc is DateTime visto && agora - visto < IntervaloMinimo && t.VistoUtilizadorId == actual.Id && t.VistoEndereco == endereco)
            return;
        t.VistoEmUtc = agora;
        t.VistoUtilizadorId = actual.Id;
        t.VistoEndereco = endereco;
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<PostoEstadoDto>> ListarAsync(CancellationToken ct = default)
    {
        var postos = await db.Terminais.AsNoTracking()
            .OrderBy(t => t.Numero == null).ThenBy(t => t.Numero).ThenBy(t => t.Nome)
            .Select(t => new
            {
                t.Id, t.Numero, t.Nome, t.Descricao, t.Activo, t.VistoEmUtc, t.VistoUtilizadorId, t.VistoEndereco,
                AgenteRegistado = t.AgenteTokenHash != null, t.AgenteVistoEmUtc, t.AgenteVersao, t.AgenteImpressoras,
                t.LigacaoTablets, t.TabletsCodigoGeradoEmUtc,
                Caixas = db.SessoesCaixa.Where(s => s.TerminalId == t.Id && s.Estado == EstadoSessaoCaixa.Aberta).Select(s => s.UtilizadorAberturaId).ToList(),
                Impressoras = db.Impressoras.Where(i => i.Activo && i.PostoId == t.Id).OrderBy(i => i.Nome).Select(i => i.Nome).ToList(),
            })
            .ToListAsync(ct);
        var nomes = await utilizadores.NomesAsync(postos.SelectMany(p => p.Caixas.Append(p.VistoUtilizadorId ?? 0)).Where(i => i > 0).Distinct(), ct);
        var agora = Agora;
        return [.. postos.Select(p => new PostoEstadoDto(
            p.Id, p.Numero, p.Nome, p.Descricao, p.Activo,
            p.Activo && p.VistoEmUtc is DateTime v && agora - v <= Online, p.VistoEmUtc,
            p.VistoUtilizadorId is int u ? nomes.GetValueOrDefault(u) : null, p.VistoEndereco,
            [.. p.Caixas.Select(c => nomes.GetValueOrDefault(c) ?? "?")],
            p.AgenteRegistado, p.AgenteRegistado && p.AgenteVistoEmUtc is DateTime a && agora - a <= ServicoAgentes.Ligado, p.AgenteVistoEmUtc, p.AgenteVersao,
            p.AgenteRegistado ? ServicoAgentes.Impressoras(p.AgenteImpressoras) : [],
            p.Impressoras, p.LigacaoTablets, p.TabletsCodigoGeradoEmUtc))];
    }

    /// <summary>
    /// Escolhe (ou retira) o posto de ligação dos tablets. Escolher um posto retira a ligação do anterior e gera um código novo,
    /// devolvido só desta vez.
    /// </summary>
    public async Task<CodigoTablets?> DefinirTabletsAsync(int terminalId, bool activo, CancellationToken ct = default)
    {
        var t = await db.Terminais.FirstOrDefaultAsync(x => x.Id == terminalId, ct) ?? throw new NaoEncontradoException("Posto não encontrado.");
        if (!activo)
        {
            if (!t.LigacaoTablets) return null;
            t.LigacaoTablets = false;
            t.TabletsCodigoHash = null;
            t.TabletsCodigoGeradoEmUtc = null;
            auditoria.Registar("posto.tablets-retirar", "Terminal", t.Id, new { t.Nome, t.Numero });
            await db.SaveChangesAsync(ct);
            return null;
        }
        if (!t.Activo)
            throw new RegraNegocioException($"O posto {t.Nome} está desactivado.");

        await using var transacao = await db.Database.BeginTransactionAsync(ct);
        var anteriores = await db.Terminais.Where(x => x.LigacaoTablets && x.Id != terminalId).ToListAsync(ct);
        foreach (var anterior in anteriores)
        {
            anterior.LigacaoTablets = false;
            anterior.TabletsCodigoHash = null;
            anterior.TabletsCodigoGeradoEmUtc = null;
        }
        if (anteriores.Count > 0)
            await db.SaveChangesAsync(ct); // primeiro libertar o índice único
        t.LigacaoTablets = true;
        var codigo = NovoCodigo(t);
        auditoria.Registar("posto.tablets-escolher", "Terminal", t.Id, new { t.Nome, t.Numero, Anteriores = anteriores.Select(a => a.Nome).ToList() });
        await db.SaveChangesAsync(ct);
        await transacao.CommitAsync(ct);
        return codigo;
    }

    /// <summary>Gera um código de emparelhamento novo para o posto dos tablets (o anterior deixa de valer).</summary>
    public async Task<CodigoTablets> NovoCodigoTabletsAsync(int terminalId, CancellationToken ct = default)
    {
        var t = await db.Terminais.FirstOrDefaultAsync(x => x.Id == terminalId, ct) ?? throw new NaoEncontradoException("Posto não encontrado.");
        if (!t.LigacaoTablets)
            throw new RegraNegocioException("Este posto não é o da ligação dos tablets.");
        var codigo = NovoCodigo(t);
        auditoria.Registar("posto.tablets-codigo", "Terminal", t.Id, new { t.Nome, t.Numero });
        await db.SaveChangesAsync(ct);
        return codigo;
    }

    /// <summary>Verifica um código de emparelhamento.</summary>
    public async Task<bool> CodigoValidoAsync(string? codigo, CancellationToken ct = default) =>
        await PostoDoCodigoAsync(codigo, ct) is not null;

    /// <summary>Emparelhamento da app de venda: o código indica o posto a que o dispositivo fica ligado (caixa e impressoras desse posto).</summary>
    public async Task<PostoEmparelhado> EmparelharAsync(string? codigo, System.Net.IPAddress? tablet, IReadOnlyList<Subrede> subredes, CancellationToken ct = default)
    {
        var posto = await PostoDoCodigoAsync(codigo, ct)
            ?? throw new RegraNegocioException("Código de emparelhamento inválido. Gere um código novo em Configurações → Postos.");
        if (RedeLocal.ErroEmparelhamento(tablet, posto.Endereco, posto.Nome, subredes) is { } erro)
            throw new RegraNegocioException(erro);
        return posto;
    }

    private async Task<PostoEmparelhado?> PostoDoCodigoAsync(string? codigo, CancellationToken ct)
    {
        var normalizado = (codigo ?? "").Replace("-", "").Trim().ToUpperInvariant();
        if (normalizado.Length != 8)
            return null;
        var hash = ServicoAgentes.Hash(normalizado);
        return await db.Terminais.AsNoTracking().Where(t => t.Activo && t.LigacaoTablets && t.TabletsCodigoHash == hash)
            .Select(t => new PostoEmparelhado(t.Id, t.Numero, t.Nome, t.VistoEndereco)).FirstOrDefaultAsync(ct);
    }

    private CodigoTablets NovoCodigo(Domain.Caixa.Terminal t)
    {
        var codigo = string.Concat(Enumerable.Range(0, 8).Select(_ => Alfabeto[RandomNumberGenerator.GetInt32(Alfabeto.Length)]));
        t.TabletsCodigoHash = ServicoAgentes.Hash(codigo);
        t.TabletsCodigoGeradoEmUtc = Agora;
        return new CodigoTablets(t.Id, $"{codigo[..4]}-{codigo[4..]}", t.TabletsCodigoGeradoEmUtc.Value);
    }
}
