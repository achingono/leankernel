using LeanKernel.Data;
using LeanKernel.Entities;

using Microsoft.EntityFrameworkCore;

namespace LeanKernel.Channels.Signal;

/// <summary>
/// Resolves bearer tokens for Signal senders by querying the channel sender bindings table.
/// </summary>
public sealed class DatabaseChannelCredentialProvider(
    IDbContextFactory<EntityContext> dbContextFactory,
    ILogger<DatabaseChannelCredentialProvider> logger) : IChannelCredentialProvider
{
    /// <summary>
    /// Resolves a bearer token for the given sender by looking up the active binding in the database.
    /// </summary>
    /// <param name="senderId">The Signal sender identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The bearer token, or an empty string if no unique active binding is found.</returns>
    public async Task<string> ResolveBearerTokenAsync(string senderId, CancellationToken ct)
    {
        var (token, matchCount) = await dbContextFactory.ResolveSenderAsync(
            senderId,
            ChannelEntity.SignalName,
            ChannelEntity.SignalName,
            ct);

        if (matchCount > 1)
        {
            logger.LogWarning("Multiple active Signal bindings found for sender {SenderId}; refusing to select a token.", senderId);
            return string.Empty;
        }

        if (string.IsNullOrWhiteSpace(token))
        {
            logger.LogWarning("No Signal JWT token found for sender {SenderId} in ChannelSenderBindings.", senderId);
        }

        return token;
    }
}
