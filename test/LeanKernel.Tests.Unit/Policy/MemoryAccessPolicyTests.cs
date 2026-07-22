using FluentAssertions;

using LeanKernel.Entities;
using LeanKernel.Logic.Policy;

using Moq;

using Xunit;

namespace LeanKernel.Tests.Unit.Policy;

public class MemoryAccessPolicyTests
{
    [Fact]
    public void Evaluate_WithWildcardShareList_ReturnsAllow()
    {
        var context = CreateContext(Guid.NewGuid(), "tenant.example.com", "teams");
        var policy = new MemoryAccessPolicy();
        var entity = new ChannelMemoryPolicyEntity { ShareList = ChannelEntity.MemoryPolicyWildcard };

        var result = policy.Evaluate(entity, context.Object);

        result.IsAllowed.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_WithMatchingChannelNameMetadata_ReturnsAllow()
    {
        var channelId = Guid.NewGuid();
        var context = CreateContext(channelId, "tenant.example.com", "teams");
        var policy = new MemoryAccessPolicy();
        var entity = new ChannelMemoryPolicyEntity { ShareList = "teams,signal" };

        var result = policy.Evaluate(entity, context.Object);

        result.IsAllowed.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_WithMatchingChannelId_ReturnsAllow()
    {
        var channelId = Guid.NewGuid();
        var context = CreateContext(channelId, "tenant.example.com", null);
        var policy = new MemoryAccessPolicy();
        var entity = new ChannelMemoryPolicyEntity { ShareList = channelId.ToString() };

        var result = policy.Evaluate(entity, context.Object);

        result.IsAllowed.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_WithOnlyHostNameMatch_ReturnsDeny()
    {
        var channelId = Guid.NewGuid();
        var context = CreateContext(channelId, "tenant.example.com", "teams");
        var policy = new MemoryAccessPolicy();
        var entity = new ChannelMemoryPolicyEntity { ShareList = "tenant.example.com" };

        var result = policy.Evaluate(entity, context.Object);

        result.IsAllowed.Should().BeFalse();
    }

    private static Mock<IPolicyContext> CreateContext(Guid channelId, string hostName, string? channelName)
    {
        var metadata = new Dictionary<string, object?>();
        if (channelName is not null)
        {
            metadata["ChannelName"] = channelName;
        }

        var context = new Mock<IPolicyContext>();
        context.SetupGet(c => c.Identity).Returns(new IdentityContext
        {
            TenantId = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            PersonId = Guid.NewGuid(),
            ChannelId = channelId,
            HostName = hostName,
            IsAuthenticated = true,
        });
        context.SetupGet(c => c.Metadata).Returns(metadata);
        return context;
    }
}