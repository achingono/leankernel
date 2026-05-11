using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using LeanKernel.Core.Configuration;

namespace LeanKernel.Tests.Unit.Archivist;

public sealed class EngagementRulesProviderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"LeanKernel-engagement-{Guid.NewGuid():N}");

    [Fact]
    public async Task LoadAsync_AutonomyLists_StopAtSiblingSubsections()
    {
        var agentsDir = Path.Combine(_root, "main");
        Directory.CreateDirectory(agentsDir);
        await File.WriteAllTextAsync(Path.Combine(agentsDir, "AGENTS.md"), """
            ---
            version: 2
            ---

            # AGENTS.md

            ## Agent Personality

            **Tone:** direct

            ## Scope of Autonomy

            ### Can Do Without Asking

            - ReadFile
            - SearchFiles

            ### Must Ask Before

            - WriteFile
            - SendMessage

            ### Never Do

            - CommitSecrets
            - ExposeSecret

            ## Time Boundaries

            **Timezone:** UTC
            """);

        var provider = new EngagementRulesProvider(
            Options.Create(new LeanKernelConfig
            {
                Agents = new AgentsConfig { BasePath = _root }
            }),
            NullLogger<EngagementRulesProvider>.Instance);

        var rules = await provider.LoadAsync(CancellationToken.None);

        Assert.Equal(["ReadFile", "SearchFiles"], rules.Autonomy.CanDoWithoutAsking);
        Assert.Equal(["WriteFile", "SendMessage"], rules.Autonomy.MustAskBefore);
        Assert.Equal(["CommitSecrets", "ExposeSecret"], rules.Autonomy.NeverDo);
    }

    [Fact]
    public async Task LoadAsync_TimeBoundaries_ParseMarkdownValues()
    {
        var agentsDir = Path.Combine(_root, "main");
        Directory.CreateDirectory(agentsDir);
        await File.WriteAllTextAsync(Path.Combine(agentsDir, "AGENTS.md"), """
            # AGENTS.md

            ## Time Boundaries

            **Timezone:** UTC
            **Active Hours Start:** 0
            **Active Hours End:** 24

            ## Communication
            """);

        var provider = new EngagementRulesProvider(
            Options.Create(new LeanKernelConfig
            {
                Agents = new AgentsConfig { BasePath = _root }
            }),
            NullLogger<EngagementRulesProvider>.Instance);

        var rules = await provider.LoadAsync(CancellationToken.None);

        Assert.Equal("UTC", rules.TimeBoundaries.Timezone);
        Assert.Equal(0, rules.TimeBoundaries.ActiveHoursStart);
        Assert.Equal(24, rules.TimeBoundaries.ActiveHoursEnd);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
