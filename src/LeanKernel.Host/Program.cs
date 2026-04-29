using Microsoft.Extensions.Options;
using LeanKernel.Archivist;
using LeanKernel.Archivist.Embedding;
using LeanKernel.Archivist.Sessions;
using LeanKernel.Archivist.Wiki;
using LeanKernel.Commander;
using LeanKernel.Commander.Adapters;
using LeanKernel.Core.Configuration;
using LeanKernel.Core.Interfaces;
using LeanKernel.Plugins;
using LeanKernel.Plugins.BuiltIn;
using LeanKernel.Scheduler;
using LeanKernel.Thinker;
using LeanKernel.Thinker.SemanticKernel;

var builder = Host.CreateApplicationBuilder(args);

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

// Archivist
builder.Services.AddSingleton<WikiIndexer>();
builder.Services.AddSingleton<WikiCompiler>();
builder.Services.AddSingleton<ConversationCompactor>();
builder.Services.AddSingleton<IContextGatekeeper, ContextGatekeeper>();

// Thinker
builder.Services.AddSingleton<KernelFactory>();
builder.Services.AddSingleton<IThinkerService, ThinkerService>();
builder.Services.AddSingleton<PromptAssembler>();

// Commander — channels
builder.Services.AddSingleton<IChannel, SignalChannel>();
builder.Services.AddSingleton<ChannelRouter>();

// Plugins
builder.Services.AddSingleton<ITool, WikiQueryTool>();
builder.Services.AddSingleton<IToolRegistry, PluginHost>();

// Scheduler
builder.Services.AddSingleton<IScheduler, CronScheduler>();

// Hosted service — main entry point
builder.Services.AddHostedService<LeanKernelHostedService>();

// Health check endpoint
builder.Services.AddHealthChecks();

var host = builder.Build();
host.Run();

/// <summary>
/// Background service that starts the channel router and scheduler.
/// </summary>
public sealed class LeanKernelHostedService : BackgroundService
{
    private readonly ChannelRouter _router;
    private readonly ILogger<LeanKernelHostedService> _logger;

    public LeanKernelHostedService(ChannelRouter router, ILogger<LeanKernelHostedService> logger)
    {
        _router = router;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        _logger.LogInformation("LeanKernel engine starting...");
        await _router.StartAsync(ct);
        _logger.LogInformation("LeanKernel engine running. Waiting for messages.");

        try
        {
            await Task.Delay(Timeout.Infinite, ct);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("LeanKernel engine shutting down...");
        }

        await _router.StopAsync(ct);
    }
}
