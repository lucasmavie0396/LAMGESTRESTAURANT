using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace LAMGestRestaurant.Api.Seguranca;

/// <summary>
/// Limite de pedidos por computador (endereço IP). O bloqueio de conta (5 tentativas) protege cada utilizador;
/// este limite impede que um computador experimente muitas contas ou PIN seguidos.
/// </summary>
public static class LimitesPedidos
{
    /// <summary>Entrada com palavra-passe ou PIN.</summary>
    public const string Entrada = "entrada";

    /// <summary>Erros do ecrã enviados pelos navegadores para o log do servidor.</summary>
    public const string ErrosCliente = "erros-cliente";

    public static IServiceCollection AddLimitesPedidos(this IServiceCollection services, IConfiguration config)
    {
        var entradas = Math.Max(1, config.GetValue("Seguranca:EntradasPorMinuto", 20));
        var erros = Math.Max(1, config.GetValue("Seguranca:ErrosClientePorMinuto", 30));
        services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            o.AddPolicy(Entrada, ctx => PorComputador(ctx, entradas));
            o.AddPolicy(ErrosCliente, ctx => PorComputador(ctx, erros));
            o.OnRejected = async (contexto, ct) =>
            {
                var http = contexto.HttpContext;
                if (contexto.Lease.TryGetMetadata(MetadataName.RetryAfter, out var espera))
                    http.Response.Headers.RetryAfter = ((int)Math.Ceiling(espera.TotalSeconds)).ToString(System.Globalization.CultureInfo.InvariantCulture);
                http.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("LAMGestRestaurant.Api.Seguranca.LimitesPedidos")
                    .LogWarning("Limite de pedidos atingido em {Caminho} pelo computador {Ip}", http.Request.Path.Value, http.Connection.RemoteIpAddress?.ToString());
                await http.Response.WriteAsJsonAsync(new ProblemDetails
                {
                    Status = StatusCodes.Status429TooManyRequests,
                    Title = "Demasiadas tentativas",
                    Detail = "Demasiadas tentativas a partir deste computador. Aguarde um minuto e tente de novo.",
                }, cancellationToken: ct);
            };
        });
        return services;
    }

    private static RateLimitPartition<string> PorComputador(HttpContext ctx, int porMinuto) =>
        RateLimitPartition.GetFixedWindowLimiter(ctx.Connection.RemoteIpAddress?.ToString() ?? "desconhecido", _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = porMinuto,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
        });
}
