namespace LeanKernel.Channels.Signal;

/// <summary>
/// Provides bearer token resolution for channel sender identities.
/// </summary>
public interface IChannelCredentialProvider
{
    /// <summary>
    /// Resolves a bearer token for the given sender identifier.
    /// </summary>
    /// <param name="senderId">The sender identifier to resolve credentials for.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The bearer token, or an empty string if no credential is available.</returns>
    Task<string> ResolveBearerTokenAsync(string senderId, CancellationToken ct);
}