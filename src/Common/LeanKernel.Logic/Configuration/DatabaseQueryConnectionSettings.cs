namespace LeanKernel.Logic.Configuration;

/// <summary>
/// Configuration for a single database query connection.
/// </summary>
public sealed class DatabaseQueryConnectionSettings
{
    /// <summary>
    /// Gets or sets the name of the connection.
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// Gets or sets the database provider name: "postgres" or "sqlite".
    /// </summary>
    public required string Provider { get; set; }

    /// <summary>
    /// Gets or sets the connection string.
    /// </summary>
    public required string ConnectionString { get; set; }

    /// <summary>
    /// Gets or sets whether the connection is read-only.
    /// </summary>
    public bool ReadOnly { get; set; } = true;

    /// <summary>
    /// Gets or sets the list of allowed schemas for this connection.
    /// </summary>
    public List<string> AllowedSchemas { get; set; } = [];
}