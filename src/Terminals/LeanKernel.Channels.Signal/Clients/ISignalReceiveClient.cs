namespace LeanKernel.Channels.Signal;

/// <summary>
/// Receives payloads from signal-cli receive endpoints.
/// </summary>
public interface ISignalReceiveClient
{
    /// <summary>
    /// Receives a single raw payload from the configured endpoint.
    /// </summary>
    /// <param name="account">Signal account being received.</param>
    /// <param name="receiveUri">Receive endpoint URI.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The raw payload, or <c>null</c> when no message is available.</returns>
    Task<string?> ReceiveAsync(string account, Uri receiveUri, CancellationToken ct);
}