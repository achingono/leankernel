using FluentAssertions;

using LeanKernel.Entities;
using LeanKernel.Logic.Policy;

using Microsoft.Extensions.DependencyInjection;

using Moq;

using Xunit;

namespace LeanKernel.Tests.Unit.Policy;

public class PolicyEvaluatorTests
{
    [Fact]
    public void Evaluate_WithNoPolicies_ReturnsAllow()
    {
        var evaluator = CreateEvaluator(services =>
        {
        });

        var context = new Mock<IPolicyContext>();
        var entity = new object();

        var result = evaluator.Evaluate(entity, context.Object);

        result.IsAllowed.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_WithAllowingPolicy_ReturnsAllow()
    {
        var policy = new Mock<IPolicy<object>>();
        policy.Setup(p => p.Name).Returns("TestPolicy");
        policy.Setup(p => p.Evaluate(It.IsAny<object>(), It.IsAny<IPolicyContext>()))
            .Returns(PolicyResult.Allow());

        var evaluator = CreateEvaluator(services => services.AddScoped(_ => policy.Object));
        var context = new Mock<IPolicyContext>();

        var result = evaluator.Evaluate(new object(), context.Object);

        result.IsAllowed.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_WithDenyingPolicy_ReturnsDenyWithReason()
    {
        var policy = new Mock<IPolicy<object>>();
        policy.Setup(p => p.Name).Returns("TestPolicy");
        policy.Setup(p => p.Evaluate(It.IsAny<object>(), It.IsAny<IPolicyContext>()))
            .Returns(PolicyResult.Deny("Not allowed"));

        var evaluator = CreateEvaluator(services => services.AddScoped(_ => policy.Object));
        var context = new Mock<IPolicyContext>();

        var result = evaluator.Evaluate(new object(), context.Object);

        result.IsAllowed.Should().BeFalse();
        result.Reason.Should().Be("Not allowed");
    }

    [Fact]
    public void Evaluate_ShortCircuitsOnFirstDeny()
    {
        var denyPolicy = new Mock<IPolicy<object>>();
        denyPolicy.Setup(p => p.Name).Returns("DenyPolicy");
        denyPolicy.Setup(p => p.Evaluate(It.IsAny<object>(), It.IsAny<IPolicyContext>()))
            .Returns(PolicyResult.Deny("First deny"));

        var allowPolicy = new Mock<IPolicy<object>>();
        allowPolicy.Setup(p => p.Name).Returns("AllowPolicy");
        allowPolicy.Setup(p => p.Evaluate(It.IsAny<object>(), It.IsAny<IPolicyContext>()))
            .Returns(PolicyResult.Allow());

        var evaluator = CreateEvaluator(services =>
        {
            services.AddScoped(_ => denyPolicy.Object);
            services.AddScoped(_ => allowPolicy.Object);
        });

        var context = new Mock<IPolicyContext>();

        var result = evaluator.Evaluate(new object(), context.Object);

        result.IsAllowed.Should().BeFalse();
        result.Reason.Should().Be("First deny");
        allowPolicy.Verify(p => p.Evaluate(It.IsAny<object>(), It.IsAny<IPolicyContext>()), Times.Never);
    }

    [Fact]
    public void EvaluateAll_ReturnsAllResults()
    {
        var policy1 = new Mock<IPolicy<object>>();
        policy1.Setup(p => p.Name).Returns("Policy1");
        policy1.Setup(p => p.Evaluate(It.IsAny<object>(), It.IsAny<IPolicyContext>()))
            .Returns(PolicyResult.Allow());

        var policy2 = new Mock<IPolicy<object>>();
        policy2.Setup(p => p.Name).Returns("Policy2");
        policy2.Setup(p => p.Evaluate(It.IsAny<object>(), It.IsAny<IPolicyContext>()))
            .Returns(PolicyResult.Deny("Not allowed"));

        var evaluator = CreateEvaluator(services =>
        {
            services.AddScoped(_ => policy1.Object);
            services.AddScoped(_ => policy2.Object);
        });

        var context = new Mock<IPolicyContext>();

        var results = evaluator.EvaluateAll(new object(), context.Object);

        results.Should().HaveCount(2);
        results[0].IsAllowed.Should().BeTrue();
        results[1].IsAllowed.Should().BeFalse();
        results[1].Reason.Should().Be("Not allowed");
    }

    [Fact]
    public void Evaluate_WithTypedPolicyRegisteredInDi_EvaluatesTypedPolicy()
    {
        var typed = new Mock<IPolicy<UserEntity>>();
        typed.Setup(p => p.Name).Returns("Typed");
        typed.Setup(p => p.Evaluate(It.IsAny<UserEntity>(), It.IsAny<IPolicyContext>()))
            .Returns(PolicyResult.Deny("typed deny"));

        var evaluator = CreateEvaluator(services => services.AddScoped(_ => typed.Object));
        var context = new Mock<IPolicyContext>();

        var result = evaluator.Evaluate(new UserEntity { Email = "user@example.com" }, context.Object);

        result.IsAllowed.Should().BeFalse();
        result.Reason.Should().Be("typed deny");
        typed.Verify(p => p.Evaluate(It.IsAny<UserEntity>(), It.IsAny<IPolicyContext>()), Times.Once);
    }

    [Fact]
    public void AddPolicyCore_RegistersTypedPolicies_AndEvaluatorRunsThem()
    {
        var services = new ServiceCollection();
        services.AddScoped<IPolicyEvaluator, PolicyEvaluator>();
        services.AddScoped<IPermit>(_ => Mock.Of<IPermit>());
        services.AddPolicyCore();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var evaluator = scope.ServiceProvider.GetRequiredService<IPolicyEvaluator>();

        var context = new Mock<IPolicyContext>();
        context.SetupGet(c => c.Identity).Returns(new IdentityContext { IsAuthenticated = true });
        context.SetupGet(c => c.Metadata).Returns(new Dictionary<string, object?>());

        var user = new LeanKernel.Entities.UserEntity
        {
            Id = Guid.NewGuid(),
            IsGuest = true,
            PersonId = Guid.NewGuid(),
            Email = "guest@example.com",
        };

        var result = evaluator.Evaluate(user, context.Object);

        result.IsAllowed.Should().BeFalse();
        result.Reason.Should().Contain("Guest");
    }

    private static IPolicyEvaluator CreateEvaluator(Action<IServiceCollection> configure)
    {
        var services = new ServiceCollection();
        services.AddScoped<IPolicyEvaluator, PolicyEvaluator>();
        configure(services);

        var provider = services.BuildServiceProvider();
        var scope = provider.CreateScope();
        return scope.ServiceProvider.GetRequiredService<IPolicyEvaluator>();
    }
}