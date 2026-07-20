namespace LeanKernel.Channels.Teams.Services;

/// <summary>Provides channel credential resolution for Teams senders.</summary>
public interface IChannelCredentialProvider
{
    /// <summary>Resolves the bearer token for the given sender identifier.</summary>
    /// <param name="senderId">The sender identifier to resolve.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The bearer token, or an empty string if no binding is found.</returns>
    Task<string> ResolveBearerTokenAsync(string senderId, CancellationToken ct);
}