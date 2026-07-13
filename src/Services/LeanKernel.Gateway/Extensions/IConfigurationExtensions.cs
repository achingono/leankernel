namespace LeanKernel.Gateway;

public static class IConfigurationExtensions
{
    public static (string? Name, string? Value) ResolveConnectionString(this IConfiguration configuration)
    {
        foreach (var connectionStringName in DbContextOptionsBuilderExtensions.ConnectionStringNames)
        {
            var connectionString = configuration.GetConnectionString(connectionStringName);
            if (!string.IsNullOrWhiteSpace(connectionString))
            {
                return (connectionStringName, connectionString);
            }
        }

        return (null, null);
    }
}