using FluentAssertions;

using LeanKernel.Entities;
using LeanKernel.Logic.Policy;

using Moq;

using Xunit;

namespace LeanKernel.Tests.Unit.Policy;

public class PolicyContextTests
{
    [Fact]
    public void Ctor_MapsIdentityFromPermit()
    {
        var tenantId = Guid.NewGuid();
        var personId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var channelId = Guid.NewGuid();
        var badge = new Badge { Id = userId, FullName = "User", Email = "user@example.com" };

        var permit = new Mock<IPermit>();
        permit.SetupGet(p => p.TenantId).Returns(tenantId);
        permit.SetupGet(p => p.PersonId).Returns(personId);
        permit.SetupGet(p => p.UserId).Returns(userId);
        permit.SetupGet(p => p.ChannelId).Returns(channelId);
        permit.SetupGet(p => p.SessionId).Returns("session-1");
        permit.SetupGet(p => p.HostName).Returns("localhost");
        permit.SetupGet(p => p.IsAuthenticated).Returns(true);
        permit.SetupGet(p => p.Badge).Returns(badge);

        var context = new PolicyContext(permit.Object);

        context.Permit.Should().BeSameAs(permit.Object);
        context.Identity.TenantId.Should().Be(tenantId);
        context.Identity.PersonId.Should().Be(personId);
        context.Identity.UserId.Should().Be(userId);
        context.Identity.ChannelId.Should().Be(channelId);
        context.Identity.SessionId.Should().Be("session-1");
        context.Metadata.Should().BeEmpty();
    }
}
