using LeanKernel.Data;
using Microsoft.EntityFrameworkCore;

namespace LeanKernel.Tests.Unit.TestDoubles;

/// <summary>
/// Creates <see cref="EntityContext"/> instances for tests.
/// </summary>
/// <param name="options">The options used to build each context.</param>
internal sealed class TestDbContextFactory(DbContextOptions<EntityContext> options) : IDbContextFactory<EntityContext>
{
    /// <inheritdoc />
    public EntityContext CreateDbContext()
    {
        return new EntityContext(options);
    }

    /// <inheritdoc />
    public Task<EntityContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new EntityContext(options));
    }
}
