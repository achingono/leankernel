using Microsoft.Extensions.AI;

namespace LeanKernel.Logic.Memory;

/// <summary>
/// Provides an <see cref="IChatClient"/> implementation that always fails because the small model is disabled.
/// </summary>
public sealed class DisabledChatClient : IChatClient
{
    /// <inheritdoc />
    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("Small model is disabled.");
    }

    /// <inheritdoc />
    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("Small model is disabled.");
    }

    /// <inheritdoc />
    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        return null;
    }

    /// <inheritdoc />
    public void Dispose() => GC.SuppressFinalize(this);
}
