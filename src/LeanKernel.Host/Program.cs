using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Serilog;
using LeanKernel.Archivist;
using LeanKernel.Archivist.Engagement;
using LeanKernel.Archivist.Identity;
using LeanKernel.Archivist.Embedding;
using LeanKernel.Archivist.Knowledge;
using LeanKernel.Archivist.Sessions;
using LeanKernel.Archivist.Wiki;
using LeanKernel.Commander;
using LeanKernel.Commander.Adapters;
using LeanKernel.Commander.Queue;
using LeanKernel.Commander.Queue.Data;
using LeanKernel.Core.Configuration;
using LeanKernel.Core.Interfaces;
using LeanKernel.Host;
using LeanKernel.Host.Services;
using LeanKernel.Host.Services.Auth;
using LeanKernel.Host.Services.Skills;
using LeanKernel.Plugins;
using LeanKernel.Plugins.Attachments;
using LeanKernel.Plugins.BuiltIn;
using LeanKernel.Plugins.BuiltIn.Skills;
using LeanKernel.Scheduler;
using LeanKernel.Thinker;
using LeanKernel.Thinker.Authorization;
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
    var configuredAgentsPath = builder.Configuration["LeanKernel:Agents:BasePath"]
        ?? Path.Combine(configuredDataDir, "agents");

    var runtimeConfigPath = Path.Combine(configuredDataDir, "runtime-settings.json");
    var onboardingStatePath = Path.Combine(configuredDataDir, "onboarding-state.json");
    var liteLlmConfigPath = builder.Configuration["LeanKernel:LiteLlm:ConfigPath"]
        ?? Path.Combine(AppContext.BaseDirectory, "config", "litellm", "config.yaml");
    builder.Configuration.AddJsonFile(runtimeConfigPath, optional: true, reloadOnChange: true);
    // Re-apply environment variables so deployment-time values can override runtime overlay.
    builder.Configuration.AddEnvironmentVariables();

    builder.Services.AddSingleton(new LeanKernelHostPaths
    {
        DataDirectory = configuredDataDir,
        AgentsDirectory = configuredAgentsPath,
        RuntimeConfigPath = runtimeConfigPath,
        OnboardingStatePath = onboardingStatePath,
        LiteLlmConfigPath = liteLlmConfigPath
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

    builder.Services.AddHttpClient("onboarding-probe");

    // Skill directories are passed to the plugin feature registration so runtime skills load at startup.
    var skillDirs = builder.Configuration["LeanKernel:Skills:BasePaths"]?.Split(',')
        ?? [Path.Combine(configuredDataDir, "skills")];

    builder.Services
        .AddArchivist()
        .AddThinker()
        .AddCommander(configuredDataDir)
        .AddPlugins(skillDirs, configuredDataDir)
        .AddScheduler(configuredDataDir);

    // Web API services
    builder.Services.AddSingleton<LogReaderService>();
    builder.Services.AddSingleton<FileBrowserService>();
    builder.Services.AddHttpClient<IAttachmentTextExtractionService, AttachmentTextExtractionService>((sp, client) =>
    {
        var config = sp.GetRequiredService<IOptions<LeanKernelConfig>>().Value;
        client.BaseAddress = new Uri(config.Unstructured.BaseUrl);
        client.Timeout = TimeSpan.FromSeconds(config.Unstructured.TimeoutSeconds);
    });
    builder.Services.AddTransient<InboundAttachmentInputProcessor>();
    builder.Services.AddSingleton<IOnboardingStateStore, OnboardingStateStore>();
    builder.Services.AddSingleton<IRuntimeLeanKernelConfigStore, RuntimeLeanKernelConfigStore>();
    builder.Services.AddSingleton<IOnboardingOrchestrator, OnboardingOrchestrator>();
    builder.Services.AddSingleton<ILiteLlmRoutingConfigService>(sp =>
    {
        var paths = sp.GetRequiredService<LeanKernelHostPaths>();
        return new LiteLlmRoutingConfigService(paths.LiteLlmConfigPath);
    });
    builder.Services.AddSingleton<IModelLimitDriftService>(sp =>
    {
        var paths = sp.GetRequiredService<LeanKernelHostPaths>();
        var configuredScriptPath = builder.Configuration["LeanKernel:LiteLlm:DriftScriptPath"];
        var scriptPath = configuredScriptPath;
        if (string.IsNullOrWhiteSpace(scriptPath))
        {
            var containerScriptPath = "/app/scripts/sync_litellm_model_limits.py";
            scriptPath = File.Exists(containerScriptPath)
                ? containerScriptPath
                : Path.Combine(AppContext.BaseDirectory, "scripts", "sync_litellm_model_limits.py");
        }
        return new ModelLimitDriftService(scriptPath, paths.LiteLlmConfigPath);
    });

    // Authentication & Authorization
    builder.Services.AddLeanKernelAuth(builder.Configuration, configuredDataDir);
    builder.Services.AddLeanKernelOidc(builder.Configuration);

    builder.Services.AddScoped<EngagementAuthorizationFilter>();
    
    builder.Services.AddSingleton<AgentsConfigurationStep>();
    builder.Services.AddSingleton<IOnboardingStep>(sp => sp.GetRequiredService<AgentsConfigurationStep>());
    builder.Services.AddSingleton<SelfConfigurationStep>();
    builder.Services.AddSingleton<IAgentSelfProfileInitializer>(sp => sp.GetRequiredService<SelfConfigurationStep>());
    builder.Services.AddSingleton<IOnboardingStep>(sp => sp.GetRequiredService<SelfConfigurationStep>());
    builder.Services.AddSingleton<UserConfigurationStep>();
    builder.Services.AddSingleton<IUserProfileSynchronizer>(sp => sp.GetRequiredService<UserConfigurationStep>());
    builder.Services.AddSingleton<IOnboardingStep>(sp => sp.GetRequiredService<UserConfigurationStep>());

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

    // Ensure the durable outbound queue is ready before hosted services start dispatching.
    using (var scope = app.Services.CreateScope())
    {
        var dbContext = scope.ServiceProvider.GetRequiredService<MessageQueueDbContext>();
        await dbContext.Database.MigrateAsync();

        var messageQueue = scope.ServiceProvider.GetRequiredService<IMessageQueue>();
        if (messageQueue is PersistentMessageQueueService persistentQueue)
        {
            await persistentQueue.RecoverUndeliveredMessagesAsync(CancellationToken.None);
        }
    }

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
        using var probe = File.Create(probePath);
        probe.WriteByte(0);
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
