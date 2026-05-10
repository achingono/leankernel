using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using LeanKernel.Archivist.Engagement;
using LeanKernel.Archivist.Identity;
using LeanKernel.Archivist.Sessions;
using LeanKernel.Archivist.Wiki;
using LeanKernel.Core.Configuration;
using LeanKernel.Core.Interfaces;
using LeanKernel.Core.Models;
using LeanKernel.Host.Services;
using LeanKernel.Thinker;
using LeanKernel.Thinker.Services;

namespace LeanKernel.Tests.Integration;

public sealed class SelfImprovementIdentityIntegrationTests : IDisposable
{
    private readonly string _root;
    private readonly string _dataDir;
    private readonly string _agentsDir;

    public SelfImprovementIdentityIntegrationTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"LEANKERNEL_integration_identity_{Guid.NewGuid():N}");
        _dataDir = Path.Combine(_root, "data");
        _agentsDir = Path.Combine(_dataDir, "agents");

        Directory.CreateDirectory(_dataDir);
        Directory.CreateDirectory(_agentsDir);
    }

    [Fact]
    public async Task PostTurnPipeline_IdentityRefresh_SelfHealsMissingIdentityFiles()
    {
        await using var provider = BuildServiceProvider();

        var postTurnPipeline = provider.GetRequiredService<PostTurnPipeline>();
        var queue = provider.GetRequiredService<TurnEventQueue>();
        var selfImprovementPipeline = provider.GetRequiredService<ISelfImprovementPipeline>();

        var sessionId = "signal_+10000000000";
        var message = CreateUserMessage("I am a software engineer.");
        var response = "Understood. I will keep that in mind.";

        await postTurnPipeline.CompleteAsync(
            sessionId,
            message,
            response,
            CreateContext(),
            errorType: null,
            errorMessage: null,
            CancellationToken.None);

        var turnEvent = await ReadSingleTurnEventAsync(queue, CancellationToken.None);
        Assert.NotNull(turnEvent);

        var result = await selfImprovementPipeline.ProcessAsync(turnEvent!, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Contains(result.StepResults, r => r.StepName == "identity-refresh" && r.Success);

        var selfPath = Path.Combine(_agentsDir, "main", "SELF.md");
        var userPath = Path.Combine(_agentsDir, "main", "USER.md");

        Assert.True(File.Exists(selfPath));
        Assert.True(File.Exists(userPath));

        var selfContent = await File.ReadAllTextAsync(selfPath);
        var userContent = await File.ReadAllTextAsync(userPath);

        Assert.Contains("# SELF.md", selfContent);
        Assert.Contains("# USER.md", userContent);
    }

    [Fact]
    public async Task PostTurnPipeline_IdentityRefresh_WhenUserFlagsMiss_UpdatesEngagementIdentityFiles()
    {
        var mainAgentDir = Path.Combine(_agentsDir, "main");
        Directory.CreateDirectory(mainAgentDir);

        var agentsPath = Path.Combine(mainAgentDir, "AGENTS.md");
        await File.WriteAllTextAsync(agentsPath, "# AGENTS\n\n## Scope of Autonomy\n\n- WriteSelfMd\n- WriteUserMd\n- WriteAgentsMd\n");

        await using var provider = BuildServiceProvider();

        var postTurnPipeline = provider.GetRequiredService<PostTurnPipeline>();
        var queue = provider.GetRequiredService<TurnEventQueue>();
        var selfImprovementPipeline = provider.GetRequiredService<ISelfImprovementPipeline>();

        var sessionId = "signal_+10000000001";
        var message = CreateUserMessage("It looks like you still haven't created USER.md and SELF.md.");
        var response = "You are right. I will correct that now.";

        await postTurnPipeline.CompleteAsync(
            sessionId,
            message,
            response,
            CreateContext(),
            errorType: null,
            errorMessage: null,
            CancellationToken.None);

        var turnEvent = await ReadSingleTurnEventAsync(queue, CancellationToken.None);
        Assert.NotNull(turnEvent);

        var result = await selfImprovementPipeline.ProcessAsync(turnEvent!, CancellationToken.None);

        Assert.True(result.Success);

        var selfPath = Path.Combine(mainAgentDir, "SELF.md");
        var userPath = Path.Combine(mainAgentDir, "USER.md");

        var selfContent = await File.ReadAllTextAsync(selfPath);
        var userContent = await File.ReadAllTextAsync(userPath);
        var agentsContent = await File.ReadAllTextAsync(agentsPath);

        Assert.Contains("## Correction Protocol", selfContent);
        Assert.Contains("User flagged a missed action", selfContent);

        Assert.Contains("## Agent Operation Preferences", userContent);
        Assert.Contains("self-correct", userContent, StringComparison.OrdinalIgnoreCase);

        Assert.Contains("## Useful By Default", agentsContent);
        Assert.Contains("User flagged a missed action", agentsContent);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private ServiceProvider BuildServiceProvider()
    {
        var services = new ServiceCollection();

        var config = new LeanKernelConfig
        {
            Wiki = new WikiConfig { BasePath = Path.Combine(_dataDir, "wiki") },
            Agents = new AgentsConfig { BasePath = _agentsDir },
            SelfImprovement = new SelfImprovementConfig
            {
                Enabled = true,
                QueuePath = "queue/learning",
                IdentityRefreshEnabled = true,
                RegexExtractionEnabled = false,
                LlmExtractionEnabled = false,
                FailureRecoveryEnabled = false
            }
        };

        var hostPaths = new LeanKernelHostPaths
        {
            DataDirectory = _dataDir,
            AgentsDirectory = _agentsDir,
            RuntimeConfigPath = Path.Combine(_dataDir, "runtime-settings.json"),
            OnboardingStatePath = Path.Combine(_dataDir, "onboarding-state.json"),
            LiteLlmConfigPath = Path.Combine(_dataDir, "litellm-config.yaml")
        };

        services.AddLogging();
        services.AddSingleton<IOptions<LeanKernelConfig>>(Options.Create(config));
        services.AddSingleton(hostPaths);

        services.AddSingleton<IWikiStore, WikiStore>();
        services.AddSingleton<ISessionStore>(sp =>
            new SessionStore(
                Path.Combine(_dataDir, "sessions"),
                sp.GetRequiredService<ILogger<SessionStore>>()));

        services.AddSingleton<IActionAuthorizer>(sp =>
            new ActionAuthorizer(config.Engagement, sp.GetRequiredService<ILogger<ActionAuthorizer>>()));

        services.AddSingleton<SelfConfigurationStep>();
        services.AddSingleton<IAgentSelfProfileInitializer>(sp => sp.GetRequiredService<SelfConfigurationStep>());

        services.AddSingleton<UserConfigurationStep>();
        services.AddSingleton<IUserProfileSynchronizer>(sp => sp.GetRequiredService<UserConfigurationStep>());

        services.AddSingleton<IIdentityFileUpdateService, IdentityFileUpdateService>();
        services.AddSingleton<ILearningStep, IdentityRefreshStep>();
        services.AddSingleton<ISelfImprovementPipeline, SelfImprovementPipeline>();

        services.AddSingleton<TurnEventQueue>();
        services.AddSingleton<ITurnEventSink>(sp => sp.GetRequiredService<TurnEventQueue>());
        services.AddSingleton<PostTurnPipeline>();

        return services.BuildServiceProvider();
    }

    private static LeanKernelMessage CreateUserMessage(string content) => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        ChannelId = "signal",
        SenderId = "user",
        Content = content,
        Timestamp = DateTimeOffset.UtcNow
    };

    private static ConversationContext CreateContext() => new()
    {
        SystemPrompt = "test",
        History = [],
        WikiLeanKernels = [],
        RetrievedLeanKernels = [],
        ActiveToolNames = []
    };

    private static async Task<TurnEvent?> ReadSingleTurnEventAsync(TurnEventQueue queue, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(3));

        await foreach (var item in queue.ReadAllAsync(timeout.Token))
        {
            return item;
        }

        return null;
    }
}
