using LeanKernel.Data;
using LeanKernel.Entities;

using Microsoft.EntityFrameworkCore;

namespace LeanKernel.Channels.Teams.Services;

/// <summary>Resolves bearer tokens for Teams senders from the database channel sender bindings.</summary>
public sealed class DatabaseChannelCredentialProvider(
    IDbContextFactory<EntityContext> dbContextFactory,
    ILogger<DatabaseChannelCredentialProvider> logger) : IChannelCredentialProvider
{
    /// <summary>Resolves the bearer token for the given sender identifier.</summary>
    /// <param name="senderId">The sender identifier to resolve.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The bearer token, or an empty string if no binding is found.</returns>
    public async Task<string> ResolveBearerTokenAsync(string senderId, CancellationToken ct)
    {
        var (token, matchCount) = await dbContextFactory.ResolveSenderAsync(
            senderId,
            ChannelEntity.TeamsName,
            ChannelEntity.TeamsName,
            ct);

        if (matchCount > 1)
        {
            logger.LogWarning("Multiple active Teams bindings found for sender {SenderId}; refusing to select a token.", senderId);
            return string.Empty;
        }

        if (string.IsNullOrWhiteSpace(token))
        {
            logger.LogWarning("No Teams JWT token found for sender {SenderId} in ChannelSenderBindings.", senderId);
        }

        return token;
    }
}