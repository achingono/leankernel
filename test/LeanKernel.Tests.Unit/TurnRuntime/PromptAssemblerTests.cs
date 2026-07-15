using FluentAssertions;
using LeanKernel.Logic.Configuration;
using LeanKernel.Logic.TurnRuntime;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace LeanKernel.Tests.Unit.TurnRuntime;

public class PromptAssemblerTests
{
    private static IPermit CreatePermit()
    {
        var mock = new Mock<IPermit>();
        mock.Setup(p => p.UserId).Returns(Guid.NewGuid());
        mock.Setup(p => p.TenantId).Returns(Guid.NewGuid());
        mock.Setup(p => p.ChannelId).Returns(Guid.NewGuid());
        mock.Setup(p => p.HostName).Returns("localhost");
        mock.Setup(p => p.IsAuthenticated).Returns(true);
        return mock.Object;
    }

    private static TurnContext CreateContext()
    {
        return new TurnContext
        {
            Permit = CreatePermit(),
            UserMessage = "hello",
            ConversationId = "conv-1",
        };
    }

    [Fact]
    public async Task ExecuteAsync_NoAdmittedNoHistory_AddsOnlyUserMessage()
    {
        var assembler = new PromptAssembler(
            Options.Create(new AgentSettings { DefaultInstructions = "" }),
            Mock.Of<ILogger<PromptAssembler>>());

        var context = CreateContext();
        await assembler.ExecuteAsync(context);

        context.Prompt.Should().HaveCount(1);
        context.Prompt[0].Role.Should().Be(ChatRole.User);
        context.Prompt[0].Text.Should().Be("hello");
    }

    [Fact]
    public async Task ExecuteAsync_WithInstructions_AddsSystemMessage()
    {
        var assembler = new PromptAssembler(
            Options.Create(new AgentSettings
            {
                DefaultInstructions = "You are helpful."
            }),
            Mock.Of<ILogger<PromptAssembler>>());

        var context = CreateContext();
        await assembler.ExecuteAsync(context);

        context.Prompt[0].Role.Should().Be(ChatRole.System);
        context.Prompt[0].Text.Should().Be("You are helpful.");
    }

    [Fact]
    public async Task ExecuteAsync_WithAdmittedContext_AddsContextMessage()
    {
        var assembler = new PromptAssembler(
            Options.Create(new AgentSettings { DefaultInstructions = "" }),
            Mock.Of<ILogger<PromptAssembler>>());

        var context = CreateContext();
        context.Admitted.Add(new ContextItem
        {
            Source = "memory",
            Content = "User prefers dark mode",
            EstimatedTokens = 50,
            Score = 0.9,
        });

        await assembler.ExecuteAsync(context);

        context.Prompt.Should().HaveCount(2);
        context.Prompt[0].Role.Should().Be(ChatRole.User);
        context.Prompt[0].Text.Should().Contain("[memory]");
        context.Prompt[0].Text.Should().Contain("User prefers dark mode");
        context.Prompt[1].Text.Should().Be("hello");
    }

    [Fact]
    public async Task ExecuteAsync_WithHistory_AddsHistoryMessages()
    {
        var assembler = new PromptAssembler(
            Options.Create(new AgentSettings { DefaultInstructions = "" }),
            Mock.Of<ILogger<PromptAssembler>>());

        var context = CreateContext();
        context.ShapedHistory.Add(new ChatMessage(ChatRole.User, "old question"));
        context.ShapedHistory.Add(new ChatMessage(ChatRole.Assistant, "old answer"));

        await assembler.ExecuteAsync(context);

        context.Prompt.Should().HaveCount(3);
        context.Prompt[0].Text.Should().Be("old question");
        context.Prompt[1].Text.Should().Be("old answer");
        context.Prompt[2].Text.Should().Be("hello");
    }

    [Fact]
    public async Task ExecuteAsync_MultipleAdmittedContext_SortedBySourcePriority()
    {
        var assembler = new PromptAssembler(
            Options.Create(new AgentSettings { DefaultInstructions = "" }),
            Mock.Of<ILogger<PromptAssembler>>());

        var context = CreateContext();
        context.Admitted.Add(new ContextItem
        {
            Source = "retrieval",
            Content = "RAG result",
            EstimatedTokens = 50,
            Score = 0.8,
        });
        context.Admitted.Add(new ContextItem
        {
            Source = "memory",
            Content = "Memory fact",
            EstimatedTokens = 50,
            Score = 0.9,
        });

        await assembler.ExecuteAsync(context);

        context.Prompt.Should().HaveCount(2);
        context.Prompt[0].Text.Should().Contain("[memory]");
        context.Prompt[0].Text.Should().Contain("[retrieval]");
    }
}
