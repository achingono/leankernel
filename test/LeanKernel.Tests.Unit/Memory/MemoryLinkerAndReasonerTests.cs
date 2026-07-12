using FluentAssertions;
using LeanKernel.Logic.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LeanKernel.Tests.Unit.Memory;

public class MemoryLinkerAndReasonerTests
{
    [Fact]
    public void Linker_BuildsRankedLinks_WithExpectedReasons()
    {
        var linker = new MemoryPageLinker();
        var target = Snapshot(
            key: "facts/what/budget/a",
            text: "Jane approved the q4 budget in seattle",
            session: "s1",
            turn: "t1",
            primary: "what",
            explicitLinks: ["facts/who/jane/b"]);

        var candidateExplicit = Snapshot(
            key: "facts/who/jane/b",
            text: "Jane approved q4 budget",
            session: "s1",
            turn: "t1",
            primary: "who");

        var candidateDimension = Snapshot(
            key: "facts/what/budget/c",
            text: "Q4 budget approved in seattle by jane",
            session: "s2",
            turn: "t2",
            primary: "what");

        var links = linker.BuildLinks(
            target,
            [target, candidateExplicit, candidateDimension],
            new Dictionary<string, string?>(),
            primaryDimension: "what",
            secondaryDimensions: ["who"]);

        links.Should().HaveCount(2);
        links[0].TargetKey.Should().Be("facts/who/jane/b");
        links[0].Reasons.Should().Contain(["explicit-related", "same-session", "same-turn"]);
        links[1].Reasons.Should().Contain("same-dimension");
    }

    [Fact]
    public async Task GraphReasoner_FiltersAndCapsModelEdges()
    {
        var deterministic = new[]
        {
            new MemoryPageLink("facts/what/a/1", "related", 80, ["semantic-related"])
        };
        var target = Snapshot("facts/what/target/1", "Target fact", "s", "t", "what");
        var candidates = new[]
        {
            Snapshot("facts/what/a/1", "A", "s", "t", "what"),
            Snapshot("facts/what/a/2", "B", "s", "t", "what"),
            Snapshot("facts/what/a/3", "C", "s", "t", "what"),
            Snapshot("facts/what/a/4", "D", "s", "t", "what")
        };

        var json = """
        {
          "links": [
            { "targetKey": "facts/what/a/2", "relation": "semantic-related", "confidence": 0.95, "reasons": ["same-event"] },
            { "targetKey": "facts/what/a/3", "relation": "semantic-related", "confidence": 0.91, "reasons": ["same-event"] },
            { "targetKey": "facts/what/a/4", "relation": "semantic-related", "confidence": 0.89, "reasons": ["same-event"] },
            { "targetKey": "facts/what/missing/9", "relation": "semantic-related", "confidence": 0.99, "reasons": ["bad"] },
            { "targetKey": "facts/what/a/1", "relation": "semantic-related", "confidence": 0.20, "reasons": ["low"] }
          ]
        }
        """;

        var reasoner = new MemoryGraphReasoner(new StaticReasoningModel(json), NullLogger<MemoryGraphReasoner>.Instance);
        var result = await reasoner.RefineLinksAsync(target, new Dictionary<string, string?>(), deterministic, candidates, CancellationToken.None);

        result.Should().HaveCount(4);
        result.Count(link => link.Source == "llm").Should().Be(3);
        result.Any(link => link.TargetKey == "facts/what/missing/9").Should().BeFalse();
        result.Any(link => link.TargetKey == "facts/what/a/1" && link.Source == "llm").Should().BeFalse();
    }

    [Fact]
    public async Task GraphReasoner_InvalidJson_FallsBackToDeterministic()
    {
        var deterministic = new[]
        {
            new MemoryPageLink("facts/what/a/1", "related", 80, ["semantic-related"])
        };

        var reasoner = new MemoryGraphReasoner(new StaticReasoningModel("not json"), NullLogger<MemoryGraphReasoner>.Instance);
        var result = await reasoner.RefineLinksAsync(
            Snapshot("facts/what/target/1", "Target", "s", "t", "what"),
            new Dictionary<string, string?>(),
            deterministic,
            [Snapshot("facts/what/a/1", "A", "s", "t", "what")],
            CancellationToken.None);

        result.Should().BeEquivalentTo(deterministic);
    }

    private static MemoryPageSnapshot Snapshot(
        string key,
        string text,
        string? session,
        string? turn,
        string primary,
        IReadOnlyList<string>? explicitLinks = null)
    {
        return new MemoryPageSnapshot(
            key,
            text,
            text,
            text.ToLowerInvariant(),
            DateTimeOffset.UtcNow,
            new Dictionary<string, string>(),
            new Dictionary<string, string?>(),
            session,
            turn,
            explicitLinks ?? [],
            null,
            primary,
            [],
            []);
    }

    private sealed class StaticReasoningModel(string response) : IReasoningModel
    {
        public bool Enabled => true;

        public Task<string?> CompleteAsync(string systemPrompt, string userPrompt, int maxOutputTokens, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<string?>(response);
        }
    }
}
