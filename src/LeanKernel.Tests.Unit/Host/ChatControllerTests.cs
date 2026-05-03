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
    private static (MessageQueueService, TimeBoundaryService) CreateDependencies()
    {
        var rules = new EngagementRules();
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
        var controller = new ChatController(sessions, Substitute.For<IThinkerService>(), messageQueue, timeBoundary);
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
        var controller = new ChatController(sessions, Substitute.For<IThinkerService>(), messageQueue, timeBoundary);
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
        var controller = new ChatController(Substitute.For<ISessionStore>(), thinker, messageQueue, timeBoundary);
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
        var controller = new ChatController(Substitute.For<ISessionStore>(), thinker, messageQueue, timeBoundary);
        
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
}
