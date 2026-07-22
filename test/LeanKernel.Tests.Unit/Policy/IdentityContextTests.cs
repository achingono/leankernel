using FluentAssertions;

using LeanKernel.Entities;

using Moq;

using Xunit;

namespace LeanKernel.Tests.Unit.Policy;

public class IdentityContextTests
{
    [Fact]
    public void FromPermit_MapsAllProperties()
    {
        var permit = new Mock<IPermit>();
        var tenantId = Guid.NewGuid();
        var personId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var channelId = Guid.NewGuid();
        var badge = new Badge { Id = userId, FullName = "Test User", Email = "test@example.com" };

        permit.Setup(p => p.TenantId).Returns(tenantId);
        permit.Setup(p => p.PersonId).Returns(personId);
        permit.Setup(p => p.UserId).Returns(userId);
        permit.Setup(p => p.ChannelId).Returns(channelId);
        permit.Setup(p => p.SessionId).Returns((string?)null);
        permit.Setup(p => p.HostName).Returns("localhost");
        permit.Setup(p => p.IsAuthenticated).Returns(true);
        permit.Setup(p => p.Badge).Returns(badge);

        var identity = IdentityContext.FromPermit(permit.Object);

        identity.TenantId.Should().Be(tenantId);
        identity.PersonId.Should().Be(personId);
        identity.UserId.Should().Be(userId);
        identity.ChannelId.Should().Be(channelId);
        identity.SessionId.Should().BeNull();
        identity.HostName.Should().Be("localhost");
        identity.IsAuthenticated.Should().BeTrue();
        identity.Badge.Should().Be(badge);
    }

    [Fact]
    public void FromPermit_WithSessionId_MapsSessionId()
    {
        var permit = new Mock<IPermit>();
        permit.Setup(p => p.TenantId).Returns(Guid.NewGuid());
        permit.Setup(p => p.PersonId).Returns(Guid.NewGuid());
        permit.Setup(p => p.UserId).Returns(Guid.NewGuid());
        permit.Setup(p => p.ChannelId).Returns(Guid.NewGuid());
        permit.Setup(p => p.SessionId).Returns("session-123");
        permit.Setup(p => p.HostName).Returns("localhost");
        permit.Setup(p => p.IsAuthenticated).Returns(false);
        permit.Setup(p => p.Badge).Returns(new Badge());

        var identity = IdentityContext.FromPermit(permit.Object);

        identity.SessionId.Should().Be("session-123");
        identity.IsAuthenticated.Should().BeFalse();
    }
}