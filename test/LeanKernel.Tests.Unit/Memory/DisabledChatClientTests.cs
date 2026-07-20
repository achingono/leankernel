using FluentAssertions;

using LeanKernel.Logic.Memory;

using Xunit;

namespace LeanKernel.Tests.Unit.Memory;

public class DisabledChatClientTests
{
    [Fact]
    public async Task GetResponseAsync_Throws()
    {
        var client = new DisabledChatClient();
        var act = async () => await client.GetResponseAsync([], null, CancellationToken.None);
        await act.Should().ThrowAsync<NotSupportedException>();
    }

    [Fact]
    public void GetStreamingResponseAsync_Throws()
    {
        var client = new DisabledChatClient();
        var act = () => client.GetStreamingResponseAsync([], null, CancellationToken.None)
            .GetAsyncEnumerator().MoveNextAsync().AsTask();
        act.Should().ThrowAsync<NotSupportedException>();
    }

    [Fact]
    public void GetService_ReturnsNull()
    {
        var client = new DisabledChatClient();
        client.GetService(typeof(object)).Should().BeNull();
    }

    [Fact]
    public void Dispose_DoesNotThrow()
    {
        var client = new DisabledChatClient();
        var act = () => client.Dispose();
        act.Should().NotThrow();
    }
}