using FluentAssertions;
using LeanKernel.Abstractions.Models;
using LeanKernel.Persistence;
using LeanKernel.Persistence.Health;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace LeanKernel.Tests.Unit.Persistence;

public class DatabaseHealthProbeTests
{
    [Fact]
    public void ProviderName_returns_database()
    {
        var probe = new DatabaseHealthProbe(CreateFactory(), NullLogger<DatabaseHealthProbe>.Instance);
        probe.ProviderName.Should().Be(ProviderNames.Database);
    }

    [Fact]
    public async Task ProbeAsync_returns_healthy_when_can_connect_succeeds()
    {
        var factory = CreateFactory();
        var probe = new DatabaseHealthProbe(factory, NullLogger<DatabaseHealthProbe>.Instance);

        var result = await probe.ProbeAsync(default);

        result.IsHealthy.Should().BeTrue();
        result.Description.Should().Be("Database connectivity probe succeeded.");
    }

    [Fact]
    public async Task ProbeAsync_returns_unhealthy_when_exception_thrown()
    {
        var factory = new Mock<IDbContextFactory<LeanKernelDbContext>>();
        factory.Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Connection failed"));
        var probe = new DatabaseHealthProbe(factory.Object, NullLogger<DatabaseHealthProbe>.Instance);

        var result = await probe.ProbeAsync(default);

        result.IsHealthy.Should().BeFalse();
        result.Description.Should().Be("Database connectivity probe failed.");
    }

    private static TestDbContextFactory CreateFactory()
        => new(new DbContextOptionsBuilder<LeanKernelDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private sealed class TestDbContextFactory(DbContextOptions<LeanKernelDbContext> options) : IDbContextFactory<LeanKernelDbContext>
    {
        public LeanKernelDbContext CreateDbContext() => new(options);
        public Task<LeanKernelDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(CreateDbContext());
    }
}
