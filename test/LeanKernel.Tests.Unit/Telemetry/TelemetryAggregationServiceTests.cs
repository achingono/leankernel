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

public sealed class TelemetryAggregationServiceTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");

    public TelemetryAggregationServiceTests()
    {
        _connection.Open();
    }

    public void Dispose()
    {
        _connection.Dispose();
    }

    [Fact]
    public async Task GetCostByModelAsync_ReturnsExpectedRollup()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var channelId = Guid.NewGuid();

        await SeedTelemetryAsync(tenantId, userId, channelId, extraSeed: context =>
        {
            InsertTurnWithTelemetry(context, tenantId, userId, channelId, "gpt-4o-mini", "openai", 100, 50, 0.002m, false, DateTimeOffset.UtcNow.AddHours(-2));
            InsertTurnWithTelemetry(context, tenantId, userId, channelId, "gpt-4o-mini", "openai", 200, 25, 0.003m, true, DateTimeOffset.UtcNow.AddHours(-1));
            InsertTurnWithTelemetry(context, tenantId, userId, channelId, "gpt-4o", "openai", 300, 100, 0.010m, false, DateTimeOffset.UtcNow.AddMinutes(-30));
        });

        var service = CreateSut(tenantId, userId, channelId);

        var result = await service.GetCostByModelAsync(DateRange.Last7Days());

        result.Should().HaveCount(2);
        var mini = result.Single(item => item.Key == "gpt-4o-mini");
        mini.TotalCost.Should().Be(0.005m);
        mini.PromptTokens.Should().Be(300);
        mini.CompletionTokens.Should().Be(75);
        mini.TotalTokens.Should().Be(375);
        mini.EstimatedTurnCount.Should().Be(1);
        mini.ReportedTurnCount.Should().Be(1);
    }

    [Fact]
    public async Task GetSummaryAsync_IsScopedToPermitPartition()
    {
        var tenantId = Guid.NewGuid();
        var channelId = Guid.NewGuid();
        var permitUserId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();

        await SeedTelemetryAsync(tenantId, permitUserId, channelId, extraSeed: context =>
        {
            InsertUser(context, otherUserId);
            InsertTurnWithTelemetry(context, tenantId, permitUserId, channelId, "gpt-4o-mini", "openai", 80, 40, 0.001m, false, DateTimeOffset.UtcNow.AddHours(-2));
            InsertTurnWithTelemetry(context, tenantId, otherUserId, channelId, "gpt-4o", "openai", 900, 500, 0.100m, false, DateTimeOffset.UtcNow.AddHours(-1));
        });

        var service = CreateSut(tenantId, permitUserId, channelId,
            te => te.Turn.Session.UserId == permitUserId);

        var summary = await service.GetSummaryAsync(DateRange.Last7Days());

        summary.TotalTurns.Should().Be(1);
        summary.TotalCost.Should().Be(0.001m);
        summary.UniqueUsers.Should().Be(1);
        summary.UniqueModels.Should().Be(1);
    }

    [Fact]
    public async Task GetTopModelsByCostAsync_RespectsTopCount()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var channelId = Guid.NewGuid();

        await SeedTelemetryAsync(tenantId, userId, channelId, extraSeed: context =>
        {
            InsertTurnWithTelemetry(context, tenantId, userId, channelId, "gpt-4o", "openai", 100, 100, 0.2m, false, DateTimeOffset.UtcNow.AddHours(-4));
            InsertTurnWithTelemetry(context, tenantId, userId, channelId, "gpt-4o-mini", "openai", 100, 100, 0.01m, false, DateTimeOffset.UtcNow.AddHours(-3));
            InsertTurnWithTelemetry(context, tenantId, userId, channelId, "claude-3-sonnet", "anthropic", 100, 100, 0.03m, false, DateTimeOffset.UtcNow.AddHours(-2));
        });

        var service = CreateSut(tenantId, userId, channelId);

        var top = await service.GetTopModelsByCostAsync(DateRange.Last7Days(), top: 2);

        top.Should().HaveCount(2);
        top[0].Key.Should().Be("gpt-4o");
        top[1].Key.Should().Be("claude-3-sonnet");
    }

    [Fact]
    public async Task GetModelEfficiencyAsync_ComputesGroupedMetrics()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var channelId = Guid.NewGuid();

        await SeedTelemetryAsync(tenantId, userId, channelId, extraSeed: context =>
        {
            InsertTurnWithTelemetry(context, tenantId, userId, channelId, "gpt-4o-mini", "openai", 100, 50, 0.003m, false, DateTimeOffset.UtcNow.AddHours(-3));
            InsertTurnWithTelemetry(context, tenantId, userId, channelId, "gpt-4o-mini", "openai", 200, 100, 0.006m, false, DateTimeOffset.UtcNow.AddHours(-2));
            InsertTurnWithTelemetry(context, tenantId, userId, channelId, "claude-3-sonnet", "anthropic", 50, 50, 0.002m, false, DateTimeOffset.UtcNow.AddHours(-1));
        });

        var service = CreateSut(tenantId, userId, channelId);

        var result = await service.GetModelEfficiencyAsync(DateRange.Last7Days());

        result.Should().HaveCount(2);
        var top = result[0];
        top.Model.Should().Be("gpt-4o-mini");
        top.Provider.Should().Be("openai");
        top.TotalTurns.Should().Be(2);
        top.TotalTokens.Should().Be(450);
        top.TotalCost.Should().Be(0.009m);
        top.CostPer1kTokens.Should().Be(0.02m);
        top.AvgPromptTokensPerTurn.Should().Be(150m);
        top.AvgCompletionTokensPerTurn.Should().Be(75m);
        top.CompletionRatio.Should().BeApproximately(0.333333333m, 0.000001m);
    }

    [Fact]
    public async Task AggregationMethods_ReturnExpectedDimensionsAndFallbacks()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var channelId = Guid.NewGuid();
        var capturedAt = DateTimeOffset.UtcNow.AddMinutes(-45);

        await SeedTelemetryAsync(tenantId, userId, channelId, extraSeed: context =>
        {
            InsertTurnWithTelemetry(context, tenantId, userId, channelId, string.Empty, string.Empty, 12, 8, 0.001m, false, capturedAt);
        });

        var service = CreateSut(tenantId, userId, channelId);
        var range = DateRange.Last7Days();

        var byProvider = await service.GetCostByProviderAsync(range);
        var byUser = await service.GetCostByUserAsync(range);
        var bySession = await service.GetCostBySessionAsync(range);
        var byDay = await service.GetCostByDayAsync(range);
        var byTenant = await service.GetCostByTenantAsync(range);
        var byModelDay = await service.GetCostByModelAndDayAsync(range);
        var byProviderDay = await service.GetCostByProviderAndDayAsync(range);
        var byUserModel = await service.GetCostByUserAndModelAsync(range);
        var topUsers = await service.GetTopUsersByCostAsync(range);

        byProvider.Single().Key.Should().Be("unknown");
        byUser.Single().Dimension.Should().Be("user");
        bySession.Single().Dimension.Should().Be("session");
        byDay.Single().Dimension.Should().Be("day");
        byTenant.Single().Dimension.Should().Be("tenant");
        byModelDay.Single().Key.Should().StartWith("unknown|");
        byProviderDay.Single().Key.Should().StartWith("unknown|");
        byUserModel.Single().Key.Should().EndWith("|unknown");
        topUsers.Should().HaveCount(1);
    }

    [Fact]
    public async Task QueryMethods_WithInvalidRange_ThrowArgumentException()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var channelId = Guid.NewGuid();
        var service = CreateSut(tenantId, userId, channelId);
        var invalidRange = new DateRange(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(-1));

        var act = () => service.GetCostByModelAsync(invalidRange);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    private async Task SeedTelemetryAsync(Guid tenantId, Guid userId, Guid channelId, Action<EntityContext> extraSeed)
    {
        var options = CreateOptions();
        await using var context = new EntityContext(options);
        await context.Database.EnsureCreatedAsync();

        InsertTenant(context, tenantId);
        InsertChannel(context, channelId);
        InsertUser(context, userId);

        extraSeed(context);
        await context.SaveChangesAsync();
    }

    private ITelemetryAggregationService CreateSut(Guid tenantId, Guid userId, Guid channelId,
        Expression<Func<TurnTelemetryEntity, bool>>? filterPredicate = null)
    {
        var options = CreateOptions();
        var context = new EntityContext(options);
        context.Database.EnsureCreated();

        var telemetryRepo = CreateRepo<TurnTelemetryEntity>(context, filterPredicate);
        return new TelemetryAggregationService(telemetryRepo);
    }

    private static IRepository<TEntity> CreateRepo<TEntity>(EntityContext context,
        Expression<Func<TEntity, bool>>? filterPredicate = null)
        where TEntity : class, IEntity
    {
        var permitMock = new Mock<IPermit<TEntity>>();
        permitMock.Setup(p => p.Can(It.IsAny<Operation>())).Returns(true);

        var filterMock = new Mock<IFilter<TEntity>>();
        filterMock.Setup(f => f.Predicate).Returns(filterPredicate ?? (Expression<Func<TEntity, bool>>?)null);

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
        bool estimated,
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
            CostIsEstimated = estimated,
            Currency = "USD",
            CapturedAt = capturedAt,
            CreatedOn = DateTime.UtcNow,
            CreatedBy = new Badge { Id = Guid.Empty, FullName = "System", Email = "system@local" }
        });
    }

    private static void InsertTenant(EntityContext context, Guid tenantId)
    {
        if (context.Tenants.Any(t => t.Id == tenantId))
        {
            return;
        }

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

    private static void InsertUser(EntityContext context, Guid userId)
    {
        if (context.Users.Any(u => u.Id == userId))
        {
            return;
        }

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

    private static void InsertChannel(EntityContext context, Guid channelId)
    {
        if (context.Channels.Any(c => c.Id == channelId))
        {
            return;
        }

        context.Channels.Add(new ChannelEntity
        {
            Id = channelId,
            Name = $"channel-{channelId:N}"
        });
    }
}