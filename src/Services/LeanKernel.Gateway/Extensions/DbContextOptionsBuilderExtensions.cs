using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;

namespace LeanKernel.Gateway;

public static class DbContextOptionsBuilderExtensions
{
    internal static readonly string[] ConnectionStringNames = ["Postgres", "SqlServer", "Sqlite"];

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