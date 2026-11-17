using System.IO.Ports;
using Microsoft.Win32;

namespace LAMGestRestaurant.Agente;

public sealed record PortaAgente(string Nome, string? Descricao);

public sealed record EcraAgente(string Nome, int Largura, int Altura, bool Principal);

/// <summary>Portas COM (com o nome do aparelho dado pelo Windows) e ecrãs deste computador, enviados ao servidor com as impressoras.</summary>
public static class DispositivosPosto
{
    private static string[] portasLidas = [];
    private static IReadOnlyList<PortaAgente> descritas = [];

    public static IReadOnlyList<PortaAgente> Portas()
    {
        var nomes = SerialPort.GetPortNames().Select(n => n.Trim().ToUpperInvariant()).Distinct().Order(StringComparer.Ordinal).ToArray();
        // Ler os nomes no registo só quando as portas mudam (percorre todos os aparelhos do Windows).
        if (!nomes.SequenceEqual(portasLidas))
        {
            var nomesAparelhos = NomesAparelhos();
            descritas = [.. nomes.Select(n => new PortaAgente(n, nomesAparelhos.GetValueOrDefault(n)))];
            portasLidas = nomes;
        }
        return descritas;
    }

    public static IReadOnlyList<EcraAgente> Ecras() =>
        [.. Screen.AllScreens.Select(s => new EcraAgente(s.DeviceName, s.Bounds.Width, s.Bounds.Height, s.Primary))];

    /// <summary>COMn → nome do aparelho (ex.: «USB-SERIAL CH340 (COM3)»), de HKLM\SYSTEM\CurrentControlSet\Enum.</summary>
    private static Dictionary<string, string> NomesAparelhos()
    {
        var nomes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var raiz = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum");
            if (raiz is null)
                return nomes;
            foreach (var enumerador in raiz.GetSubKeyNames())
            {
                using var e = Abrir(raiz, enumerador);
                if (e is null)
                    continue;
                foreach (var aparelho in e.GetSubKeyNames())
                {
                    using var a = Abrir(e, aparelho);
                    if (a is null)
                        continue;
                    foreach (var instancia in a.GetSubKeyNames())
                    {
                        using var i = Abrir(a, instancia);
                        using var parametros = i is null ? null : Abrir(i, "Device Parameters");
                        if (parametros?.GetValue("PortName") is string porta && porta.StartsWith("COM", StringComparison.OrdinalIgnoreCase)
                            && (i!.GetValue("FriendlyName") ?? i.GetValue("DeviceDesc")) is string nome)
                            nomes[porta] = nome.Contains(';') ? nome[(nome.LastIndexOf(';') + 1)..] : nome; // DeviceDesc: «@ficheiro.inf,...;Nome»
                    }
                }
            }
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
        }
        return nomes;
    }

    private static RegistryKey? Abrir(RegistryKey pai, string nome)
    {
        try
        {
            return pai.OpenSubKey(nome);
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }
}
