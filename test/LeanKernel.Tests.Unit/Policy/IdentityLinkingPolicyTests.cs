using FluentAssertions;

using LeanKernel.Entities;
using LeanKernel.Logic.Policy;

using Moq;

using Xunit;

namespace LeanKernel.Tests.Unit.Policy;

public class IdentityLinkingPolicyTests
{
    [Fact]
    public void Evaluate_GuestWithOwnPersonId_ReturnsAllow()
    {
        var userId = Guid.NewGuid();
        var user = new UserEntity
        {
            Id = userId,
            IsGuest = true,
            PersonId = userId,
            Email = "guest@example.com",
        };

        var policy = new IdentityLinkingPolicy();
        var context = new Mock<IPolicyContext>();

        var result = policy.Evaluate(user, context.Object);

        result.IsAllowed.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_GuestWithDifferentPersonId_ReturnsDeny()
    {
        var user = new UserEntity
        {
            Id = Guid.NewGuid(),
            IsGuest = true,
            PersonId = Guid.NewGuid(),
            Email = "guest@example.com",
        };

        var policy = new IdentityLinkingPolicy();
        var context = new Mock<IPolicyContext>();

        var result = policy.Evaluate(user, context.Object);

        result.IsAllowed.Should().BeFalse();
        result.Reason.Should().Contain("Guest");
    }

    [Fact]
    public void Evaluate_AuthenticatedUser_ReturnsAllow()
    {
        var userId = Guid.NewGuid();
        var user = new UserEntity
        {
            Id = userId,
            IsGuest = false,
            PersonId = userId,
            Email = "user@example.com",
        };

        var policy = new IdentityLinkingPolicy();
        var context = new Mock<IPolicyContext>();

        var result = policy.Evaluate(user, context.Object);

        result.IsAllowed.Should().BeTrue();
    }
}