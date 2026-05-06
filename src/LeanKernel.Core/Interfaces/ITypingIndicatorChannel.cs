namespace LeanKernel.Core.Interfaces;

/// <summary>
/// Optional capability for channels that can show a typing/processing indicator.
/// </summary>
public interface ITypingIndicatorChannel
{
    ValueTask<IAsyncDisposable> BeginTypingAsync(string recipientId, CancellationToken ct);
}

/// <summary>
/// Reusable no-op async disposable for channels that don't expose typing state.
/// </summary>
public sealed class NoopAsyncDisposable : IAsyncDisposable
{
    public static NoopAsyncDisposable Instance { get; } = new();

    private NoopAsyncDisposable()
    {
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
