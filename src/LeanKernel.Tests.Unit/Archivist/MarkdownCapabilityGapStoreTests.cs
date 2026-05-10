using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using LeanKernel.Archivist.CapabilityGaps;
using LeanKernel.Core.Configuration;
using LeanKernel.Core.Models;

namespace LeanKernel.Tests.Unit.Archivist;

public sealed class MarkdownCapabilityGapStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task AppendAsync_PersistsGapToAgentCapabilityGapFile()
    {
        var store = CreateStore();
        var gap = CreateGap("missing_tool", "Needs a calendar integration.");

        await store.AppendAsync(gap, CancellationToken.None);

        var content = await File.ReadAllTextAsync(Path.Combine(_root, "main", "capability-gaps.md"));
        Assert.Contains("missing_tool", content);
        Assert.Contains("Needs a calendar integration.", content);
    }

    [Fact]
    public async Task ReadPromptSectionAsync_ReturnsPromptReadySummary()
    {
        var store = CreateStore();
        await store.AppendAsync(CreateGap("missing_tool", "Needs a calendar integration."), CancellationToken.None);

        var promptSection = await store.ReadPromptSectionAsync(CancellationToken.None);

        Assert.NotNull(promptSection);
        Assert.Contains("Known Capability Gaps", promptSection);
        Assert.Contains("Needs a calendar integration.", promptSection);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private MarkdownCapabilityGapStore CreateStore()
    {
        var config = Options.Create(new LeanKernelConfig
        {
            Agents = new AgentsConfig { BasePath = _root }
        });

        return new MarkdownCapabilityGapStore(config, NullLogger<MarkdownCapabilityGapStore>.Instance);
    }

    private static CapabilityGap CreateGap(string gapType, string description) => new()
    {
        TurnEventId = "turn-1",
        SessionId = "session-1",
        UserRequest = "Please add this to my calendar.",
        GapType = gapType,
        Description = description
    };
}
