using LeanKernel.Abstractions.Configuration;
using LeanKernel.Agents;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.FluentUI.AspNetCore.Components;
using LeanKernel.Channels;
using LeanKernel.Context;
using LeanKernel.Diagnostics;
using LeanKernel.Gateway;
using LeanKernel.Gateway.Components;
using LeanKernel.Gateway.Middleware;
using LeanKernel.Gateway.Services;
using LeanKernel.Knowledge;
using LeanKernel.Learning;
using LeanKernel.Persistence;
using LeanKernel.Scheduler;
using LeanKernel.Tools;
using Microsoft.EntityFrameworkCore;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .Enrich.With(new LeanKernelLogEnricher())
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Services.AddOpenApi();
    builder.Services.AddHttpClient();
    builder.Services.AddRazorComponents()
        .AddInteractiveServerComponents();
    builder.Services.AddFluentUIComponents();
    builder.Services.AddScoped<ChatService>();
    builder.Services.AddScoped<OnboardingService>();
    builder.Services.AddScoped<DiagnosticsService>();
    builder.Services.AddScoped<KnowledgeUiService>();
    builder.Services.AddScoped<AdminService>();

    builder.Host.UseSerilog((context, services, configuration) => configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .Enrich.With(new LeanKernelLogEnricher())
        .WriteTo.Console());

    var leanKernelConfig = new LeanKernelConfig();
    builder.Configuration.GetSection(LeanKernelConfig.SectionName).Bind(leanKernelConfig);
    builder.Services.Configure<LeanKernelConfig>(
        builder.Configuration.GetSection(LeanKernelConfig.SectionName));

    ConfigureOpenTelemetry(builder, leanKernelConfig);

    builder.Services.AddLeanKernelPersistence(leanKernelConfig.Database);
    builder.Services.AddLeanKernelKnowledge(leanKernelConfig.GBrain);
    builder.Services.AddLeanKernelContext(leanKernelConfig);
    builder.Services.AddLeanKernelIdentity(leanKernelConfig.Identity);
    builder.Services.AddLeanKernelTools();
    builder.Services.AddLeanKernelAgents(leanKernelConfig);
    builder.Services.AddLeanKernelDiagnostics(leanKernelConfig.Diagnostics);
    builder.Services.AddLeanKernelChannels(leanKernelConfig.Channels);
    builder.Services.AddLeanKernelLearning(leanKernelConfig.Learning);
    builder.Services.AddLeanKernelScheduler(leanKernelConfig.Scheduler);
    builder.Services.AddLeanKernelHardening(leanKernelConfig.Hardening);

    var app = builder.Build();

    try
    {
        await using var scope = app.Services.CreateAsyncScope();
        var dbContextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<LeanKernelDbContext>>();
        await using var dbContext = await dbContextFactory.CreateDbContextAsync().ConfigureAwait(false);
        await dbContext.Database.ExecuteSqlRawAsync("CREATE SCHEMA IF NOT EXISTS engine").ConfigureAwait(false);
        var hasSessionsTable = await dbContext.Database
            .SqlQueryRaw<int>("""SELECT CAST(COUNT(*) AS int) AS "Value" FROM information_schema.tables WHERE table_schema = 'engine' AND table_name = 'Sessions'""")
            .FirstOrDefaultAsync().ConfigureAwait(false);
        if (hasSessionsTable == 0)
        {
            // Drop partial schema state from previous failed initializations
            await dbContext.Database.ExecuteSqlRawAsync("""DROP TABLE IF EXISTS engine."ScheduledJobExecutions" CASCADE""").ConfigureAwait(false);
            var script = dbContext.Database.GenerateCreateScript();
            await dbContext.Database.ExecuteSqlRawAsync(script).ConfigureAwait(false);
        }
        await dbContext.EnsureSchedulerSchemaAsync().ConfigureAwait(false);
    }
    catch (Exception ex)
    {
        Log.Warning(ex, "Database initialization skipped because persistence is unavailable; continuing in degraded mode");
    }

    app.UseMiddleware<CorrelationIdMiddleware>();
    app.UseMiddleware<RateLimitingMiddleware>();
    app.UseStaticFiles();
    app.UseAntiforgery();
    app.MapStaticAssets();

    if (app.Environment.IsDevelopment())
    {
        app.MapOpenApi();
    }

    app.MapHealthChecks("/healthz");
    app.MapEndpoints();
    app.MapRazorComponents<App>()
        .AddInteractiveServerRenderMode();

    app.Run();
}
catch (Exception ex) when (
    ex is not HostAbortedException
    && !string.Equals(ex.GetType().Name, "StopTheHostException", StringComparison.Ordinal))
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}

static void ConfigureOpenTelemetry(WebApplicationBuilder builder, LeanKernelConfig leanKernelConfig)
{
    var consoleExporterEnabled = builder.Configuration.GetValue<bool>("OpenTelemetry:ConsoleExporterEnabled");
    var otlpEndpoint = builder.Configuration["OpenTelemetry:Otlp:Endpoint"]
        ?? builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];

    if (!consoleExporterEnabled && string.IsNullOrWhiteSpace(otlpEndpoint))
    {
        return;
    }

    var serviceName = string.IsNullOrWhiteSpace(leanKernelConfig.Diagnostics.ServiceName)
        ? "leankernel"
        : leanKernelConfig.Diagnostics.ServiceName;

    builder.Services.AddOpenTelemetry()
        .ConfigureResource(resource => resource.AddService(serviceName))
        .WithTracing(tracing =>
        {
            tracing
                .AddSource("LeanKernel.Diagnostics")
                .AddSource("LeanKernel.Persistence")
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation();

            if (consoleExporterEnabled)
            {
                tracing.AddConsoleExporter();
            }

            if (!string.IsNullOrWhiteSpace(otlpEndpoint))
            {
                tracing.AddOtlpExporter(options => options.Endpoint = new Uri(otlpEndpoint, UriKind.Absolute));
            }
        })
        .WithMetrics(metrics =>
        {
            metrics
                .AddMeter("LeanKernel")
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation();

            if (consoleExporterEnabled)
            {
                metrics.AddConsoleExporter();
            }

            if (!string.IsNullOrWhiteSpace(otlpEndpoint))
            {
                metrics.AddOtlpExporter(options => options.Endpoint = new Uri(otlpEndpoint, UriKind.Absolute));
            }
        });

    builder.Logging.AddOpenTelemetry(options =>
    {
        options.IncludeFormattedMessage = true;
        options.IncludeScopes = true;
        options.ParseStateValues = true;

        if (consoleExporterEnabled)
        {
            options.AddConsoleExporter();
        }

        if (!string.IsNullOrWhiteSpace(otlpEndpoint))
        {
            options.AddOtlpExporter(exporterOptions => exporterOptions.Endpoint = new Uri(otlpEndpoint, UriKind.Absolute));
        }
    });
}

public partial class Program
{
}
