using System.IO.Ports;
using System.Text;

namespace LAMGestRestaurant.Agente;

/// <summary>Porta COM da balança neste Windows (8N1 ou 7E1), aberta só enquanto o POS pede o peso.</summary>
public sealed class PortaBalancaWindows : IPortaBalanca
{
    private readonly SerialPort porta;

    public PortaBalancaWindows(BalancaRecebida b)
    {
        var (paridade, bits) = b.Formato == "Sete_E_1" ? (Parity.Even, 7) : (Parity.None, 8);
        porta = new SerialPort(b.Porta!, b.Velocidade, paridade, bits, StopBits.One)
        {
            Handshake = Handshake.None,
            Encoding = Encoding.Latin1,
            ReadTimeout = 500,
            WriteTimeout = 1000,
            DtrEnable = true, // algumas balanças só respondem com DTR/RTS activos
            RtsEnable = true,
        };
        try
        {
            porta.Open();
            porta.DiscardInBuffer();
        }
        catch
        {
            porta.Dispose();
            throw;
        }
    }

    public void Escrever(byte[] dados)
    {
        porta.DiscardInBuffer();
        porta.Write(dados, 0, dados.Length);
    }

    public string Ler(TimeSpan espera)
    {
        var limite = DateTime.UtcNow + espera;
        while (porta.BytesToRead == 0 && DateTime.UtcNow < limite)
            Thread.Sleep(20);
        if (porta.BytesToRead == 0)
            return "";
        Thread.Sleep(60); // o resto da resposta (a 9600 bps, uma trama de 20 caracteres demora ~20 ms)
        return porta.ReadExisting();
    }

    public void Dispose() => porta.Dispose();
}
