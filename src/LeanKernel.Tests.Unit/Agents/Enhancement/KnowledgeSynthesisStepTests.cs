using FluentAssertions;
using LeanKernel.Abstractions.Interfaces;
using LeanKernel.Abstractions.Models;
using LeanKernel.Agents.Enhancement;

namespace LeanKernel.Tests.Unit.Agents.Enhancement;

public class KnowledgeSynthesisStepTests
{
    [Fact]
    public async Task ExecuteAsync_appends_sources_note_when_retrieved_knowledge_matches_response()
    {
        var step = new KnowledgeSynthesisStep();

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
        result.Response.Should().EndWith("Sources: atlas-release");
    }

    [Fact]
    public async Task ExecuteAsync_is_idempotent_when_sources_note_is_already_present()
    {
        var step = new KnowledgeSynthesisStep();

        var result = await step.ExecuteAsync(new EnhancementStepInput
        {
            Response = "Atlas shipped with better diagnostics.\n\nSources: atlas-release",
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
        result.Response.Should().Be("Atlas shipped with better diagnostics.\n\nSources: atlas-release");
    }
}
