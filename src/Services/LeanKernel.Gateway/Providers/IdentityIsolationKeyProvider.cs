using LeanKernel;
using Microsoft.Agents.AI.Hosting;

namespace LeanKernel.Gateway.Providers;

/// <summary>
/// Resolves the session isolation key from the current request's <see cref="IPermit"/>.
/// Authenticated users are isolated by <c>TenantId|ChannelId|UserId</c>.
/// Anonymous users are isolated by <c>TenantId|ChannelId|UserId|SessionId</c> where
/// UserId is the resolved persisted guest user.
/// </summary>
public sealed class IdentityIsolationKeyProvider(IPermit permit) : SessionIsolationKeyProvider
{
    /// <inheritdoc />
    public override ValueTask<string?> GetSessionIsolationKeyAsync(CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;
        var subjectKey = permit.UserId.ToString();

        // Anonymous users add SessionId as an additional isolation dimension
        if (!permit.IsAuthenticated)
        {
            var sessionId = permit.SessionId ?? throw new InvalidOperationException(
                "Session is required for anonymous isolation. Ensure session middleware is enabled.");
            subjectKey = $"{permit.UserId}|{sessionId}";
        }

        var key = $"{permit.TenantId}|{permit.ChannelId}|{subjectKey}";
        return ValueTask.FromResult<string?>(key);
    }
}
