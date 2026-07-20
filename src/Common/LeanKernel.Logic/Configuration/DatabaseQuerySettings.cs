namespace LeanKernel.Logic.Configuration;

/// <summary>
/// Database query tool configuration nested under <c>Agents:Tools:DatabaseQuery</c>.
/// </summary>
public sealed class DatabaseQuerySettings
{
    /// <summary>
    /// Gets or sets a value indicating whether the database query tool is enabled.
    /// </summary>
    public bool Enabled { get; set; }

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
    public List<DatabaseQueryConnectionSettings> Connections { get; set; } = [];
}