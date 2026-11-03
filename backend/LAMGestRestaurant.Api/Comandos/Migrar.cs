using LAMGestRestaurant.Infrastructure.Dados;
using Microsoft.EntityFrameworkCore;

namespace LAMGestRestaurant.Api.Comandos;

/// <summary>
/// Comando de consola usado pelo instalador do servidor:
/// <c>migrar --listar</c> escreve cada migration pendente numa linha «PENDENTE: nome» (nada é alterado);
/// <c>migrar</c> aplica as pendentes e semeia os dados de referência.
/// A confirmação de migrations numa base com dados é feita pelo instalador, depois de uma cópia de segurança.
/// </summary>
public static class Migrar
{
    public const string Comando = "migrar";
    /// <summary>As linhas com este prefixo são as únicas que o instalador interpreta (os logs podem ir para a mesma saída).</summary>
    public const string PrefixoPendente = "PENDENTE: ";

    public static async Task<int> ExecutarAsync(IServiceProvider servicos, bool apenasListar)
    {
        using var scope = servicos.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        try
        {
            var pendentes = (await db.Database.GetPendingMigrationsAsync()).ToList();
            if (apenasListar)
            {
                foreach (var m in pendentes)
                    Console.WriteLine(PrefixoPendente + m);
                return 0;
            }

            if (pendentes.Count > 0)
            {
                Console.WriteLine($"A aplicar {pendentes.Count} migration(s): {string.Join(", ", pendentes)}");
                await db.Database.MigrateAsync();
            }
            await scope.ServiceProvider.GetRequiredService<SemeadorDados>().ExecutarAsync();
            Console.WriteLine("Base de dados actualizada.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Não foi possível actualizar a base de dados: " + ex.Message);
            return 1;
        }
    }
}
