using LAMGestRestaurant.Application.Caixa;
using LAMGestRestaurant.Application.Catalogo;
using LAMGestRestaurant.Application.Sala;
using Microsoft.Extensions.DependencyInjection;

namespace LAMGestRestaurant.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddAplicacao(this IServiceCollection services)
    {
        services.AddScoped<ServicoCatalogo>();
        services.AddScoped<ServicoSala>();
        services.AddScoped<ServicoCaixa>();
        return services;
    }
}
