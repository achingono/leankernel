using FluentAssertions;

using LeanKernel.Data;
using LeanKernel.Entities;
using LeanKernel.Services.Gateway.Configuration;
using LeanKernel.Services.Gateway.Providers;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Moq;

using Xunit;

namespace LeanKernel.Tests.Unit.Diagnostics;

public class DiagnosticsCleanupHostedServiceTests
{
    [Fact]
    public async Task PurgeAsync_RemovesEntriesOlderThanRetentionDays()
    {
        var dbName = Guid.NewGuid().ToString();
        var dbContextFactory = new Mock<IDbContextFactory<EntityContext>>();
        dbContextFactory.Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                var opts = new DbContextOptionsBuilder<EntityContext>()
                    .UseInMemoryDatabase(dbName)
                    .Options;
                var ctx = new EntityContext(opts);
                ctx.Database.EnsureCreated();
                return ctx;
            });

        var scopeServiceProvider = new Mock<IServiceProvider>();
        scopeServiceProvider.Setup(sp => sp.GetService(typeof(EntityContext)))
            .Returns(() =>
            {
                var opts = new DbContextOptionsBuilder<EntityContext>()
                    .UseInMemoryDatabase(dbName)
                    .Options;
                var ctx = new EntityContext(opts);
                ctx.Database.EnsureCreated();
                return ctx;
            });

        var scope = new Mock<IServiceScope>();
        scope.Setup(s => s.ServiceProvider).Returns(scopeServiceProvider.Object);

        var scopeFactory = new Mock<IServiceScopeFactory>();
        scopeFactory.Setup(f => f.CreateScope()).Returns(scope.Object);

        var serviceProvider = new Mock<IServiceProvider>();
        serviceProvider.Setup(sp => sp.GetService(typeof(IServiceScopeFactory)))
            .Returns(scopeFactory.Object);

        var hostedService = new DiagnosticsCleanupHostedService(
            serviceProvider.Object,
            Options.Create(new DiagnosticsSettings { RetentionDays = 30, CleanupIntervalMinutes = 60 }),
            Mock.Of<ILogger<DiagnosticsCleanupHostedService>>());

        var seedOpts = new DbContextOptionsBuilder<EntityContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        using (var seed = new EntityContext(seedOpts))
        {
            seed.DiagnosticEntries.AddRange(
                new DiagnosticEntry
                {
                    Source = "old",
                    Category = "test",
                    PayloadJson = "{}",
                    CapturedAt = DateTimeOffset.UtcNow.AddDays(-31),
                },
                new DiagnosticEntry
                {
                    Source = "new",
                    Category = "test",
                    PayloadJson = "{}",
                    CapturedAt = DateTimeOffset.UtcNow,
                });
            await seed.SaveChangesAsync();
        }

        await hostedService.StartAsync(CancellationToken.None);
        await Task.Delay(1000);
        await hostedService.StopAsync(CancellationToken.None);

        using var check = new EntityContext(seedOpts);
        var remaining = await check.DiagnosticEntries.ToListAsync();
        remaining.Should().ContainSingle(e => e.Source == "new");
        remaining.Should().NotContain(e => e.Source == "old");
    }
}