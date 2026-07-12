using LeanKernel;
using Microsoft.Agents.AI.Hosting;

namespace LeanKernel.Gateway.Identity;

/// <summary>
/// Resolves the session isolation key from the current request's <see cref="IPermit"/>.
/// Authenticated users are isolated by <c>TenantId|ChannelId|UserId</c>.
/// Anonymous users add <c>SessionId</c> as an additional dimension.
/// </summary>
public sealed class IdentityIsolationKeyProvider(IPermit permit) : SessionIsolationKeyProvider
{
    public override ValueTask<string?> GetSessionIsolationKeyAsync(CancellationToken ct = default)
    {
        var subjectKey = permit.IsAuthenticated
            ? permit.UserId.ToString()
            : permit.SessionId ?? throw new InvalidOperationException(
                "Session is required for anonymous isolation. Ensure session middleware is enabled.");

        var key = $"{permit.TenantId}|{permit.ChannelId}|{subjectKey}";
        return ValueTask.FromResult<string?>(key);
    }
}
