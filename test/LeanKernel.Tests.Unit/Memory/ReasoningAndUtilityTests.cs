using FluentAssertions;
using LeanKernel.Logic.Configuration;
using LeanKernel.Logic.Memory;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LeanKernel.Tests.Unit.Memory;

public class ReasoningAndUtilityTests
{
    [Fact]
    public async Task ReasoningModel_Disabled_ReturnsNull()
    {
        var model = new ReasoningModel(
            new FakeChatClient(new ChatMessage(ChatRole.Assistant, "ok")),
            new SmallModelSettings { Enabled = false },
            NullLogger<ReasoningModel>.Instance);

        var response = await model.CompleteAsync("sys", "usr", 100, CancellationToken.None);
        response.Should().BeNull();
    }

    [Fact]
    public async Task ReasoningModel_MapsChatResponseText()
    {
        var model = new ReasoningModel(
            new FakeChatClient(new ChatMessage(ChatRole.Assistant, "{\"ok\":true}")),
            new SmallModelSettings { Enabled = true, TimeoutSeconds = 5, MaxOutputTokens = 16, MaxConcurrency = 1 },
            NullLogger<ReasoningModel>.Instance);

        var response = await model.CompleteAsync("sys", "usr", 100, CancellationToken.None);
        response.Should().Be("{\"ok\":true}");
    }

    [Fact]
    public async Task ReasoningModel_NotSupported_AndTimeout_ReturnNull()
    {
        var notSupported = new ReasoningModel(
            new ThrowingChatClient(new NotSupportedException("disabled")),
            new SmallModelSettings { Enabled = true, TimeoutSeconds = 1 },
            NullLogger<ReasoningModel>.Instance);
        var timeout = new ReasoningModel(
            new ThrowingChatClient(new OperationCanceledException()),
            new SmallModelSettings { Enabled = true, TimeoutSeconds = 1 },
            NullLogger<ReasoningModel>.Instance);

        (await notSupported.CompleteAsync("s", "u", 8, CancellationToken.None)).Should().BeNull();
        (await timeout.CompleteAsync("s", "u", 8, CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public void DisabledChatClient_ThrowsOnCalls()
    {
        var client = new DisabledChatClient();

        var act = () => client.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")], cancellationToken: CancellationToken.None);
        act.Should().ThrowAsync<NotSupportedException>();
        client.GetService(typeof(object)).Should().BeNull();
    }

    [Fact]
    public void MemoryPageFields_NormalizeDimension_DefaultsToWhat()
    {
        MemoryPageFields.NormalizeDimension("WHO").Should().Be("who");
        MemoryPageFields.NormalizeDimension("unknown").Should().Be("what");
        MemoryPageFields.FiveWOneH.Should().ContainInOrder("Who", "What", "When", "Where", "Why", "How");
    }

    private sealed class FakeChatClient(ChatMessage responseMessage) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new ChatResponse(responseMessage));
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            return AsyncEnumerable.Empty<ChatResponseUpdate>();
        }

        public object? GetService(Type serviceType, object? serviceKey = null)
        {
            return null;
        }

        public void Dispose()
        {
        }
    }

    private sealed class ThrowingChatClient(Exception ex) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            return Task.FromException<ChatResponse>(ex);
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            return AsyncEnumerable.Empty<ChatResponseUpdate>();
        }

        public object? GetService(Type serviceType, object? serviceKey = null)
        {
            return null;
        }

        public void Dispose()
        {
        }
    }
}
