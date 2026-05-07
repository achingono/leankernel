using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using LeanKernel.Core.Configuration;
using LeanKernel.Core.Interfaces;
using LeanKernel.Core.Models;
using LeanKernel.Host.Controllers;
using LeanKernel.Host.Services;
using NSubstitute;
using Xunit;

namespace LeanKernel.Tests.Unit.Host;

public class ChatControllerTests
{
    private static InboundAttachmentInputProcessor CreateAttachmentProcessor()
    {
        var extractor = Substitute.For<IAttachmentTextExtractionService>();
        extractor.ExtractTextAsync(Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<byte[]>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
                Task.FromResult(InboundAttachmentTextExtractor.TryExtractText(
                    callInfo.ArgAt<string?>(0),
                    callInfo.ArgAt<string?>(1),
                    callInfo.ArgAt<byte[]>(2))));
        extractor.CanExtractText(Arg.Any<string?>(), Arg.Any<string?>())
            .Returns(callInfo => InboundAttachmentTextExtractor.CanExtractText(
                callInfo.ArgAt<string?>(0),
                callInfo.ArgAt<string?>(1)));

        return new InboundAttachmentInputProcessor(extractor);
    }

    private static (MessageQueueService, TimeBoundaryService) CreateDependencies()
    {
        var rules = new EngagementRules
        {
            TimeBoundaries = new TimeBoundaries
            {
                ActiveHoursStart = null,
                ActiveHoursEnd = null
            }
        };
        var timeBoundaryLogger = Substitute.For<ILogger<TimeBoundaryService>>();
        var timeBoundary = new TimeBoundaryService(rules, timeBoundaryLogger);
        
        var messageQueueLogger = Substitute.For<ILogger<MessageQueueService>>();
        var messageQueue = new MessageQueueService(timeBoundary, messageQueueLogger);
        
        return (messageQueue, timeBoundary);
    }

    [Fact]
    public async Task ListSessions_ReturnsOkWithSessions()
    {
        var sessions = Substitute.For<ISessionStore>();
        sessions.ListSessionsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<string>>(["s1", "s2"]));

        var (messageQueue, timeBoundary) = CreateDependencies();
        var controller = new ChatController(sessions, Substitute.For<IThinkerService>(), messageQueue, timeBoundary, CreateAttachmentProcessor());
        var result = await controller.ListSessions(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var list = Assert.IsAssignableFrom<IReadOnlyList<string>>(ok.Value);
        Assert.Equal(2, list.Count);
    }

    [Fact]
    public async Task GetSession_ReturnsHistory()
    {
        var sessions = Substitute.For<ISessionStore>();
        sessions.GetHistoryAsync("s1", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new List<ConversationTurn>
            {
                new() { Role = "user", Content = "Hi", Timestamp = DateTimeOffset.UtcNow }
            }));

        var (messageQueue, timeBoundary) = CreateDependencies();
        var controller = new ChatController(sessions, Substitute.For<IThinkerService>(), messageQueue, timeBoundary, CreateAttachmentProcessor());
        var result = await controller.GetSession("s1", CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task SendMessage_ProcessesMessage()
    {
        var thinker = Substitute.For<IThinkerService>();
        thinker.ProcessAsync(Arg.Any<LeanKernelMessage>(), Arg.Any<CancellationToken>())
            .Returns("Hello back!");

        var (messageQueue, timeBoundary) = CreateDependencies();
        var controller = new ChatController(Substitute.For<ISessionStore>(), thinker, messageQueue, timeBoundary, CreateAttachmentProcessor());
        var request = new ChatMessageRequest { Content = "Hello" };
        var result = await controller.SendMessage(request, CancellationToken.None);

        // Should accept either Ok (processed) or Accepted (queued) depending on time configuration
        Assert.True(result is OkObjectResult or AcceptedResult);
    }

    [Fact]
    public async Task SendMessage_WithUrgentFlag_ProcessesOrQueues()
    {
        var thinker = Substitute.For<IThinkerService>();
        thinker.ProcessAsync(Arg.Any<LeanKernelMessage>(), Arg.Any<CancellationToken>())
            .Returns("Urgent response");

        var (messageQueue, timeBoundary) = CreateDependencies();
        var controller = new ChatController(Substitute.For<ISessionStore>(), thinker, messageQueue, timeBoundary, CreateAttachmentProcessor());
        
        var request = new ChatMessageRequest 
        { 
            Content = "Urgent message",
            IsUrgent = true
        };
        
        var result = await controller.SendMessage(request, CancellationToken.None);
        
        // Urgent messages should return a valid result
        Assert.NotNull(result);
        Assert.True(result is OkObjectResult or AcceptedResult);
    }

    [Fact]
    public async Task SendMessage_WithAttachments_FormatsPromptContent()
    {
        var thinker = Substitute.For<IThinkerService>();
        thinker.ProcessAsync(Arg.Any<LeanKernelMessage>(), Arg.Any<CancellationToken>())
            .Returns("Attachment response");

        var (messageQueue, timeBoundary) = CreateDependencies();
        var controller = new ChatController(Substitute.For<ISessionStore>(), thinker, messageQueue, timeBoundary, CreateAttachmentProcessor());

        var request = new ChatMessageRequest
        {
            Content = "Please review these notes.",
            Attachments =
            [
                new InboundAttachmentInput
                {
                    FileName = "meeting-notes.md",
                    ContentType = "text/markdown",
                    Text = "# Notes\n- Hiring plan"
                }
            ]
        };

        await controller.SendMessage(request, CancellationToken.None);

        await thinker.Received(1).ProcessAsync(
            Arg.Is<LeanKernelMessage>(message =>
                message.Content.Contains("Received 1 attachment:")
                && message.Content.Contains("meeting-notes.md")
                && message.Metadata["web:attachment_count"] == "1"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendMessage_InvalidAttachmentBase64_ReturnsBadRequest()
    {
        var (messageQueue, timeBoundary) = CreateDependencies();
        var controller = new ChatController(
            Substitute.For<ISessionStore>(),
            Substitute.For<IThinkerService>(),
            messageQueue,
            timeBoundary,
            CreateAttachmentProcessor());

        var result = await controller.SendMessage(new ChatMessageRequest
        {
            Content = "bad file",
            Attachments =
            [
                new InboundAttachmentInput
                {
                    FileName = "notes.txt",
                    ContentType = "text/plain",
                    Base64Content = "not-base64"
                }
            ]
        }, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
    }
}
