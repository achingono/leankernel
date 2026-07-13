using FluentAssertions;
using LeanKernel.Logic.Memory;
using Xunit;

namespace LeanKernel.Tests.Unit.Memory;

/// <summary>
/// Covers basic contract behavior for memory model record types.
/// </summary>
public class MemoryModelContractsTests
{
    /// <summary>
    /// Verifies record-based memory models expose their assigned values.
    /// </summary>
    [Fact]
    public void ModelRecords_ExposeExpectedValues()
    {
        var link = new MemoryPageLink("facts/x", "related", 50, ["same-session"], 0.8, "llm");
        var score = new MemoryDimensionScore("what", 100, "populated", "deterministic");
        var result = new MemoryPageNormalizationResult(
            "# Learned Fact",
            new Dictionary<string, string?> { ["What"] = "x" },
            ["Who"],
            "what",
            ["where"],
            [score],
            [link],
            "hybrid-llm",
            "facts/what/x/1");

        var snapshot = new MemoryPageSnapshot(
            "facts/what/x/1",
            "content",
            "fact",
            "fact",
            DateTimeOffset.UtcNow,
            new Dictionary<string, string>(),
            new Dictionary<string, string?>(),
            "s",
            "t",
            ["facts/what/y/2"],
            null,
            "what",
            ["who"],
            [link]);

        var dimReq = new DimensionExtractionRequest("fact", new Dictionary<string, string?>(), [], []);
        var dimRes = new DimensionExtractionResponse("what", ["who"], new Dictionary<string, string> { ["what"] = "reason" }, new Dictionary<string, IReadOnlyList<string>>());
        var graphReq = new GraphReasoningRequest("fact", new Dictionary<string, string?>(), [link], []);
        var edge = new ProposedEdge("facts/z", "semantic-related", 0.9, ["shared-subject"]);
        var graphRes = new GraphReasoningResponse([edge]);

        result.IsPartial.Should().BeTrue();
        snapshot.PrimaryDimension.Should().Be("what");
        dimReq.FactText.Should().Be("fact");
        dimRes.PrimaryDimension.Should().Be("what");
        graphReq.DeterministicLinks.Should().ContainSingle();
        graphRes.Links.Should().ContainSingle();
    }
}
