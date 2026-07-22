using FluentAssertions;

using LeanKernel.Logic.Policy;

using Moq;

using Xunit;

namespace LeanKernel.Tests.Unit.Policy;

public class AuthorizationGatePolicyTests
{
    [Fact]
    public void Evaluate_WhenPermitAllows_ReturnsAllow()
    {
        var permit = new Mock<IPermit<object>>();
        permit.Setup(p => p.Can(Operation.Update)).Returns(true);

        var policy = new AuthorizationGatePolicy<object>(permit.Object, Operation.Update);
        var context = new Mock<IPolicyContext>();

        var result = policy.Evaluate(new object(), context.Object);

        result.IsAllowed.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_WhenPermitDenies_ReturnsDenyWithOperationAndType()
    {
        var permit = new Mock<IPermit<object>>();
        permit.Setup(p => p.Can(Operation.Delete)).Returns(false);

        var policy = new AuthorizationGatePolicy<object>(permit.Object, Operation.Delete);
        var context = new Mock<IPolicyContext>();

        var result = policy.Evaluate(new object(), context.Object);

        result.IsAllowed.Should().BeFalse();
        result.Reason.Should().Contain("Delete");
        result.Reason.Should().Contain("Object");
    }

    [Fact]
    public void Name_IncludesEntityAndOperation()
    {
        var permit = new Mock<IPermit<string>>();
        var policy = new AuthorizationGatePolicy<string>(permit.Object, Operation.Read);

        policy.Name.Should().Be("AuthorizationGate:String:Read");
    }
}
