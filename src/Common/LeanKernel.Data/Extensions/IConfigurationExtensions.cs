namespace Microsoft.Extensions.Configuration;

/// <summary>
/// Extension methods for resolving connection strings from configuration.
/// </summary>
public static class ConnectionStringResolverExtensions
{
    /// <summary>
    /// Resolves the first non-empty connection string from a prioritized list of names.
    /// </summary>
    /// <param name="configuration">The configuration instance.</param>
    /// <param name="connectionStringNames">Ordered list of connection string names to try.</param>
    /// <returns>
    /// A tuple containing the name and value of the first resolved connection string,
    /// or <c>(null, null)</c> if none are configured.
    /// </returns>
    public static (string? Name, string? Value) ResolveConnectionString(
        this IConfiguration configuration,
        IReadOnlyList<string> connectionStringNames)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(connectionStringNames);

        foreach (var connectionStringName in connectionStringNames)
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