using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using LeanKernel.Core.Configuration;
using LeanKernel.Host.Services;
using LeanKernel.Thinker.Enhancement;

namespace LeanKernel.Tests.Unit.Thinker.Enhancement;

public sealed class EngagementFileMaintenanceResponseEnhancerTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _agentsDir;

    public EngagementFileMaintenanceResponseEnhancerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"LEANKERNEL_engagement_enhancer_{Guid.NewGuid():N}");
        _agentsDir = Path.Combine(_tempDir, "agents");
        Directory.CreateDirectory(_agentsDir);
    }

    [Fact]
    public async Task EnhanceResponseAsync_FileMaintenanceRequest_UpdatesAndVerifiesEngagementFiles()
    {
        var config = new LeanKernelConfig
        {
            Agents = new AgentsConfig { BasePath = _agentsDir }
        };
        var hostPaths = new LeanKernelHostPaths
        {
            DataDirectory = _tempDir,
            AgentsDirectory = _agentsDir,
            RuntimeConfigPath = Path.Combine(_tempDir, "runtime-settings.json"),
            OnboardingStatePath = Path.Combine(_tempDir, "onboarding-state.json"),
            LiteLlmConfigPath = Path.Combine(_tempDir, "litellm-config.yaml")
        };

        var wiki = Substitute.For<IWikiStore>();
        var authorizer = Substitute.For<IActionAuthorizer>();
        authorizer.AuthorizeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(x => Task.FromResult(new AuthorizationResult
            {
                ActionType = x.Arg<string>(),
                IsAuthorized = true
            }));

        var selfInitializer = new SelfConfigurationStep(
            hostPaths,
            NullLogger<SelfConfigurationStep>.Instance);
        var userSynchronizer = new UserConfigurationStep(
            hostPaths,
            wiki,
            NullLogger<UserConfigurationStep>.Instance);
        var identityUpdater = new IdentityFileUpdateService(
            Options.Create(config),
            wiki,
            selfInitializer,
            userSynchronizer,
            authorizer,
            NullLogger<IdentityFileUpdateService>.Instance);
        var enhancer = new EngagementFileMaintenanceResponseEnhancer(
            identityUpdater,
            Options.Create(config),
            NullLogger<EngagementFileMaintenanceResponseEnhancer>.Instance);

        var context = new ConversationContext
        {
            SystemPrompt = "test",
            History =
            [
                new ConversationTurn
                {
                    Role = "user",
                    Content = "Agent name: Kem\nEngagement model: proactive, focused on exploring options and covering blind spots\nCommunications: direct, skip pleasantries, no pandering\nTimezone: America/Toronto. Saturdays are Sabbaths.",
                    Timestamp = DateTimeOffset.UtcNow
                },
                new ConversationTurn
                {
                    Role = "assistant",
                    Content = "I will remember that.",
                    Timestamp = DateTimeOffset.UtcNow
                }
            ],
            WikiLeanKernels = [],
            RetrievedLeanKernels = [],
            ActiveToolNames = []
        };

        var result = await enhancer.EnhanceResponseAsync(
            "Update the AGENTS.md, USER.md, and SELF.md with insights gained so far.",
            "I've updated the files accordingly.",
            context,
            CancellationToken.None);

        Assert.Contains("Engagement files verified and updated", result);
        Assert.True(File.Exists(Path.Combine(_agentsDir, "main", "AGENTS.md")));
        Assert.True(File.Exists(Path.Combine(_agentsDir, "main", "SELF.md")));
        Assert.True(File.Exists(Path.Combine(_agentsDir, "main", "USER.md")));

        var userContent = await File.ReadAllTextAsync(Path.Combine(_agentsDir, "main", "USER.md"));
        var selfContent = await File.ReadAllTextAsync(Path.Combine(_agentsDir, "main", "SELF.md"));

        Assert.Contains("Kem", userContent);
        Assert.Contains("proactive", userContent);
        Assert.Contains("direct, skip pleasantries", userContent);
        Assert.Contains("America/Toronto", userContent);
        Assert.Contains("Sabbath", userContent);
        Assert.Contains("Kem", selfContent);
    }

    [Fact]
    public async Task EnhanceResponseAsync_GenericProceedOrPermissionLanguage_DoesNotRunMaintenance()
    {
        var identityUpdater = Substitute.For<IIdentityFileUpdateService>();
        var enhancer = new EngagementFileMaintenanceResponseEnhancer(
            identityUpdater,
            Options.Create(new LeanKernelConfig
            {
                Agents = new AgentsConfig { BasePath = _agentsDir }
            }),
            NullLogger<EngagementFileMaintenanceResponseEnhancer>.Instance);
        var context = new ConversationContext
        {
            SystemPrompt = "test",
            History = [],
            WikiLeanKernels = [],
            RetrievedLeanKernels = [],
            ActiveToolNames = []
        };
        var response = "Proceed by writing a test plan first.";

        var result = await enhancer.EnhanceResponseAsync(
            "How should I proceed with testing? Do I need permission to read USER.md?",
            response,
            context,
            CancellationToken.None);

        Assert.Equal(response, result);
        await identityUpdater.DidNotReceive().UpdateFromTurnAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EnhanceResponseAsync_SemanticCommunicationPreference_UsesClassifierToUpdateEngagementFiles()
    {
        var config = new LeanKernelConfig
        {
            Agents = new AgentsConfig { BasePath = _agentsDir }
        };
        var hostPaths = new LeanKernelHostPaths
        {
            DataDirectory = _tempDir,
            AgentsDirectory = _agentsDir,
            RuntimeConfigPath = Path.Combine(_tempDir, "runtime-settings.json"),
            OnboardingStatePath = Path.Combine(_tempDir, "onboarding-state.json"),
            LiteLlmConfigPath = Path.Combine(_tempDir, "litellm-config.yaml")
        };
        var wiki = Substitute.For<IWikiStore>();
        var authorizer = Substitute.For<IActionAuthorizer>();
        authorizer.AuthorizeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(x => Task.FromResult(new AuthorizationResult
            {
                ActionType = x.Arg<string>(),
                IsAuthorized = true
            }));
        var classifier = Substitute.For<IEngagementIntentClassifier>();
        classifier.ClassifyAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new EngagementIntentClassification(
                true,
                "communication",
                "communicate more directly",
                "User expressed a durable communication preference."));
        var identityUpdater = new IdentityFileUpdateService(
            Options.Create(config),
            wiki,
            new SelfConfigurationStep(hostPaths, NullLogger<SelfConfigurationStep>.Instance),
            new UserConfigurationStep(hostPaths, wiki, NullLogger<UserConfigurationStep>.Instance),
            authorizer,
            NullLogger<IdentityFileUpdateService>.Instance);
        var enhancer = new EngagementFileMaintenanceResponseEnhancer(
            identityUpdater,
            Options.Create(config),
            NullLogger<EngagementFileMaintenanceResponseEnhancer>.Instance,
            classifier);
        var context = new ConversationContext
        {
            SystemPrompt = "test",
            History = [],
            WikiLeanKernels = [],
            RetrievedLeanKernels = [],
            ActiveToolNames = []
        };

        var result = await enhancer.EnhanceResponseAsync(
            "I wish you would communicate more directly.",
            "Got it.",
            context,
            CancellationToken.None);

        Assert.Contains("Engagement files verified and updated", result);
        var userContent = await File.ReadAllTextAsync(Path.Combine(_agentsDir, "main", "USER.md"));
        Assert.Contains("communicate more directly", userContent);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }
}
