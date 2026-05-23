using LeanKernel.Core.Models;

namespace LeanKernel.Tests.Unit.Thinker;

public class PromptAssemblerTests
{
    [Fact]
    public void Assemble_IncludesSystemPrompt()
    {
        var assembler = new LeanKernel.Thinker.PromptAssembler(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<LeanKernel.Thinker.PromptAssembler>.Instance);

        var context = CreateMinimalContext("You are LeanKernel.");
        var result = assembler.Assemble(context);

        Assert.Contains("You are LeanKernel.", result);
    }

    [Fact]
    public void Assemble_IncludesWikiLeanKernels()
    {
        var assembler = new LeanKernel.Thinker.PromptAssembler(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<LeanKernel.Thinker.PromptAssembler>.Instance);

        var context = CreateMinimalContext("system") with
        {
            WikiLeanKernels =
            [
                new RelevanceScore { EntryId = "w1", Content = "[Who:Alice] Project manager", EstimatedTokens = 5, Score = 0.9 }
            ]
        };

        var result = assembler.Assemble(context);
        Assert.Contains("[Who:Alice] Project manager", result);
        Assert.Contains("Wiki", result);
    }

    [Fact]
    public void Assemble_MarksCompactedTurns()
    {
        var assembler = new LeanKernel.Thinker.PromptAssembler(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<LeanKernel.Thinker.PromptAssembler>.Instance);

        var context = CreateMinimalContext("system") with
        {
            History =
            [
                new ConversationTurn { Role = "user", Content = "old msg", IsCompacted = true },
                new ConversationTurn { Role = "assistant", Content = "old reply", IsCompacted = false }
            ]
        };

        var result = assembler.Assemble(context);
        Assert.Contains("[compacted]", result);
    }

    private static ConversationContext CreateMinimalContext(string systemPrompt) =>
        new()
        {
            SystemPrompt = systemPrompt,
            History = [],
            WikiLeanKernels = [],
            RetrievedLeanKernels = [],
            ActiveToolNames = [],
            EstimatedTotalTokens = 0
        };
}
