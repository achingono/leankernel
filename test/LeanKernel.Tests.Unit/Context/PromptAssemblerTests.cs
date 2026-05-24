using FluentAssertions;
using LeanKernel.Abstractions.Models;
using LeanKernel.Context;
using Microsoft.Extensions.Logging.Abstractions;

namespace LeanKernel.Tests.Unit.Context;

public class PromptAssemblerTests
{
    [Fact]
    public void AssembleSystemMessage_returns_system_prompt_when_context_is_empty()
    {
        var assembler = CreateAssembler();

        var systemMessage = assembler.AssembleSystemMessage(new ConversationContext
        {
            SystemPrompt = "Base policy"
        });

        systemMessage.Should().Be("Base policy");
    }

    [Fact]
    public void AssembleSystemMessage_uses_stable_section_order()
    {
        var assembler = CreateAssembler();
        var systemMessage = assembler.AssembleSystemMessage(new ConversationContext
        {
            SystemPrompt = "Base policy",
            Identity = new IdentityContext
            {
                UserId = "user-1",
                PromptSegments =
                [
                    "### User Preferences (identity-user-default)\n- preferred_name: Alex"
                ],
            },
            Onboarding = new OnboardingResult
            {
                HasGaps = true,
                OnboardingDirective = "Continue answering the user's current request."
            },
            WikiFacts =
            [
                new RetrievalCandidate { Key = "wiki-1", Content = "Alice owns Atlas", Source = "wiki", Score = 0.9, TokenCount = 2 }
            ],
            RetrievedKnowledge =
            [
                new RetrievalCandidate { Key = "doc-1", Content = "Atlas shipped", Source = "gbrain", Score = 0.8, TokenCount = 2 }
            ],
            ActiveToolNames = ["search", "notes"]
        });

        systemMessage.Should().Contain("Base policy");
        systemMessage.Should().Contain("## Identity Context");
        systemMessage.Should().Contain("### User Preferences (identity-user-default)");
        systemMessage.Should().Contain("## Onboarding Guidance");
        systemMessage.Should().Contain("## Relevant Knowledge");
        systemMessage.Should().Contain("- [wiki] Alice owns Atlas");
        systemMessage.Should().Contain("## Retrieved Context");
        systemMessage.Should().Contain("- [gbrain:doc-1] Atlas shipped");
        systemMessage.Should().Contain("## Available Tools: search, notes");
        systemMessage.IndexOf("Base policy", StringComparison.Ordinal).Should().BeLessThan(systemMessage.IndexOf("## Identity Context", StringComparison.Ordinal));
        systemMessage.IndexOf("## Identity Context", StringComparison.Ordinal).Should().BeLessThan(systemMessage.IndexOf("## Onboarding Guidance", StringComparison.Ordinal));
        systemMessage.IndexOf("## Onboarding Guidance", StringComparison.Ordinal).Should().BeLessThan(systemMessage.IndexOf("## Relevant Knowledge", StringComparison.Ordinal));
        systemMessage.IndexOf("## Relevant Knowledge", StringComparison.Ordinal).Should().BeLessThan(systemMessage.IndexOf("## Retrieved Context", StringComparison.Ordinal));
        systemMessage.IndexOf("## Retrieved Context", StringComparison.Ordinal).Should().BeLessThan(systemMessage.IndexOf("## Available Tools", StringComparison.Ordinal));
    }

    [Fact]
    public void AssembleFullPrompt_appends_history_and_marks_compacted_turns()
    {
        var assembler = CreateAssembler();
        var fullPrompt = assembler.AssembleFullPrompt(new ConversationContext
        {
            SystemPrompt = "Base policy",
            History =
            [
                new ConversationTurn { Role = "user", Content = "What changed?" },
                new ConversationTurn { Role = "assistant", Content = "Earlier summary", IsCompacted = true },
            ]
        });

        fullPrompt.Should().Contain("Base policy");
        fullPrompt.Should().Contain("--- Conversation ---");
        fullPrompt.Should().Contain("User: What changed?");
        fullPrompt.Should().Contain("Assistant [compacted]: Earlier summary");
    }

    private static PromptAssembler CreateAssembler()
        => new(new SimpleTokenEstimator(), NullLogger<PromptAssembler>.Instance);
}
