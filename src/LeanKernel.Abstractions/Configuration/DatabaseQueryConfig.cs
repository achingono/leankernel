namespace LeanKernel.Abstractions.Configuration;

/// <summary>
/// Configuration settings for database query operations.
/// </summary>
public sealed class DatabaseQueryConfig
{
    /// <summary>
    /// Gets or sets the maximum number of rows to return in a query.
    /// </summary>
    public int MaxRows { get; set; } = 200;

    /// <summary>
    /// Gets or sets the default timeout in seconds for a database query.
    /// </summary>
    public int DefaultTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Gets or sets the list of database query connections.
    /// </summary>
    public List<DatabaseQueryConnectionConfig> Connections { get; set; } = [];
}

/// <summary>
/// Configuration settings for a single database query connection.
/// </summary>
public sealed class DatabaseQueryConnectionConfig
{
    /// <summary>
    /// Gets or sets the name of the connection.
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// Gets or sets the database provider name.
    /// </summary>
    public required string Provider { get; set; }

    /// <summary>
    /// Gets or sets the connection string for the database.
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
