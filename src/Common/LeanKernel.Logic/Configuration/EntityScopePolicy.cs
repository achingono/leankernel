namespace LeanKernel.Logic.Configuration;

/// <summary>
/// Defines the scope constraint dimensions and optional navigation path for a single entity type.
/// </summary>
public sealed class EntityScopePolicy
{
    /// <summary>
    /// Gets or sets the CLR type name of the entity (e.g. <c>SessionEntity</c>).
    /// </summary>
    public string EntityType { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the scope dimensions that constrain access to this entity.
    /// </summary>
    public ScopeDimension Dimensions { get; set; } = ScopeDimension.Tenant | ScopeDimension.User;

    /// <summary>
    /// Gets or sets the optional navigation path to resolve scope properties through related entities.
    /// Example: <c>Turn.Session</c> to resolve TenantId on TurnEntity via its Session navigation.
    /// </summary>
    public string? NavigationPath { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether authentication is required to access this entity.
    /// </summary>
    public bool RequireAuthentication { get; set; }
}