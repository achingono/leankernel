namespace LeanKernel.Abstractions.Configuration;

/// <summary>
/// Configuration settings for the database.
/// </summary>
public sealed class DatabaseConfig
{
    /// <summary>
    /// Gets or sets the connection string for the database.
    /// </summary>
    public string ConnectionString { get; set; } = "Host=database;Database=leankernel;Username=leankernel;Password=leankernel";
}
