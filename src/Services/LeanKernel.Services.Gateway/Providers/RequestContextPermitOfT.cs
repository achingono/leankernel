#pragma warning disable SA1649 // File name should match first type name

using System.Security.Claims;

using LeanKernel.Entities;
using LeanKernel.Logic.Filters;
using LeanKernel.Services.Gateway.Requests;

namespace LeanKernel.Services.Gateway.Providers;

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
        this._inner = inner;
        this._principalAccessor = principalAccessor;
        this._scopePolicyProvider = scopePolicyProvider;
    }

    /// <inheritdoc />
    public Guid PersonId => this._inner.PersonId;

    /// <inheritdoc />
    public Guid UserId => this._inner.UserId;

    /// <inheritdoc />
    public Guid TenantId => this._inner.TenantId;

    /// <inheritdoc />
    public Guid ChannelId => this._inner.ChannelId;

    /// <inheritdoc />
    public string HostName => this._inner.HostName;

    /// <inheritdoc />
    public bool IsAuthenticated => this._inner.IsAuthenticated;

    /// <inheritdoc />
    public string? SessionId => this._inner.SessionId;

    /// <inheritdoc />
    public Badge Badge => this._inner.Badge;

    /// <inheritdoc />
    public Guid Id => this._inner.Id;

    /// <inheritdoc />
    public bool Can(Operation operation)
    {
        var policy = this._scopePolicyProvider.GetPolicy(typeof(TEntity));

        // Guests are allowed only when authentication is not required and scoped identity is present.
        if (!this.IsAuthenticated)
        {
            if (policy.RequireAuthentication)
            {
                return false;
            }

            return this.TenantId != Guid.Empty && this.UserId != Guid.Empty;
        }

        // Admin bypass: authenticated principals with the admin role can perform any operation.
        var principal = this._principalAccessor.Principal as ClaimsPrincipal;
        if (principal is not null && principal.HasClaim(ClaimTypes.Role, "admin"))
        {
            return true;
        }

        // Canonical claim contract: type="right", value="{Operation}:{EntityName}"
        var entityName = typeof(TEntity).Name;
        return principal?.HasClaim("right", $"{operation}:{entityName}") == true;
    }
}