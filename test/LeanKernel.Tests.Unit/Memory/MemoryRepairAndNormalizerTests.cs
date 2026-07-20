using FluentAssertions;

using LeanKernel.Logic.Memory;

using Microsoft.Extensions.Logging.Abstractions;

using Xunit;

namespace LeanKernel.Tests.Unit.Memory;

/// <summary>
/// Covers memory field repair and normalization behavior.
/// </summary>
public class MemoryRepairAndNormalizerTests
{
    /// <summary>
    /// Verifies repair only fills fields that are currently missing.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task RepairService_FillsOnlyMissingFields()
    {
        var json = """
        { "Who":"Jane", "What":"Should be ignored", "When":"2026-01-01", "Where":null, "Why":"Budget approval", "How":"Finance review" }
        """;

        var service = new MemoryFieldRepairService(new TestReasoningModel(json, enabled: true));
        var currentFields = new Dictionary<string, string?>
        {
            ["Who"] = null,
            ["What"] = "Existing what",
            ["When"] = null,
            ["Where"] = null,
            ["Why"] = null,
            ["How"] = null
        };

        var repaired = await service.TryRepairMissingFieldsAsync(
            SeedSnapshot(),
            currentFields,
            ["Who", "When", "Where", "Why", "How"],
            [],
            CancellationToken.None);

        repaired.Should().ContainKey("Who");
        repaired.Should().NotContainKey("What");
        repaired.Should().ContainKey("Why");
    }

    /// <summary>
    /// Verifies disabled or invalid repair output yields no repaired fields.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task RepairService_InvalidOrDisabled_ReturnsEmpty()
    {
        var disabled = new MemoryFieldRepairService(new TestReasoningModel("{ \"Who\": \"Jane\" }", enabled: false));
        var invalid = new MemoryFieldRepairService(new TestReasoningModel("garbage", enabled: true));

        var one = await disabled.TryRepairMissingFieldsAsync(SeedSnapshot(), new Dictionary<string, string?>(), ["Who"], [], CancellationToken.None);
        var two = await invalid.TryRepairMissingFieldsAsync(SeedSnapshot(), new Dictionary<string, string?>(), ["Who"], [], CancellationToken.None);

        one.Should().BeEmpty();
        two.Should().BeEmpty();
    }

    /// <summary>
    /// Verifies normalization produces canonical content and a scope-relative key.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task Normalizer_ProducesCanonicalContentAndScopeRelativeKey()
    {
        var reasoning = new TestReasoningModel(null, enabled: false);
        var classifier = new MemoryDimensionClassifier(reasoning);
        var linker = new MemoryPageLinker();
        var graphReasoner = new MemoryGraphReasoner(reasoning, NullLogger<MemoryGraphReasoner>.Instance);
        var repairService = new MemoryFieldRepairService(reasoning);
        var renderer = new MemoryPageRenderer();
        var keyBuilder = new MemoryPageKeyBuilder();
        var normalizer = new MemoryPageNormalizer(classifier, linker, graphReasoner, repairService, renderer, keyBuilder);

        var snapshot = new MemoryPageSnapshot(
            string.Empty,
            "# Learned Fact\n\nJane approved Q4 budget in Seattle\n",
            "Jane approved Q4 budget in Seattle",
            "jane approved q4 budget in seattle",
            DateTimeOffset.Parse("2026-07-10T12:00:00Z"),
            new Dictionary<string, string> { ["RecordedAt"] = "2026-07-10T12:00:00Z" },
            new Dictionary<string, string?> { ["Who"] = "Jane", ["Where"] = "Seattle" },
            "session-1",
            "turn-1",
            [],
            null,
            "what",
            [],
            []);

        var result = await normalizer.NormalizeAsync(snapshot, [], enableRepair: false, CancellationToken.None);

        result.ScopeRelativeKey.Should().StartWith("facts/what/");
        result.Content.Should().Contain("## 5W1H");
        result.Content.Should().Contain("## Dimensions");
        result.Content.Should().Contain("## Links");
        result.NormalizationMethod.Should().Be("deterministic");
        result.MissingFields.Should().Contain("Why");
    }

    /// <summary>
    /// Creates a minimal learned fact snapshot for repair tests.
    /// </summary>
    private static MemoryPageSnapshot SeedSnapshot()
    {
        return new MemoryPageSnapshot(
            "facts/what/test/1",
            "# Learned Fact\n\nFact text",
            "Fact text",
            "fact text",
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

    /// <summary>
    /// Returns a configurable reasoning response for repair tests.
    /// </summary>
    /// <param name="response">The completion text to return.</param>
    /// <param name="enabled">Whether the model reports itself as enabled.</param>
    private sealed class TestReasoningModel(string? response, bool enabled) : IReasoningModel
    {
        public bool Enabled => enabled;

        /// <inheritdoc />
        public Task<string?> CompleteAsync(string systemPrompt, string userPrompt, int maxOutputTokens, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(response);
        }
    }
}