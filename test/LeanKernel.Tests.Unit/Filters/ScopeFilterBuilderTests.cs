using FluentAssertions;

using LeanKernel.Entities;
using LeanKernel.Logic.Configuration;
using LeanKernel.Logic.Filters;

using Moq;

using Xunit;

namespace LeanKernel.Tests.Unit.Filters;

/// <summary>
/// Covers <see cref="ScopeFilterBuilder"/> expression generation for direct and navigation predicates.
/// </summary>
public sealed class ScopeFilterBuilderTests
{
    private readonly ScopeFilterBuilder _builder = new();

    [Fact]
    public void Build_WithTenantUserChannelDirect_AppliesAllEqualities()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var channelId = Guid.NewGuid();

        var permit = new Mock<IPermit>();
        permit.Setup(p => p.TenantId).Returns(tenantId);
        permit.Setup(p => p.UserId).Returns(userId);
        permit.Setup(p => p.ChannelId).Returns(channelId);

        var policy = new EntityScopePolicy
        {
            Dimensions = ScopeDimension.Tenant | ScopeDimension.User | ScopeDimension.Channel,
            NavigationPath = null,
        };

        var predicate = _builder.Build<SessionEntity>(policy, permit.Object);
        var compiled = predicate.Compile();

        var matching = new SessionEntity
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            UserId = userId,
            ChannelId = channelId,
            Tenant = null!,
            User = null!,
            Channel = null!,
        };

        var nonMatchingTenant = new SessionEntity
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            UserId = userId,
            ChannelId = channelId,
            Tenant = null!,
            User = null!,
            Channel = null!,
        };

        compiled(matching).Should().BeTrue();
        compiled(nonMatchingTenant).Should().BeFalse();
    }

    [Fact]
    public void Build_WithTenantOnlyDirect_AppliesOnlyTenantEqualities()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var permit = new Mock<IPermit>();
        permit.Setup(p => p.TenantId).Returns(tenantId);
        permit.Setup(p => p.UserId).Returns(userId);

        var policy = new EntityScopePolicy
        {
            Dimensions = ScopeDimension.Tenant,
            NavigationPath = null,
        };

        var predicate = _builder.Build<SessionEntity>(policy, permit.Object);
        var compiled = predicate.Compile();

        var matching = new SessionEntity
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            UserId = userId,
            ChannelId = Guid.NewGuid(),
            Tenant = null!,
            User = null!,
            Channel = null!,
        };

        var nonMatching = new SessionEntity
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            UserId = userId,
            ChannelId = Guid.NewGuid(),
            Tenant = null!,
            User = null!,
            Channel = null!,
        };

        compiled(matching).Should().BeTrue();
        compiled(nonMatching).Should().BeFalse();
    }

    [Fact]
    public void Build_WithNavigationPath_ResolvesThroughNavigation()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var channelId = Guid.NewGuid();

        var permit = new Mock<IPermit>();
        permit.Setup(p => p.TenantId).Returns(tenantId);
        permit.Setup(p => p.UserId).Returns(userId);
        permit.Setup(p => p.ChannelId).Returns(channelId);

        var policy = new EntityScopePolicy
        {
            Dimensions = ScopeDimension.Tenant | ScopeDimension.User | ScopeDimension.Channel,
            NavigationPath = "Turn.Session",
        };

        var sessionId = Guid.NewGuid();
        var predicate = _builder.Build<TurnTelemetryEntity>(policy, permit.Object);
        var compiled = predicate.Compile();

        var matching = new TurnTelemetryEntity
        {
            Id = Guid.NewGuid(),
            TurnId = sessionId,
            Turn = new TurnEntity
            {
                Id = sessionId,
                SessionId = sessionId,
                Session = new SessionEntity
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    UserId = userId,
                    ChannelId = channelId,
                    Tenant = null!,
                    User = null!,
                    Channel = null!,
                },
                Role = "assistant",
                Content = "test",
            },
        };

        var nonMatching = new TurnTelemetryEntity
        {
            Id = Guid.NewGuid(),
            TurnId = sessionId,
            Turn = new TurnEntity
            {
                Id = sessionId,
                SessionId = sessionId,
                Session = new SessionEntity
                {
                    Id = Guid.NewGuid(),
                    TenantId = Guid.NewGuid(),
                    UserId = userId,
                    ChannelId = channelId,
                    Tenant = null!,
                    User = null!,
                    Channel = null!,
                },
                Role = "assistant",
                Content = "test",
            },
        };

        compiled(matching).Should().BeTrue();
        compiled(nonMatching).Should().BeFalse();
    }

    [Fact]
    public void Build_WithSessionNavigationPath_ResolvesThroughSession()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var channelId = Guid.NewGuid();

        var permit = new Mock<IPermit>();
        permit.Setup(p => p.TenantId).Returns(tenantId);
        permit.Setup(p => p.UserId).Returns(userId);
        permit.Setup(p => p.ChannelId).Returns(channelId);

        var policy = new EntityScopePolicy
        {
            Dimensions = ScopeDimension.Tenant | ScopeDimension.User | ScopeDimension.Channel,
            NavigationPath = "Session",
        };

        var predicate = _builder.Build<TurnEntity>(policy, permit.Object);
        var compiled = predicate.Compile();

        var matching = new TurnEntity
        {
            Id = Guid.NewGuid(),
            SessionId = Guid.NewGuid(),
            Session = new SessionEntity
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                UserId = userId,
                ChannelId = channelId,
                Tenant = null!,
                User = null!,
                Channel = null!,
            },
            Role = "assistant",
            Content = "test",
        };

        var nonMatching = new TurnEntity
        {
            Id = Guid.NewGuid(),
            SessionId = Guid.NewGuid(),
            Session = new SessionEntity
            {
                Id = Guid.NewGuid(),
                TenantId = Guid.NewGuid(),
                UserId = userId,
                ChannelId = channelId,
                Tenant = null!,
                User = null!,
                Channel = null!,
            },
            Role = "assistant",
            Content = "test",
        };

        compiled(matching).Should().BeTrue();
        compiled(nonMatching).Should().BeFalse();
    }
}