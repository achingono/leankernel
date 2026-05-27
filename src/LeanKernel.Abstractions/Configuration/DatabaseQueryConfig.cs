namespace LeanKernel.Abstractions.Configuration;

public sealed class DatabaseQueryConfig
{
    public int MaxRows { get; set; } = 200;
    public int DefaultTimeoutSeconds { get; set; } = 30;
    public List<DatabaseQueryConnectionConfig> Connections { get; set; } = [];
}

public sealed class DatabaseQueryConnectionConfig
{
    public required string Name { get; set; }
    public required string Provider { get; set; }
    public required string ConnectionString { get; set; }
    public bool ReadOnly { get; set; } = true;
    public List<string> AllowedSchemas { get; set; } = [];
}
