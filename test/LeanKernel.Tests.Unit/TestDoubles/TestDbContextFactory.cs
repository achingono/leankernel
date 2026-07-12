using LeanKernel.Data;
using Microsoft.EntityFrameworkCore;

namespace LeanKernel.Tests.Unit.TestDoubles;

internal sealed class TestDbContextFactory(DbContextOptions<EntityContext> options) : IDbContextFactory<EntityContext>
{
    public EntityContext CreateDbContext()
    {
        return new EntityContext(options);
    }

    public Task<EntityContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new EntityContext(options));
    }
}
