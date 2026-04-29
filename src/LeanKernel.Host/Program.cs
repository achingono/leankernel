using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Options;
using Serilog;
using LeanKernel.Archivist;
using LeanKernel.Archivist.Embedding;
using LeanKernel.Archivist.Sessions;
using LeanKernel.Archivist.Wiki;
using LeanKernel.Commander;
using LeanKernel.Commander.Adapters;
using LeanKernel.Core.Configuration;
using LeanKernel.Core.Interfaces;
using LeanKernel.Host;
using LeanKernel.Host.Services;
using LeanKernel.Plugins;
using LeanKernel.Plugins.BuiltIn;
using LeanKernel.Scheduler;
using LeanKernel.Thinker;
using LeanKernel.Thinker.Agents;

// Configure Serilog early for bootstrap logging
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    // Runtime config overlay persisted by onboarding
    var configuredWikiPath = builder.Configuration["LeanKernel:Wiki:BasePath"] ?? "/app/data/wiki";
    var configuredDataDir = Path.GetFullPath(Path.GetDirectoryName(configuredWikiPath) ?? "/app/data");
    Directory.CreateDirectory(configuredDataDir);

    var runtimeConfigPath = Path.Combine(configuredDataDir, "runtime-settings.json");
    var onboardingStatePath = Path.Combine(configuredDataDir, "onboarding-state.json");
    builder.Configuration.AddJsonFile(runtimeConfigPath, optional: true, reloadOnChange: true);

    builder.Services.AddSingleton(new LeanKernelHostPaths
    {
        DataDirectory = configuredDataDir,
        RuntimeConfigPath = runtimeConfigPath,
        OnboardingStatePath = onboardingStatePath
    });

    // Serilog — structured logging to console + rolling file
    builder.Services.AddSerilog((services, lc) => lc
        .ReadFrom.Configuration(builder.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .WriteTo.Console(
            outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext} {Message:lj}{NewLine}{Exception}")
        .WriteTo.File(
            path: Path.Combine(
                Path.GetDirectoryName(
                    builder.Configuration["LeanKernel:Wiki:BasePath"] ?? "/app/data/wiki") ?? "/app/data",
                "logs", "LeanKernel-.log"),
            rollingInterval: RollingInterval.Day,
            retainedFileCountLimit: 14,
            fileSizeLimitBytes: 10_000_000));

    // Bind configuration
    builder.Services.Configure<LeanKernelConfig>(
        builder.Configuration.GetSection(LeanKernelConfig.SectionName));

    // Core services
    builder.Services.AddSingleton<IWikiStore, WikiStore>();
    builder.Services.AddSingleton<ISessionStore>(sp =>
    {
        var config = sp.GetRequiredService<IOptions<LeanKernelConfig>>().Value;
        var sessionsPath = Path.Combine(
            Path.GetDirectoryName(config.Wiki.BasePath) ?? "/app/data",
            "sessions");
        return new SessionStore(sessionsPath, sp.GetRequiredService<ILogger<SessionStore>>());
    });
    builder.Services.AddSingleton<IEmbeddingService, EmbeddingService>();
    builder.Services.AddHttpClient<IEmbeddingService, EmbeddingService>((sp, client) =>
    {
        var config = sp.GetRequiredService<IOptions<LeanKernelConfig>>().Value;
        client.BaseAddress = new Uri(config.LiteLlm.BaseUrl);
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {config.LiteLlm.ApiKey}");
    });
    builder.Services.AddHttpClient("onboarding-probe");

    // Archivist
    builder.Services.AddSingleton<WikiIndexer>();
    builder.Services.AddSingleton<WikiCompiler>();
    builder.Services.AddSingleton<ConversationCompactor>();
    builder.Services.AddSingleton<IContextGatekeeper, ContextGatekeeper>();

    // Thinker
    builder.Services.AddSingleton<LeanKernel.Thinker.Middleware.FunctionLoggingMiddleware>();
    builder.Services.AddSingleton<LeanKernel.Thinker.Middleware.DiagnosticsMiddleware>();
    builder.Services.AddSingleton<LeanKernel.Thinker.Middleware.ContextGatingMiddleware>();
    builder.Services.AddSingleton<AgentFactory>();
    builder.Services.AddSingleton<ToolFunctionAdapter>();
    builder.Services.AddSingleton<PromptAssembler>();
    builder.Services.AddSingleton<IThinkerService, ThinkerService>();

    // Multi-Agent Orchestration
    builder.Services.AddSingleton<WorkerAgent, ResearchWorker>();
    builder.Services.AddSingleton<WorkerAgent, CodeWorker>();
    builder.Services.AddSingleton<WorkerAgent, ScheduleWorker>();
    builder.Services.AddSingleton<IAgentOrchestrator, AgentOrchestrator>();
    builder.Services.AddSingleton<LeanKernel.Thinker.Workflows.LeanKernelWorkflowBuilder>();

    // Commander — channels
    builder.Services.AddSingleton<IChannel, SignalChannel>();
    builder.Services.AddSingleton<ChannelRouter>();

    // Plugins
    builder.Services.AddSingleton<ITool, WikiQueryTool>();
    builder.Services.AddSingleton<IToolRegistry, PluginHost>();

    // Scheduler
    builder.Services.AddSingleton<IScheduler, CronScheduler>();

    // Web API services
    builder.Services.AddSingleton<LogReaderService>();
    builder.Services.AddSingleton<FileBrowserService>();
    builder.Services.AddSingleton<IOnboardingStateStore, OnboardingStateStore>();
    builder.Services.AddSingleton<IRuntimeLeanKernelConfigStore, RuntimeLeanKernelConfigStore>();
    builder.Services.AddSingleton<IOnboardingOrchestrator, OnboardingOrchestrator>();

    // ASP.NET Core
    builder.Services.AddControllers();
    builder.Services.AddRazorComponents()
        .AddInteractiveServerComponents();

    // HttpClient for Blazor components to call our own API
    builder.Services.AddScoped(sp =>
    {
        var nav = sp.GetRequiredService<NavigationManager>();
        return new HttpClient { BaseAddress = new Uri(nav.BaseUri) };
    });

    // Background service — channels + scheduler
    builder.Services.AddHostedService<LeanKernelHostedService>();

    // Health checks
    builder.Services.AddHealthChecks()
        .AddCheck<LeanKernelHealthCheck>("LeanKernel");

    // CORS for local development
    builder.Services.AddCors(options =>
    {
        options.AddDefaultPolicy(policy =>
            policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
    });

    var app = builder.Build();

    app.UseCors();
    app.UseStaticFiles();
    app.UseAntiforgery();

    // API routes
    app.MapControllers();
    app.MapHealthChecks("/api/health");

    // Blazor
    app.MapRazorComponents<LeanKernel.Host.Components.App>()
        .AddInteractiveServerRenderMode();

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "LeanKernel terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}

/// <summary>
/// Background service that starts the channel router and scheduler.
/// </summary>
[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
public sealed class LeanKernelHostedService : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly IOnboardingStateStore _onboardingState;
    private readonly ILogger<LeanKernelHostedService> _logger;

    public LeanKernelHostedService(
        IServiceProvider services,
        IOnboardingStateStore onboardingState,
        ILogger<LeanKernelHostedService> logger)
    {
        _services = services;
        _onboardingState = onboardingState;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        _logger.LogInformation("LeanKernel engine starting...");
        var waitingLogged = false;
        while (!ct.IsCancellationRequested && !await _onboardingState.IsCompletedAsync(ct))
        {
            if (!waitingLogged)
            {
                _logger.LogInformation("Onboarding not complete yet; channel startup deferred");
                waitingLogged = true;
            }

            await Task.Delay(TimeSpan.FromSeconds(5), ct);
        }

        if (ct.IsCancellationRequested)
            return;

        var router = _services.GetRequiredService<ChannelRouter>();
        await router.StartAsync(ct);
        _logger.LogInformation("LeanKernel engine running. Waiting for messages.");

        try
        {
            await Task.Delay(Timeout.Infinite, ct);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("LeanKernel engine shutting down...");
        }

        await router.StopAsync(ct);
    }
}
