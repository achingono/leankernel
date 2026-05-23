namespace LeanKernel.Abstractions.Configuration;

/// <summary>
/// Configures scoped knowledge retrieval behavior for context assembly.
/// </summary>
public sealed class RetrievalConfig
{
    /// <summary>
    /// Gets or sets a value indicating whether scoped retrieval is enabled.
    /// </summary>
    public bool ScopingEnabled { get; set; } = true;

    /// <summary>
    /// Gets or sets the default retrieval scope name.
    /// </summary>
    public string DefaultScope { get; set; } = "global";

    /// <summary>
    /// Gets or sets the maximum number of related results discovered during entity expansion.
    /// </summary>
    public int MaxEntityExpansionResults { get; set; } = 5;

    /// <summary>
    /// Gets or sets the score multiplier applied to entity-matching candidates.
    /// </summary>
    public double EntityBoostMultiplier { get; set; } = 1.5;

    /// <summary>
    /// Gets or sets the minimum relevance score required after scoped adjustments.
    /// </summary>
    public double MinScopeRelevanceScore { get; set; } = 0.3;

    /// <summary>
    /// Gets or sets a value indicating whether detailed retrieval diagnostics should be emitted.
    /// </summary>
    public bool EmitRetrievalDiagnostics { get; set; } = true;

    /// <summary>
    /// Gets or sets the configured retrieval scope policies.
    /// </summary>
    public List<ScopePolicyDefinition> ScopePolicies { get; set; } = new();
}

/// <summary>
/// Defines deterministic filtering rules for a named retrieval scope.
/// </summary>
public sealed class ScopePolicyDefinition
{
    /// <summary>
    /// Gets or sets the scope policy name.
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// Gets or sets the namespaces that are explicitly allowed.
    /// </summary>
    public List<string> IncludeNamespaces { get; set; } = new();

    /// <summary>
    /// Gets or sets the namespaces that are explicitly denied.
    /// </summary>
    public List<string> ExcludeNamespaces { get; set; } = new();

    /// <summary>
    /// Gets or sets metadata keys that must be present for a candidate to be admitted.
    /// </summary>
    public List<string> RequiredMetadataKeys { get; set; } = new();

    /// <summary>
    /// Gets or sets the minimum candidate score required by this policy.
    /// </summary>
    public double MinScore { get; set; }
}
