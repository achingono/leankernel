using FluentAssertions;

using LeanKernel.Entities;

using Xunit;

namespace LeanKernel.Tests.Unit.Entities;

public sealed class EntityDefaultsTests
{
    [Fact]
    public void SessionEntity_DefaultNavigationsAndTimestamps_AreInitialized()
    {
        var session = new SessionEntity
        {
            ChannelId = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            CreatedBy = new Badge { Id = Guid.NewGuid(), FullName = "Test", Email = "test@example.com" }
        };

        session.User.Should().NotBeNull();
        session.Channel.Should().NotBeNull();
        session.Tenant.Should().NotBeNull();
        session.Turns.Should().NotBeNull();
        session.CreatedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
        session.UpdatedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void TurnEntity_ComputeIdempotencyKey_IsStableWithinFiveMinuteBucket()
    {
        var sessionId = Guid.NewGuid();

        var first = new TurnEntity
        {
            SessionId = sessionId,
            Role = "user",
            Content = "hello",
            Timestamp = new DateTimeOffset(2026, 1, 1, 10, 2, 0, TimeSpan.Zero),
            CreatedBy = new Badge { Id = Guid.NewGuid(), FullName = "Test", Email = "test@example.com" }
        };
        var second = new TurnEntity
        {
            SessionId = sessionId,
            Role = "user",
            Content = "hello",
            Timestamp = new DateTimeOffset(2026, 1, 1, 10, 4, 59, TimeSpan.Zero),
            CreatedBy = new Badge { Id = Guid.NewGuid(), FullName = "Test", Email = "test@example.com" }
        };

        var firstKey = first.ComputeIdempotencyKey();
        var secondKey = second.ComputeIdempotencyKey();

        firstKey.Should().Be(secondKey);
        firstKey.Should().HaveLength(64);
    }

    [Fact]
    public void ChannelPolicyAndSenderBinding_Defaults_AreInitialized()
    {
        var policy = new ChannelMemoryPolicyEntity();
        var binding = new ChannelSenderBindingEntity();

        policy.ShareList.Should().Be(ChannelEntity.MemoryPolicyWildcard);
        policy.AccessList.Should().Be(ChannelEntity.MemoryPolicyWildcard);
        policy.CreatedOn.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));

        binding.Issuer.Should().BeEmpty();
        binding.Subject.Should().BeEmpty();
        binding.BearerToken.Should().BeEmpty();
        binding.IsActive.Should().BeTrue();
        binding.CreatedOn.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }
}