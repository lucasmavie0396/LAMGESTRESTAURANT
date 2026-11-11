using System.Net;
using System.Net.Sockets;

namespace LAMGestRestaurant.Application.Caixa;

/// <summary>Rede IPv4 de uma placa do servidor.</summary>
public sealed record Subrede(IPAddress Endereco, IPAddress Mascara)
{
    public bool Contem(IPAddress ip) => RedeLocal.MesmaRede(Endereco, ip, Mascara);
}

/// <summary>
/// Regras da rede local: o sistema e os tablets funcionam sem internet, só com a rede do estabelecimento.
/// Nunca se oferece um endereço público aos tablets e o registo só é aceite a partir da rede do posto.
/// </summary>
public static class RedeLocal
{
    private static readonly IPAddress Mascara24 = IPAddress.Parse("255.255.255.0");

    private static IPAddress Normalizar(IPAddress ip) => ip.IsIPv4MappedToIPv6 ? ip.MapToIPv4() : ip;

    /// <summary>10.x, 172.16-31.x ou 192.168.x: endereços que só existem na rede local.</summary>
    public static bool Privado(IPAddress ip)
    {
        if (ip.AddressFamily != AddressFamily.InterNetwork) return false;
        var b = ip.GetAddressBytes();
        return b[0] == 10 || (b[0] == 172 && b[1] is >= 16 and <= 31) || (b[0] == 192 && b[1] == 168);
    }

    /// <summary>169.254.x: a rede sem router (sem DHCP), em que os aparelhos se atribuem um endereço.</summary>
    public static bool Automatico(IPAddress ip) =>
        ip.AddressFamily == AddressFamily.InterNetwork && ip.GetAddressBytes() is [169, 254, ..];

    public static bool MesmaRede(IPAddress a, IPAddress b, IPAddress mascara)
    {
        var (x, y, m) = (Normalizar(a).GetAddressBytes(), Normalizar(b).GetAddressBytes(), mascara.GetAddressBytes());
        if (x.Length != 4 || y.Length != 4 || m.Length != 4) return false;
        for (var i = 0; i < 4; i++)
            if ((x[i] & m[i]) != (y[i] & m[i])) return false;
        return true;
    }

    /// <summary>
    /// Verifica de onde vem o registo do tablet: tem de estar numa rede do servidor e, se o endereço do posto for conhecido,
    /// na mesma rede desse posto. Devolve o motivo da recusa, ou null. Sem endereço do tablet (ou na própria máquina) não há o que verificar.
    /// </summary>
    public static string? ErroEmparelhamento(IPAddress? tablet, string? enderecoPosto, string nomePosto, IReadOnlyList<Subrede> subredes)
    {
        if (tablet is null || IPAddress.IsLoopback(tablet)) return null;
        var ip = Normalizar(tablet);
        if (ip.AddressFamily != AddressFamily.InterNetwork) return null;
        if (!subredes.Any(s => s.Contem(ip)))
            return "O tablet tem de estar ligado à mesma rede local do servidor.";
        if (!IPAddress.TryParse(enderecoPosto, out var posto) || IPAddress.IsLoopback(posto)) return null;
        posto = Normalizar(posto);
        if (posto.AddressFamily != AddressFamily.InterNetwork) return null;
        var mascara = subredes.FirstOrDefault(s => s.Contem(posto))?.Mascara ?? Mascara24;
        return MesmaRede(posto, ip, mascara) ? null : $"O tablet tem de estar na mesma rede do posto {nomePosto} ({posto}).";
    }
}
