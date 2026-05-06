using LeanKernel.Core.Models;

namespace LeanKernel.Commander.Adapters;

/// <summary>
/// Abstraction over Signal transport backends (jsonRpc subprocess or HTTP daemon).
/// </summary>
public interface ISignalAdapter : IAsyncDisposable
{
    /// <summary>Raised when a well-formed inbound message is received.</summary>
    event Action<SignalInboundMessage>? OnMessage;

    /// <summary>Raised on recoverable transport errors (log-level warnings).</summary>
    event Action<string>? OnError;

    /// <summary>Start the adapter and begin receiving messages.</summary>
    Task StartAsync(CancellationToken ct);

    /// <summary>Send an outbound text message to <paramref name="recipient"/>.</summary>
    Task SendMessageAsync(string recipient, string message, CancellationToken ct);

    /// <summary>
    /// Start or stop the typing indicator for <paramref name="recipient"/>.
    /// Fire-and-forget semantics — implementations should swallow transport errors.
    /// </summary>
    Task SendTypingAsync(string recipient, bool stop, CancellationToken ct);
}
