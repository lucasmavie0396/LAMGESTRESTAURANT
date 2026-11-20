using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace LAMGestRestaurant.Configurar.Motor;

/// <summary>
/// Tarefa agendada das cópias de segurança diárias (criada pelo LAMGest-Setup.exe). O comando tem a instância e a base
/// escritas (-Servidor "…" -BaseDados "…"): ao mudar de base, o configurador muda-as, mantendo a hora e o resto.
/// </summary>
public static class TarefaBackup
{
    /// <summary>Troca a instância e a base nos argumentos da tarefa (XML do schtasks). Devolve null se não tiver esses argumentos.</summary>
    public static string? MudarBase(string xml, string instancia, string baseDados)
    {
        var alterado = false;
        string Trocar(string texto, string parametro, string valor)
        {
            var re = new Regex($"(-{parametro}\\s+)(&quot;|\")[^\"&]*(&quot;|\")", RegexOptions.CultureInvariant);
            if (!re.IsMatch(texto))
                return texto;
            alterado = true;
            var escapado = System.Security.SecurityElement.Escape(valor)!;
            return re.Replace(texto, m => m.Groups[1].Value + m.Groups[2].Value + escapado + m.Groups[3].Value, 1);
        }

        var novo = Trocar(Trocar(xml, "Servidor", instancia), "BaseDados", baseDados);
        return alterado ? novo : null;
    }

    /// <summary>Instância e base que a tarefa copia hoje (null se não existir).</summary>
    public static async Task<string?> LerXmlAsync()
    {
        var r = await Processos.CorrerAsync("schtasks.exe", $"/Query /TN {Processos.Aspas(Locais.NomeTarefaBackup)} /XML", consola: true);
        return r.Codigo == 0 && r.Saida.Contains("<Task") ? r.Saida : null;
    }

    public static async Task GravarXmlAsync(string xml)
    {
        var ficheiro = Path.Combine(Path.GetTempPath(), $"lamgest-tarefa-{Guid.NewGuid():N}.xml");
        try
        {
            // O schtasks lê o XML em UTF-16 (como o exporta).
            File.WriteAllText(ficheiro, Regex.Replace(xml, "encoding=\"[^\"]*\"", "encoding=\"UTF-16\""), Encoding.Unicode);
            var r = await Processos.CorrerAsync("schtasks.exe", $"/Create /F /TN {Processos.Aspas(Locais.NomeTarefaBackup)} /XML {Processos.Aspas(ficheiro)}", consola: true);
            if (r.Codigo != 0)
                throw new InvalidOperationException("Não foi possível alterar a tarefa das cópias de segurança: " + r.Saida);
        }
        finally
        {
            File.Delete(ficheiro);
        }
    }
}
