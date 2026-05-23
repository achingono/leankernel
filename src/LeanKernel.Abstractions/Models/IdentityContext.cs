namespace LeanKernel.Abstractions.Models;

/// <summary>
/// Represents the durable identity context available for a turn.
/// </summary>
public sealed record IdentityContext
{
    /// <summary>
    /// Gets the active user identifier.
    /// </summary>
    public required string UserId { get; init; }

    /// <summary>
    /// Gets the agent profile page, if available.
    /// </summary>
    public IdentityPage? AgentProfile { get; init; }

    /// <summary>
    /// Gets the user preference page, if available.
    /// </summary>
    public IdentityPage? UserPreferences { get; init; }

    /// <summary>
    /// Gets prompt-safe identity segments for context assembly.
    /// </summary>
    public IReadOnlyList<string> PromptSegments { get; init; } = [];

    /// <summary>
    /// Gets the aggregate confidence for the loaded identity state.
    /// </summary>
    public double OverallConfidence { get; init; }
}

/// <summary>
/// Represents a parsed identity page from GBrain.
/// </summary>
public sealed record IdentityPage
{
    /// <summary>
    /// Gets the page key.
    /// </summary>
    public required string Key { get; init; }

    /// <summary>
    /// Gets the original page content.
    /// </summary>
    public required string Content { get; init; }

    /// <summary>
    /// Gets the parsed structured identity fields.
    /// </summary>
    public IReadOnlyDictionary<string, IdentityField> Fields { get; init; } = new Dictionary<string, IdentityField>();
}

/// <summary>
/// Represents a single structured identity field.
/// </summary>
public sealed record IdentityField
{
    /// <summary>
    /// Gets the field name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets the field value.
    /// </summary>
    public required string Value { get; init; }

    /// <summary>
    /// Gets the confidence score for the field value.
    /// </summary>
    public double Confidence { get; init; } = 1.0;

    /// <summary>
    /// Gets the last updated timestamp for the field.
    /// </summary>
    public DateTimeOffset LastUpdated { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Gets the provenance source for the field value.
    /// </summary>
    public string? Source { get; init; }
}
