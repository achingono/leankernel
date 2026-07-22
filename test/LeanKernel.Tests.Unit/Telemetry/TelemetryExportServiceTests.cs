using System.Linq.Expressions;

using FluentAssertions;

using LeanKernel.Data;
using LeanKernel.Entities;
using LeanKernel.Logic.Interfaces;
using LeanKernel.Logic.Repositories;
using LeanKernel.Logic.Telemetry;
using LeanKernel.Logic.Telemetry.Models;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Moq;

using Xunit;

namespace LeanKernel.Tests.Unit.Telemetry;

public sealed class TelemetryExportServiceTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");

    public TelemetryExportServiceTests()
    {
        _connection.Open();
    }

    public void Dispose()
    {
        _connection.Dispose();
    }

    [Fact]
    public async Task ExportAsync_ReturnsDeterministicTimestampOrder()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var channelId = Guid.NewGuid();

        var baseTime = DateTimeOffset.UtcNow.AddHours(-1);
        await SeedAsync(tenantId, userId, channelId, context =>
        {
            InsertTurnWithTelemetry(context, tenantId, userId, channelId, "gpt-4o-mini", "openai", 10, 5, 0.001m, baseTime.AddMinutes(10));
            InsertTurnWithTelemetry(context, tenantId, userId, channelId, "gpt-4o-mini", "openai", 20, 7, 0.002m, baseTime.AddMinutes(5));
        });

        var service = CreateSut(tenantId, userId, channelId);
        var result = await service.ExportAsync(DateRange.Last7Days());

        result.Should().HaveCount(2);
        result[0].Timestamp.Should().BeBefore(result[1].Timestamp);
        result.Select(item => item.PromptTokens).Should().Equal(20, 10);
    }

    [Fact]
    public async Task ExportAsync_ReturnsOnlyPiiFreeShape()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var channelId = Guid.NewGuid();

        await SeedAsync(tenantId, userId, channelId, context =>
        {
            InsertTurnWithTelemetry(context, tenantId, userId, channelId, "gpt-4o-mini", "openai", 10, 5, 0.001m, DateTimeOffset.UtcNow.AddMinutes(-2));
        });

        var service = CreateSut(tenantId, userId, channelId);
        var result = await service.ExportAsync(DateRange.Last7Days());

        var propertyNames = typeof(TelemetryExportRecord)
            .GetProperties()
            .Select(property => property.Name)
            .ToList();

        propertyNames.Should().NotContain(new[] { "UserId", "TenantId", "Content", "AuthorName" });
        result.Single().ServedModel.Should().Be("gpt-4o-mini");
    }

    [Fact]
    public async Task ExportAsync_WhenModelsOrProviderMissing_UsesFallbackValues()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var channelId = Guid.NewGuid();

        await SeedAsync(tenantId, userId, channelId, context =>
        {
            InsertTurnWithCustomTelemetry(
                context,
                tenantId,
                userId,
                channelId,
                requestedModel: "   ",
                servedModel: string.Empty,
                modelId: "gpt-fallback",
                provider: null,
                capturedAt: DateTimeOffset.UtcNow.AddMinutes(-2));
        });

        var service = CreateSut(tenantId, userId, channelId);
        var result = await service.ExportAsync(DateRange.Last7Days());

        var record = result.Single();
        record.RequestedModel.Should().Be("unknown");
        record.ServedModel.Should().Be("gpt-fallback");
        record.Provider.Should().Be("unknown");
    }

    [Fact]
    public async Task ExportAsync_WhenServedAndFallbackMissing_UsesUnknownModel()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var channelId = Guid.NewGuid();

        await SeedAsync(tenantId, userId, channelId, context =>
        {
            InsertTurnWithCustomTelemetry(
                context,
                tenantId,
                userId,
                channelId,
                requestedModel: "requested-model",
                servedModel: "  ",
                modelId: string.Empty,
                provider: "openai",
                capturedAt: DateTimeOffset.UtcNow.AddMinutes(-2));
        });

        var service = CreateSut(tenantId, userId, channelId);
        var result = await service.ExportAsync(DateRange.Last7Days());

        result.Single().ServedModel.Should().Be("unknown");
    }

    [Fact]
    public async Task ExportAsync_WithInvalidRange_ThrowsArgumentException()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var channelId = Guid.NewGuid();
        var service = CreateSut(tenantId, userId, channelId);
        var invalidRange = new DateRange(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(-1));

        var act = () => service.ExportAsync(invalidRange);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    private async Task SeedAsync(Guid tenantId, Guid userId, Guid channelId, Action<EntityContext> seed)
    {
        var options = CreateOptions();
        await using var context = new EntityContext(options);
        await context.Database.EnsureCreatedAsync();

        if (!context.Tenants.Any(t => t.Id == tenantId))
        {
            context.Tenants.Add(new TenantEntity
            {
                Id = tenantId,
                Name = $"tenant-{tenantId:N}",
                Description = "test tenant",
                HostName = $"{tenantId:N}.local",
                IsActive = true,
                CreatedOn = DateTime.UtcNow,
                CreatedBy = new Badge { Id = Guid.Empty, FullName = "System", Email = "system@local" }
            });
        }

        if (!context.Users.Any(u => u.Id == userId))
        {
            context.Users.Add(new UserEntity
            {
                Id = userId,
                Email = $"{userId:N}@example.test",
                UserName = $"user-{userId:N}",
                FirstName = "Test",
                LastName = "User",
                FullName = "Test User",
                IsActive = true,
                IsLockedOut = false,
                CreatedOn = DateTime.UtcNow,
                CreatedBy = new Badge { Id = Guid.Empty, FullName = "System", Email = "system@local" },
                Issuer = "tests",
                Subject = userId.ToString("N"),
                IsGuest = false
            });
        }

        if (!context.Channels.Any(c => c.Id == channelId))
        {
            context.Channels.Add(new ChannelEntity
            {
                Id = channelId,
                Name = $"channel-{channelId:N}"
            });
        }

        seed(context);
        await context.SaveChangesAsync();
    }

    private ITelemetryExportService CreateSut(Guid tenantId, Guid userId, Guid channelId)
    {
        var options = CreateOptions();
        var context = new EntityContext(options);
        context.Database.EnsureCreated();

        var telemetryRepo = CreateRepo<TurnTelemetryEntity>(context);
        return new TelemetryExportService(telemetryRepo);
    }

    private static IRepository<TEntity> CreateRepo<TEntity>(EntityContext context)
        where TEntity : class, IEntity
    {
        var permitMock = new Mock<IPermit<TEntity>>();
        permitMock.Setup(p => p.Can(It.IsAny<Operation>())).Returns(true);

        var filterMock = new Mock<IFilter<TEntity>>();
        filterMock.Setup(f => f.Predicate).Returns((Expression<Func<TEntity, bool>>?)null);

        return new EntityRepository<TEntity>(context, filterMock.Object, permitMock.Object);
    }

    private DbContextOptions<EntityContext> CreateOptions()
    {
        return new DbContextOptionsBuilder<EntityContext>()
            .UseSqlite(_connection)
            .Options;
    }

    private static void InsertTurnWithTelemetry(
        EntityContext context,
        Guid tenantId,
        Guid userId,
        Guid channelId,
        string model,
        string provider,
        int promptTokens,
        int completionTokens,
        decimal cost,
        DateTimeOffset capturedAt)
    {
        var session = new SessionEntity
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            UserId = userId,
            ChannelId = channelId,
            Tenant = null!,
            User = null!,
            Channel = null!,
            CreatedAt = capturedAt,
            UpdatedAt = capturedAt,
            CreatedOn = DateTime.UtcNow,
            CreatedBy = new Badge { Id = Guid.Empty, FullName = "System", Email = "system@local" }
        };
        var turn = new TurnEntity
        {
            Id = Guid.NewGuid(),
            SessionId = session.Id,
            Session = session,
            Role = "assistant",
            Content = "response",
            Timestamp = capturedAt,
            CreatedOn = DateTime.UtcNow,
            CreatedBy = new Badge { Id = Guid.Empty, FullName = "System", Email = "system@local" }
        };

        context.Sessions.Add(session);
        context.Turns.Add(turn);
        context.TurnTelemetry.Add(new TurnTelemetryEntity
        {
            Id = Guid.NewGuid(),
            TurnId = turn.Id,
            Turn = turn,
            RequestedModel = model,
            ServedModel = model,
            Provider = provider,
            ModelId = model,
            PromptTokens = promptTokens,
            CompletionTokens = completionTokens,
            TotalTokens = promptTokens + completionTokens,
            ResponseCost = cost,
            CostIsEstimated = false,
            Currency = "USD",
            CapturedAt = capturedAt,
            CreatedOn = DateTime.UtcNow,
            CreatedBy = new Badge { Id = Guid.Empty, FullName = "System", Email = "system@local" }
        });
    }

    private static void InsertTurnWithCustomTelemetry(
        EntityContext context,
        Guid tenantId,
        Guid userId,
        Guid channelId,
        string? requestedModel,
        string? servedModel,
        string? modelId,
        string? provider,
        DateTimeOffset capturedAt)
    {
        var session = new SessionEntity
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            UserId = userId,
            ChannelId = channelId,
            Tenant = null!,
            User = null!,
            Channel = null!,
            CreatedAt = capturedAt,
            UpdatedAt = capturedAt,
            CreatedOn = DateTime.UtcNow,
            CreatedBy = new Badge { Id = Guid.Empty, FullName = "System", Email = "system@local" }
        };
        var turn = new TurnEntity
        {
            Id = Guid.NewGuid(),
            SessionId = session.Id,
            Session = session,
            Role = "assistant",
            Content = "response",
            Timestamp = capturedAt,
            CreatedOn = DateTime.UtcNow,
            CreatedBy = new Badge { Id = Guid.Empty, FullName = "System", Email = "system@local" }
        };

        context.Sessions.Add(session);
        context.Turns.Add(turn);
        context.TurnTelemetry.Add(new TurnTelemetryEntity
        {
            Id = Guid.NewGuid(),
            TurnId = turn.Id,
            Turn = turn,
            RequestedModel = requestedModel,
            ServedModel = servedModel,
            Provider = provider,
            ModelId = modelId,
            PromptTokens = 1,
            CompletionTokens = 1,
            TotalTokens = 2,
            ResponseCost = 0.001m,
            CostIsEstimated = false,
            Currency = "USD",
            CapturedAt = capturedAt,
            CreatedOn = DateTime.UtcNow,
            CreatedBy = new Badge { Id = Guid.Empty, FullName = "System", Email = "system@local" }
        });
    }
}