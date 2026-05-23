using Microsoft.Extensions.Options;
using LeanKernel.Archivist;
using LeanKernel.Core.Configuration;
using LeanKernel.Core.Interfaces;
using NSubstitute;

namespace LeanKernel.Tests.Unit.Archivist;

public sealed class SystemPromptBuilderTests
{
    [Fact]
    public async Task BuildAsync_IncludesConfiguredFilesystemLocations_WhenIdentityFilesAreMissing()
    {
        var root = CreateTempRoot();
        var config = CreateConfig(root);
        var builder = new SystemPromptBuilder(Options.Create(config));

        var prompt = await builder.BuildAsync(CancellationToken.None);

        Assert.Contains("## Configured filesystem locations", prompt);
        Assert.Contains($"Data root: {root}", prompt);
        Assert.Contains($"Agent identity folder: {Path.Combine(root, "agents", "main")}", prompt);
        Assert.Contains($"Agents base folder: {Path.Combine(root, "agents")}", prompt);
        Assert.Contains($"Knowledge documents folder: {Path.Combine(root, "agents", "main", "documents")}", prompt);
        Assert.Contains($"Wiki folder: {Path.Combine(root, "wiki")}", prompt);
        Assert.Contains("use file_search first", prompt);
    }

    [Fact]
    public async Task BuildAsync_IncludesConfiguredFilesystemLocations_WithIdentityFiles()
    {
        var root = CreateTempRoot();
        var config = CreateConfig(root);
        var agentDir = Path.Combine(config.Agents.BasePath, "main");
        Directory.CreateDirectory(agentDir);
        await File.WriteAllTextAsync(Path.Combine(agentDir, "SELF.md"), "custom self prompt");
        await File.WriteAllTextAsync(Path.Combine(agentDir, "USER.md"), "custom user prompt");
        var builder = new SystemPromptBuilder(Options.Create(config));

        var prompt = await builder.BuildAsync(CancellationToken.None);

        Assert.Contains("custom self prompt", prompt);
        Assert.Contains("custom user prompt", prompt);
        Assert.Contains("## Configured filesystem locations", prompt);
        Assert.Contains($"Agent identity folder: {agentDir}", prompt);
        Assert.Contains("AGENTS.md, SELF.md, and USER.md", prompt);
    }

    [Fact]
    public async Task BuildAsync_FiltersTransientUserSectionsAndInternalGapDetails()
    {
        var root = CreateTempRoot();
        var config = CreateConfig(root);
        var agentDir = Path.Combine(config.Agents.BasePath, "main");
        Directory.CreateDirectory(agentDir);

        var userContent = """
            # USER.md - User Profile & Preferences

            ## Verified Preferences and Priorities

            - Communication preference: direct, concise.

            ## Availability and Time Boundaries

            - My sermon presentation is scheduled for this coming Sabbath. Help me identify two songs.
            """;
        await File.WriteAllTextAsync(Path.Combine(agentDir, "SELF.md"), "custom self prompt");
        await File.WriteAllTextAsync(Path.Combine(agentDir, "USER.md"), userContent);

        var gapStore = Substitute.For<ICapabilityGapStore>();
        gapStore.ReadPromptSectionAsync(Arg.Any<CancellationToken>())
            .Returns("## Known Capability Gaps\n- Retry failed after 4 tries. (Connection refused (litellm:4000))");

        var builder = new SystemPromptBuilder(Options.Create(config), gapStore);
        var prompt = await builder.BuildAsync(CancellationToken.None);

        Assert.Contains("Communication preference: direct, concise.", prompt);
        Assert.DoesNotContain("Help me identify two songs", prompt);
        Assert.DoesNotContain("Connection refused", prompt);
        Assert.Contains("Do not disclose internal diagnostics", prompt);
    }

    private static LeanKernelConfig CreateConfig(string root) => new()
    {
        Agents = new AgentsConfig { BasePath = Path.Combine(root, "agents") },
        Wiki = new WikiConfig { BasePath = Path.Combine(root, "wiki") },
        Knowledge = new KnowledgeConfig { DocumentsPath = Path.Combine(root, "agents", "main", "documents") }
    };

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"leankernel-system-prompt-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        return root;
    }
}
