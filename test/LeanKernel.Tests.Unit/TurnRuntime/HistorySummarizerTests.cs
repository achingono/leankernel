using FluentAssertions;
using LeanKernel.Logic.Configuration;
using LeanKernel.Logic.TurnRuntime;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace LeanKernel.Tests.Unit.TurnRuntime;

public class HistorySummarizerTests
{
    [Fact]
    public async Task SummarizeAsync_ReturnsTrimmedSummaryFromChatClient()
    {
        var service = new HistorySummarizer(
            new StaticChatClient("  summary text  "),
            Options.Create(new TurnPipelineSettings
            {
                SummarizationTemperature = 0.2,
                SummarizationMaxOutputTokens = 256
            }),
            Mock.Of<Microsoft.Extensions.Logging.ILogger<HistorySummarizer>>());

        var result = await service.SummarizeAsync(
            [new ChatMessage(ChatRole.User, "hello"), new ChatMessage(ChatRole.Assistant, "hi")],
            CancellationToken.None);

        result.Should().Be("summary text");
    }

    [Fact]
    public async Task SummarizeAsync_WhenChatClientFails_ReturnsNull()
    {
        var service = new HistorySummarizer(
            new ThrowingChatClient(new InvalidOperationException("boom")),
            Options.Create(new TurnPipelineSettings()),
            Mock.Of<Microsoft.Extensions.Logging.ILogger<HistorySummarizer>>());

        var result = await service.SummarizeAsync([new ChatMessage(ChatRole.User, "hello")], CancellationToken.None);

        result.Should().BeNull();
    }

    private sealed class StaticChatClient(string text) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, text)));
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
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

    private sealed class ThrowingChatClient(Exception exception) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            throw exception;
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            throw exception;
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
