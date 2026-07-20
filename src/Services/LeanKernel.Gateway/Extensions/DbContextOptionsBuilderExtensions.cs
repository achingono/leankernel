using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace LeanKernel.Gateway;

/// <summary>
/// Provides helpers for configuring <see cref="DbContextOptionsBuilder"/> instances.
/// </summary>
public static class DbContextOptionsBuilderExtensions
{
    internal static readonly string[] ConnectionStringNames = ["Postgres", "SqlServer", "Sqlite"];

    /// <summary>
    /// Configures the database provider and common EF Core diagnostics options.
    /// </summary>
    /// <param name="options">The options builder to configure.</param>
    /// <param name="connectionStringName">The configured connection string name that determines the provider.</param>
    /// <param name="connectionString">The connection string value.</param>
    /// <param name="allowEmptyConnectionString">Whether an empty connection string is allowed.</param>
    /// <param name="enableDetailedErrors">Whether detailed EF Core errors are enabled.</param>
    /// <param name="enableSensitiveDataLogging">Whether sensitive data logging is enabled.</param>
    /// <returns>The configured <see cref="DbContextOptionsBuilder"/>.</returns>
    public static DbContextOptionsBuilder ConfigureOptions(this DbContextOptionsBuilder options,
        string? connectionStringName, string? connectionString, bool allowEmptyConnectionString = false,
        bool enableDetailedErrors = false, bool enableSensitiveDataLogging = false)
    {
        if (string.IsNullOrWhiteSpace(connectionString) && !allowEmptyConnectionString)
        {
            throw new InvalidOperationException(
                $"Connection string is missing. Specify any of '{string.Join(",", ConnectionStringNames)}'.");
        }

        switch (connectionStringName)
        {
            case "SqlServer":
                options.UseSqlServer(connectionString, sqlOptions => sqlOptions.EnableRetryOnFailure());
                break;
            case "Sqlite":
                options.UseSqlite(connectionString);
                break;
            case "Postgres":
                options.UseNpgsql(connectionString);
                break;
            case null when !allowEmptyConnectionString:
                throw new InvalidOperationException(
                    $"Connection string is missing. Specify any of '{string.Join(",", ConnectionStringNames)}'.");
            default:
                throw new InvalidOperationException(
                    $"Unsupported connection string name '{connectionStringName}'. Specify any of '{string.Join(",", ConnectionStringNames)}'.");
        }

        options.EnableDetailedErrors(enableDetailedErrors)
                   .EnableSensitiveDataLogging(enableSensitiveDataLogging)
                   .ConfigureWarnings(warnings => warnings.Ignore(RelationalEventId.PendingModelChangesWarning));

        return options;
    }
}