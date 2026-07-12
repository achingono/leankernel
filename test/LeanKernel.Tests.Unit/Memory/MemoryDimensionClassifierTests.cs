using FluentAssertions;
using LeanKernel.Logic.Memory;
using Xunit;

namespace LeanKernel.Tests.Unit.Memory;

public class MemoryDimensionClassifierTests
{
    [Fact]
    public async Task ActionCentricFact_DefaultsToWhatPrimary()
    {
        var classifier = new MemoryDimensionClassifier(new StubReasoningModel());
        var snapshot = new MemoryPageSnapshot(
            "k",
            "",
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

    [Fact]
    public void KeyBuilder_EmitsScopeRelativeFactKey()
    {
        var keyBuilder = new MemoryPageKeyBuilder();
        var key = keyBuilder.BuildScopeRelativeKey("what", "Q4 budget approval", "Q4 budget approval", DateTimeOffset.Parse("2026-07-10T12:00:00Z"));

        key.Should().StartWith("facts/what/q4-budget-approval/");
        key.Should().NotStartWith("memory/");
    }
}

file sealed class StubReasoningModel : IReasoningModel
{
    public bool Enabled => false;

    public Task<string?> CompleteAsync(string systemPrompt, string userPrompt, int maxOutputTokens, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<string?>(null);
    }
}
