namespace LeanKernel.Abstractions.Configuration;

public sealed class DatabaseConfig
{
    public string ConnectionString { get; set; } = "Host=database;Database=leankernel;Username=leankernel;Password=leankernel";
}
