using System.Text.Json.Serialization;
using LAMGestRestaurant.Api.Comandos;
using LAMGestRestaurant.Application;
using LAMGestRestaurant.Api.Erros;
using LAMGestRestaurant.Api.Seguranca;
using LAMGestRestaurant.Api.TempoReal;
using LAMGestRestaurant.Application.Comum;
using LAMGestRestaurant.Application.Licencas;
using LAMGestRestaurant.Infrastructure;
using LAMGestRestaurant.Infrastructure.Dados;
using LAMGestRestaurant.Infrastructure.Identidade;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting.WindowsServices;
using Serilog;

Log.Logger = new LoggerConfiguration().WriteTo.Console().CreateBootstrapLogger();

try
{
    // Como serviço Windows o directório actual é System32: os caminhos relativos (logs, wwwroot) passam a ser os da aplicação.
    var comoServico = WindowsServiceHelpers.IsWindowsService();
    if (comoServico)
        Directory.SetCurrentDirectory(AppContext.BaseDirectory);
    var builder = WebApplication.CreateBuilder(new WebApplicationOptions
    {
        Args = args,
        ContentRootPath = comoServico ? AppContext.BaseDirectory : null,
    });
    builder.Host.UseWindowsService(o => o.ServiceName = "LAMGestRestaurant");
    // Ficheiro escrito pelo instalador (connection string, pastas), protegido por ACL: só o serviço e os administradores o lêem.
    if (Environment.GetEnvironmentVariable("LAMGEST_CONFIG") is { Length: > 0 } ficheiroConfig)
        builder.Configuration.AddJsonFile(ficheiroConfig, optional: false, reloadOnChange: false);

    builder.Host.UseSerilog((ctx, services, cfg) => cfg
        .ReadFrom.Configuration(ctx.Configuration)
        .ReadFrom.Services(services)
        // O middleware regista como erro todas as excepções, também as recusas de regras de negócio (422, 409…).
        // Os erros a sério (500/503) são registados pelo TratadorExcepcoes; as recusas ficam como informação.
        .MinimumLevel.Override("Microsoft.AspNetCore.Diagnostics.ExceptionHandlerMiddleware", Serilog.Events.LogEventLevel.Fatal));

    // Chaves que cifram o cookie de sessão. Sem pasta persistente, um serviço sem perfil de utilizador
    // perde-as ao reiniciar e todos os utilizadores têm de voltar a entrar.
    var protecao = builder.Services.AddDataProtection().SetApplicationName("LAMGestRestaurant");
    var pastaChaves = builder.Configuration["DataProtection:Pasta"];
    if (!string.IsNullOrWhiteSpace(pastaChaves))
    {
        protecao.PersistKeysToFileSystem(new DirectoryInfo(pastaChaves));
        if (OperatingSystem.IsWindows())
            protecao.ProtectKeysWithDpapi(protectToLocalMachine: true);
    }
    else if (!builder.Environment.IsDevelopment() && !args.Contains(Migrar.Comando) && !args.Contains(BootstrapAdmin.Comando))
    {
        Log.Warning("DataProtection:Pasta não configurada: as sessões podem perder-se quando o serviço reinicia (ver docs/INSTALACAO_LAN.md).");
    }

    builder.Services.AddHttpContextAccessor();
    builder.Services.AddScoped<IUtilizadorActual, UtilizadorActual>();
    builder.Services.AddInfraestrutura(builder.Configuration);
    builder.Services.AddAplicacao();
    // Testes automáticos (E2E e integração): chave pública de teste. Em produção vale sempre a chave do fabricante embutida.
    if ((builder.Environment.IsEnvironment("E2E") || builder.Environment.IsEnvironment("Testing"))
        && builder.Configuration["Licenciamento:ChavePublicaTeste"] is { Length: > 0 } chaveTeste)
        builder.Services.AddSingleton(new ChaveLicencas(LAMGestRestaurant.Licenciamento.ChavesLicenca.Publica(chaveTeste)));
    builder.Services.AddSignalR();
    builder.Services.AddSingleton<INotificador, NotificadorSignalR>();

    builder.Services.ConfigureApplicationCookie(o =>
    {
        o.Cookie.Name = "LAMGest.Sessao";
        o.Cookie.HttpOnly = true;
        // SameSite=Strict protege contra CSRF: o frontend é servido na mesma origem que a API.
        o.Cookie.SameSite = SameSiteMode.Strict;
        // Com HTTPS configurado na LAN, Seguranca:CookieSoHttps=true impede o envio do cookie em HTTP simples.
        o.Cookie.SecurePolicy = builder.Configuration.GetValue<bool>("Seguranca:CookieSoHttps")
            ? CookieSecurePolicy.Always
            : CookieSecurePolicy.SameAsRequest;
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
    builder.Services.AddControllers()
        // Enums como texto no JSON ("Mesa", "Aberta"...), coerentes com a BD e legíveis no frontend.
        .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()))
        .ConfigureApiBehaviorOptions(o =>
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
    if (args.Contains(Migrar.Comando))
        return await Migrar.ExecutarAsync(app.Services, apenasListar: args.Contains("--listar"));

    await PrepararBaseDadosAsync(app);

    // Primeiro o registo dos pedidos: assim regista o estado final (ex.: 422), não a excepção antes de ser tratada.
    app.UseSerilogRequestLogging();
    app.UseExceptionHandler();
    app.UseStatusCodePages();
    app.Use(async (ctx, next) =>
    {
        var cabecalhos = ctx.Response.Headers;
        cabecalhos.XContentTypeOptions = "nosniff";
        cabecalhos.XFrameOptions = "DENY";
        cabecalhos["Referrer-Policy"] = "no-referrer";
        await next();
    });

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
    // Licença expirada, inexistente ou inválida: só consultas (GET). Entrar, sair e activar a licença continuam possíveis.
    app.Use(async (ctx, next) =>
    {
        var caminho = ctx.Request.Path;
        if (caminho.StartsWithSegments("/api")
            && !HttpMethods.IsGet(ctx.Request.Method) && !HttpMethods.IsHead(ctx.Request.Method) && !HttpMethods.IsOptions(ctx.Request.Method)
            && !caminho.StartsWithSegments("/api/auth") && !caminho.StartsWithSegments("/api/licenca") && !caminho.StartsWithSegments("/api/agente")
            && await ctx.RequestServices.GetRequiredService<ServicoLicenca>().EstadoAsync(ctx.RequestAborted) is { Bloqueada: true } estado)
        {
            await Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Licença", type: "licenca-bloqueada", detail: estado.Mensagem).ExecuteAsync(ctx);
            return;
        }
        await next();
    });
    app.UseAuthorization();

    app.MapControllers();
    app.MapHub<HubOperacoes>(HubOperacoes.Rota);
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
        // Produtos que controlam stock e ainda não têm artigo (criados antes desta regra): passam a aparecer no Stock.
        var activados = await scope.ServiceProvider.GetRequiredService<LAMGestRestaurant.Application.Catalogo.ServicoStockProdutos>().SincronizarTodosAsync();
        if (activados > 0)
            Log.Information("Stock activado em {Total} produto(s) sem artigo de stock.", activados);
        // Verificação da licença no arranque (depois, de 5 em 5 minutos quando é usada).
        var licenca = await scope.ServiceProvider.GetRequiredService<ServicoLicenca>().EstadoAsync();
        if (licenca.Bloqueada)
            Log.Warning("Licença: {Estado}. {Mensagem}", licenca.Estado, licenca.Mensagem);
        else
            Log.Information("Licença: {Estado} — {Nome} (NUIT {Nuit}), válida até {ValidaAte}", licenca.Estado, licenca.NomeComercial, licenca.Nuit, licenca.ValidaAte);
    }
    catch (Exception ex)
    {
        Log.Error(ex, "Não foi possível preparar a base de dados no arranque. A API vai arrancar, mas sem acesso a dados.");
    }
}

public partial class Program;
