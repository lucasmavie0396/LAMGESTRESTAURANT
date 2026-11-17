using System.Text;

namespace LAMGestRestaurant.Agente;

/// <summary>
/// Bytes a enviar ao visor de poste (2 linhas) pela porta COM. As linhas já vêm do servidor com a largura do visor e sem acentos.
/// EscPos: Epson e compatíveis (limpar, cursor na linha 1 e na linha 2). Cd5220: «ESC Q A» e «ESC Q B». Simples: limpar e escrever seguido.
/// </summary>
public static class ComandosVisor
{
    private const byte Esc = 0x1B, Us = 0x1F, Limpar = 0x0C, Cr = 0x0D;

    public static byte[] Bytes(string protocolo, string linha1, string linha2, int colunas)
    {
        var a = Ascii(linha1, colunas);
        var b = Ascii(linha2, colunas);
        var s = new List<byte>(colunas * 2 + 16);
        switch (protocolo)
        {
            case "Cd5220":
                s.AddRange([Esc, (byte)'@', Limpar, Esc, (byte)'Q', (byte)'A']);
                s.AddRange(a);
                s.AddRange([Cr, Esc, (byte)'Q', (byte)'B']);
                s.AddRange(b);
                s.Add(Cr);
                break;
            case "Simples":
                s.Add(Limpar);
                s.AddRange(a);
                s.AddRange(b);
                break;
            default: // EscPos
                s.AddRange([Esc, (byte)'@', Limpar, Us, (byte)'$', 1, 1]);
                s.AddRange(a);
                s.AddRange([Us, (byte)'$', 1, 2]);
                s.AddRange(b);
                break;
        }
        return [.. s];
    }

    /// <summary>Exactamente <paramref name="colunas"/> caracteres ASCII visíveis.</summary>
    private static byte[] Ascii(string texto, int colunas)
    {
        var t = texto.Length > colunas ? texto[..colunas] : texto.PadRight(colunas);
        return Encoding.ASCII.GetBytes(t.Select(c => c is >= ' ' and <= '~' ? c : '?').ToArray());
    }
}
