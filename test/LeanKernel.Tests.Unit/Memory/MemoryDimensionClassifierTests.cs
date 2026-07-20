using FluentAssertions;

using LeanKernel.Logic.Memory;

using Xunit;

namespace LeanKernel.Tests.Unit.Memory;

/// <summary>
/// Covers deterministic and LLM-assisted memory dimension classification.
/// </summary>
public class MemoryDimensionClassifierTests
{
    /// <summary>
    /// Verifies action-heavy facts default to the what dimension.
    /// </summary>
    [Fact]
    public async Task ActionCentricFact_DefaultsToWhatPrimary()
    {
        var classifier = new MemoryDimensionClassifier(new StubReasoningModel());
        var snapshot = new MemoryPageSnapshot(
            "k",
            string.Empty,
            "Jane approved the Q4 budget in Seattle",
            "jane approved the q4 budget in seattle",
            DateTimeOffset.UtcNow,
            new Dictionary<string, string>(),
            new Dictionary<string, string?>(),
            "s",
            "t",
            [],
            null,
            "what",
            [],
            []);

        var fields = new Dictionary<string, string?>
        {
            ["Who"] = "Jane",
            ["What"] = "Jane approved the Q4 budget in Seattle",
            ["When"] = "last week",
            ["Where"] = "Seattle",
            ["Why"] = null,
            ["How"] = null
        };

        var result = await classifier.ClassifyAsync(snapshot, fields, ["Why", "How"], [], CancellationToken.None);

        result.PrimaryDimension.Should().Be("what");
        result.SecondaryDimensions.Should().Contain("who");
        result.Source.Should().Be("deterministic");
    }

    /// <summary>
    /// Verifies fact keys remain scope-relative.
    /// </summary>
    [Fact]
    public void KeyBuilder_EmitsScopeRelativeFactKey()
    {
        var keyBuilder = new MemoryPageKeyBuilder();
        var key = keyBuilder.BuildScopeRelativeKey("what", "Q4 budget approval", "Q4 budget approval", DateTimeOffset.Parse("2026-07-10T12:00:00Z"));

        key.Should().StartWith("facts/what/q4-budget-approval/");
        key.Should().NotStartWith("memory/");
    }

    /// <summary>
    /// Verifies valid LLM refinements override ambiguous deterministic output.
    /// </summary>
    [Fact]
    public async Task AmbiguousPage_UsesLlmRefinement_WhenValidJsonReturned()
    {
        var modelJson = """
        {
          "primaryDimension": "who",
          "secondaryDimensions": ["where", "when"],
          "dimensionRationales": { "who": "named actor" },
          "normalizedDimensionValues": { "who": ["Jane Doe"] }
        }
        """;
        var classifier = new MemoryDimensionClassifier(new StubReasoningModel(true, modelJson));
        var snapshot = CreateSnapshot("someone did something");
        var fields = new Dictionary<string, string?>
        {
            ["Who"] = "someone",
            ["What"] = "something",
            ["When"] = null,
            ["Where"] = null,
            ["Why"] = null,
            ["How"] = null
        };

        var result = await classifier.ClassifyAsync(snapshot, fields, ["When", "Where", "Why", "How"], [], CancellationToken.None);

        result.PrimaryDimension.Should().Be("who");
        result.SecondaryDimensions.Should().ContainInOrder("where", "when");
        result.Source.Should().Be("hybrid-llm");
        result.DimensionScores.Should().Contain(score => score.Source == "llm-refined");
    }

    /// <summary>
    /// Verifies invalid LLM output falls back to deterministic classification.
    /// </summary>
    [Fact]
    public async Task AmbiguousPage_InvalidJson_FallsBackToDeterministic()
    {
        var classifier = new MemoryDimensionClassifier(new StubReasoningModel(true, "not-json"));
        var snapshot = CreateSnapshot("someone did something");
        var fields = new Dictionary<string, string?>
        {
            ["Who"] = "someone",
            ["What"] = "somehow",
            ["When"] = null,
            ["Where"] = null,
            ["Why"] = null,
            ["How"] = null
        };

        var result = await classifier.ClassifyAsync(snapshot, fields, ["When", "Where", "Why", "How"], [], CancellationToken.None);

        result.Source.Should().Be("deterministic");
        result.PrimaryDimension.Should().Be("what");
    }

    /// <summary>
    /// Verifies the LLM is skipped when the page is not ambiguous.
    /// </summary>
    [Fact]
    public async Task NonAmbiguousPage_DoesNotInvokeLlm()
    {
        var model = new StubReasoningModel(true, "{\"primaryDimension\":\"who\"}");
        var classifier = new MemoryDimensionClassifier(model);
        var snapshot = CreateSnapshot("Jane approved Q4 budget in Seattle yesterday");
        var fields = new Dictionary<string, string?>
        {
            ["Who"] = "Jane Doe",
            ["What"] = "approved Q4 budget",
            ["When"] = "2026-07-01",
            ["Where"] = "Seattle office",
            ["Why"] = "planning",
            ["How"] = "review"
        };

        var result = await classifier.ClassifyAsync(snapshot, fields, [], [], CancellationToken.None);

        result.Source.Should().Be("deterministic");
        model.CallCount.Should().Be(0);
    }

    /// <summary>
    /// Creates a minimal snapshot for classification tests.
    /// </summary>
    private static MemoryPageSnapshot CreateSnapshot(string factText)
    {
        return new MemoryPageSnapshot(
            "k",
            factText,
            factText,
            factText.ToLowerInvariant(),
            DateTimeOffset.UtcNow,
            new Dictionary<string, string>(),
            new Dictionary<string, string?>(),
            "s",
            "t",
            [],
            null,
            "what",
            [],
            []);
    }
}

/// <summary>
/// Captures reasoning model calls for classifier tests.
/// </summary>
/// <param name="enabled">Whether the model reports itself as enabled.</param>
/// <param name="response">The completion text to return.</param>
file sealed class StubReasoningModel(bool enabled = false, string? response = null) : IReasoningModel
{
    public int CallCount { get; private set; }

    public bool Enabled => enabled;

    /// <inheritdoc />
    public Task<string?> CompleteAsync(string systemPrompt, string userPrompt, int maxOutputTokens, CancellationToken cancellationToken = default)
    {
        CallCount++;
        return Task.FromResult(response);
    }
}