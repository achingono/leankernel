namespace LeanKernel.Data.Design;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

/// <summary>
/// Design-time factory for <see cref="EntityContext"/> used by EF Core migrations tooling.
/// Targets PostgreSQL to ensure migrations are provider-compatible at runtime.
/// </summary>
public class EntityContextDesignFactory : IDesignTimeDbContextFactory<EntityContext>
{
    /// <inheritdoc />
    public EntityContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Postgres")
            ?? "Host=localhost;Port=5432;Database=leankernel;Username=leankernel;Password=leankernel-dev-password";

        var optionsBuilder = new DbContextOptionsBuilder<EntityContext>();
        optionsBuilder.UseNpgsql(connectionString);
        return new EntityContext(optionsBuilder.Options);
    }
}