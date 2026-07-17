using Microsoft.Extensions.Configuration;

namespace LeanKernel.Channels.Common.Configuration;

public static class ConnectionStringResolverExtensions
{
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
                return (connectionStringName, connectionString);
        }

        return (null, null);
    }
}
