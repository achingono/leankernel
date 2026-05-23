using LeanKernel.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LeanKernel.Tests.Unit.Scheduler;

internal sealed class TestTimeProvider(DateTimeOffset utcNow) : TimeProvider
{
    private DateTimeOffset _utcNow = utcNow;

    public override DateTimeOffset GetUtcNow() => _utcNow;

    public void Advance(TimeSpan delta) => _utcNow = _utcNow.Add(delta);

    public void SetUtcNow(DateTimeOffset value) => _utcNow = value;
}

internal sealed class TestDbContextFactory(DbContextOptions<LeanKernelDbContext> options) : IDbContextFactory<LeanKernelDbContext>
{
    public LeanKernelDbContext CreateDbContext() => new(options);

    public Task<LeanKernelDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(CreateDbContext());
}
