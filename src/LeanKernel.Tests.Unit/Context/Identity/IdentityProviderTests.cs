using FluentAssertions;
using LeanKernel.Abstractions.Configuration;
using LeanKernel.Abstractions.Interfaces;
using LeanKernel.Abstractions.Models;
using LeanKernel.Context.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace LeanKernel.Tests.Unit.Context.Identity;

public class IdentityProviderTests
{
    [Fact]
    public async Task LoadIdentityAsync_parses_frontmatter_and_builds_prompt_segments()
    {
        var knowledge = new Mock<IKnowledgeService>(MockBehavior.Strict);
        knowledge
            .Setup(service => service.GetPageAsync("identity-agent-main", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new KnowledgePage
            {
                Key = "identity-agent-main",
                Content = "---\nid: identity-agent-main\ncommunication_style:\n  value: concise\n  confidence: 0.8\n  source: seed\n---\nHelpful coding agent"
            });
        knowledge
            .Setup(service => service.GetPageAsync("identity-user-default", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new KnowledgePage
            {
                Key = "identity-user-default",
                Content = "---\npreferred_name:\n  value: Alex\n  confidence: 0.9\ntimezone:\n  value: UTC+2\n  confidence: 0.7\n---\nPrimary user preferences"
            });

        var provider = new IdentityProvider(
            knowledge.Object,
            Options.Create(new IdentityConfig()),
            NullLogger<IdentityProvider>.Instance);

        var result = await provider.LoadIdentityAsync("user-1");

        result.UserId.Should().Be("user-1");
        result.AgentProfile.Should().NotBeNull();
        result.UserPreferences.Should().NotBeNull();
        result.AgentProfile!.Fields["communication_style"].Value.Should().Be("concise");
        result.UserPreferences!.Fields["preferred_name"].Value.Should().Be("Alex");
        result.UserPreferences.Fields["timezone"].Confidence.Should().Be(0.7);
        result.PromptSegments.Should().HaveCount(2);
        result.PromptSegments[0].Should().Contain("Agent Profile");
        result.PromptSegments[1].Should().Contain("preferred_name: Alex");
        result.OverallConfidence.Should().BeApproximately(0.8, 0.001);

        knowledge.VerifyAll();
    }

    [Fact]
    public async Task LoadIdentityAsync_handles_missing_and_unstructured_pages()
    {
        var knowledge = new Mock<IKnowledgeService>(MockBehavior.Strict);
        knowledge
            .Setup(service => service.GetPageAsync("identity-agent-main", It.IsAny<CancellationToken>()))
            .ReturnsAsync((KnowledgePage?)null);
        knowledge
            .Setup(service => service.GetPageAsync("identity-user-default", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new KnowledgePage
            {
                Key = "identity-user-default",
                Content = "Loves concise responses and prefers morning updates."
            });

        var provider = new IdentityProvider(
            knowledge.Object,
            Options.Create(new IdentityConfig()),
            NullLogger<IdentityProvider>.Instance);

        var result = await provider.LoadIdentityAsync("user-1");

        result.AgentProfile.Should().BeNull();
        result.UserPreferences.Should().NotBeNull();
        result.UserPreferences!.Fields.Should().BeEmpty();
        result.PromptSegments.Should().ContainSingle();
        result.PromptSegments[0].Should().Contain("Summary: Loves concise responses");
        result.OverallConfidence.Should().Be(0.5);

        knowledge.VerifyAll();
    }
}
