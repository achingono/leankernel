namespace LeanKernel.Logic.Configuration;

/// <summary>
/// Configuration root for entity-scope policy definitions, bound from <c>Agents:EntityScopePolicies</c>.
/// </summary>
public sealed class EntityScopePolicies
{
    /// <summary>
    /// Gets or sets the collection of per-entity scope policy definitions.
    /// </summary>
    public List<EntityScopePolicy> Policies { get; set; } = [];
}