using System.Data;
using System.Data.Common;
using System.Text.RegularExpressions;
using LAMGestRestaurant.Application.Comum;
using LAMGestRestaurant.Infrastructure.Dados;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace LAMGestRestaurant.Infrastructure.Sistema;

/// <summary>Dados técnicos que se podem limpar (nunca registos de negócio nem auditoria).</summary>
public enum TipoLimpeza { TrabalhosImpressao, Logs, Copias, RegistoTransaccoes }

/// <summary>Tabela da base com as linhas e o espaço ocupado.</summary>
public sealed record TabelaEspaco(string Nome, long Linhas, decimal Mb);

/// <summary>
/// Uma limpeza possível: quanto apagaria (com as regras pedidas ou as mínimas), o espaço que liberta e se está disponível.
/// <see cref="Motivo"/>: porque não está disponível, ou nota.
/// </summary>
public sealed record ItemLimpeza(TipoLimpeza Tipo, string Titulo, string Descricao, int Quantidade, decimal Mb, bool Disponivel, string? Motivo,
    int Minimo, int Valor, string? Local);

public sealed record EstadoLimpeza(decimal BaseMb, decimal? LimiteMb, decimal? RegistoTransaccoesMb, IReadOnlyList<TabelaEspaco> Tabelas, IReadOnlyList<ItemLimpeza> Itens);

/// <summary><see cref="Valor"/>: dias (trabalhos e logs: apaga os mais antigos que isto) ou cópias a manter (as mais recentes).</summary>
public sealed record PedidoLimpeza(TipoLimpeza Tipo, int Valor);

public sealed record ResultadoLimpeza(TipoLimpeza Tipo, int Apagados, decimal MbLibertados, string Mensagem);

/// <summary>
/// Limpeza de dados técnicos que enchem o disco ou a base sem servirem o negócio: trabalhos de impressão já concluídos,
/// ficheiros de log antigos, cópias de segurança antigas (ficam sempre as 7 mais recentes) e o ficheiro de registo de
/// transacções do SQL Server (só depois de uma cópia bem feita há menos de 24 h). Pedidos, pagamentos, documentos,
/// movimentos e auditoria nunca se apagam por aqui. Cada limpeza fica na auditoria.
/// </summary>
public sealed partial class ServicoLimpeza(AppDbContext db, IConfiguration configuracao, ServicoEstadoSistema estado, IAuditoria auditoria, TimeProvider relogio)
{
    public const int MinimoDiasTrabalhos = 7;
    public const int MinimoDiasLogs = 7;
    public const int MinimoCopias = 7;

    [GeneratedRegex(@"^lamgest-\d{8}(_\d+)?\.log$", RegexOptions.IgnoreCase)] private static partial Regex PadraoLog();

    private DateTime Agora => relogio.GetUtcNow().UtcDateTime;

    public async Task<EstadoLimpeza> EstadoAsync(int diasTrabalhos = 30, int diasLogs = 30, int manterCopias = 14, CancellationToken ct = default)
    {
        var sistema = await estado.EstadoAsync(ct);
        var tabelas = await TabelasAsync(ct);
        var (registoMb, simples) = await RegistoTransaccoesAsync(ct);

        diasTrabalhos = Math.Max(MinimoDiasTrabalhos, diasTrabalhos);
        var (nTrab, mbTrab) = await TrabalhosAntigosAsync(diasTrabalhos, ct);
        diasLogs = Math.Max(MinimoDiasLogs, diasLogs);
        var (pastaLogs, logs) = LogsAntigos(diasLogs);
        manterCopias = Math.Max(MinimoCopias, manterCopias);
        var (pastaCopias, copias, motivoCopias) = CopiasAntigas(sistema.BaseDados, sistema.Copias.Ficheiro, manterCopias);
        var motivoRegisto = MotivoRegisto(sistema.Copias, registoMb, simples);

        return new EstadoLimpeza(sistema.Espaco.BaseMb, sistema.Espaco.LimiteMb, registoMb, tabelas,
        [
            new(TipoLimpeza.TrabalhosImpressao, "Trabalhos de impressão já impressos",
                "Cópia de cada talão enviado aos postos (agente de impressão). Depois de impresso já não serve para nada.",
                nTrab, mbTrab, true, null, MinimoDiasTrabalhos, diasTrabalhos, null),
            new(TipoLimpeza.Logs, "Ficheiros de log do servidor",
                "Registo técnico do servidor, um ficheiro por dia (serve para diagnosticar erros recentes). O de hoje nunca se apaga.",
                logs.Count, Mb(logs.Sum(f => f.Length)), pastaLogs is not null, pastaLogs is null ? "Pasta dos logs não encontrada." : null, MinimoDiasLogs, diasLogs, pastaLogs),
            new(TipoLimpeza.Copias, "Cópias de segurança antigas",
                "Ficheiros .bak das cópias diárias. Ficam sempre as mais recentes (no mínimo 7).",
                copias.Count, Mb(copias.Sum(f => f.Length)), pastaCopias is not null, motivoCopias, MinimoCopias, manterCopias, pastaCopias),
            new(TipoLimpeza.RegistoTransaccoes, "Ficheiro de registo de transacções (SQL Server)",
                "O ficheiro .ldf cresce com o uso e não volta a encolher sozinho. Reduz-se para 64 MB; nenhum dado se perde.",
                registoMb is > 64 ? 1 : 0, registoMb is decimal r && r > 64 ? r - 64 : 0, motivoRegisto is null, motivoRegisto, 0, 0, null),
        ]);
    }

    public async Task<ResultadoLimpeza> LimparAsync(PedidoLimpeza pedido, CancellationToken ct = default)
    {
        ResultadoLimpeza r;
        switch (pedido.Tipo)
        {
            case TipoLimpeza.TrabalhosImpressao:
            {
                var dias = ValidarMinimo(pedido.Valor, MinimoDiasTrabalhos, "dias");
                var limite = Agora.AddDays(-dias);
                var (_, mb) = await TrabalhosAntigosAsync(dias, ct);
                var n = await db.TrabalhosImpressaoPosto.Where(t => t.ConcluidoEmUtc != null && t.ConcluidoEmUtc < limite).ExecuteDeleteAsync(ct);
                r = new(pedido.Tipo, n, mb, $"{n} trabalhos de impressão concluídos há mais de {dias} dias apagados.");
                break;
            }
            case TipoLimpeza.Logs:
            {
                var dias = ValidarMinimo(pedido.Valor, MinimoDiasLogs, "dias");
                var (_, ficheiros) = LogsAntigos(dias);
                var (n, bytes) = Apagar(ficheiros);
                r = new(pedido.Tipo, n, Mb(bytes), $"{n} ficheiros de log com mais de {dias} dias apagados.");
                break;
            }
            case TipoLimpeza.Copias:
            {
                var manter = ValidarMinimo(pedido.Valor, MinimoCopias, "cópias");
                var sistema = await estado.EstadoAsync(ct);
                var (_, ficheiros, motivo) = CopiasAntigas(sistema.BaseDados, sistema.Copias.Ficheiro, manter);
                if (motivo is not null && ficheiros.Count == 0)
                    throw new RegraNegocioException(motivo);
                var (n, bytes) = Apagar(ficheiros);
                r = new(pedido.Tipo, n, Mb(bytes), $"{n} cópias antigas apagadas; ficaram as {manter} mais recentes.");
                break;
            }
            default:
            {
                var sistema = await estado.EstadoAsync(ct);
                var (antes, simples) = await RegistoTransaccoesAsync(ct);
                if (MotivoRegisto(sistema.Copias, antes, simples) is { } motivo)
                    throw new RegraNegocioException(motivo);
                await ReduzirRegistoAsync(ct);
                var (depois, _) = await RegistoTransaccoesAsync(ct);
                r = new(pedido.Tipo, 1, Math.Max(0, (antes ?? 0) - (depois ?? 0)), $"Registo de transacções reduzido de {antes:N0} MB para {depois:N0} MB.");
                break;
            }
        }
        auditoria.Registar("sistema.limpeza", "Sistema", null, new { r.Tipo, pedido.Valor, r.Apagados, r.MbLibertados });
        await db.SaveChangesAsync(ct);
        return r;
    }

    private static int ValidarMinimo(int valor, int minimo, string unidade) =>
        valor >= minimo ? valor : throw new RegraNegocioException($"O mínimo é {minimo} {unidade}.");

    private static decimal Mb(long bytes) => Math.Round(bytes / 1024m / 1024m, 2);

    // ---------- Trabalhos de impressão ----------

    private async Task<(int N, decimal Mb)> TrabalhosAntigosAsync(int dias, CancellationToken ct)
    {
        var limite = Agora.AddDays(-dias);
        var q = db.TrabalhosImpressaoPosto.AsNoTracking().Where(t => t.ConcluidoEmUtc != null && t.ConcluidoEmUtc < limite);
        var n = await q.CountAsync(ct);
        var bytes = n == 0 ? 0 : await q.SumAsync(t => (long)(EF.Functions.DataLength(t.Documento) ?? 0), ct);
        return (n, Mb(bytes));
    }

    // ---------- Ficheiros ----------

    /// <summary>Pasta dos logs do servidor (Serilog, ficheiro «lamgest-.log»; relativa à pasta da aplicação).</summary>
    private string? PastaLogs()
    {
        var caminho = configuracao.GetSection("Serilog:WriteTo").GetChildren()
            .Select(s => s["Args:path"]).FirstOrDefault(p => p is { Length: > 0 } && p.Contains("lamgest-", StringComparison.OrdinalIgnoreCase));
        if (caminho is null) return null;
        var pasta = Path.GetDirectoryName(Path.IsPathRooted(caminho) ? caminho : Path.Combine(AppContext.BaseDirectory, caminho));
        return pasta is not null && Directory.Exists(pasta) ? pasta : null;
    }

    private (string? Pasta, List<FileInfo> Ficheiros) LogsAntigos(int dias)
    {
        var pasta = PastaLogs();
        if (pasta is null) return (null, []);
        var limite = Agora.AddDays(-dias);
        var hoje = Agora.Date;
        var ficheiros = new DirectoryInfo(pasta).EnumerateFiles("lamgest-*.log")
            .Where(f => PadraoLog().IsMatch(f.Name) && f.LastWriteTimeUtc < limite && f.LastWriteTimeUtc.Date < hoje)
            .ToList();
        return (pasta, ficheiros);
    }

    /// <summary>
    /// Cópias do script diário (<c>Base_AAAAMMDD_HHMMSS.bak</c>) na pasta da última cópia, excepto as <paramref name="manter"/>
    /// mais recentes. Só quando o SQL Server grava as cópias neste computador.
    /// </summary>
    private static (string? Pasta, List<FileInfo> Ficheiros, string? Motivo) CopiasAntigas(string baseDados, string? ultima, int manter)
    {
        var pasta = ultima is { Length: > 0 } ? Path.GetDirectoryName(ultima) : null;
        if (pasta is null || !Directory.Exists(pasta))
            return (null, [], "Pasta das cópias não encontrada neste computador (as cópias são feitas pelo SQL Server noutro sítio).");
        var padrao = new Regex("^" + Regex.Escape(baseDados) + @"_\d{8}_\d{6}\.bak$", RegexOptions.IgnoreCase);
        var copias = new DirectoryInfo(pasta).EnumerateFiles(baseDados + "_*.bak").Where(f => padrao.IsMatch(f.Name))
            .OrderByDescending(f => f.Name).ToList();
        return (pasta, [.. copias.Skip(manter)], null);
    }

    private static (int N, long Bytes) Apagar(IEnumerable<FileInfo> ficheiros)
    {
        int n = 0;
        long bytes = 0;
        foreach (var f in ficheiros)
        {
            try
            {
                var tamanho = f.Length;
                f.Delete();
                n++;
                bytes += tamanho;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Em uso ou sem permissão: fica para a próxima.
            }
        }
        return (n, bytes);
    }

    // ---------- SQL Server ----------

    private string? MotivoRegisto(EstadoCopias copias, decimal? registoMb, bool simples)
    {
        if (registoMb is null) return "Não foi possível ler o ficheiro de registo de transacções.";
        if (!simples) return "A base está em modo de recuperação completo: o registo só encolhe depois de uma cópia do registo.";
        if (registoMb <= 64) return "O ficheiro já é pequeno.";
        if (copias.UltimaComSucesso == false || copias.UltimaCopiaUtc is not DateTime u || Agora - u > TimeSpan.FromHours(24))
            return "Precisa de uma cópia de segurança bem feita nas últimas 24 horas.";
        return null;
    }

    private async Task<(decimal? Mb, bool Simples)> RegistoTransaccoesAsync(CancellationToken ct)
    {
        try
        {
            var mb = await EscalarAsync("SELECT CAST(SUM(CAST(size AS bigint)) * 8 / 1024.0 AS decimal(18,2)) FROM sys.database_files WHERE type = 1", ct);
            var modelo = await EscalarAsync("SELECT recovery_model_desc FROM sys.databases WHERE name = DB_NAME()", ct);
            return (mb is decimal d ? d : null, string.Equals(modelo as string, "SIMPLE", StringComparison.OrdinalIgnoreCase));
        }
        catch (DbException)
        {
            return (null, false);
        }
    }

    private async Task ReduzirRegistoAsync(CancellationToken ct)
    {
        var nome = await EscalarAsync("SELECT TOP 1 name FROM sys.database_files WHERE type = 1", ct) as string
                   ?? throw new RegraNegocioException("Ficheiro de registo não encontrado.");
        await db.Database.ExecuteSqlRawAsync("CHECKPOINT", ct);
        // O nome lógico vem do próprio SQL Server; QUOTENAME evita qualquer injecção.
        await db.Database.ExecuteSqlRawAsync("DECLARE @f nvarchar(300) = {0}; DECLARE @sql nvarchar(400) = N'DBCC SHRINKFILE (' + QUOTENAME(@f) + N', 64) WITH NO_INFOMSGS'; EXEC (@sql);",
            [nome], ct);
    }

    private async Task<IReadOnlyList<TabelaEspaco>> TabelasAsync(CancellationToken ct)
    {
        try
        {
            var ligacao = db.Database.GetDbConnection();
            var abriu = ligacao.State != ConnectionState.Open;
            if (abriu) await ligacao.OpenAsync(ct);
            try
            {
                await using var cmd = ligacao.CreateCommand();
                cmd.CommandText = """
                    SELECT TOP 12 OBJECT_NAME(s.object_id), SUM(CASE WHEN s.index_id IN (0, 1) THEN s.row_count ELSE 0 END),
                           CAST(SUM(s.reserved_page_count) * 8 / 1024.0 AS decimal(18,2))
                    FROM sys.dm_db_partition_stats s JOIN sys.tables t ON t.object_id = s.object_id
                    GROUP BY s.object_id ORDER BY SUM(s.reserved_page_count) DESC
                    """;
                var lista = new List<TabelaEspaco>();
                await using var r = await cmd.ExecuteReaderAsync(ct);
                while (await r.ReadAsync(ct))
                    lista.Add(new TabelaEspaco(r.GetString(0), r.GetInt64(1), r.GetDecimal(2)));
                return lista;
            }
            finally
            {
                if (abriu) await ligacao.CloseAsync();
            }
        }
        catch (DbException)
        {
            return []; // sem permissão para ver o espaço das tabelas
        }
    }

    private async Task<object?> EscalarAsync(string sql, CancellationToken ct)
    {
        var ligacao = db.Database.GetDbConnection();
        var abriu = ligacao.State != ConnectionState.Open;
        if (abriu) await ligacao.OpenAsync(ct);
        try
        {
            await using var cmd = ligacao.CreateCommand();
            cmd.CommandText = sql;
            var v = await cmd.ExecuteScalarAsync(ct);
            return v is DBNull ? null : v;
        }
        finally
        {
            if (abriu) await ligacao.CloseAsync();
        }
    }
}
