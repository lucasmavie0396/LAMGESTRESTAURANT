using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.ServiceProcess;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace LAMGestRestaurant.Configurar.Motor;

/// <summary>O servidor LAMGest instalado neste computador (serviço Windows e configuração protegida).</summary>
public sealed class ServidorInstalado
{
    public string PastaInstalacao { get; set; } = "";
    public string Executavel => Path.Combine(PastaInstalacao, "LAMGestRestaurant.Api.exe");
    public int Porta { get; set; } = 8080;
    public string FicheiroConfig { get; set; } = "";
    public string? LigacaoServico { get; set; }
    public string Instancia { get; set; } = "";
    public string BaseDados { get; set; } = "";
    public string? EstadoServico { get; set; }

    /// <summary>Lista das migrations do programa instalado (migracoes.txt, posto pelo Setup junto da aplicação).</summary>
    public IReadOnlyList<string> MigracoesDoPrograma()
    {
        var f = Path.Combine(PastaInstalacao, "migracoes.txt");
        return File.Exists(f)
            ? File.ReadAllLines(f, Encoding.UTF8).Select(l => l.Trim().TrimStart('﻿')).Where(l => l.Length > 0).ToArray()
            : Array.Empty<string>();
    }

    public static bool Existe()
    {
        try
        {
            return ServiceController.GetServices().Any(s => s.ServiceName == Locais.NomeServico);
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>Lê o serviço (pasta, porta, ficheiro de configuração) e a ligação que o servidor usa hoje.</summary>
    public static ServidorInstalado Ler(Locais locais)
    {
        var s = new ServidorInstalado { FicheiroConfig = locais.ConfigServidor };
        using (var chave = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{Locais.NomeServico}"))
        {
            if (chave is null)
                throw new InvalidOperationException("O serviço LAMGest Restaurant não está instalado neste computador.");
            var imagem = (chave.GetValue("ImagePath") as string ?? "").Trim();
            var exe = imagem.StartsWith("\"") ? imagem.Substring(1, Math.Max(0, imagem.IndexOf('"', 1) - 1)) : imagem.Split(' ')[0];
            s.PastaInstalacao = Path.GetDirectoryName(Environment.ExpandEnvironmentVariables(exe)) ?? "";
            if (chave.GetValue("Environment") is string[] ambiente)
                AplicarAmbiente(s, ambiente);
        }
        if (File.Exists(s.FicheiroConfig))
        {
            var config = Json.LerFicheiro(s.FicheiroConfig);
            if (Json.Obter(config, "ConnectionStrings") is IDictionary<string, object?> ligacoes)
                s.LigacaoServico = Json.Texto(ligacoes, "Default");
        }
        if (s.LigacaoServico is { Length: > 0 } ligacao)
        {
            var partes = TextoLigacao.Ler(ligacao);
            s.Instancia = partes.Servidor;
            s.BaseDados = partes.BaseDados;
        }
        try
        {
            using var servico = new ServiceController(Locais.NomeServico);
            s.EstadoServico = servico.Status switch
            {
                ServiceControllerStatus.Running => "a correr",
                ServiceControllerStatus.Stopped => "parado",
                _ => servico.Status.ToString(),
            };
        }
        catch (InvalidOperationException)
        {
            s.EstadoServico = null;
        }
        return s;
    }

    internal static void AplicarAmbiente(ServidorInstalado s, IEnumerable<string> ambiente)
    {
        foreach (var linha in ambiente)
        {
            var i = linha.IndexOf('=');
            if (i <= 0)
                continue;
            var nome = linha.Substring(0, i);
            var valor = linha.Substring(i + 1);
            if (nome.Equals("ASPNETCORE_URLS", StringComparison.OrdinalIgnoreCase) && Regex.Match(valor, @":(\d+)") is { Success: true } m)
                s.Porta = int.Parse(m.Groups[1].Value);
            else if (nome.Equals("LAMGEST_CONFIG", StringComparison.OrdinalIgnoreCase) && valor.Length > 0)
                s.FicheiroConfig = valor;
        }
    }
}

/// <summary>Pedido de troca da base que o servidor abre.</summary>
public sealed class PedidoTroca
{
    public LigacaoAdmin Admin { get; set; } = null!;
    public string BaseDados { get; set; } = "";
    /// <summary>A base tem actualizações pendentes e o administrador confirmou (é feita cópia de segurança antes).</summary>
    public bool ActualizacaoConfirmada { get; set; }
}

/// <summary>
/// Troca a base do servidor: verifica a base, dá acesso à conta do serviço, actualiza a base se for preciso (com cópia de
/// segurança antes), muda a configuração e a tarefa das cópias, reinicia o serviço e confirma que responde. Se o serviço
/// não responder com a base nova, repõe a configuração anterior.
/// </summary>
public sealed class TrocaBase
{
    private readonly Locais locais;
    private readonly ServidorInstalado servidor;
    private readonly IProgress<string> progresso;

    public TrocaBase(Locais locais, ServidorInstalado servidor, IProgress<string> progresso)
    {
        this.locais = locais;
        this.servidor = servidor;
        this.progresso = progresso;
    }

    public async Task ExecutarAsync(PedidoTroca p)
    {
        var de = $"{servidor.Instancia}/{servidor.BaseDados}";
        var para = $"{p.Admin.Instancia}/{p.BaseDados}";
        Passo($"A verificar a base {p.BaseDados}…");
        var info = await BasesSql.AnalisarAsync(p.Admin, p.BaseDados, await MigracoesDeReferenciaAsync(p.Admin));
        if (!info.PodeSerUsada)
            throw new InvalidOperationException($"A base «{p.BaseDados}» não pode ser usada: {info.Descricao}.");
        if (info.Situacao == SituacaoBase.PrecisaActualizacao && !p.ActualizacaoConfirmada)
            throw new InvalidOperationException("A base é de uma versão anterior: confirme a actualização.");

        if (info.Situacao == SituacaoBase.PrecisaActualizacao)
        {
            Passo("Cópia de segurança da base antes de a actualizar…");
            Info("Cópia criada: " + await BasesSql.CopiaSegurancaAsync(p.Admin, p.BaseDados, "antes-troca-base"));
        }

        Passo("A dar acesso da conta do serviço à base…");
        var ligacaoNova = await BasesSql.GarantirAcessoServicoAsync(p.Admin, p.BaseDados, servidor.LigacaoServico);

        if (info.Situacao == SituacaoBase.PrecisaActualizacao)
        {
            Passo($"A actualizar a base ({info.Pendentes.Count} actualização(ões))…");
            var r = await Processos.CorrerAsync(servidor.Executavel, "migrar", servidor.PastaInstalacao, new Dictionary<string, string>
            {
                ["ConnectionStrings__Default"] = p.Admin.Ligacao(p.BaseDados, 30),
                ["ASPNETCORE_ENVIRONMENT"] = "Production",
                ["LAMGEST_CONFIG"] = "",
            });
            if (r.Codigo != 0)
                throw new InvalidOperationException("A actualização da base falhou: " + r.Saida);
        }

        var xmlTarefa = await TarefaBackup.LerXmlAsync();
        string? xmlNovo = null;
        if (xmlTarefa is not null)
        {
            Passo("A preparar as cópias de segurança diárias para a base nova…");
            await BasesSql.GarantirPermissoesBackupAsync(p.Admin, p.BaseDados);
            xmlNovo = TarefaBackup.MudarBase(xmlTarefa, p.Admin.Instancia, p.BaseDados);
        }

        var configAnterior = File.Exists(servidor.FicheiroConfig) ? File.ReadAllText(servidor.FicheiroConfig, Encoding.UTF8) : null;
        Passo("A parar o serviço LAMGest…");
        PararServico();
        try
        {
            Passo("A gravar a configuração do servidor…");
            GravarLigacao(servidor.FicheiroConfig, ligacaoNova);
            if (xmlNovo is not null)
                await TarefaBackup.GravarXmlAsync(xmlNovo);
            Passo("A iniciar o serviço e a confirmar que responde…");
            await IniciarEVerificarAsync();
        }
        catch (Exception)
        {
            Passo("O servidor não arrancou com a base nova: a repor a configuração anterior…");
            if (configAnterior is not null)
                File.WriteAllText(servidor.FicheiroConfig, configAnterior, new UTF8Encoding(false));
            if (xmlNovo is not null && xmlTarefa is not null)
                await TarefaBackup.GravarXmlAsync(xmlTarefa);
            try
            {
                PararServico();
                await IniciarEVerificarAsync();
                Info("Configuração anterior reposta; o servidor está a funcionar com " + de + ".");
            }
            catch (Exception ex2)
            {
                Info("⚠ Também não arrancou com a configuração anterior: " + ex2.Message);
            }
            RegistoAlteracoes.Escrever(locais, $"FALHOU troca de base {de} -> {para}; configuração anterior reposta");
            throw;
        }

        var detalhes = Json.Escrever(new Dictionary<string, object?>
        {
            ["De"] = de,
            ["Para"] = para,
            ["Computador"] = Environment.MachineName,
            ["ContaWindows"] = $"{Environment.UserDomainName}\\{Environment.UserName}",
            ["Actualizada"] = info.Situacao == SituacaoBase.PrecisaActualizacao,
        });
        Passo("A registar a troca na auditoria…");
        foreach (var (instancia, nome) in new[] { (servidor.Instancia, servidor.BaseDados), (p.Admin.Instancia, p.BaseDados) }.Distinct())
        {
            if (!BasesSql.NomeValido(nome))
                continue;
            try
            {
                var admin = new LigacaoAdmin(instancia, p.Admin.AutenticacaoWindows, p.Admin.Utilizador, p.Admin.PalavraPasse);
                if (await BasesSql.AuditarAsync(admin, nome, "sistema.trocar-base-dados", detalhes))
                    Info("Auditoria de " + nome);
            }
            catch (SqlException ex)
            {
                Info($"⚠ Sem registo na auditoria de {nome}: {ex.Message}");
            }
        }
        RegistoAlteracoes.Escrever(locais, $"Base do servidor: {de} -> {para}");
        servidor.Instancia = p.Admin.Instancia;
        servidor.BaseDados = p.BaseDados;
        servidor.LigacaoServico = ligacaoNova;
        Passo("Concluído: o servidor está a funcionar com " + para + ".");
    }

    /// <summary>Migrations do programa instalado; numa instalação antiga sem a lista, as da base em uso (que o programa já usa).</summary>
    public async Task<IReadOnlyList<string>> MigracoesDeReferenciaAsync(LigacaoAdmin admin)
    {
        var lista = servidor.MigracoesDoPrograma();
        if (lista.Count > 0 || !BasesSql.NomeValido(servidor.BaseDados))
            return lista;
        try
        {
            var actual = new LigacaoAdmin(servidor.Instancia, admin.AutenticacaoWindows, admin.Utilizador, admin.PalavraPasse);
            return await BasesSql.MigracoesAplicadasAsync(actual, servidor.BaseDados);
        }
        catch (SqlException)
        {
            return Array.Empty<string>();
        }
    }

    /// <summary>Muda só ConnectionStrings:Default no servidor.json (o resto da configuração mantém-se).</summary>
    internal static void GravarLigacao(string ficheiro, string ligacao)
    {
        var config = File.Exists(ficheiro) ? Json.LerFicheiro(ficheiro) : new Dictionary<string, object?>();
        if (Json.Obter(config, "ConnectionStrings") is not IDictionary<string, object?> ligacoes)
        {
            ligacoes = new Dictionary<string, object?>();
            Json.Definir(config, "ConnectionStrings", ligacoes);
        }
        Json.Definir(ligacoes, "Default", ligacao);
        Json.GravarFicheiro(ficheiro, config);
    }

    private static void PararServico()
    {
        using var servico = new ServiceController(Locais.NomeServico);
        if (servico.Status == ServiceControllerStatus.Stopped)
            return;
        servico.Stop();
        servico.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(60));
    }

    private async Task IniciarEVerificarAsync()
    {
        using (var servico = new ServiceController(Locais.NomeServico))
        {
            if (servico.Status != ServiceControllerStatus.Running)
                servico.Start();
            servico.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(60));
        }
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        for (var i = 0; i < 45; i++)
        {
            try
            {
                using var r = await http.GetAsync($"http://localhost:{servidor.Porta}/api/saude");
                var corpo = Json.LerObjecto(await r.Content.ReadAsStringAsync());
                if (r.IsSuccessStatusCode && Json.Obter(corpo, "baseDados") is true)
                {
                    Info("O servidor responde e está ligado à base de dados.");
                    return;
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or FormatException or ArgumentException or InvalidOperationException)
            {
                // Ainda a arrancar.
            }
            await Task.Delay(2000);
        }
        throw new InvalidOperationException($"O serviço não respondeu em http://localhost:{servidor.Porta}/api/saude com a base ligada.");
    }

    private void Passo(string texto) => progresso.Report("• " + texto);
    private void Info(string texto) => progresso.Report("   " + texto);
}
