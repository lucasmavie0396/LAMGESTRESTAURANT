using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;

namespace LAMGestRestaurant.Instalador.Motor;

/// <summary>Chave do agente devolvida pelo servidor no registo (igual ao AgenteRegistado da API).</summary>
public sealed record RegistoAgente(int Posto, string Nome, string Chave);

/// <summary>Onde o agente fica instalado (configurável para testes).</summary>
public sealed record PastasAgente(string Programa, string Dados, string Arranque)
{
    public static PastasAgente Padrao() => new(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "LAMGestRestaurant", "Agente"),
        InstalacaoServidor.PastaDados,
        Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup));
}

/// <summary>
/// Agente de impressão do posto: o programa que imprime nas impressoras instaladas neste computador (USB ou de rede)
/// os talões e recibos que o servidor lhe manda. Instala-se em Programas, arranca com o Windows de qualquer utilizador
/// e autentica-se no servidor com uma chave própria, pedida com a conta de um administrador do LAMGest.
/// </summary>
public static class AgentePosto
{
    public const string NomeExecutavel = "LAMGest-Agente.exe";
    public const string NomeAtalho = "LAMGest Agente de impressão.lnk";
    public const string NomeConfiguracao = "agente.json";

    public static bool Incluido => typeof(AgentePosto).Assembly.GetManifestResourceInfo(NomeExecutavel) is not null;

    /// <summary>Entra com o administrador e regista o agente do posto (uma chave nova; a anterior deixa de valer).</summary>
    public static async Task<RegistoAgente> RegistarAsync(string servidor, int porta, int numero, string utilizador, string palavraPasse, CancellationToken ct = default)
    {
        using var manipulador = new HttpClientHandler { CookieContainer = new CookieContainer(), UseCookies = true };
        using var http = new HttpClient(manipulador) { Timeout = TimeSpan.FromSeconds(15) };
        using (var login = await http.PostAsJsonAsync(InstalacaoPosto.Endereco(servidor, porta, "api/auth/login"),
                   new { nomeUtilizador = utilizador.Trim(), palavraPasse }, ct))
        {
            if (login.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity)
                throw new InvalidOperationException("Utilizador ou palavra-passe do LAMGest inválidos.");
            if (login.StatusCode == HttpStatusCode.Locked)
                throw new InvalidOperationException("A conta está bloqueada temporariamente por tentativas falhadas. Tente mais tarde.");
            login.EnsureSuccessStatusCode();
        }
        try
        {
            using var registo = await http.PostAsync(InstalacaoPosto.Endereco(servidor, porta, $"api/postos/{numero}/agente"), null, ct);
            return registo.StatusCode switch
            {
                HttpStatusCode.Forbidden => throw new InvalidOperationException("Esta conta não tem a permissão «Configurações → gerir». Use a conta de um administrador."),
                HttpStatusCode.NotFound => throw new InvalidOperationException($"O posto {numero} não está registado no servidor. Registe-o em Caixa → Terminais (campo «N.º posto»)."),
                _ => await registo.EnsureSuccessStatusCode().Content.ReadFromJsonAsync<RegistoAgente>(cancellationToken: ct)
                     ?? throw new InvalidOperationException("Resposta vazia do servidor."),
            };
        }
        finally
        {
            try
            {
                using var _ = await http.PostAsync(InstalacaoPosto.Endereco(servidor, porta, "api/auth/logout"), null, CancellationToken.None);
            }
            catch (HttpRequestException)
            {
            }
        }
    }

    /// <summary>Instala (ou actualiza) o agente e a sua configuração e põe-no a arrancar com o Windows. Devolve o caminho do executável.</summary>
    public static string Instalar(string servidor, int porta, RegistoAgente registo, PastasAgente pastas, IProgress<string> progresso, Stream? executavel = null)
    {
        PararAgentes(progresso);
        Directory.CreateDirectory(pastas.Programa);
        var exe = Path.Combine(pastas.Programa, NomeExecutavel);
        using (var origem = executavel ?? typeof(AgentePosto).Assembly.GetManifestResourceStream(NomeExecutavel)
                   ?? throw new InvalidOperationException("Este Setup não inclui o agente de impressão. Gere-o com scripts\\criar-setup.ps1."))
        using (var destino = File.Create(exe))
            origem.CopyTo(destino);
        progresso.Report("• Agente de impressão: " + exe);

        Directory.CreateDirectory(pastas.Dados);
        var config = Path.Combine(pastas.Dados, NomeConfiguracao);
        File.WriteAllText(config, JsonSerializer.Serialize(new
        {
            Servidor = InstalacaoPosto.Endereco(servidor, porta).ToString().TrimEnd('/'),
            registo.Posto,
            registo.Chave,
        }, new JsonSerializerOptions { WriteIndented = true }));
        Proteger(config);
        progresso.Report("• Configuração do agente (só administradores a alteram): " + config);

        Directory.CreateDirectory(pastas.Arranque);
        var atalho = Path.Combine(pastas.Arranque, NomeAtalho);
        InstalacaoPosto.CriarAtalho(atalho, exe, "", $"LAMGest Restaurant — impressão do posto {registo.Posto}", exe);
        progresso.Report("• Arranque automático (todos os utilizadores): " + atalho);
        return exe;
    }

    /// <summary>Abre o agente na sessão de quem está no computador, sem privilégios de administrador (através do Explorador).</summary>
    public static void Iniciar(string exe, IProgress<string> progresso)
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{exe}\"") { UseShellExecute = false })?.Dispose();
            progresso.Report("• Agente de impressão iniciado (ícone junto ao relógio).");
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            progresso.Report("⚠ Não foi possível iniciar o agente agora: arranca no próximo início de sessão. " + ex.Message);
        }
    }

    /// <summary>Fecha os agentes em execução (actualizar o executável e ler a chave nova).</summary>
    private static void PararAgentes(IProgress<string> progresso)
    {
        foreach (var p in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(NomeExecutavel)))
            using (p)
            {
                try
                {
                    p.Kill();
                    p.WaitForExit(5000);
                    progresso.Report("• Agente anterior fechado (sessão " + p.SessionId + ").");
                }
                catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
                {
                }
            }
    }

    /// <summary>A chave só pode ser alterada por administradores; os utilizadores do posto lêem-na (o agente corre na sessão deles).</summary>
    private static void Proteger(string ficheiro)
    {
        if (!OperatingSystem.IsWindows())
            return;
        var acl = new FileSecurity();
        acl.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        acl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), FileSystemRights.FullControl, AccessControlType.Allow));
        acl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), FileSystemRights.FullControl, AccessControlType.Allow));
        acl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null), FileSystemRights.Read, AccessControlType.Allow));
        new FileInfo(ficheiro).SetAccessControl(acl);
    }
}
