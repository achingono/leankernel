using System.Reflection;

using FluentAssertions;

using LeanKernel.Data;
using LeanKernel.Entities;
using LeanKernel.Services.Gateway.Configuration;
using LeanKernel.Services.Gateway.Providers;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
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

    [Fact]
    public async Task PurgeAsync_WithNoEntries_DoesNotThrow()
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

        var act = () => hostedService.StartAsync(CancellationToken.None);
        await act.Should().NotThrowAsync();

        await Task.Delay(1000);
        await hostedService.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task PurgeAsync_RemovesEntriesAtRetentionBoundary()
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
            Options.Create(new DiagnosticsSettings { RetentionDays = 1, CleanupIntervalMinutes = 1 }),
            Mock.Of<ILogger<DiagnosticsCleanupHostedService>>());

        var seedOpts = new DbContextOptionsBuilder<EntityContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        using (var seed = new EntityContext(seedOpts))
        {
            seed.DiagnosticEntries.AddRange(
                new DiagnosticEntry
                {
                    Source = "boundary",
                    Category = "test",
                    PayloadJson = "{}",
                    CapturedAt = DateTimeOffset.UtcNow.AddDays(-1).AddMinutes(-1),
                },
                new DiagnosticEntry
                {
                    Source = "keep",
                    Category = "test",
                    PayloadJson = "{}",
                    CapturedAt = DateTimeOffset.UtcNow.AddHours(-12),
                });
            await seed.SaveChangesAsync();
        }

        await hostedService.StartAsync(CancellationToken.None);
        await Task.Delay(2000);
        await hostedService.StopAsync(CancellationToken.None);

        using var check = new EntityContext(seedOpts);
        var remaining = await check.DiagnosticEntries.ToListAsync();
        remaining.Should().ContainSingle(e => e.Source == "keep");
        remaining.Should().NotContain(e => e.Source == "boundary");
    }

    [Fact]
    public async Task ExecuteAsync_WhenPurgeThrows_LogsErrorAndContinues()
    {
        var serviceProvider = new Mock<IServiceProvider>();
        serviceProvider.Setup(sp => sp.GetService(typeof(IServiceScopeFactory)))
            .Returns((IServiceScopeFactory?)null);

        using var cts = new CancellationTokenSource();

        var loggerMock = new Mock<ILogger<DiagnosticsCleanupHostedService>>();

        var hostedService = new DiagnosticsCleanupHostedService(
            serviceProvider.Object,
            Options.Create(new DiagnosticsSettings { RetentionDays = 30, CleanupIntervalMinutes = 1 }),
            loggerMock.Object);

        await hostedService.StartAsync(cts.Token);

        await Task.Delay(500);

        loggerMock.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Diagnostics cleanup failed")),
                It.IsAny<InvalidOperationException>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);

        await hostedService.StopAsync(CancellationToken.None);
        await Task.Delay(200);
    }

    [Fact]
    public async Task ExecuteAsync_WithCancelledToken_ExitsImmediately()
    {
        var serviceProvider = new Mock<IServiceProvider>();
        var hostedService = new DiagnosticsCleanupHostedService(
            serviceProvider.Object,
            Options.Create(new DiagnosticsSettings { RetentionDays = 30, CleanupIntervalMinutes = 60 }),
            Mock.Of<ILogger<DiagnosticsCleanupHostedService>>());

        var cts = new CancellationTokenSource();
        cts.Cancel();

        var method = typeof(BackgroundService).GetMethod("ExecuteAsync", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(method);

        var task = (Task)(method.Invoke(hostedService, new object[] { cts.Token }) ?? Task.CompletedTask);
        await task;

        Assert.True(cts.Token.IsCancellationRequested);
    }
}