using LeanKernel.Data;
using Microsoft.EntityFrameworkCore;

namespace LeanKernel.Channels.Common.Credentials;

public static class ChannelSenderBindingTokenResolver
{
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
