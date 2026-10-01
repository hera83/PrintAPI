using api.BgWorkers;
using api.Data;
using api.Data.Seeds;
using api.Services.AiGateway;
using api.Services.AiGateway.Interfaces;
using api.Services.Authentication;
using api.Services.ErrorHandling;
using api.Services.Hp;
using api.Services.Hp.Interfaces;
using api.Services.Ipp;
using api.Services.Ipp.Interfaces;
using api.Services.Logging;
using api.Services.Logging.Interfaces;
using api.Services.Mail;
using api.Services.Mail.Interfaces;
using api.Services.Mdns;
using api.Services.Mdns.Interfaces;
using api.Services.Ollama;
using api.Services.Ollama.Interfaces;
using api.Services.Printing;
using api.Services.Printing.Interfaces;
using api.Services.Sms;
using api.Services.Sms.Interfaces;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Serilog;
using Serilog.Events;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

var apiName = builder.Configuration["Api:Name"] ?? "api";

var environmentName = builder.Environment.EnvironmentName;
var environmentBadgeColor = environmentName switch
{
    "Production" => "brightgreen",
    "Development" => "orange",
    _ => "yellow"
};
var environmentBadgeMarkdown =
    $"![{environmentName}](https://img.shields.io/badge/env-{Uri.EscapeDataString(environmentName)}-{environmentBadgeColor})";

var logDbPath = Path.Combine(builder.Environment.ContentRootPath, "app_dbs", "log.db");
Directory.CreateDirectory(Path.GetDirectoryName(logDbPath)!);

builder.Host.UseSerilog((_, loggerConfiguration) =>
{
    loggerConfiguration
        .MinimumLevel.Information()
        .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
        .Enrich.FromLogContext()
        .WriteTo.Console()
        .WriteTo.SQLite(
            sqliteDbPath: logDbPath,
            tableName: LogDatabaseSettings.TableName,
            storeTimestampInUtc: true,
            maxDatabaseSize: (uint)(LogDatabaseSettings.MaxSizeBytes / (1024 * 1024)),
            rollOver: false);
});

builder.Services.AddSingleton(new LogDatabaseSettings(logDbPath));
builder.Services.AddScoped<ILogQueryService, LogQueryService>();
builder.Services.AddHostedService<LogCleanupService>();

builder.Services.AddControllers();

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer<ApiKeySecuritySchemeTransformer>();
    options.AddDocumentTransformer((document, _, _) =>
    {
        document.Info.Title = apiName;
        document.Info.Description = environmentBadgeMarkdown;
        document.Servers?.Clear();
        return Task.CompletedTask;
    });
});

builder.Services.AddHttpClient();

builder.Services.AddDbContext<ApiDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("Default")));

builder.Services.AddAuthentication(ApiKeyAuthenticationOptions.Scheme)
    .AddScheme<ApiKeyAuthenticationOptions, ApiKeyAuthenticationHandler>(
        ApiKeyAuthenticationOptions.Scheme, null);

builder.Services.AddAuthorization(options =>
{
    var masterKeyOnly = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .RequireClaim(ApiKeyClaimTypes.KeyType, ApiKeyClaimTypes.Master)
        .Build();

    // Deny by default: everything requires the master key unless an endpoint opts in to AnyApiKey or [AllowAnonymous].
    options.FallbackPolicy = masterKeyOnly;
    options.AddPolicy(AuthorizationPolicies.MasterKeyOnly, masterKeyOnly);
    options.AddPolicy(AuthorizationPolicies.AnyApiKey, policy => policy.RequireAuthenticatedUser());
});

builder.Services.AddScoped<IOllamaConfigurationProvider, OllamaConfigurationProvider>();
builder.Services.AddScoped<OllamaHttpClientFactory>();
builder.Services.AddScoped<IOllamaService, OllamaService>();

builder.Services.AddScoped<IAiGatewayConfigurationProvider, AiGatewayConfigurationProvider>();
builder.Services.AddScoped<AiGatewayHttpClientFactory>();
builder.Services.AddScoped<IAiGatewayService, AiGatewayService>();

builder.Services.AddScoped<IMailService, MailService>();

builder.Services.AddHttpClient<ISmsService, SmsService>((sp, client) =>
{
    var baseUrl = sp.GetRequiredService<IConfiguration>()["Sms:BaseUrl"];
    if (!string.IsNullOrWhiteSpace(baseUrl))
    {
        client.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/", UriKind.Absolute);
    }
});

builder.Services.AddScoped<IMdnsBrowser, MdnsBrowser>();
// IppClient applies its own per-operation timeouts (a print job may take far longer than an attribute query).
builder.Services.AddHttpClient<IIppClient, IppClient>(client => client.Timeout = Timeout.InfiniteTimeSpan);
builder.Services.AddScoped<IHpPrinterDiscoveryService, HpPrinterDiscoveryService>();
builder.Services.AddScoped<IHpPrintService, HpPrintService>();

builder.Services.AddSingleton(new PrintJobFileStore(Path.Combine(builder.Environment.ContentRootPath, "app_files", "PrintJobs")));
builder.Services.AddSingleton<PrintQueueSignal>();
builder.Services.AddScoped<IPrintJobProcessor, PrintJobProcessor>();
builder.Services.AddHostedService<PrintQueueWorker>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
    await dbContext.Database.MigrateAsync();
    await DataSeeder.SeedAsync(dbContext);
}

app.UseExceptionHandler();

app.Use(async (context, next) =>
{
    if (context.Request.Path == "/" && HttpMethods.IsGet(context.Request.Method))
    {
        context.Response.Redirect("/swagger");
        return;
    }

    await next();
});

app.MapOpenApi().AllowAnonymous();

app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/openapi/v1.json", apiName);
    options.DocumentTitle = apiName;
    options.DocExpansion(Swashbuckle.AspNetCore.SwaggerUI.DocExpansion.None);
    options.DefaultModelsExpandDepth(-1);
});

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
