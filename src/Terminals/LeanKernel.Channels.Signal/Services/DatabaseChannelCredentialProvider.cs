using LeanKernel.Data;
using LeanKernel.Entities;

using Microsoft.EntityFrameworkCore;

namespace LeanKernel.Channels.Signal;

public sealed class DatabaseChannelCredentialProvider(
    IDbContextFactory<EntityContext> dbContextFactory,
    ILogger<DatabaseChannelCredentialProvider> logger) : IChannelCredentialProvider
{
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
