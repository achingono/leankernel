using FluentAssertions;

using LeanKernel.Entities;
using LeanKernel.Logic.Configuration;
using LeanKernel.Logic.Filters;

using Microsoft.Extensions.Options;

using Xunit;

namespace LeanKernel.Tests.Unit.Filters;

/// <summary>
/// Covers <see cref="ConfigurationScopePolicyProvider"/> resolution and fail-closed behavior.
/// </summary>
public sealed class ConfigurationScopePolicyProviderTests
{
    [Fact]
    public void GetPolicy_WhenPolicyConfigured_ReturnsPolicy()
    {
        var policies = new EntityScopePolicies
        {
            Policies =
            [
                new EntityScopePolicy
                {
                    EntityType = typeof(SessionEntity).FullName!,
                    Dimensions = ScopeDimension.Tenant | ScopeDimension.User | ScopeDimension.Channel,
                },
            ],
        };

        var provider = new ConfigurationScopePolicyProvider(Options.Create(policies));

        var result = provider.GetPolicy(typeof(SessionEntity));

        result.Should().NotBeNull();
        result.Dimensions.Should().Be(ScopeDimension.Tenant | ScopeDimension.User | ScopeDimension.Channel);
    }

    [Fact]
    public void GetPolicy_WhenPolicyMissing_ThrowsInvalidOperation()
    {
        var policies = new EntityScopePolicies { Policies = [] };
        var provider = new ConfigurationScopePolicyProvider(Options.Create(policies));

        var act = () => provider.GetPolicy(typeof(SessionEntity));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*No scope policy configured*");
    }

    [Fact]
    public void GetPolicy_UsesTypeNameWhenFullNameConfigured()
    {
        var policies = new EntityScopePolicies
        {
            Policies =
            [
                new EntityScopePolicy
                {
                    EntityType = nameof(SessionEntity),
                    Dimensions = ScopeDimension.Tenant,
                },
            ],
        };

        var provider = new ConfigurationScopePolicyProvider(Options.Create(policies));

        var result = provider.GetPolicy(typeof(SessionEntity));

        result.Should().NotBeNull();
        result.Dimensions.Should().Be(ScopeDimension.Tenant);
    }
}