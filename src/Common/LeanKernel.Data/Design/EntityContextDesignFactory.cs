using LeanKernel.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace LeanKernel.Data.Design;

/// <summary>
/// Design-time factory for <see cref="EntityContext"/> used by EF Core migrations tooling.
/// </summary>
public class EntityContextDesignFactory : IDesignTimeDbContextFactory<EntityContext>
{
    public EntityContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<EntityContext>();
        optionsBuilder.UseSqlite("Data Source=leankernel.db");
        return new EntityContext(optionsBuilder.Options);
    }
}
