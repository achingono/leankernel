#pragma warning disable SA1649 // File name should match first type name

using System.Security.Claims;

using LeanKernel.Entities;
using LeanKernel.Gateway.Requests;
using LeanKernel.Logic.Filters;

namespace LeanKernel.Gateway.Providers;

/// <summary>
/// Generic permit that decorates the request-scoped <see cref="IPermit"/> for entity-level
/// authorization. Delegates identity properties to the injected inner permit and implements
/// <see cref="Can"/> using the canonical claim contract: claim type <c>right</c>,
/// claim value <c>{Operation}:{EntityName}</c>.
/// </summary>
/// <typeparam name="TEntity">The entity type to authorize against.</typeparam>
public sealed class RequestContextPermit<TEntity> : IPermit<TEntity>
    where TEntity : class
{
    private readonly IPermit _inner;
    private readonly IPrincipalAccessor _principalAccessor;
    private readonly IScopePolicyProvider _scopePolicyProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="RequestContextPermit{TEntity}"/> class.
    /// </summary>
    /// <param name="inner">The resolved request-scoped permit.</param>
    /// <param name="principalAccessor">The principal accessor for claim evaluation.</param>
    /// <param name="scopePolicyProvider">Resolves entity scope policy for auth requirements.</param>
    public RequestContextPermit(
        IPermit inner,
        IPrincipalAccessor principalAccessor,
        IScopePolicyProvider scopePolicyProvider)
    {
        _inner = inner;
        _principalAccessor = principalAccessor;
        _scopePolicyProvider = scopePolicyProvider;
    }

    /// <inheritdoc />
    public Guid PersonId => _inner.PersonId;

    /// <inheritdoc />
    public Guid UserId => _inner.UserId;

    /// <inheritdoc />
    public Guid TenantId => _inner.TenantId;

    /// <inheritdoc />
    public Guid ChannelId => _inner.ChannelId;

    /// <inheritdoc />
    public string HostName => _inner.HostName;

    /// <inheritdoc />
    public bool IsAuthenticated => _inner.IsAuthenticated;

    /// <inheritdoc />
    public string? SessionId => _inner.SessionId;

    /// <inheritdoc />
    public Badge Badge => _inner.Badge;

    /// <inheritdoc />
    public Guid Id => _inner.Id;

    /// <inheritdoc />
    public bool Can(Operation operation)
    {
        var policy = _scopePolicyProvider.GetPolicy(typeof(TEntity));

        // Guests are allowed only when authentication is not required and scoped identity is present.
        if (!IsAuthenticated)
        {
            if (policy.RequireAuthentication)
            {
                return false;
            }

            return TenantId != Guid.Empty && UserId != Guid.Empty;
        }

        // Admin bypass: authenticated principals with the admin role can perform any operation.
        var principal = _principalAccessor.Principal as ClaimsPrincipal;
        if (principal is not null && principal.HasClaim(ClaimTypes.Role, "admin"))
        {
            return true;
        }

        // Canonical claim contract: type="right", value="{Operation}:{EntityName}"
        var entityName = typeof(TEntity).Name;
        return principal?.HasClaim("right", $"{operation}:{entityName}") == true;
    }
}