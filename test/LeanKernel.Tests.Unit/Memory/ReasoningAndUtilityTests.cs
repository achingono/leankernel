using FluentAssertions;
using LeanKernel.Logic.Configuration;
using LeanKernel.Logic.Memory;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LeanKernel.Tests.Unit.Memory;

/// <summary>
/// Covers reasoning model and utility behavior used by the memory pipeline.
/// </summary>
public class ReasoningAndUtilityTests
{
    /// <summary>
    /// Verifies disabled reasoning returns no completion.
    /// </summary>
    [Fact]
    public async Task ReasoningModel_Disabled_ReturnsNull()
    {
        var model = new ReasoningModel(
            new FakeChatClient(new ChatMessage(ChatRole.Assistant, "ok")),
            new MemorySettings { Enabled = false },
            NullLogger<ReasoningModel>.Instance);

        var response = await model.CompleteAsync("sys", "usr", 100, CancellationToken.None);
        response.Should().BeNull();
    }

    /// <summary>
    /// Verifies chat response text is mapped into the reasoning completion.
    /// </summary>
    [Fact]
    public async Task ReasoningModel_MapsChatResponseText()
    {
        var model = new ReasoningModel(
            new FakeChatClient(new ChatMessage(ChatRole.Assistant, "{\"ok\":true}")),
            new MemorySettings { Enabled = true, TimeoutSeconds = 5, MaxOutputTokens = 16, MaxConcurrency = 1 },
            NullLogger<ReasoningModel>.Instance);

        var response = await model.CompleteAsync("sys", "usr", 100, CancellationToken.None);
        response.Should().Be("{\"ok\":true}");
    }

    /// <summary>
    /// Verifies unsupported clients and timeouts are treated as no result.
    /// </summary>
    [Fact]
    public async Task ReasoningModel_NotSupported_AndTimeout_ReturnNull()
    {
        var notSupported = new ReasoningModel(
            new ThrowingChatClient(new NotSupportedException("disabled")),
            new MemorySettings { Enabled = true, TimeoutSeconds = 1 },
            NullLogger<ReasoningModel>.Instance);
        var timeout = new ReasoningModel(
            new ThrowingChatClient(new OperationCanceledException()),
            new MemorySettings { Enabled = true, TimeoutSeconds = 1 },
            NullLogger<ReasoningModel>.Instance);

        (await notSupported.CompleteAsync("s", "u", 8, CancellationToken.None)).Should().BeNull();
        (await timeout.CompleteAsync("s", "u", 8, CancellationToken.None)).Should().BeNull();
    }

    /// <summary>
    /// Verifies the disabled chat client throws when invoked.
    /// </summary>
    [Fact]
    public void DisabledChatClient_ThrowsOnCalls()
    {
        var client = new DisabledChatClient();

        var act = () => client.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")], cancellationToken: CancellationToken.None);
        act.Should().ThrowAsync<NotSupportedException>();
        client.GetService(typeof(object)).Should().BeNull();
    }

    /// <summary>
    /// Verifies dimension normalization falls back to the what dimension.
    /// </summary>
    [Fact]
    public void MemoryPageFields_NormalizeDimension_DefaultsToWhat()
    {
        MemoryPageFields.NormalizeDimension("WHO").Should().Be("who");
        MemoryPageFields.NormalizeDimension("unknown").Should().Be("what");
        MemoryPageFields.FiveWOneH.Should().ContainInOrder("Who", "What", "When", "Where", "Why", "How");
    }

    /// <summary>
    /// Returns a fixed chat response for reasoning model tests.
    /// </summary>
    /// <param name="responseMessage">The chat message to return.</param>
    private sealed class FakeChatClient(ChatMessage responseMessage) : IChatClient
    {
        /// <inheritdoc />
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new ChatResponse(responseMessage));
        }

        /// <inheritdoc />
        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            return AsyncEnumerable.Empty<ChatResponseUpdate>();
        }

        /// <inheritdoc />
        public object? GetService(Type serviceType, object? serviceKey = null)
        {
            return null;
        }

        /// <inheritdoc />
        public void Dispose()
        {
        }
    }

    /// <summary>
    /// Throws a configured exception from chat completions.
    /// </summary>
    /// <param name="ex">The exception to throw.</param>
    private sealed class ThrowingChatClient(Exception ex) : IChatClient
    {
        /// <inheritdoc />
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            return Task.FromException<ChatResponse>(ex);
        }

        /// <inheritdoc />
        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            return AsyncEnumerable.Empty<ChatResponseUpdate>();
        }

        /// <inheritdoc />
        public object? GetService(Type serviceType, object? serviceKey = null)
        {
            return null;
        }

        /// <inheritdoc />
        public void Dispose()
        {
        }
    }
}
