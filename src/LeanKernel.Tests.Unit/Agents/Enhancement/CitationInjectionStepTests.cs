using FluentAssertions;
using LeanKernel.Abstractions.Interfaces;
using LeanKernel.Abstractions.Models;
using LeanKernel.Agents.Enhancement;

namespace LeanKernel.Tests.Unit.Agents.Enhancement;

public class CitationInjectionStepTests
{
    [Fact]
    public async Task ExecuteAsync_injects_inline_citation_when_sentence_matches_retrieved_knowledge()
    {
        var step = new CitationInjectionStep();

        var result = await step.ExecuteAsync(new EnhancementStepInput
        {
            Response = "Atlas shipped with better diagnostics.",
            UserMessage = "What changed in Atlas?",
            RetrievedKnowledge =
            [
                new RetrievalCandidate
                {
                    Key = "atlas-release",
                    Content = "Atlas shipped with better diagnostics and rollout notes.",
                    Source = "gbrain",
                    Score = 0.91,
                    TokenCount = 8
                }
            ]
        });

        result.Modified.Should().BeTrue();
        result.Response.Should().Be("Atlas shipped with better diagnostics [source: atlas-release].");
    }

    [Fact]
    public async Task ExecuteAsync_is_idempotent_when_inline_citation_is_already_present()
    {
        var step = new CitationInjectionStep();

        var result = await step.ExecuteAsync(new EnhancementStepInput
        {
            Response = "Atlas shipped with better diagnostics [source: atlas-release].",
            UserMessage = "What changed in Atlas?",
            RetrievedKnowledge =
            [
                new RetrievalCandidate
                {
                    Key = "atlas-release",
                    Content = "Atlas shipped with better diagnostics and rollout notes.",
                    Source = "gbrain",
                    Score = 0.91,
                    TokenCount = 8
                }
            ]
        });

        result.Modified.Should().BeFalse();
        result.Response.Should().Be("Atlas shipped with better diagnostics [source: atlas-release].");
    }
}
