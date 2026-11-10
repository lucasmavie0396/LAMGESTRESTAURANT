using System.Security.Cryptography;
using System.Text;

namespace LAMGestRestaurant.Instalador.Motor;

/// <summary>
/// Código que liberta o instalador. Só a derivação PBKDF2 (SHA-256, 200 000 iterações, com sal) fica no programa:
/// o código em si não aparece no ficheiro nem no repositório. É um travão contra uso não autorizado, não uma protecção
/// contra quem altere o próprio instalador.
/// </summary>
public static class CodigoInstalacao
{
    private static readonly byte[] Sal = Convert.FromBase64String("gdGMZRGW6Sq3qCsQk0keIw==");
    private static readonly byte[] Derivado = Convert.FromBase64String("aZl34N6/GEWRCc58+gnzuxeFY+fQFFwp+L85VlM4GQE=");
    private const int Iteracoes = 200_000;

    public static bool Valido(string? codigo)
    {
        if (string.IsNullOrEmpty(codigo))
            return false;
        var tentativa = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(codigo), Sal, Iteracoes, HashAlgorithmName.SHA256, Derivado.Length);
        return CryptographicOperations.FixedTimeEquals(tentativa, Derivado);
    }
}
