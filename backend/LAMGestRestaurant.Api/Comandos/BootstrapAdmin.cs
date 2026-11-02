using System.Text;
using LAMGestRestaurant.Application.Comum;
using LAMGestRestaurant.Application.Seguranca;
using LAMGestRestaurant.Infrastructure.Dados;
using LAMGestRestaurant.Infrastructure.Identidade;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace LAMGestRestaurant.Api.Comandos;

/// <summary>
/// Cria o primeiro administrador de forma interactiva na consola do servidor:
///   dotnet run --project LAMGestRestaurant.Api -- bootstrap-admin
/// Recusa-se a correr se já existir algum utilizador com o perfil Administrador.
/// A palavra-passe é lida sem eco e nunca é escrita em ficheiros ou logs.
/// </summary>
public static class BootstrapAdmin
{
    public const string Comando = "bootstrap-admin";

    public static async Task<int> ExecutarAsync(IServiceProvider servicos)
    {
        using var scope = servicos.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var utilizadores = scope.ServiceProvider.GetRequiredService<UserManager<Utilizador>>();
        var semeador = scope.ServiceProvider.GetRequiredService<SemeadorDados>();
        var auditoria = scope.ServiceProvider.GetRequiredService<IAuditoria>();

        if ((await db.Database.GetPendingMigrationsAsync()).Any())
        {
            Console.Error.WriteLine("Existem migrations por aplicar. Aplique-as primeiro (ver README.md).");
            return 1;
        }

        await semeador.ExecutarAsync();

        if ((await utilizadores.GetUsersInRoleAsync(PerfisPadrao.Administrador)).Count > 0)
        {
            Console.Error.WriteLine("Já existe um administrador. Use a gestão de utilizadores na aplicação.");
            return 1;
        }

        Console.WriteLine("=== LAMGest Restaurant ERP — criação do primeiro administrador ===");
        var nomeUtilizador = Ler("Nome de utilizador: ");
        var nomeCompleto = Ler("Nome completo: ");

        string palavraPasse;
        while (true)
        {
            palavraPasse = LerOculto("Palavra-passe (mín. 8, com letra minúscula e algarismo): ");
            if (palavraPasse == LerOculto("Confirme a palavra-passe: "))
                break;
            Console.Error.WriteLine("As palavras-passe não coincidem. Tente novamente.");
        }

        var admin = new Utilizador
        {
            UserName = nomeUtilizador,
            NomeCompleto = nomeCompleto,
            Activo = true,
            CriadoEmUtc = DateTime.UtcNow,
        };

        await using var transacao = await db.Database.BeginTransactionAsync();
        var criado = await utilizadores.CreateAsync(admin, palavraPasse);
        if (!criado.Succeeded)
        {
            Console.Error.WriteLine("Não foi possível criar o utilizador: " + string.Join(" ", criado.Errors.Select(e => e.Description)));
            return 1;
        }
        var perfil = await utilizadores.AddToRoleAsync(admin, PerfisPadrao.Administrador);
        if (!perfil.Succeeded)
        {
            Console.Error.WriteLine("Não foi possível atribuir o perfil: " + string.Join(" ", perfil.Errors.Select(e => e.Description)));
            return 1;
        }
        auditoria.Registar("utilizador.bootstrap-admin", "Utilizador", admin.Id, new { admin.UserName });
        await db.SaveChangesAsync();
        await transacao.CommitAsync();

        Console.WriteLine($"Administrador '{nomeUtilizador}' criado com sucesso.");
        return 0;
    }

    private static string Ler(string pergunta)
    {
        while (true)
        {
            Console.Write(pergunta);
            var valor = Limpar(Console.ReadLine());
            if (!string.IsNullOrEmpty(valor))
                return valor;
        }
    }

    /// <summary>Remove espaços e o BOM (U+FEFF) que algumas consolas acrescentam à entrada redireccionada.</summary>
    private static string Limpar(string? linha) => (linha ?? string.Empty).Replace("﻿", string.Empty).Trim();

    private static string LerOculto(string pergunta)
    {
        Console.Write(pergunta);
        if (Console.IsInputRedirected)
            return Limpar(Console.ReadLine());

        var texto = new StringBuilder();
        while (true)
        {
            var tecla = Console.ReadKey(intercept: true);
            if (tecla.Key == ConsoleKey.Enter)
                break;
            if (tecla.Key == ConsoleKey.Backspace)
            {
                if (texto.Length > 0) texto.Length--;
            }
            else if (!char.IsControl(tecla.KeyChar))
            {
                texto.Append(tecla.KeyChar);
            }
        }
        Console.WriteLine();
        return texto.ToString();
    }
}
