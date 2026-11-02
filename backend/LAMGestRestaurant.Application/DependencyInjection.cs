using LAMGestRestaurant.Application.Caixa;
using LAMGestRestaurant.Application.Catalogo;
using LAMGestRestaurant.Application.Comum;
using LAMGestRestaurant.Application.Cozinha;
using LAMGestRestaurant.Application.Sala;
using LAMGestRestaurant.Application.Stock;
using LAMGestRestaurant.Application.Vendas;
using Microsoft.Extensions.DependencyInjection;

namespace LAMGestRestaurant.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddAplicacao(this IServiceCollection services)
    {
        services.AddScoped<ConfiguracoesSistema>();
        services.AddScoped<ServicoNumeracao>();
        services.AddScoped<ServicoCatalogo>();
        services.AddScoped<ServicoSala>();
        services.AddScoped<ServicoCaixa>();
        services.AddScoped<ServicoPedidos>();
        services.AddScoped<ServicoPagamentos>();
        services.AddScoped<ServicoBaixaStock>();
        services.AddScoped<ServicoCozinha>();
        services.AddScoped<Relatorios.ServicoRelatorios>();
        return services;
    }
}
