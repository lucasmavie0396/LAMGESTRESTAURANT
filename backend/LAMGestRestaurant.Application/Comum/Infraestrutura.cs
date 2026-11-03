using System.Data;
using System.Globalization;
using LAMGestRestaurant.Domain.Sistema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace LAMGestRestaurant.Application.Comum;

/// <summary>Notificações em tempo real (SignalR). Chamar só depois do commit da transacção.</summary>
public interface INotificador
{
    /// <summary>Pedido criado/alterado/pago/cancelado. afectaCozinha = o KDS deve recarregar.</summary>
    Task PedidoAlteradoAsync(int pedidoId, bool afectaCozinha);
    Task MesasAlteradasAsync();
    /// <summary>Itens ficaram prontos na cozinha (alerta no POS).</summary>
    Task ItensProntosAsync(int pedidoId, string pedidoNumero, string? mesa, IReadOnlyList<string> produtos);
}

/// <summary>Leitura das configurações do sistema (tabela Configuracoes) com valores por omissão.</summary>
public class ConfiguracoesSistema(IAppDbContext db)
{
    public async Task<string> TextoAsync(string chave, string padrao = "") =>
        await db.Configuracoes.AsNoTracking().Where(c => c.Chave == chave).Select(c => c.Valor).FirstOrDefaultAsync() ?? padrao;

    /// <summary>Números das configurações: ponto decimal e sinal opcional, sem separadores de milhares (evita "50,5" → 505).</summary>
    public const NumberStyles EstiloDecimal = NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign;

    public async Task<decimal> DecimalAsync(string chave, decimal padrao = 0m) =>
        decimal.TryParse(await TextoAsync(chave), EstiloDecimal, CultureInfo.InvariantCulture, out var v) ? v : padrao;

    public async Task<bool> BoolAsync(string chave, bool padrao = false) =>
        bool.TryParse(await TextoAsync(chave), out var v) ? v : padrao;

    public async Task<TimeZoneInfo> FusoAsync()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(await TextoAsync(ChavesConfiguracao.FusoHorario, "Africa/Maputo")); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.Utc; }
    }

    /// <summary>Data local de hoje no fuso configurado.</summary>
    public async Task<DateOnly> HojeAsync(TimeProvider relogio) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(relogio.GetUtcNow().UtcDateTime, await FusoAsync()));

    /// <summary>Intervalo UTC [início, fim) que cobre os dias locais de «desde» a «ate» (inclusive).</summary>
    public async Task<(DateTime InicioUtc, DateTime FimUtc)> IntervaloUtcAsync(DateOnly desde, DateOnly ate)
    {
        var fuso = await FusoAsync();
        DateTime Utc(DateOnly d) => TimeZoneInfo.ConvertTimeToUtc(d.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified), fuso);
        return (Utc(desde), Utc(ate.AddDays(1)));
    }
}

/// <summary>
/// Numeração sequencial de documentos por série e ano (ex.: PED-2026-000001).
/// O UPDATE ... OUTPUT bloqueia a linha da série até ao fim da transacção do chamador,
/// pelo que pedidos simultâneos nunca recebem o mesmo número nem deixam saltos por concorrência.
/// Tem de ser chamado dentro de uma transacção.
/// </summary>
public class ServicoNumeracao(IAppDbContext db, ConfiguracoesSistema config, TimeProvider relogio)
{
    public const string SeriePedidos = "PED";
    public const string SerieRecibos = "REC";

    public async Task<string> ProximoAsync(string serie)
    {
        var transacao = db.Database.CurrentTransaction
                        ?? throw new InvalidOperationException("A numeração de documentos exige uma transacção activa.");
        var fuso = await config.FusoAsync();
        var ano = TimeZoneInfo.ConvertTimeFromUtc(relogio.GetUtcNow().UtcDateTime, fuso).Year;

        for (var tentativa = 0; tentativa < 2; tentativa++)
        {
            var numero = await IncrementarAsync(transacao, serie, ano);
            if (numero is int n)
                return $"{serie}-{ano}-{n:000000}";
            try
            {
                await ExecutarAsync(transacao,
                    "INSERT INTO SeriesDocumento (Codigo, Prefixo, Ano, UltimoNumero) VALUES (@codigo, @codigo, @ano, 0)", serie, ano);
            }
            catch (Exception) when (tentativa == 0)
            {
                // Outra transacção criou a série ao mesmo tempo: volta a tentar o incremento.
            }
        }
        throw new InvalidOperationException($"Não foi possível obter número para a série {serie}.");
    }

    private async Task<int?> IncrementarAsync(IDbContextTransaction transacao, string serie, int ano)
    {
        await using var cmd = Comando(transacao,
            "UPDATE SeriesDocumento SET UltimoNumero = UltimoNumero + 1 OUTPUT inserted.UltimoNumero WHERE Codigo = @codigo AND Ano = @ano",
            serie, ano);
        var resultado = await cmd.ExecuteScalarAsync();
        return resultado is null or DBNull ? null : Convert.ToInt32(resultado, CultureInfo.InvariantCulture);
    }

    private async Task ExecutarAsync(IDbContextTransaction transacao, string sql, string serie, int ano)
    {
        await using var cmd = Comando(transacao, sql, serie, ano);
        await cmd.ExecuteNonQueryAsync();
    }

    private System.Data.Common.DbCommand Comando(IDbContextTransaction transacao, string sql, string serie, int ano)
    {
        var ligacao = db.Database.GetDbConnection();
        var cmd = ligacao.CreateCommand();
        cmd.Transaction = transacao.GetDbTransaction();
        cmd.CommandText = sql;
        var pCodigo = cmd.CreateParameter();
        pCodigo.ParameterName = "@codigo";
        pCodigo.DbType = DbType.String;
        pCodigo.Value = serie;
        var pAno = cmd.CreateParameter();
        pAno.ParameterName = "@ano";
        pAno.DbType = DbType.Int32;
        pAno.Value = ano;
        cmd.Parameters.Add(pCodigo);
        cmd.Parameters.Add(pAno);
        return cmd;
    }
}
