using NSubstitute;

namespace LeanKernel.Tests.Unit.Thinker;

public sealed class AgentRuntimeTests
{
    [Fact]
    public async Task RunTurnAsync_DelegatesToThinker()
    {
        var message = new LeanKernelMessage
        {
            Id = "msg-1",
            ChannelId = "test",
            SenderId = "user",
            Content = "hello"
        };
        var thinker = Substitute.For<IThinkerService>();
        thinker.ProcessAsync(message, Arg.Any<CancellationToken>())
            .Returns("hi");
        var runtime = new LeanKernel.Thinker.AgentRuntime(thinker);

        var response = await runtime.RunTurnAsync(message, CancellationToken.None);

        Assert.Equal("hi", response);
        await thinker.Received(1).ProcessAsync(message, Arg.Any<CancellationToken>());
    }
}
