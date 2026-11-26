using System.Globalization;
using LAMGestRestaurant.Application.Caixa;
using LAMGestRestaurant.Application.Comum;
using LAMGestRestaurant.Application.Impressao;
using LAMGestRestaurant.Infrastructure.Dados;
using Microsoft.EntityFrameworkCore;

namespace LAMGestRestaurant.Api.Comandos;

/// <summary>
/// Posto do próprio servidor, preparado pelo instalador só com a ligação à base de dados (sem rede nem palavra-passe):
///   LAMGestRestaurant.Api.exe posto-servidor --numero 1 [--agente]
/// Cria (ou reutiliza) o posto com esse número e marca-o como o do servidor. Com --agente, gera a chave do agente de
/// impressão (a anterior deixa de valer). Saída, uma linha cada: «POSTO numero id criado|existente nome» e «CHAVE chave».
/// </summary>
public static class PostoServidor
{
    public const string Comando = "posto-servidor";

    public static async Task<int> ExecutarAsync(IServiceProvider servicos, string[] args)
    {
        // O instalador lê a saída em UTF-8 (nomes com acentos e «—»); a consola redireccionada usaria a página de códigos do Windows.
        Console.OutputEncoding = new System.Text.UTF8Encoding(false);
        var i = Array.IndexOf(args, "--numero");
        var numero = 1;
        if (i >= 0 && (i + 1 >= args.Length || !int.TryParse(args[i + 1], NumberStyles.None, CultureInfo.InvariantCulture, out numero)))
        {
            Console.Error.WriteLine("Indique o número do posto: --numero 1");
            return 1;
        }

        using var scope = servicos.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if ((await db.Database.GetPendingMigrationsAsync()).Any())
        {
            Console.Error.WriteLine("Existem migrations por aplicar. Aplique-as primeiro (comando migrar).");
            return 1;
        }

        try
        {
            var posto = await scope.ServiceProvider.GetRequiredService<ServicoPostoServidor>().GarantirAsync(numero);
            Console.WriteLine($"POSTO {posto.Numero} {posto.TerminalId} {(posto.Criado ? "criado" : "existente")} {posto.Nome}");
            if (args.Contains("--agente"))
            {
                var agente = await scope.ServiceProvider.GetRequiredService<ServicoAgentes>().RegistarAsync(numero);
                Console.WriteLine("CHAVE " + agente.Chave);
            }
            return 0;
        }
        catch (Exception ex) when (ex is RegraNegocioException or NaoEncontradoException or ConflitoException)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }
}
