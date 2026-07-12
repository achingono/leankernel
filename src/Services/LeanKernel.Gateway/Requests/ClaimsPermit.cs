using System.Security.Claims;
using System.Security.Principal;
using LeanKernel.Entities;

namespace LeanKernel.Gateway.Requests;

/// <summary>
/// Provides authorization logic based on claims for a specific entity type.
/// Kept for backward compatibility; prefer <see cref="Identity.RequestContextPermit"/> for new code.
/// </summary>
/// <typeparam name="TEntity">The type of entity for which permissions are checked.</typeparam>
public class ClaimsPermit<TEntity> : IPermit<TEntity>
    where TEntity : class
{
    internal IPrincipal Principal { get; }

    public Guid UserId
    {
        get
        {
            if (Principal is ClaimsPrincipal claimsPrincipal)
            {
                var id = claimsPrincipal.Claims
                    .Where(x => x.Type == ClaimTypes.NameIdentifier || x.Type == "sub")
                    .Select(x => Guid.TryParse(x.Value, out var g) ? g : Guid.Empty)
                    .FirstOrDefault();
                return id;
            }
            return Guid.Empty;
        }
    }

    public Guid TenantId => Guid.Empty;
    public Guid ChannelId => Guid.Empty;

    public bool IsAuthenticated =>
        Principal?.Identity?.IsAuthenticated == true;

    public string? SessionId => null;

    public string HostName { get; }

    public Badge Badge
    {
        get
        {
            if (Principal is ClaimsPrincipal claimsPrincipal)
            {
                var name = claimsPrincipal.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Name)?.Value ?? "Unknown";
                var email = claimsPrincipal.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Email)?.Value ?? string.Empty;
                return new Badge { Id = UserId, FullName = name, Email = email };
            }

            return new Badge { Id = Guid.NewGuid(), FullName = "Unknown", Email = string.Empty };
        }
    }

    public ClaimsPermit(IPrincipal principal, IHostNameAccessor hostNameAccessor)
    {
        Principal = principal;
        HostName = hostNameAccessor.HostName;
    }

    public virtual bool Can(Operation operation)
    {
        if (Principal == null || Principal.Identity == null || !Principal.Identity.IsAuthenticated)
            return false;

        if ((!string.IsNullOrEmpty(Principal.Identity.Name) &&
             Principal.Identity.Name.Equals("Administrator Account", StringComparison.OrdinalIgnoreCase)) ||
            Principal.IsInRole("Administrators"))
            return true;

        if (Principal is ClaimsPrincipal claimsPrincipal)
        {
            var entityName = typeof(TEntity).Name;
            var claimValue = $"{operation}:{entityName}";
            return claimsPrincipal.HasClaim("right", claimValue);
        }

        return false;
    }
}
