namespace LeanKernel.Core.Interfaces;

/// <summary>
/// Optional capability for channels that can show a typing/processing indicator.
/// </summary>
public interface ITypingIndicatorChannel
{
    /// <summary>
    /// Starts a typing or processing indicator for the target recipient.
    /// </summary>
    ValueTask<IAsyncDisposable> BeginTypingAsync(string recipientId, CancellationToken ct);
}

/// <summary>
/// Reusable no-op async disposable for channels that don't expose typing state.
/// </summary>
public sealed class NoopAsyncDisposable : IAsyncDisposable
{
    /// <summary>
    /// Gets the shared no-op async disposable instance.
    /// </summary>
    public static NoopAsyncDisposable Instance { get; } = new();

    private NoopAsyncDisposable()
    {
    }

    /// <summary>
    /// Completes without releasing resources.
    /// </summary>
    /// <returns>The operation result.</returns>
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
