using FluentAssertions;

using LeanKernel.Logic.Policy;

using Moq;

using Xunit;

namespace LeanKernel.Tests.Unit.Policy;

public class BudgetCheckPolicyTests
{
    [Fact]
    public void Evaluate_Authenticated_ReturnsAllow()
    {
        var identity = new IdentityContext { IsAuthenticated = true };
        var context = new Mock<IPolicyContext>();
        context.Setup(c => c.Identity).Returns(identity);

        var policy = new BudgetCheckPolicy();
        var result = policy.Evaluate(new object(), context.Object);

        result.IsAllowed.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_NotAuthenticated_ReturnsDeny()
    {
        var identity = new IdentityContext { IsAuthenticated = false };
        var context = new Mock<IPolicyContext>();
        context.Setup(c => c.Identity).Returns(identity);

        var policy = new BudgetCheckPolicy();
        var result = policy.Evaluate(new object(), context.Object);

        result.IsAllowed.Should().BeFalse();
        result.Reason.Should().Contain("authenticated");
    }
}