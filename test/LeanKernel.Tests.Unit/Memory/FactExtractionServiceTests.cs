using FluentAssertions;
using LeanKernel.Logic.Memory;
using Xunit;

namespace LeanKernel.Tests.Unit.Memory;

public class FactExtractionServiceTests
{
    [Theory]
    [InlineData("[\"Fact A\", \"Fact B\"]", 2)]
    [InlineData("- Fact A\n- Fact B", 2)]
    [InlineData("Fact A\nFact B", 2)]
    [InlineData("[]", 0)]
    public void ParseFacts_HandlesFallbackShapes(string input, int count)
    {
        var facts = FactExtractionService.ParseFacts(input);
        facts.Should().HaveCount(count);
    }

    [Fact]
    public void TranscriptBuilder_IncludesUserHistoryAndAssistant()
    {
        var history = new[]
        {
            new Microsoft.Extensions.AI.ChatMessage(Microsoft.Extensions.AI.ChatRole.User, "u1"),
            new Microsoft.Extensions.AI.ChatMessage(Microsoft.Extensions.AI.ChatRole.Assistant, "a1")
        };

        var transcript = FactExtractionService.BuildConversationTranscript("hello", "answer", history);
        transcript.Should().Contain("User message:");
        transcript.Should().Contain("Recent history:");
        transcript.Should().Contain("Assistant response:");
    }
}
