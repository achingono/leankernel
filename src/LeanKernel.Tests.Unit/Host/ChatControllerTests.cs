using Microsoft.AspNetCore.Mvc;
using LeanKernel.Core.Interfaces;
using LeanKernel.Core.Models;
using LeanKernel.Host.Controllers;
using NSubstitute;
using Xunit;

namespace LeanKernel.Tests.Unit.Host;

public class ChatControllerTests
{
    [Fact]
    public async Task ListSessions_ReturnsOkWithSessions()
    {
        var sessions = Substitute.For<ISessionStore>();
        sessions.ListSessionsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<string>>(["s1", "s2"]));

        var controller = new ChatController(sessions, Substitute.For<IThinkerService>());
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

        var controller = new ChatController(sessions, Substitute.For<IThinkerService>());
        var result = await controller.GetSession("s1", CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task SendMessage_ProcessesAndReturns()
    {
        var thinker = Substitute.For<IThinkerService>();
        thinker.ProcessAsync(Arg.Any<LeanKernelMessage>(), Arg.Any<CancellationToken>())
            .Returns("Hello back!");

        var controller = new ChatController(Substitute.For<ISessionStore>(), thinker);
        var request = new ChatMessageRequest { Content = "Hello" };
        var result = await controller.SendMessage(request, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<ChatMessageResponse>(ok.Value);
        Assert.Equal("Hello back!", response.Response);
        Assert.NotEmpty(response.MessageId);
    }

    [Fact]
    public async Task SendMessage_DefaultSenderId_IsWebUser()
    {
        var thinker = Substitute.For<IThinkerService>();
        thinker.ProcessAsync(Arg.Is<LeanKernelMessage>(m => m.SenderId == "web-user"), Arg.Any<CancellationToken>())
            .Returns("ok");

        var controller = new ChatController(Substitute.For<ISessionStore>(), thinker);
        var request = new ChatMessageRequest { Content = "Test" };
        await controller.SendMessage(request, CancellationToken.None);

        await thinker.Received(1).ProcessAsync(
            Arg.Is<LeanKernelMessage>(m => m.SenderId == "web-user"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendMessage_CustomSenderId_Used()
    {
        var thinker = Substitute.For<IThinkerService>();
        thinker.ProcessAsync(Arg.Any<LeanKernelMessage>(), Arg.Any<CancellationToken>())
            .Returns("ok");

        var controller = new ChatController(Substitute.For<ISessionStore>(), thinker);
        var request = new ChatMessageRequest { Content = "Test", SenderId = "custom-user" };
        await controller.SendMessage(request, CancellationToken.None);

        await thinker.Received(1).ProcessAsync(
            Arg.Is<LeanKernelMessage>(m => m.SenderId == "custom-user"),
            Arg.Any<CancellationToken>());
    }
}
