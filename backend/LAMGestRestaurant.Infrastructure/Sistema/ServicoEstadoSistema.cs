using System.Data;
using System.Data.Common;
using System.Text.Json;
using LAMGestRestaurant.Infrastructure.Dados;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace LAMGestRestaurant.Infrastructure.Sistema;

/// <summary>Resultado da última execução do script de cópias (backup-estado-&lt;Base&gt;.json, escrito por scripts\backup-sql.ps1).</summary>
public sealed record EstadoCopias(
    DateTime? UltimaExecucaoUtc,
    bool? UltimaComSucesso,
    string? Mensagem,
    DateTime? UltimaCopiaUtc,
    string? Ficheiro,
    decimal? TamanhoMb);

public sealed record EstadoEspaco(
    decimal BaseMb,
    /// <summary>10 GB no SQL Server Express (limite por base); null nas outras edições.</summary>
    decimal? LimiteMb,
    string? Disco,
    decimal? DiscoLivreMb,
    decimal? DiscoTotalMb);

/// <summary>Nivel: «aviso» (âmbar) ou «critico» (vermelho).</summary>
public sealed record AvisoSistema(string Nivel, string Titulo, string Texto);

/// <summary>Certificado HTTPS do servidor (null se o servidor só tiver HTTP).</summary>
public sealed record EstadoCertificado(DateTime ValidoAteUtc, IReadOnlyList<string> Nomes, IReadOnlyList<string> EnderecosEmFalta);

public sealed record EstadoSistema(string BaseDados, EstadoCopias Copias, EstadoEspaco Espaco, IReadOnlyList<AvisoSistema> Avisos, EstadoCertificado? Certificado = null);

/// <summary>Dados lidos do SQL Server e do disco (separados das regras, para estas poderem ser testadas sem servidor).</summary>
public sealed record LeituraSistema(
    string BaseDados,
    DateTime BaseCriadaUtc,
    EstadoCopias Copias,
    EstadoEspaco Espaco,
    EstadoCertificado? Certificado = null);

/// <summary>
/// Saúde do servidor para o administrador: cópias de segurança e espaço (base e disco). O SQL Server Express não tem
/// Agent nem alertas: sem isto, uma cópia que falha ou uma base perto dos 10 GB só se descobrem tarde de mais.
/// </summary>
public sealed class ServicoEstadoSistema(AppDbContext db, IConfiguration configuracao, TimeProvider relogio)
{
    public const decimal LimiteExpressMb = 10 * 1024;

    public async Task<EstadoSistema> EstadoAsync(CancellationToken ct)
    {
        var leitura = await LerAsync(ct);
        return new EstadoSistema(leitura.BaseDados, leitura.Copias, leitura.Espaco, Avisos(leitura, relogio.GetUtcNow().UtcDateTime), leitura.Certificado);
    }

    /// <summary>Regras dos avisos (testáveis sem SQL Server nem disco).</summary>
    public static IReadOnlyList<AvisoSistema> Avisos(LeituraSistema l, DateTime agoraUtc)
    {
        var avisos = new List<AvisoSistema>();
        var c = l.Copias;
        if (c.UltimaComSucesso == false)
            avisos.Add(new("critico", "Cópia de segurança falhou",
                $"A última cópia de segurança automática falhou ({Hora(c.UltimaExecucaoUtc)}): {c.Mensagem}"));
        if (c.UltimaCopiaUtc is DateTime ultima)
        {
            var horas = (agoraUtc - ultima).TotalHours;
            if (horas > 72)
                avisos.Add(new("critico", "Sem cópia de segurança recente", $"A última cópia de segurança tem {(int)(horas / 24)} dias. Verifique a tarefa «LAMGest Restaurant - Backup SQL» no servidor."));
            else if (horas > 36)
                avisos.Add(new("aviso", "Sem cópia de segurança recente", $"A última cópia de segurança tem {(int)horas} horas. Verifique se o servidor esteve desligado à hora da cópia."));
        }
        else if ((agoraUtc - l.BaseCriadaUtc).TotalHours > 36)
            avisos.Add(new("aviso", "Sem cópias de segurança", "Não há registo de nenhuma cópia de segurança desta base. Agende-a no LAMGest-Setup (modo Servidor) ou veja docs/BACKUP.md."));

        var e = l.Espaco;
        if (e.LimiteMb is decimal limite && limite > 0)
        {
            var uso = e.BaseMb / limite;
            if (uso >= 0.9m)
                avisos.Add(new("critico", "Base de dados quase cheia", $"A base ocupa {Gb(e.BaseMb)} de {Gb(limite)} (limite do SQL Server Express). Ao chegar ao limite deixa de ser possível gravar: passe para uma edição sem limite ou contacte o suporte."));
            else if (uso >= 0.7m)
                avisos.Add(new("aviso", "Base de dados a crescer", $"A base ocupa {Gb(e.BaseMb)} de {Gb(limite)} (limite do SQL Server Express). Planeie a passagem para uma edição sem limite."));
        }
        if (e.DiscoLivreMb is decimal livre)
        {
            var fraccao = e.DiscoTotalMb is decimal total && total > 0 ? livre / total : 1m;
            if (livre < 1024 || fraccao < 0.03m)
                avisos.Add(new("critico", "Disco quase cheio", $"O disco {e.Disco} da base de dados tem só {Gb(livre)} livres: a base e as cópias de segurança podem deixar de ser gravadas."));
            else if (livre < 2048 || fraccao < 0.10m)
                avisos.Add(new("aviso", "Pouco espaço em disco", $"O disco {e.Disco} da base de dados tem {Gb(livre)} livres. Liberte espaço ou mova as cópias de segurança para outro disco."));
        }
        if (l.Certificado is { } cert)
        {
            var dias = (cert.ValidoAteUtc - agoraUtc).TotalDays;
            if (dias < 0)
                avisos.Add(new("critico", "Certificado HTTPS expirado", "O certificado HTTPS do servidor expirou: os postos e tablets em HTTPS mostram um erro de segurança. Volte a correr o LAMGest-Setup no servidor (modo Servidor) para o renovar."));
            else if (dias < 60)
                avisos.Add(new("aviso", "Certificado HTTPS a expirar", $"O certificado HTTPS do servidor expira em {(int)dias} dias. Volte a correr o LAMGest-Setup no servidor (modo Servidor) para o renovar."));
            if (cert.EnderecosEmFalta.Count > 0)
                avisos.Add(new("aviso", "Endereço do servidor mudou", $"O certificado HTTPS não inclui o endereço {string.Join(", ", cert.EnderecosEmFalta)} deste servidor. Quem entrar por esse endereço vê um erro de segurança: volte a correr o LAMGest-Setup no servidor ou fixe o IP do servidor no router."));
        }
        return avisos;
    }

    /// <summary>Certificado HTTPS configurado (Kestrel:Certificates:Default) e os endereços IPv4 deste computador que ele não cobre.</summary>
    private EstadoCertificado? LerCertificado()
    {
        if (configuracao["Kestrel:Certificates:Default:Path"] is not { Length: > 0 } ficheiro || !File.Exists(ficheiro))
            return null;
        try
        {
            using var c = System.Security.Cryptography.X509Certificates.X509CertificateLoader.LoadPkcs12FromFile(ficheiro, configuracao["Kestrel:Certificates:Default:Password"]);
            var san = c.Extensions.OfType<System.Security.Cryptography.X509Certificates.X509SubjectAlternativeNameExtension>().FirstOrDefault();
            var ips = san?.EnumerateIPAddresses().Select(i => i.ToString()).ToList() ?? [];
            var nomes = (san?.EnumerateDnsNames() ?? []).Concat(ips).ToList();
            var locais = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up
                            && n.NetworkInterfaceType != System.Net.NetworkInformation.NetworkInterfaceType.Loopback)
                .SelectMany(n => n.GetIPProperties().UnicastAddresses)
                .Where(a => a.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && !a.Address.ToString().StartsWith("169.254.", StringComparison.Ordinal))
                .Select(a => a.Address.ToString())
                .Distinct();
            return new EstadoCertificado(c.NotAfter.ToUniversalTime(), nomes, [.. locais.Where(ip => !ips.Contains(ip))]);
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return new EstadoCertificado(DateTime.MinValue, [], []);
        }
    }

    private static string Gb(decimal mb) => mb < 1024m
        ? mb.ToString("0", System.Globalization.CultureInfo.GetCultureInfo("pt-PT")) + " MB"
        : (mb / 1024m).ToString("0.0", System.Globalization.CultureInfo.GetCultureInfo("pt-PT")) + " GB";

    private static string Hora(DateTime? utc) => utc is DateTime d ? d.ToString("dd/MM/yyyy HH:mm", System.Globalization.CultureInfo.InvariantCulture) + " UTC" : "data desconhecida";

    private async Task<LeituraSistema> LerAsync(CancellationToken ct)
    {
        var ligacao = db.Database.GetDbConnection();
        var abriu = ligacao.State != ConnectionState.Open;
        if (abriu)
            await ligacao.OpenAsync(ct);
        try
        {
            var nome = ligacao.Database;
            var criada = await EscalarAsync<DateTime>(ligacao, "SELECT create_date FROM sys.databases WHERE name = DB_NAME()", ct);
            var baseMb = Convert.ToDecimal(await EscalarAsync<object>(ligacao,
                "SELECT CAST(SUM(CAST(size AS bigint)) * 8 / 1024.0 AS decimal(18,2)) FROM sys.database_files WHERE type = 0", ct));
            var express = Convert.ToInt32(await EscalarAsync<object>(ligacao, "SELECT CAST(SERVERPROPERTY('EngineEdition') AS int)", ct)) == 4;
            var maquina = Convert.ToString(await EscalarAsync<object>(ligacao, "SELECT CAST(SERVERPROPERTY('MachineName') AS nvarchar(128))", ct));
            var ficheiro = Convert.ToString(await EscalarAsync<object>(ligacao, "SELECT TOP 1 physical_name FROM sys.database_files WHERE type = 0", ct));

            string? disco = null;
            decimal? livre = null, total = null;
            // O disco só se pode ver quando o SQL Server corre neste computador.
            if (string.Equals(maquina, Environment.MachineName, StringComparison.OrdinalIgnoreCase) && Path.GetPathRoot(ficheiro) is { Length: > 0 } raiz)
            {
                try
                {
                    var d = new DriveInfo(raiz);
                    disco = d.Name.TrimEnd('\\');
                    livre = Math.Round(d.AvailableFreeSpace / 1024m / 1024m, 0);
                    total = Math.Round(d.TotalSize / 1024m / 1024m, 0);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
                {
                }
            }

            var copias = LerFicheiroCopias(nome);
            var msdb = await UltimaCopiaMsdbAsync(ligacao, ct);
            if (msdb is DateTime m && (copias.UltimaCopiaUtc is null || m > copias.UltimaCopiaUtc))
                copias = copias with { UltimaCopiaUtc = m };

            return new LeituraSistema(nome, DateTime.SpecifyKind(criada, DateTimeKind.Local).ToUniversalTime(), copias,
                new EstadoEspaco(baseMb, express ? LimiteExpressMb : null, disco, livre, total), LerCertificado());
        }
        finally
        {
            if (abriu)
                await ligacao.CloseAsync();
        }
    }

    /// <summary>Pasta onde o script de cópias escreve o estado (a mesma dos logs do servidor).</summary>
    private string PastaEstado() =>
        configuracao["Copias:PastaEstado"] is { Length: > 0 } p
            ? p
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "LAMGestRestaurant", "logs");

    public EstadoCopias LerFicheiroCopias(string baseDados)
    {
        var ficheiro = Path.Combine(PastaEstado(), $"backup-estado-{baseDados}.json");
        try
        {
            if (!File.Exists(ficheiro))
                return new EstadoCopias(null, null, null, null, null, null);
            using var json = JsonDocument.Parse(File.ReadAllText(ficheiro).TrimStart('﻿'));
            var r = json.RootElement;
            DateTime? Data(string nome) => r.TryGetProperty(nome, out var v) && v.ValueKind == JsonValueKind.String && DateTime.TryParse(v.GetString(),
                System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var d) ? d : null;
            string? Texto(string nome) => r.TryGetProperty(nome, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
            var sucesso = r.TryGetProperty("Sucesso", out var s) && s.ValueKind is JsonValueKind.True or JsonValueKind.False ? s.GetBoolean() : (bool?)null;
            decimal? tamanho = r.TryGetProperty("TamanhoMB", out var t) && t.ValueKind == JsonValueKind.Number ? t.GetDecimal() : null;
            return new EstadoCopias(Data("DataUtc"), sucesso, Texto("Mensagem"), Data("UltimaCopiaUtc"), Texto("Ficheiro"), tamanho);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new EstadoCopias(null, false, "Não foi possível ler o estado das cópias: " + ex.Message, null, null, null);
        }
    }

    /// <summary>Última cópia completa registada pelo SQL Server (inclui as feitas pelo Setup e pelo configurador). Sem permissão no msdb: null.</summary>
    private static async Task<DateTime?> UltimaCopiaMsdbAsync(DbConnection ligacao, CancellationToken ct)
    {
        try
        {
            var r = await EscalarAsync<object>(ligacao,
                "SELECT MAX(backup_finish_date) FROM msdb.dbo.backupset WHERE database_name = DB_NAME() AND type = 'D'", ct);
            return r is DateTime d ? DateTime.SpecifyKind(d, DateTimeKind.Local).ToUniversalTime() : null;
        }
        catch (DbException)
        {
            return null;
        }
    }

    private static async Task<T> EscalarAsync<T>(DbConnection ligacao, string sql, CancellationToken ct)
    {
        await using var cmd = ligacao.CreateCommand();
        cmd.CommandText = sql;
        return (T)(await cmd.ExecuteScalarAsync(ct))!;
    }
}
