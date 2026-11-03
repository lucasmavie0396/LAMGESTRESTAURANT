using LAMGestRestaurant.Application.Comum;
using LAMGestRestaurant.Application.Impressao;
using LAMGestRestaurant.Infrastructure.Auditoria;
using LAMGestRestaurant.Infrastructure.Impressao;
using LAMGestRestaurant.Infrastructure.Dados;
using LAMGestRestaurant.Infrastructure.Identidade;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LAMGestRestaurant.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfraestrutura(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default");
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException(
                "Connection string 'Default' não configurada. Em desenvolvimento use 'dotnet user-secrets'; " +
                "em produção defina a variável de ambiente ConnectionStrings__Default. Ver README.md.");

        services.AddSingleton(TimeProvider.System);
        services.AddDbContext<AppDbContext>(o => o.UseSqlServer(connectionString,
            sql => sql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName)));

        services.AddIdentity<Utilizador, Perfil>(o =>
            {
                o.Password.RequiredLength = 8;
                o.Password.RequireDigit = true;
                o.Password.RequireLowercase = true;
                o.Password.RequireUppercase = false;
                o.Password.RequireNonAlphanumeric = false;
                o.Lockout.MaxFailedAccessAttempts = 5;
                o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                o.Lockout.AllowedForNewUsers = true;
                o.User.RequireUniqueEmail = false;
            })
            .AddEntityFrameworkStores<AppDbContext>()
            .AddDefaultTokenProviders()
            .AddClaimsPrincipalFactory<FabricaClaims>()
            .AddErrorDescriber<DescritorErrosIdentidade>();

        // Revalida o carimbo de segurança a cada minuto: alterações de perfis/permissões ou
        // desactivação de contas reflectem-se rapidamente nas sessões abertas.
        services.Configure<SecurityStampValidatorOptions>(o => o.ValidationInterval = TimeSpan.FromMinutes(1));

        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());
        services.AddScoped<IAuditoria, ServicoAuditoria>();
        services.AddScoped<SemeadorDados>();
        services.AddScoped<ServicoUtilizadores>();
        services.AddScoped<IConsultaUtilizadores, ConsultaUtilizadores>();

        if (OperatingSystem.IsWindows())
            services.AddSingleton<IDispositivoImpressao, ImpressoraWindows>();
        else
            services.AddSingleton<IDispositivoImpressao, SemImpressoras>();
        services.AddScoped<Imagens.ServicoImagens>();
        services.AddSingleton<FilaImpressao>();
        services.AddSingleton<IFilaImpressao>(sp => sp.GetRequiredService<FilaImpressao>());
        services.AddHostedService(sp => sp.GetRequiredService<FilaImpressao>());
        return services;
    }
}
