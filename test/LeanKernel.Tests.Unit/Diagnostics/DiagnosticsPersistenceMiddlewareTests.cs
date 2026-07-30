using FluentAssertions;

using LeanKernel.Data;
using LeanKernel.Entities;
using LeanKernel.Logic.Diagnostics;
using LeanKernel.Services.Gateway.Providers;

using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Moq;

using Xunit;

namespace LeanKernel.Tests.Unit.Diagnostics;

public class DiagnosticsPersistenceMiddlewareTests : IDisposable
{
    private readonly EntityContext _dbContext;

    public DiagnosticsPersistenceMiddlewareTests()
    {
        var options = new DbContextOptionsBuilder<EntityContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _dbContext = new EntityContext(options);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }

    [Fact]
    public async Task InvokeAsync_WithEntries_PersistsToDatabase()
    {
        var collector = new DiagnosticsCollector();
        var entry = new DiagnosticEntry { Source = "gateway", Category = "turn", PayloadJson = "{}" };
        collector.Capture(entry);

        var ctx = new DefaultHttpContext();
        ctx.Response.Body = new MemoryStream();
        var nextCalled = false;
        var middleware = new DiagnosticsPersistenceMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(
            ctx,
            collector,
            _dbContext,
            Mock.Of<ILogger<DiagnosticsPersistenceMiddleware>>());

        nextCalled.Should().BeTrue();
        var saved = await _dbContext.DiagnosticEntries.ToListAsync();
        saved.Should().ContainSingle();
        saved[0].Source.Should().Be("gateway");
    }

    [Fact]
    public async Task InvokeAsync_WithoutEntries_DoesNotPersist()
    {
        var collector = new DiagnosticsCollector();

        var ctx = new DefaultHttpContext();
        ctx.Response.Body = new MemoryStream();
        var middleware = new DiagnosticsPersistenceMiddleware(_ => Task.CompletedTask);

        await middleware.InvokeAsync(
            ctx,
            collector,
            _dbContext,
            Mock.Of<ILogger<DiagnosticsPersistenceMiddleware>>());

        var saved = await _dbContext.DiagnosticEntries.ToListAsync();
        saved.Should().BeEmpty();
    }

    [Fact]
    public async Task InvokeAsync_WhenSaveChangesThrows_LogsErrorAndCompletes()
    {
        var collector = new DiagnosticsCollector();
        collector.Capture(new DiagnosticEntry { Source = "gateway", Category = "turn", PayloadJson = "{}" });

        using var brokenCtx = new ThrowingEntityContext(
            new DbContextOptionsBuilder<EntityContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options);

        var ctx = new DefaultHttpContext();
        ctx.Response.Body = new MemoryStream();
        var nextCalled = false;
        var middleware = new DiagnosticsPersistenceMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        var logger = new Mock<ILogger<DiagnosticsPersistenceMiddleware>>();

        await middleware.InvokeAsync(ctx, collector, brokenCtx, logger.Object);

        nextCalled.Should().BeTrue();
        logger.Verify(
            l => l.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) => true),
                It.IsAny<Exception>(),
                It.Is<Func<It.IsAnyType, Exception?, string>>((o, t) => true)),
            Times.Once);
    }

    private sealed class ThrowingEntityContext(DbContextOptions<EntityContext> options) : EntityContext(options)
    {
        public override async Task<int> SaveChangesAsync(CancellationToken ct = default)
        {
            await Task.Yield();
            throw new InvalidOperationException("Simulated database failure.");
        }

        public override int SaveChanges() => throw new InvalidOperationException("Simulated database failure.");
    }
}