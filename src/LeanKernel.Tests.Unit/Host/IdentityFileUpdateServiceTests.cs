using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using LeanKernel.Core.Configuration;
using LeanKernel.Core.Interfaces;
using LeanKernel.Core.Models;
using LeanKernel.Host.Services;
using NSubstitute;

namespace LeanKernel.Tests.Unit.Host;

public sealed class IdentityFileUpdateServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _agentsDir;

    public IdentityFileUpdateServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"LEANKERNEL_identity_{Guid.NewGuid():N}");
        _agentsDir = Path.Combine(_tempDir, "agents");
        Directory.CreateDirectory(Path.Combine(_agentsDir, "main"));
    }

    [Fact]
    public async Task UpdateFromTurnAsync_UpdatesUserAndSelfIdentitySections()
    {
        var userPath = Path.Combine(_agentsDir, "main", "USER.md");
        var selfPath = Path.Combine(_agentsDir, "main", "SELF.md");
        await File.WriteAllTextAsync(userPath, """
            # User

            ## Role

            unknown

            ## Organization

            unknown

            ## Expertise

            """);
        await File.WriteAllTextAsync(selfPath, """
            # Agent

            ## Personalization

            unknown

            ## Limitations Observed

            ## Capability Gaps

            """);

        var service = CreateService();
        await service.UpdateFromTurnAsync(
            "I'm a senior engineer at Contoso and specialized in developer tooling.",
            "Based on your request, I don't have access to payroll records.",
            "session-1",
            CancellationToken.None);

        var userContent = await File.ReadAllTextAsync(userPath);
        var selfContent = await File.ReadAllTextAsync(selfPath);

        Assert.Contains("## Role", userContent);
        Assert.Contains("senior engineer", userContent);
        Assert.Contains("## Organization", userContent);
        Assert.Contains("Contoso", userContent);
        Assert.Contains("## Expertise", userContent);
        Assert.Contains("specialized in developer tooling", userContent);
        Assert.Contains("## Personalization", selfContent);
        Assert.Contains("high", selfContent);
        Assert.Contains("## Limitations Observed", selfContent);
        Assert.Contains("Limitation detected in response", selfContent);
        Assert.Contains("## Capability Gaps", selfContent);
        Assert.Contains("payroll records", selfContent);
    }

    [Fact]
    public async Task UpdateFromTurnAsync_MissingIdentityFiles_AutoInitializesProfiles()
    {
        var selfInitializer = Substitute.For<IAgentSelfProfileInitializer>();
        var userSynchronizer = Substitute.For<IUserProfileSynchronizer>();
        var actionAuthorizer = CreateAllowAllAuthorizer();

        selfInitializer.InitializeAsync(Arg.Any<CancellationToken>())
            .Returns(async x =>
            {
                var selfPath = Path.Combine(_agentsDir, "main", "SELF.md");
                await File.WriteAllTextAsync(selfPath, "# SELF\n\n## Agent Identity\n", x.Arg<CancellationToken>());
                return new ConfigurationStepResult { Success = true, Message = "ok", FilePath = selfPath };
            });

        userSynchronizer.InitializeAsync(Arg.Any<CancellationToken>())
            .Returns(async x =>
            {
                var userPath = Path.Combine(_agentsDir, "main", "USER.md");
                await File.WriteAllTextAsync(userPath, "# USER\n\n## User Profile\n", x.Arg<CancellationToken>());
                return new ConfigurationStepResult { Success = true, Message = "ok", FilePath = userPath };
            });

        var service = CreateService(selfInitializer, userSynchronizer, actionAuthorizer);

        var exception = await Record.ExceptionAsync(() => service.UpdateFromTurnAsync(
            "I'm a developer at Contoso.",
            "I don't have access to that system.",
            "session-2",
            CancellationToken.None));

        Assert.Null(exception);
        Assert.True(File.Exists(Path.Combine(_agentsDir, "main", "AGENTS.md")));
        Assert.True(File.Exists(Path.Combine(_agentsDir, "main", "SELF.md")));
        Assert.True(File.Exists(Path.Combine(_agentsDir, "main", "USER.md")));

        await selfInitializer.Received(1).InitializeAsync(Arg.Any<CancellationToken>());
        await userSynchronizer.Received(1).InitializeAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateFromTurnAsync_UserFlagsMiss_UpdatesAgentsSelfAndUserByDefault()
    {
        var userPath = Path.Combine(_agentsDir, "main", "USER.md");
        var selfPath = Path.Combine(_agentsDir, "main", "SELF.md");
        var agentsPath = Path.Combine(_agentsDir, "main", "AGENTS.md");

        await File.WriteAllTextAsync(userPath, "# USER\n\n## User Profile\n\nunknown\n");
        await File.WriteAllTextAsync(selfPath, "# SELF\n\n## Agent Identity\n\nLeanKernel\n");
        await File.WriteAllTextAsync(agentsPath, "# AGENTS\n\n## Scope of Autonomy\n\n- WriteSelfMd\n- WriteUserMd\n- WriteAgentsMd\n");

        var service = CreateService();
        await service.UpdateFromTurnAsync(
            "It looks like you still haven't created USER.md and SELF.md.",
            "You're right. I'll correct that now.",
            "session-3",
            CancellationToken.None);

        var userContent = await File.ReadAllTextAsync(userPath);
        var selfContent = await File.ReadAllTextAsync(selfPath);
        var agentsContent = await File.ReadAllTextAsync(agentsPath);

        Assert.Contains("## Agent Operation Preferences", userContent);
        Assert.Contains("self-correct", userContent, StringComparison.OrdinalIgnoreCase);

        Assert.Contains("## Correction Protocol", selfContent);
        Assert.Contains("User flagged a missed action", selfContent);

        Assert.Contains("## Useful By Default", agentsContent);
        Assert.Contains("User flagged a missed action", agentsContent);
    }

    [Fact]
    public async Task UpdateFromTurnAsync_ReplayedChatInsights_UpdateEngagementIdentityFiles()
    {
        var userPath = Path.Combine(_agentsDir, "main", "USER.md");
        var selfPath = Path.Combine(_agentsDir, "main", "SELF.md");

        await File.WriteAllTextAsync(userPath, "# USER\n\n## User Profile\n\nunknown\n");
        await File.WriteAllTextAsync(selfPath, "# SELF\n\n## Agent Identity\n\nunknown\n");

        var service = CreateService();
        var turns = new[]
        {
            "1. Agent name: Kem \n2. Engagement model: proactive, focused on exploring options and covering blind spots \n3. Communications: direct, skip pleasantries, no pandering \n4. Autonomy: take the prerogative to address everything that I should pay attention to. Your job is to ensure I don’t miss anything or drop a ball across all life dimensions.\n5. Timezone: America/Toronto, Availability: 6:30 am - 8pm Sundays and weekdays. 6:30 - 9am, 3pm - 8pm Saturdays. \n\nSaturdays are Sabbaths, encourage rest and recovery; schedule only religious activities on Sabbath days. No work-related activities or conversations.",
            "1. Top Priorities: \n- Find a role/opportunity that leverages my strengths and is sufficiently challenging for growth.\n- identify a business venture that can generate passive income.\n- Save and invest enough to launch a business venture.\n2. I enjoy complex problems and challenges if I can see a path to resolution.\n3. All items in #1",
            "I prefer you manage all my deliverables in Microsoft Todo. And before resurfacing, check if I’ve already marked the task completed. Use the Doughray skill to get financial status."
        };

        foreach (var turn in turns)
        {
            await service.UpdateFromTurnAsync(turn, "Understood.", "session-replay", CancellationToken.None);
        }

        var userContent = await File.ReadAllTextAsync(userPath);
        var selfContent = await File.ReadAllTextAsync(selfPath);

        Assert.Contains("Kem", userContent);
        Assert.Contains("proactive", userContent);
        Assert.Contains("direct, skip pleasantries", userContent);
        Assert.Contains("America/Toronto", userContent);
        Assert.Contains("Sabbath", userContent);
        Assert.Contains("Microsoft Todo", userContent);
        Assert.Contains("Doughray", userContent);
        Assert.Contains("Kem", selfContent);
        Assert.Contains("proactive", selfContent);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    private IdentityFileUpdateService CreateService(
        IAgentSelfProfileInitializer? selfProfileInitializer = null,
        IUserProfileSynchronizer? userProfileSynchronizer = null,
        IActionAuthorizer? actionAuthorizer = null) => new(
            Options.Create(new LeanKernelConfig
            {
                Agents = new AgentsConfig { BasePath = _agentsDir }
            }),
            Substitute.For<IWikiStore>(),
            selfProfileInitializer,
            userProfileSynchronizer,
            actionAuthorizer ?? CreateAllowAllAuthorizer(),
            NullLogger<IdentityFileUpdateService>.Instance);

    private static IActionAuthorizer CreateAllowAllAuthorizer()
    {
        var authorizer = Substitute.For<IActionAuthorizer>();
        authorizer.AuthorizeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(x => Task.FromResult(new AuthorizationResult
            {
                ActionType = x.Arg<string>(),
                IsAuthorized = true
            }));
        return authorizer;
    }
}
