using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using LeanKernel.Core.Interfaces;
using LeanKernel.Core.Models;
using LeanKernel.Core.Enums;
using LeanKernel.Host.Services;

namespace LeanKernel.Tests.Unit.Host;

public sealed class UserConfigurationStepTests
{
    [Fact]
    public async Task SyncFromWikiAsync_FiltersTransientFacts()
    {
        var root = Path.Combine(Path.GetTempPath(), $"lk-user-config-step-{Guid.NewGuid():N}");
        try
        {
            var paths = new LeanKernelHostPaths
            {
                DataDirectory = root,
                AgentsDirectory = Path.Combine(root, "agents"),
                RuntimeConfigPath = Path.Combine(root, "runtime-settings.json"),
                OnboardingStatePath = Path.Combine(root, "onboarding-state.json")
            };

            var wiki = Substitute.For<IWikiStore>();
            wiki.QueryAsync(Arg.Any<WikiQuery>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<IReadOnlyList<WikiEntry>>(
                [
                    new WikiEntry
                    {
                        Id = "who-user-profile",
                        Dimension = WikiDimension.Who,
                        Subject = "User",
                        Facts =
                        [
                            new WikiFact { Claim = "User name is Alfero Chingono" },
                            new WikiFact { Claim = "Can you review this tomorrow?" }
                        ]
                    },
                    new WikiEntry
                    {
                        Id = "what-user-preferences",
                        Dimension = WikiDimension.What,
                        Subject = "User preferences",
                        Facts =
                        [
                            new WikiFact { Claim = "User prefers concise responses" },
                            new WikiFact { Claim = "Help me identify two songs for this coming Sabbath sermon." }
                        ]
                    }
                ]));

            var step = new UserConfigurationStep(paths, wiki, NullLogger<UserConfigurationStep>.Instance);
            await step.InitializeAsync(CancellationToken.None);

            var result = await step.SyncFromWikiAsync(CancellationToken.None);
            Assert.True(result.Success);

            var userPath = Path.Combine(paths.AgentsDirectory, "main", "USER.md");
            var content = await File.ReadAllTextAsync(userPath);

            Assert.Contains("User name is Alfero Chingono", content);
            Assert.Contains("User prefers concise responses", content);
            Assert.DoesNotContain("identify two songs", content, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("review this tomorrow", content, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
