using LAMGestRestaurant.Api.Comandos;
using LAMGestRestaurant.Api.Erros;
using LAMGestRestaurant.Api.Seguranca;
using LAMGestRestaurant.Application.Comum;
using LAMGestRestaurant.Infrastructure;
using LAMGestRestaurant.Infrastructure.Dados;
using LAMGestRestaurant.Infrastructure.Identidade;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Serilog;

Log.Logger = new LoggerConfiguration().WriteTo.Console().CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog((ctx, services, cfg) => cfg
        .ReadFrom.Configuration(ctx.Configuration)
        .ReadFrom.Services(services));

    builder.Services.AddHttpContextAccessor();
    builder.Services.AddScoped<IUtilizadorActual, UtilizadorActual>();
    builder.Services.AddInfraestrutura(builder.Configuration);

    builder.Services.ConfigureApplicationCookie(o =>
    {
        o.Cookie.Name = "LAMGest.Sessao";
        o.Cookie.HttpOnly = true;
        // SameSite=Strict protege contra CSRF: o frontend é servido na mesma origem que a API.
        o.Cookie.SameSite = SameSiteMode.Strict;
        o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        o.ExpireTimeSpan = TimeSpan.FromHours(12);
        o.SlidingExpiration = true;
        // API: responder 401/403 em vez de redireccionar para uma página de login.
        o.Events.OnRedirectToLogin = ctx => { ctx.Response.StatusCode = StatusCodes.Status401Unauthorized; return Task.CompletedTask; };
        o.Events.OnRedirectToAccessDenied = ctx => { ctx.Response.StatusCode = StatusCodes.Status403Forbidden; return Task.CompletedTask; };
    });

    builder.Services.AddSingleton<IAuthorizationPolicyProvider, ProvedorPoliticasPermissao>();
    builder.Services.AddAuthorization(o =>
    {
        // Por omissão todos os endpoints exigem sessão; os públicos usam [AllowAnonymous].
        o.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
    });

    builder.Services.AddProblemDetails();
    builder.Services.AddExceptionHandler<TratadorExcepcoes>();
    builder.Services.AddControllers().ConfigureApiBehaviorOptions(o =>
    {
        o.InvalidModelStateResponseFactory = ctx =>
        {
            var problema = new ValidationProblemDetails(ctx.ModelState)
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Dados inválidos",
                Detail = "Corrija os campos assinalados.",
            };
            return new BadRequestObjectResult(problema);
        };
    });
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(o => o.SwaggerDoc("v1", new() { Title = "LAMGest Restaurant ERP API", Version = "v1" }));

    var origens = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
    if (origens.Length > 0)
        builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.WithOrigins(origens).AllowAnyHeader().AllowAnyMethod().AllowCredentials()));

    var app = builder.Build();

    if (args.Contains(BootstrapAdmin.Comando))
        return await BootstrapAdmin.ExecutarAsync(app.Services);

    await PrepararBaseDadosAsync(app);

    app.UseExceptionHandler();
    app.UseStatusCodePages();
    app.UseSerilogRequestLogging();

    if (app.Configuration.GetValue<bool>("Swagger:Activo"))
    {
        app.UseSwagger();
        app.UseSwaggerUI();
    }

    // Frontend compilado (frontend/dist copiado para wwwroot na publicação).
    app.UseDefaultFiles();
    app.UseStaticFiles();

    if (origens.Length > 0)
        app.UseCors();
    app.UseAuthentication();
    // Enquanto o utilizador tiver de alterar a palavra-passe, só pode usar /api/auth (e /api/saude).
    app.Use(async (ctx, next) =>
    {
        var caminho = ctx.Request.Path;
        if (ctx.User.HasClaim(FabricaClaims.DeveAlterarPalavraPasse, "true")
            && caminho.StartsWithSegments("/api")
            && !caminho.StartsWithSegments("/api/auth")
            && !caminho.StartsWithSegments("/api/saude"))
        {
            await Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Alteração de palavra-passe obrigatória",
                detail: "Tem de alterar a palavra-passe antes de continuar.").ExecuteAsync(ctx);
            return;
        }
        await next();
    });
    app.UseAuthorization();

    app.MapControllers();
    // Rotas do React Router: qualquer caminho que não seja /api, /hubs ou /swagger devolve o index.html.
    app.MapFallbackToFile("index.html").AllowAnonymous();

    await app.RunAsync();
    return 0;
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "A aplicação terminou devido a um erro fatal");
    return 1;
}
finally
{
    await Log.CloseAndFlushAsync();
}

// As migrations NÃO são aplicadas automaticamente (aplicação controlada, ver README.md).
// Se a base estiver actualizada, semeia os dados de referência em falta.
static async Task PrepararBaseDadosAsync(WebApplication app)
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    try
    {
        var pendentes = (await db.Database.GetPendingMigrationsAsync()).ToList();
        if (pendentes.Count > 0)
        {
            Log.Warning("Existem {Total} migrations por aplicar ({Lista}). Operações de gravação ficarão indisponíveis até serem aplicadas.",
                pendentes.Count, string.Join(", ", pendentes));
            return;
        }
        await scope.ServiceProvider.GetRequiredService<SemeadorDados>().ExecutarAsync();
    }
    catch (Exception ex)
    {
        Log.Error(ex, "Não foi possível preparar a base de dados no arranque. A API vai arrancar, mas sem acesso a dados.");
    }
}

public partial class Program;
