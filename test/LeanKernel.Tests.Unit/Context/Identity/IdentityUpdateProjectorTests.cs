using FluentAssertions;
using LeanKernel.Abstractions.Configuration;
using LeanKernel.Abstractions.Enums;
using LeanKernel.Abstractions.Interfaces;
using LeanKernel.Abstractions.Models;
using LeanKernel.Context.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace LeanKernel.Tests.Unit.Context.Identity;

public class IdentityUpdateProjectorTests
{
    [Fact]
    public async Task EnhanceAsync_writes_allowlisted_updates_to_the_user_page()
    {
        var knowledge = new Mock<IKnowledgeService>(MockBehavior.Strict);
        string? persistedContent = null;

        knowledge
            .Setup(service => service.GetPageAsync("identity-user-default", It.IsAny<CancellationToken>()))
            .ReturnsAsync((KnowledgePage?)null);
        knowledge
            .Setup(service => service.PutPageAsync("identity-user-default", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, CancellationToken>((_, content, _) => persistedContent = content)
            .Returns(Task.CompletedTask);

        var projector = CreateProjector(knowledge.Object);
        var context = new ConversationContext
        {
            SystemPrompt = "Base policy",
            SessionId = "session-1",
            Identity = new IdentityContext
            {
                UserId = "user-1",
            },
        };

        var response = await projector.EnhanceAsync("I will call you Alex. timezone UTC+2.", context);

        response.Should().Be("I will call you Alex. timezone UTC+2.");
        persistedContent.Should().NotBeNull();
        persistedContent.Should().Contain("preferred_name:");
        persistedContent.Should().Contain("Alex");
        persistedContent.Should().Contain("timezone:");
        persistedContent.Should().Contain("UTC+2");
        persistedContent.Should().Contain("subject: user-1");

        knowledge.VerifyAll();
    }

    [Fact]
    public async Task EnhanceAsync_respects_the_allowed_field_whitelist()
    {
        var knowledge = new Mock<IKnowledgeService>(MockBehavior.Strict);
        var projector = CreateProjector(knowledge.Object, new IdentityConfig
        {
            AllowedIdentityFields = ["timezone"]
        });

        var response = await projector.EnhanceAsync(
            "I will call you Alex.",
            new ConversationContext
            {
                SystemPrompt = "Base policy",
                SessionId = "session-1",
                Identity = new IdentityContext { UserId = "user-1" },
            });

        response.Should().Be("I will call you Alex.");
        knowledge.Verify(service => service.GetPageAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        knowledge.Verify(service => service.PutPageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task EnhanceAsync_preserves_higher_confidence_values_and_records_conflicts()
    {
        var knowledge = new Mock<IKnowledgeService>(MockBehavior.Strict);
        var diagnostics = new Mock<IDiagnosticsSink>(MockBehavior.Strict);

        knowledge
            .Setup(service => service.GetPageAsync("identity-user-default", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new KnowledgePage
            {
                Key = "identity-user-default",
                Content = "---\npreferred_name:\n  value: Sam\n  confidence: 0.95\n---\nExisting profile"
            });
        diagnostics
            .Setup(sink => sink.RecordAsync(It.Is<DiagnosticEntry>(entry =>
                entry.SessionId == "session-1"
                && entry.Category == DiagnosticCategory.ResponseEnhancement.ToString()), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var projector = CreateProjector(knowledge.Object, diagnostics: diagnostics.Object);

        var response = await projector.EnhanceAsync(
            "I will call you Alex.",
            new ConversationContext
            {
                SystemPrompt = "Base policy",
                SessionId = "session-1",
                Identity = new IdentityContext { UserId = "user-1" },
            });

        response.Should().Be("I will call you Alex.");
        knowledge.Verify(service => service.PutPageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        diagnostics.VerifyAll();
        knowledge.VerifyAll();
    }

    private static IdentityUpdateProjector CreateProjector(
        IKnowledgeService knowledgeService,
        IdentityConfig? config = null,
        IDiagnosticsSink? diagnostics = null)
        => new(
            knowledgeService,
            Options.Create(config ?? new IdentityConfig()),
            NullLogger<IdentityUpdateProjector>.Instance,
            diagnostics);
}
