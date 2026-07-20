namespace Microsoft.EntityFrameworkCore;

using LeanKernel.Data;

/// <summary>
/// Extension methods for <see cref="IDbContextFactory{TContext}"/>.
/// </summary>
public static class IDbContextFactoryExtensions
{
    /// <summary>
    /// Resolves a bearer token for a channel sender binding from the database.
    /// </summary>
    /// <param name="dbContextFactory">The EF Core context factory.</param>
    /// <param name="senderId">The sender's identifier (subject).</param>
    /// <param name="issuer">The identity provider issuer.</param>
    /// <param name="channelName">The channel name.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// A tuple containing the first matching bearer token and the total number of matches found.
    /// </returns>
    public static async Task<(string Token, int MatchCount)> ResolveSenderAsync(
        this IDbContextFactory<EntityContext> dbContextFactory,
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