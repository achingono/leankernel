using LeanKernel.Core.Models;

namespace LeanKernel.Tests.Unit.CoreModels;

public sealed class IdentityDurabilityHeuristicsTests
{
    [Theory]
    [InlineData("User prefers concise responses and direct communication.", true)]
    [InlineData("User can speak Mandarin and Spanish fluently.", true)]
    [InlineData("User has diabetes and monitors diet consistently.", true)]
    [InlineData("User observes Sabbath on Saturdays.", true)]
    [InlineData("Can you review this tomorrow?", false)]
    [InlineData("Help me prepare a presentation for next week.", false)]
    [InlineData("Schedule a reminder at 3:00 PM.", false)]
    [InlineData("My event is on 2026-06-01.", false)]
    [InlineData("Meeting is Dec 25, 2026.", false)]
    [InlineData("this is short", false)]
    public void IsDurableFact_ClassifiesExpectedCases(string value, bool expected)
    {
        var actual = IdentityDurabilityHeuristics.IsDurableFact(value);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void IsTransientInstruction_InvertsDurableClassifier()
    {
        const string durable = "User prefers explanation-first responses and concise summaries.";
        const string transient = "Please remind me tomorrow morning.";

        Assert.False(IdentityDurabilityHeuristics.IsTransientInstruction(durable));
        Assert.True(IdentityDurabilityHeuristics.IsTransientInstruction(transient));
    }
}
