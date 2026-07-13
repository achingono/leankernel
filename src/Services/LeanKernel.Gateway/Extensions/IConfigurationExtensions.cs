namespace LeanKernel.Gateway;

/// <summary>
/// Provides configuration helpers for the LeanKernel gateway.
/// </summary>
public static class IConfigurationExtensions
{
    /// <summary>
    /// Resolves the first configured database connection string supported by the gateway.
    /// </summary>
    /// <param name="configuration">The application configuration.</param>
    /// <returns>
    /// A tuple containing the matching connection string name and value, or <c>(null, null)</c>
    /// when no supported connection string is configured.
    /// </returns>
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
