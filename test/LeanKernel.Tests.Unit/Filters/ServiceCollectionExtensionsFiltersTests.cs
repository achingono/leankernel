using FluentAssertions;

using LeanKernel.Entities;
using LeanKernel.Logic.Configuration;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Xunit;

namespace LeanKernel.Tests.Unit.Filters;

/// <summary>
/// Covers filter service registration defaults.
/// </summary>
public sealed class ServiceCollectionExtensionsFiltersTests
{
    [Fact]
    public void AddFilters_WhenNoConfiguredPolicies_AddsKnownEntityDefaults()
    {
        var services = new ServiceCollection();
        services.AddOptions();

        services.AddFilters();

        using var provider = services.BuildServiceProvider();
        var policies = provider.GetRequiredService<IOptions<EntityScopePolicies>>().Value;

        policies.Policies.Should().Contain(p => p.EntityType == typeof(SessionEntity).FullName);
        policies.Policies.Should().Contain(p => p.EntityType == typeof(TurnEntity).FullName && p.NavigationPath == "Session");
        policies.Policies.Should().Contain(p => p.EntityType == typeof(TurnTelemetryEntity).FullName && p.NavigationPath == "Turn.Session");
        policies.Policies.Should().Contain(p => p.EntityType == typeof(ChannelSenderBindingEntity).FullName);
        policies.Policies.Should().Contain(p => p.EntityType == typeof(ChannelMemoryPolicyEntity).FullName);
        policies.Policies.Should().Contain(p => p.EntityType == typeof(UserEntity).FullName);
        policies.Policies.Should().Contain(p => p.EntityType == typeof(TenantEntity).FullName);
    }
}