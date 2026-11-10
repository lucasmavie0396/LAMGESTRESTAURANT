using QRCoder;

namespace LAMGestRestaurant.Infrastructure.Impressao;

/// <summary>QR code (correcção de erros M), com a margem branca de 4 módulos à volta, como pedem os leitores.</summary>
public static class Qr
{
    public static IReadOnlyList<IReadOnlyList<bool>> Matriz(string texto)
    {
        using var gerador = new QRCodeGenerator();
        using var dados = gerador.CreateQrCode(texto, QRCodeGenerator.ECCLevel.M);
        return [.. dados.ModuleMatrix.Select(linha => (IReadOnlyList<bool>)[.. linha.Cast<bool>()])];
    }
}
