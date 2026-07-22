namespace LeanKernel.Logic.Policy;

/// <summary>
/// Default <see cref="IPolicyContext"/> implementation that derives identity
/// from the request-scoped <see cref="IPermit"/>.
/// </summary>
public sealed class PolicyContext : IPolicyContext
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PolicyContext"/> class.
    /// </summary>
    /// <param name="permit">The request-scoped permit.</param>
    public PolicyContext(IPermit permit)
    {
        Permit = permit;
        Identity = IdentityContext.FromPermit(permit);
    }

    /// <inheritdoc />
    public IdentityContext Identity { get; }

    /// <inheritdoc />
    public IPermit Permit { get; }

    /// <inheritdoc />
    public IReadOnlyDictionary<string, object?> Metadata { get; init; } = new Dictionary<string, object?>();
}