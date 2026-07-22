using System.Security.Claims;

using FluentAssertions;

using LeanKernel.Gateway.Providers;
using LeanKernel.Gateway.Requests;
using LeanKernel.Logic.Configuration;
using LeanKernel.Logic.Filters;

using Moq;

using Xunit;

namespace LeanKernel.Tests.Unit.Identity;

/// <summary>
/// Covers <see cref="RequestContextPermit{TEntity}"/> behavior.
/// </summary>
public sealed class RequestContextPermitOfTTests
{
    private sealed record TestEntity;

    [Fact]
    public void Can_WhenNotAuthenticatedAndAuthRequired_ReturnsFalse()
    {
        var (permit, _, _, _) = CreateSut<TestEntity>(
            isAuthenticated: false,
            requireAuthentication: true);

        var result = permit.Can(Operation.Read);

        result.Should().BeFalse();
    }

    [Fact]
    public void Can_WhenNotAuthenticatedAndAuthNotRequiredWithScopedIdentity_ReturnsTrue()
    {
        var (permit, _, _, _) = CreateSut<TestEntity>(
            isAuthenticated: false,
            requireAuthentication: false);

        var result = permit.Can(Operation.Read);

        result.Should().BeTrue();
    }

    [Fact]
    public void Can_WhenAuthenticatedWithAdminRole_ReturnsTrue()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Role, "admin"),
        }, "Bearer"));

        var (permit, _, _, _) = CreateSut<TestEntity>(principal: principal, isAuthenticated: true);

        var result = permit.Can(Operation.Delete);

        result.Should().BeTrue();
    }

    [Fact]
    public void Can_WhenAuthenticatedWithCorrectClaim_ReturnsTrue()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim("right", "Read:TestEntity"),
        }, "Bearer"));

        var (permit, _, _, _) = CreateSut<TestEntity>(principal: principal, isAuthenticated: true);

        var result = permit.Can(Operation.Read);

        result.Should().BeTrue();
    }

    [Fact]
    public void Can_WhenAuthenticatedWithWrongClaim_ReturnsFalse()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim("right", "Read:OtherEntity"),
        }, "Bearer"));

        var (permit, _, _, _) = CreateSut<TestEntity>(principal: principal, isAuthenticated: true);

        var result = permit.Can(Operation.Read);

        result.Should().BeFalse();
    }

    [Fact]
    public void Can_WhenAuthenticatedWithWrongOperationClaim_ReturnsFalse()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim("right", "Delete:TestEntity"),
        }, "Bearer"));

        var (permit, _, _, _) = CreateSut<TestEntity>(principal: principal, isAuthenticated: true);

        var result = permit.Can(Operation.Update);

        result.Should().BeFalse();
    }

    [Fact]
    public void DelegatesPropertiesToInnerPermit()
    {
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var channelId = Guid.NewGuid();

        var innerPermit = new Mock<IPermit>();
        innerPermit.Setup(p => p.UserId).Returns(userId);
        innerPermit.Setup(p => p.TenantId).Returns(tenantId);
        innerPermit.Setup(p => p.ChannelId).Returns(channelId);
        innerPermit.Setup(p => p.IsAuthenticated).Returns(true);
        innerPermit.Setup(p => p.HostName).Returns("test.local");

        var principalAccessor = new Mock<IPrincipalAccessor>();
        var policyProvider = new Mock<IScopePolicyProvider>();
        policyProvider.Setup(p => p.GetPolicy(typeof(TestEntity))).Returns(new EntityScopePolicy
        {
            EntityType = typeof(TestEntity).FullName!,
            Dimensions = ScopeDimension.Tenant,
        });

        var permit = new RequestContextPermit<TestEntity>(innerPermit.Object, principalAccessor.Object, policyProvider.Object);

        permit.UserId.Should().Be(userId);
        permit.TenantId.Should().Be(tenantId);
        permit.ChannelId.Should().Be(channelId);
        permit.IsAuthenticated.Should().BeTrue();
        permit.HostName.Should().Be("test.local");
    }

    private static (RequestContextPermit<TEntity> permit, Mock<IPermit> innerPermit, Mock<IPrincipalAccessor> principalAccessor, Mock<IScopePolicyProvider> policyProvider) CreateSut<TEntity>(
        ClaimsPrincipal? principal = null,
        bool isAuthenticated = false,
        bool requireAuthentication = true)
        where TEntity : class
    {
        var innerPermit = new Mock<IPermit>();
        innerPermit.Setup(p => p.IsAuthenticated).Returns(isAuthenticated);
        innerPermit.Setup(p => p.UserId).Returns(Guid.NewGuid());
        innerPermit.Setup(p => p.TenantId).Returns(Guid.NewGuid());
        innerPermit.Setup(p => p.ChannelId).Returns(Guid.NewGuid());

        var principalAccessor = new Mock<IPrincipalAccessor>();
        principalAccessor.Setup(a => a.Principal).Returns(principal);

        var policyProvider = new Mock<IScopePolicyProvider>();
        policyProvider.Setup(p => p.GetPolicy(typeof(TEntity))).Returns(new EntityScopePolicy
        {
            EntityType = typeof(TEntity).FullName!,
            Dimensions = ScopeDimension.Tenant | ScopeDimension.User,
            RequireAuthentication = requireAuthentication,
        });

        var permit = new RequestContextPermit<TEntity>(innerPermit.Object, principalAccessor.Object, policyProvider.Object);
        return (permit, innerPermit, principalAccessor, policyProvider);
    }
}