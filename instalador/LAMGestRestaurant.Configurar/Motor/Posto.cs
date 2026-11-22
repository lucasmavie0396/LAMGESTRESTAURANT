using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace LAMGestRestaurant.Configurar.Motor;

/// <summary>Posto de trabalho configurado pelo LAMGest-Setup.exe (posto.json, atalhos e agente de impressão).</summary>
public sealed class PostoInstalado
{
    public string Servidor { get; set; } = "";
    public int Porta { get; set; } = 8080;
    public int Numero { get; set; } = 1;
    /// <summary>Porta HTTPS em uso (null = o posto está em HTTP).</summary>
    public int? PortaHttps { get; set; }
    public bool TemAgente { get; set; }
    public string? ServidorAgente { get; set; }

    public static bool Existe(Locais locais) => File.Exists(locais.ConfigPosto);

    public static PostoInstalado Ler(Locais locais)
    {
        var d = Json.LerFicheiro(locais.ConfigPosto);
        var p = new PostoInstalado
        {
            Servidor = Json.Texto(d, "Servidor") ?? "",
            Porta = Json.Inteiro(d, "Porta") ?? 8080,
            Numero = Json.Inteiro(d, "Numero") ?? 1,
            PortaHttps = Json.Inteiro(d, "PortaHttps"),
            TemAgente = File.Exists(locais.ConfigAgente),
        };
        if (p.TemAgente)
        {
            try
            {
                p.ServidorAgente = Json.Texto(Json.LerFicheiro(locais.ConfigAgente), "Servidor");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException or ArgumentException)
            {
            }
        }
        return p;
    }
}

/// <summary>Dados novos do posto: servidor, porta e número; opcionalmente regista de novo o agente de impressão.</summary>
public sealed class PedidoPosto
{
    public string Servidor { get; set; } = "";
    public int Porta { get; set; }
    public int Numero { get; set; }
    public bool RegistarAgente { get; set; }
    public string? UtilizadorLamgest { get; set; }
    public string? PalavraPasseLamgest { get; set; }
}

public static class ConfiguracaoPosto
{
    public static string? ValidarServidor(string servidor) =>
        string.IsNullOrWhiteSpace(servidor) || Uri.CheckHostName(servidor.Trim()) == UriHostNameType.Unknown
            ? "Indique o IP ou o nome do servidor (ex.: 192.168.1.10)."
            : null;

    /// <summary>Endereço no servidor (igual ao do instalador): http(s)://servidor:porta/caminho.</summary>
    public static Uri Endereco(string servidor, int porta, string caminho = "", bool https = false) =>
        new UriBuilder(https ? "https" : "http", servidor.Trim(), porta, "/" + caminho.TrimStart('/')).Uri;

    /// <summary>
    /// Novo endereço do atalho: troca o servidor, a porta e o número do posto, mantendo a página e as outras opções (ex.: tactil=1).
    /// </summary>
    public static string NovoEndereco(string antigo, string servidor, int porta, int numero, bool https = false)
    {
        var u = new Uri(antigo);
        var b = new UriBuilder(u) { Scheme = https ? "https" : "http", Host = servidor.Trim(), Port = porta };
        var partes = u.Query.TrimStart('?').Split(new[] { '&' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(q => !q.StartsWith("posto=", StringComparison.OrdinalIgnoreCase)).ToList();
        partes.Insert(0, "posto=" + numero);
        b.Query = string.Join("&", partes);
        return b.Uri.ToString();
    }

    /// <summary>Troca o --app=… dos argumentos do navegador. Devolve null se o atalho não tiver --app.</summary>
    public static string? NovosArgumentos(string argumentos, string servidor, int porta, int numero, bool https = false)
    {
        var m = Regex.Match(argumentos, @"--app=(""[^""]+""|\S+)");
        if (!m.Success)
            return null;
        var antigo = m.Groups[1].Value.Trim('"');
        return argumentos.Substring(0, m.Index) + "--app=" + NovoEndereco(antigo, servidor, porta, numero, https) + argumentos.Substring(m.Index + m.Length);
    }

    /// <summary>Confirma que é um servidor LAMGest e que o posto está registado nele. Devolve o nome do posto.</summary>
    public static async Task<string> TestarAsync(string servidor, int porta, int numero)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(6) };
        Dictionary<string, object?> saude;
        try
        {
            using var r = await http.GetAsync(Endereco(servidor, porta, "api/saude"));
            saude = Json.LerObjecto(await r.Content.ReadAsStringAsync());
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or FormatException or ArgumentException or InvalidOperationException)
        {
            throw new InvalidOperationException($"O servidor {servidor}:{porta} não respondeu. Confirme o IP, a porta, a firewall do servidor e que o serviço LAMGest está a correr.", ex);
        }
        if (Json.Obter(saude, "api") is null)
            throw new InvalidOperationException("Esse endereço responde, mas não é um servidor LAMGest.");
        if (Json.Obter(saude, "baseDados") is not true)
            throw new InvalidOperationException("O servidor LAMGest responde, mas está sem ligação à base de dados.");

        using var posto = await http.GetAsync(Endereco(servidor, porta, $"api/postos/{numero}"));
        if (posto.StatusCode == HttpStatusCode.NotFound)
            throw new InvalidOperationException($"O posto {numero} não está registado neste servidor. Registe-o no LAMGest em Caixa → Terminais (campo «N.º posto»).");
        posto.EnsureSuccessStatusCode();
        return Json.Texto(Json.LerObjecto(await posto.Content.ReadAsStringAsync()), "nome") ?? $"Posto {numero}";
    }

    /// <summary>
    /// HTTPS do servidor (igual ao instalador): lê em HTTP a autoridade local e confirma a impressão digital. Null se o servidor
    /// só tiver HTTP ou for de uma versão anterior.
    /// </summary>
    public static async Task<(int Porta, X509Certificate2 Ca, string Impressao)?> LerHttpsAsync(string servidor, int porta)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(6) };
        using var r = await http.GetAsync(Endereco(servidor, porta, "api/sistema/certificado"));
        if (r.StatusCode == HttpStatusCode.NotFound)
            return null;
        r.EnsureSuccessStatusCode();
        var d = Json.LerObjecto(await r.Content.ReadAsStringAsync());
        if (Json.Obter(d, "https") is not true || Json.Inteiro(d, "porta") is not int portaHttps || Json.Texto(d, "certificado") is not { } base64)
            return null;
        var ca = new X509Certificate2(Convert.FromBase64String(base64));
        var impressao = ImpressaoDigital(ca);
        if (!string.Equals(impressao, Json.Texto(d, "impressaoDigital"), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("O certificado recebido do servidor não confere. Tente de novo; se repetir, verifique a rede.");
        return (portaHttps, ca, impressao);
    }

    public static string ImpressaoDigital(X509Certificate2 c)
    {
        using var sha = SHA256.Create();
        return BitConverter.ToString(sha.ComputeHash(c.RawData)).Replace("-", "");
    }

    public static string Legivel(string impressao) =>
        string.Join(" ", Enumerable.Range(0, Math.Min(impressao.Length, 32) / 4).Select(i => impressao.Substring(i * 4, 4)));

    /// <summary>Confia na autoridade local do servidor neste computador (raízes fidedignas do computador; o configurador corre como administrador).</summary>
    public static void ConfiarNaAutoridade(X509Certificate2 ca)
    {
        var raiz = new X509Store(StoreName.Root, StoreLocation.LocalMachine);
        try
        {
            raiz.Open(OpenFlags.ReadWrite);
            if (raiz.Certificates.Find(X509FindType.FindByThumbprint, ca.Thumbprint, false).Count == 0)
                raiz.Add(new X509Certificate2(ca.Export(X509ContentType.Cert)));
        }
        finally
        {
            raiz.Close();
        }
    }

    /// <summary>Com a autoridade já instalada: o servidor responde em HTTPS por este endereço (o certificado inclui-o).</summary>
    public static async Task<bool> HttpsRespondeAsync(string servidor, int portaHttps)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(6) };
            using var r = await http.GetAsync(Endereco(servidor, portaHttps, "api/saude", https: true));
            return r.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return false;
        }
    }

    /// <summary>Entra com um administrador do LAMGest e regista o agente deste posto (chave nova). Devolve (nome, chave).</summary>
    public static async Task<(string Nome, string Chave)> RegistarAgenteAsync(string servidor, int porta, int numero, string utilizador, string palavraPasse, bool https = false)
    {
        using var manipulador = new HttpClientHandler { CookieContainer = new CookieContainer(), UseCookies = true };
        using var http = new HttpClient(manipulador) { Timeout = TimeSpan.FromSeconds(15) };
        var corpo = Json.Escrever(new Dictionary<string, object?> { ["nomeUtilizador"] = utilizador.Trim(), ["palavraPasse"] = palavraPasse });
        using (var login = await http.PostAsync(Endereco(servidor, porta, "api/auth/login", https), new StringContent(corpo, Encoding.UTF8, "application/json")))
        {
            if (login.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.BadRequest || (int)login.StatusCode == 422)
                throw new InvalidOperationException("Utilizador ou palavra-passe do LAMGest inválidos.");
            if ((int)login.StatusCode == 423)
                throw new InvalidOperationException("A conta está bloqueada temporariamente por tentativas falhadas. Tente mais tarde.");
            login.EnsureSuccessStatusCode();
        }
        try
        {
            using var registo = await http.PostAsync(Endereco(servidor, porta, $"api/postos/{numero}/agente", https), null);
            if (registo.StatusCode == HttpStatusCode.Forbidden)
                throw new InvalidOperationException("Esta conta não tem a permissão «Configurações → gerir». Use a conta de um administrador.");
            if (registo.StatusCode == HttpStatusCode.NotFound)
                throw new InvalidOperationException($"O posto {numero} não está registado no servidor. Registe-o em Caixa → Terminais (campo «N.º posto»).");
            registo.EnsureSuccessStatusCode();
            var d = Json.LerObjecto(await registo.Content.ReadAsStringAsync());
            var chave = Json.Texto(d, "chave") ?? throw new InvalidOperationException("Resposta do servidor sem a chave do agente.");
            return (Json.Texto(d, "nome") ?? $"Posto {numero}", chave);
        }
        finally
        {
            try
            {
                using var _ = await http.PostAsync(Endereco(servidor, porta, "api/auth/logout", https), null);
            }
            catch (HttpRequestException)
            {
            }
        }
    }

    /// <summary>Atalhos «LAMGest Restaurant» do posto (ambiente de trabalho, menu Iniciar e arranque, para todos os utilizadores).</summary>
    public static IEnumerable<string> AtalhosDoPosto() =>
        new[] { Environment.SpecialFolder.CommonDesktopDirectory, Environment.SpecialFolder.CommonPrograms, Environment.SpecialFolder.CommonStartup }
            .Select(p => Path.Combine(Environment.GetFolderPath(p), Locais.NomeAtalhoPosto))
            .Where(File.Exists);

    public static async Task AplicarAsync(Locais locais, PostoInstalado actual, PedidoPosto p, IProgress<string> progresso, IEnumerable<string>? atalhos = null)
    {
        void Passo(string t) => progresso.Report("• " + t);
        if (ValidarServidor(p.Servidor) is { } erro)
            throw new InvalidOperationException(erro);
        Passo($"A confirmar o servidor {p.Servidor}:{p.Porta} e o posto {p.Numero}…");
        var nomePosto = await TestarAsync(p.Servidor, p.Porta, p.Numero);
        progresso.Report("   " + nomePosto);

        // HTTPS: com a autoridade local do servidor instalada, os atalhos e o agente passam para https://servidor:porta-https.
        int? portaHttps = null;
        try
        {
            if (await LerHttpsAsync(p.Servidor, p.Porta) is { } h)
            {
                ConfiarNaAutoridade(h.Ca);
                if (await HttpsRespondeAsync(p.Servidor, h.Porta))
                {
                    portaHttps = h.Porta;
                    Passo($"Ligação cifrada (HTTPS, porta {h.Porta}). Autoridade local do servidor: {Legivel(h.Impressao)}");
                }
                else
                    progresso.Report($"⚠ O servidor tem HTTPS (porta {h.Porta}), mas o certificado não serve para «{p.Servidor.Trim()}» ou a porta está fechada: o posto fica em HTTP.");
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or FormatException or ArgumentException or System.Security.Cryptography.CryptographicException)
        {
            progresso.Report("⚠ Não foi possível confirmar o HTTPS do servidor: o posto fica em HTTP. " + ex.Message);
        }
        var https = portaHttps is not null;
        var portaUsada = portaHttps ?? p.Porta;

        (string Nome, string Chave)? agente = null;
        if (p.RegistarAgente)
        {
            Passo("A registar o agente de impressão no servidor…");
            agente = await RegistarAgenteAsync(p.Servidor, portaUsada, p.Numero, p.UtilizadorLamgest ?? "", p.PalavraPasseLamgest ?? "", https);
        }

        foreach (var caminho in atalhos ?? AtalhosDoPosto())
        {
            var dados = Atalhos.Ler(caminho);
            var novos = NovosArgumentos(dados.Argumentos, p.Servidor, portaUsada, p.Numero, https);
            if (novos is null)
                continue;
            Atalhos.Alterar(caminho, novos, $"LAMGest Restaurant — posto {p.Numero} ({p.Servidor.Trim()}:{portaUsada})");
            Passo("Atalho: " + caminho);
        }

        var registo = Json.LerFicheiro(locais.ConfigPosto);
        Json.Definir(registo, "Servidor", p.Servidor.Trim());
        Json.Definir(registo, "Porta", p.Porta);
        Json.Definir(registo, "Numero", p.Numero);
        Json.Definir(registo, "PortaHttps", portaHttps);
        if (Json.Texto(registo, "Endereco") is { Length: > 0 } endereco)
            Json.Definir(registo, "Endereco", NovoEndereco(endereco, p.Servidor, portaUsada, p.Numero, https));
        Json.Definir(registo, "AlteradoEm", DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss"));
        Json.GravarFicheiro(locais.ConfigPosto, registo);
        Passo("Registo do posto: " + locais.ConfigPosto);

        if (agente is { } a)
        {
            GravarAgente(locais.ConfigAgente, Endereco(p.Servidor, portaUsada, "", https).ToString().TrimEnd('/'), p.Numero, a.Chave);
            Passo("Configuração do agente de impressão: " + locais.ConfigAgente);
            ReiniciarAgente(progresso);
            progresso.Report("   Escolha as impressoras deste posto no LAMGest: Configurações → Impressoras → «Ligada a: " + a.Nome + "».");
        }
        else if (actual.TemAgente)
            progresso.Report("⚠ O agente de impressão não foi registado de novo: se o servidor ou a base mudou, os talões deste posto não saem até o registar.");

        RegistoAlteracoes.Escrever(locais, $"Posto: {actual.Servidor}:{actual.Porta} n.º {actual.Numero} -> {p.Servidor.Trim()}:{p.Porta} n.º {p.Numero}" + (agente is null ? "" : " (agente registado)"));
        Passo("Concluído. Feche e abra o «LAMGest Restaurant» neste posto.");
    }

    /// <summary>Configuração do agente (como o instalador a escreve): só os administradores a alteram, os utilizadores lêem-na.</summary>
    internal static void GravarAgente(string ficheiro, string servidor, int posto, string chave)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ficheiro)!);
        Json.GravarFicheiro(ficheiro, new Dictionary<string, object?> { ["Servidor"] = servidor, ["Posto"] = posto, ["Chave"] = chave });
        var acl = new FileSecurity();
        acl.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        acl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), FileSystemRights.FullControl, AccessControlType.Allow));
        acl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), FileSystemRights.FullControl, AccessControlType.Allow));
        acl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null), FileSystemRights.Read, AccessControlType.Allow));
        File.SetAccessControl(ficheiro, acl);
    }

    /// <summary>Fecha o agente e abre-o de novo na sessão de quem está no computador (sem privilégios, através do Explorador).</summary>
    private static void ReiniciarAgente(IProgress<string> progresso)
    {
        string? exe = null;
        foreach (var processo in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(Locais.NomeAgente)))
            using (processo)
            {
                try
                {
                    exe ??= processo.MainModule?.FileName;
                }
                catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
                {
                }
                try
                {
                    processo.Kill();
                    processo.WaitForExit(5000);
                }
                catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
                {
                }
            }
        exe ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "LAMGestRestaurant", "Agente", Locais.NomeAgente);
        if (!File.Exists(exe))
        {
            progresso.Report("   O agente arranca no próximo início de sessão.");
            return;
        }
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", Processos.Aspas(exe)) { UseShellExecute = false })?.Dispose();
            progresso.Report("• Agente de impressão reiniciado.");
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            progresso.Report("   O agente arranca no próximo início de sessão: " + ex.Message);
        }
    }
}
