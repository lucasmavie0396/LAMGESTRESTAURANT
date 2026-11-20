using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace LAMGestRestaurant.Configurar.Motor;

/// <summary>Pastas e ficheiros do LAMGest neste computador (os mesmos que o LAMGest-Setup.exe usa).</summary>
public sealed class Locais
{
    public const string NomeServico = "LAMGestRestaurant";
    public const string NomeTarefaBackup = "LAMGest Restaurant - Backup SQL";
    public const string NomeAtalhoPosto = "LAMGest Restaurant.lnk";
    public const string NomeAgente = "LAMGest-Agente.exe";

    public Locais(string pastaDados) => PastaDados = pastaDados;

    public static Locais Padrao() =>
        new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "LAMGestRestaurant"));

    public string PastaDados { get; }
    public string PastaConfig => Path.Combine(PastaDados, "config");
    public string ConfigServidor => Path.Combine(PastaConfig, "servidor.json");
    public string RegistoAlteracoes => Path.Combine(PastaConfig, "alteracoes-base.log");
    public string FicheiroBloqueio => Path.Combine(PastaConfig, "configurar.bloqueio");
    public string ConfigPosto => Path.Combine(PastaDados, "posto.json");
    public string ConfigAgente => Path.Combine(PastaDados, "agente.json");
}

/// <summary>Registo das alterações (quem, quando, de quê para quê) em config\alteracoes-base.log.</summary>
public static class RegistoAlteracoes
{
    public static void Escrever(Locais locais, string texto)
    {
        try
        {
            Directory.CreateDirectory(locais.PastaConfig);
            var linha = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {Environment.MachineName}  {Environment.UserDomainName}\\{Environment.UserName}  {texto}{Environment.NewLine}";
            File.AppendAllText(locais.RegistoAlteracoes, linha, new UTF8Encoding(false));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // O registo nunca impede a operação.
        }
    }
}

/// <summary>JSON dos ficheiros de configuração: lê para dicionários (mantém as chaves que o configurador não conhece) e grava indentado.</summary>
public static class Json
{
    private static JavaScriptSerializer Serializador() => new() { MaxJsonLength = int.MaxValue };

    public static Dictionary<string, object?> LerObjecto(string texto)
    {
        var lido = Serializador().DeserializeObject(texto) as Dictionary<string, object?>;
        return lido ?? throw new FormatException("O ficheiro não tem um objecto JSON.");
    }

    public static Dictionary<string, object?> LerFicheiro(string caminho) =>
        LerObjecto(File.ReadAllText(caminho, Encoding.UTF8).TrimStart('﻿'));

    /// <summary>Valor de uma chave sem distinguir maiúsculas (os ficheiros do instalador usam PascalCase, a API camelCase).</summary>
    public static object? Obter(IDictionary<string, object?> d, string chave) =>
        d.FirstOrDefault(p => string.Equals(p.Key, chave, StringComparison.OrdinalIgnoreCase)).Value;

    public static string? Texto(IDictionary<string, object?> d, string chave) => Obter(d, chave) is { } v ? Convert.ToString(v, CultureInfo.InvariantCulture) : null;

    public static int? Inteiro(IDictionary<string, object?> d, string chave) =>
        Obter(d, chave) is { } v && int.TryParse(Convert.ToString(v, CultureInfo.InvariantCulture), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : null;

    /// <summary>Altera o valor da chave existente (mesma grafia) ou acrescenta-a.</summary>
    public static void Definir(IDictionary<string, object?> d, string chave, object? valor)
    {
        var existente = d.Keys.FirstOrDefault(k => string.Equals(k, chave, StringComparison.OrdinalIgnoreCase));
        d[existente ?? chave] = valor;
    }

    public static string Escrever(object? valor)
    {
        var s = new StringBuilder();
        EscreverValor(s, valor, 0);
        return s.ToString();
    }

    public static void GravarFicheiro(string caminho, object valor) => File.WriteAllText(caminho, Escrever(valor), new UTF8Encoding(false));

    private static void EscreverValor(StringBuilder s, object? v, int nivel)
    {
        switch (v)
        {
            case null:
                s.Append("null");
                break;
            case string t:
                s.Append(Serializador().Serialize(t));
                break;
            case bool b:
                s.Append(b ? "true" : "false");
                break;
            case DateTime dt:
                s.Append(Serializador().Serialize(dt.ToString("o", CultureInfo.InvariantCulture)));
                break;
            case IDictionary<string, object?> d:
                EscreverObjecto(s, d.Select(p => new KeyValuePair<string, object?>(p.Key, p.Value)).ToList(), nivel);
                break;
            case IDictionary d:
                EscreverObjecto(s, d.Keys.Cast<object>().Select(k => new KeyValuePair<string, object?>(Convert.ToString(k, CultureInfo.InvariantCulture)!, d[k])).ToList(), nivel);
                break;
            case IEnumerable lista:
                var itens = lista.Cast<object?>().ToList();
                if (itens.Count == 0)
                {
                    s.Append("[]");
                    break;
                }
                s.Append('[').Append('\n');
                for (var i = 0; i < itens.Count; i++)
                {
                    s.Append(' ', (nivel + 1) * 2);
                    EscreverValor(s, itens[i], nivel + 1);
                    s.Append(i < itens.Count - 1 ? ",\n" : "\n");
                }
                s.Append(' ', nivel * 2).Append(']');
                break;
            case IFormattable f:
                s.Append(f.ToString(null, CultureInfo.InvariantCulture));
                break;
            default:
                s.Append(Serializador().Serialize(v));
                break;
        }
    }

    private static void EscreverObjecto(StringBuilder s, List<KeyValuePair<string, object?>> pares, int nivel)
    {
        if (pares.Count == 0)
        {
            s.Append("{}");
            return;
        }
        s.Append('{').Append('\n');
        for (var i = 0; i < pares.Count; i++)
        {
            s.Append(' ', (nivel + 1) * 2).Append(Serializador().Serialize(pares[i].Key)).Append(": ");
            EscreverValor(s, pares[i].Value, nivel + 1);
            s.Append(i < pares.Count - 1 ? ",\n" : "\n");
        }
        s.Append(' ', nivel * 2).Append('}');
    }
}

public sealed class ResultadoProcesso
{
    public ResultadoProcesso(int codigo, string saida)
    {
        Codigo = codigo;
        Saida = saida;
    }

    public int Codigo { get; }
    public string Saida { get; }
}

public static class Processos
{
    /// <summary>Corre um programa sem janela e devolve o código e a saída (stdout + stderr).</summary>
    /// <param name="consola">Programas do Windows (schtasks…) escrevem na página de códigos da consola; a API escreve em UTF-8.</param>
    public static Task<ResultadoProcesso> CorrerAsync(string ficheiro, string argumentos, string? pasta = null, IDictionary<string, string>? ambiente = null, bool consola = false) =>
        Task.Run(() =>
        {
            var codificacao = consola ? Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage) : Encoding.UTF8;
            var inicio = new ProcessStartInfo(ficheiro, argumentos)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = codificacao,
                StandardErrorEncoding = codificacao,
                WorkingDirectory = pasta ?? Environment.CurrentDirectory,
            };
            if (ambiente is not null)
                foreach (var p in ambiente)
                    inicio.EnvironmentVariables[p.Key] = p.Value;
            using var processo = Process.Start(inicio) ?? throw new InvalidOperationException($"Não foi possível executar {ficheiro}.");
            var erros = processo.StandardError.ReadToEndAsync();
            var saida = processo.StandardOutput.ReadToEnd();
            processo.WaitForExit();
            return new ResultadoProcesso(processo.ExitCode, (saida + erros.Result).Trim());
        });

    /// <summary>Argumento entre aspas para a linha de comandos do Windows.</summary>
    public static string Aspas(string valor) => "\"" + valor.Replace("\"", "\\\"") + "\"";
}

/// <summary>Atalhos do Windows (.lnk) através do WScript.Shell.</summary>
public static class Atalhos
{
    public sealed class Dados
    {
        public string Alvo { get; set; } = "";
        public string Argumentos { get; set; } = "";
        public string Descricao { get; set; } = "";
    }

    public static Dados Ler(string caminho) => Usar(caminho, atalho => new Dados
    {
        Alvo = (string)Obter(atalho, "TargetPath"),
        Argumentos = (string)Obter(atalho, "Arguments"),
        Descricao = (string)Obter(atalho, "Description"),
    });

    /// <summary>Muda só os argumentos e a descrição (o resto do atalho — alvo, ícone, pasta — mantém-se).</summary>
    public static void Alterar(string caminho, string argumentos, string descricao) => Usar<object?>(caminho, atalho =>
    {
        Definir(atalho, "Arguments", argumentos);
        Definir(atalho, "Description", descricao);
        atalho.GetType().InvokeMember("Save", System.Reflection.BindingFlags.InvokeMethod, null, atalho, null);
        return null;
    });

    private static T Usar<T>(string caminho, Func<object, T> accao)
    {
        var tipo = Type.GetTypeFromProgID("WScript.Shell") ?? throw new InvalidOperationException("WScript.Shell indisponível.");
        var shell = Activator.CreateInstance(tipo)!;
        try
        {
            var atalho = tipo.InvokeMember("CreateShortcut", System.Reflection.BindingFlags.InvokeMethod, null, shell, new object[] { caminho })!;
            try
            {
                return accao(atalho);
            }
            finally
            {
                System.Runtime.InteropServices.Marshal.FinalReleaseComObject(atalho);
            }
        }
        finally
        {
            System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);
        }
    }

    private static object Obter(object o, string p) => o.GetType().InvokeMember(p, System.Reflection.BindingFlags.GetProperty, null, o, null) ?? "";

    private static void Definir(object o, string p, object v) => o.GetType().InvokeMember(p, System.Reflection.BindingFlags.SetProperty, null, o, new[] { v });
}
