using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;
using Serilog;
using LeanKernel.Archivist;
using LeanKernel.Archivist.Embedding;
using LeanKernel.Archivist.Knowledge;
using LeanKernel.Archivist.Sessions;
using LeanKernel.Archivist.Wiki;
using LeanKernel.Commander;
using LeanKernel.Commander.Adapters;
using LeanKernel.Core.Configuration;
using LeanKernel.Core.Interfaces;
using LeanKernel.Host;
using LeanKernel.Host.Services;
using LeanKernel.Host.Services.Auth;
using LeanKernel.Plugins;
using LeanKernel.Plugins.BuiltIn;
using LeanKernel.Scheduler;
using LeanKernel.Thinker;
using LeanKernel.Thinker.Agents;
using LeanKernel.Thinker.Workflows;

// Configure Serilog early for bootstrap logging
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    // Runtime config overlay persisted by onboarding
    var configuredWikiPath = builder.Configuration["LeanKernel:Wiki:BasePath"] ?? "/app/data/wiki";
    var configuredDataDir = ResolveWritableDataDirectory(configuredWikiPath);

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
    builder.Services.AddSingleton<IKnowledgeSearchService, KnowledgeSearchService>();
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

    // Intelligent routing (FR-1 through FR-8) — disabled by default until LeanKernel:Routing:Enabled = true
    builder.Services.AddSingleton<LeanKernel.Thinker.Routing.TaskComplexityScorer>();
    builder.Services.AddSingleton<LeanKernel.Thinker.Routing.ProviderHealthTracker>(sp =>
    {
        var cfg = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<LeanKernelConfig>>().Value;
        return new LeanKernel.Thinker.Routing.ProviderHealthTracker(
            TimeSpan.FromSeconds(cfg.Routing.CooldownSeconds));
    });
    builder.Services.AddSingleton<LeanKernel.Thinker.Routing.SpendGuard>();
    builder.Services.AddSingleton<LeanKernel.Thinker.Routing.PolicyModelSelector>();
    builder.Services.AddSingleton<LeanKernel.Thinker.Routing.ResponseQualityGate>();
    builder.Services.AddSingleton<LeanKernel.Thinker.Routing.SelectionLogStore>();
    builder.Services.AddSingleton<LeanKernel.Thinker.Routing.ModelRoutingService>();

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
    builder.Services.AddSingleton<ITool, KnowledgeSearchTool>();
    builder.Services.AddSingleton<IToolRegistry, PluginHost>();

    // Scheduler
    builder.Services.AddSingleton<IScheduler, CronScheduler>();
    builder.Services.AddSingleton<LeanKernel.Scheduler.Jobs.WikiMaintenanceJob>();
    builder.Services.AddSingleton<LeanKernel.Scheduler.Jobs.ModelLimitSyncJob>();
    builder.Services.AddSingleton<LeanKernel.Scheduler.ProactiveTaskRunner>();

    // Web API services
    builder.Services.AddSingleton<LogReaderService>();
    builder.Services.AddSingleton<FileBrowserService>();
    builder.Services.AddSingleton<IOnboardingStateStore, OnboardingStateStore>();
    builder.Services.AddSingleton<IRuntimeLeanKernelConfigStore, RuntimeLeanKernelConfigStore>();
    builder.Services.AddSingleton<IOnboardingOrchestrator, OnboardingOrchestrator>();

    // Authentication & Authorization
    builder.Services.AddLeanKernelAuth(builder.Configuration, configuredDataDir);
    builder.Services.AddLeanKernelOidc(builder.Configuration);

    // Engagement Rules (AGENTS.md) — rules of engagement between user and agent
    builder.Services.AddSingleton<IEngagementRulesProvider, EngagementRulesProvider>();
    builder.Services.AddSingleton<IActionAuthorizer, ActionAuthorizer>();
    builder.Services.AddSingleton(sp =>
    {
        var rulesProvider = sp.GetRequiredService<IEngagementRulesProvider>();
        var logger = sp.GetRequiredService<ILogger<TimeBoundaryService>>();
        var rules = rulesProvider.GetCurrent();
        return new TimeBoundaryService(rules, logger);
    });
    builder.Services.AddScoped<EngagementAuthorizationFilter>();

    // Forwarded headers (for reverse proxy HTTPS detection)
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        options.KnownIPNetworks.Clear();
        options.KnownProxies.Clear();
    });

    // ASP.NET Core
    builder.Services.AddControllers(options =>
    {
        options.Filters.Add<EngagementAuthorizationFilter>();
    });
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

    // CORS — same-origin by default; explicit origins when configured
    builder.Services.AddCors(options =>
    {
        options.AddDefaultPolicy(policy =>
        {
            var authConfig = builder.Configuration.GetSection("LeanKernel:Auth").Get<AuthConfig>();
            var origins = authConfig?.AllowedOrigins ?? [];
            if (origins.Length > 0)
            {
                policy.WithOrigins(origins)
                    .AllowAnyMethod()
                    .AllowAnyHeader()
                    .AllowCredentials();
            }
            else
            {
                // Same-origin only (no cross-origin requests)
                policy.SetIsOriginAllowed(_ => false);
            }
        });
    });

    var app = builder.Build();

    // Load engagement rules (AGENTS.md) on startup
    var rulesProvider = app.Services.GetRequiredService<IEngagementRulesProvider>();
    await rulesProvider.LoadAsync(CancellationToken.None);

    app.UseForwardedHeaders();
    app.UseCors();
    app.UseStaticFiles();
    app.UseLeanKernelAuth();
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

static string ResolveWritableDataDirectory(string configuredWikiPath)
{
    var parentDirectory = Path.GetFullPath(Path.GetDirectoryName(configuredWikiPath) ?? "/app/data");
    if (CanWriteToDirectory(parentDirectory))
        return parentDirectory;

    var wikiDirectory = Path.GetFullPath(configuredWikiPath);
    if (CanWriteToDirectory(wikiDirectory))
        return wikiDirectory;

    throw new UnauthorizedAccessException(
        $"Neither '{parentDirectory}' nor '{wikiDirectory}' is writable for runtime configuration persistence.");
}

static bool CanWriteToDirectory(string directoryPath)
{
    try
    {
        Directory.CreateDirectory(directoryPath);
        var probePath = Path.Combine(directoryPath, $".LeanKernel-write-probe-{Guid.NewGuid():N}");
        using (File.Create(probePath)) { }
        File.Delete(probePath);
        return true;
    }
    catch (UnauthorizedAccessException)
    {
        return false;
    }
    catch (IOException)
    {
        return false;
    }
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

        var taskRunner = _services.GetRequiredService<LeanKernel.Scheduler.ProactiveTaskRunner>();
        await taskRunner.StartAsync(ct);

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
