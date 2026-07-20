using LeanKernel.Data;

using Microsoft.EntityFrameworkCore;

namespace LeanKernel.Channels.Common.Credentials;

/// <summary>
/// Resolves bearer tokens for channel sender bindings from the database.
/// </summary>
public static class ChannelSenderBindingTokenResolver
{
    /// <summary>
    /// Resolves a bearer token for the given sender identity and channel.
    /// </summary>
    /// <param name="dbContextFactory">Factory for creating <see cref="EntityContext"/> instances.</param>
    /// <param name="senderId">The sender's identifier (subject).</param>
    /// <param name="issuer">The identity provider issuer.</param>
    /// <param name="channelName">The channel name.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// A tuple containing the first matching bearer token and the total number of matches found.
    /// </returns>
    public static async Task<(string Token, int MatchCount)> ResolveAsync(
        IDbContextFactory<EntityContext> dbContextFactory,
        string senderId,
        string issuer,
        string channelName,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(dbContextFactory);

        if (string.IsNullOrWhiteSpace(senderId)
            || string.IsNullOrWhiteSpace(issuer)
            || string.IsNullOrWhiteSpace(channelName))
        {
            return (string.Empty, 0);
        }

        await using var context = await dbContextFactory.CreateDbContextAsync(ct);

        var matches = await context.ChannelSenderBindings
            .AsNoTracking()
            .Where(binding => binding.IsActive
                              && binding.Issuer == issuer
                              && binding.Subject == senderId
                              && binding.Channel.Name == channelName
                              && !string.IsNullOrWhiteSpace(binding.BearerToken))
            .Select(binding => binding.BearerToken)
            .Take(2)
            .ToListAsync(ct);

        return (matches.FirstOrDefault() ?? string.Empty, matches.Count);
    }
}