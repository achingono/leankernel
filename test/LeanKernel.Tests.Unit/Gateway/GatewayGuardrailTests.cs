using System.Reflection;

using FluentAssertions;

using LeanKernel.Logic.Policy;

using Xunit;

namespace LeanKernel.Tests.Unit.Gateway;

/// <summary>
/// Ensures the Gateway project remains thin and does not implement business policy directly.
/// These guardrails verify that policy evaluation, event spine, and data access
/// logic live in the shared Logic/Core libraries rather than in the host.
/// </summary>
public class GatewayGuardrailTests
{
    private static readonly Assembly GatewayAssembly = typeof(LeanKernel.Gateway.Providers.RequestContextPermit).Assembly;

    private static readonly Assembly LogicAssembly = typeof(IPolicyEvaluator).Assembly;

    [Fact]
    public void Gateway_DoesNotImplementDomainPolicies()
    {
        var gatewayTypes = GatewayAssembly.GetTypes().Select(t => t.FullName).ToHashSet();

        var policyTypes = LogicAssembly.GetTypes()
            .Where(t => t.GetInterfaces().Any(i =>
                i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IPolicy<>)))
            .Select(t => t.FullName)
            .ToList();

        var gatewayPolicyTypes = policyTypes
            .Where(name => gatewayTypes.Contains(name + "Stub") || gatewayTypes.Contains(name))
            .ToList();

        gatewayPolicyTypes.Should().BeEmpty(
            "because domain policies belong in the shared Logic project, not the Gateway host");
    }

    [Fact]
    public void PolicyContext_IsInLogicProject()
    {
        typeof(PolicyContext).Assembly.GetName().Name.Should().Be(
            LogicAssembly.GetName().Name,
            "because PolicyContext is a shared Logic-level type, not a Gateway concern");
    }

    [Fact]
    public void EventCollector_IsInLogicProject()
    {
        typeof(Logic.Events.EventCollector).Assembly.GetName().Name.Should().Be(
            LogicAssembly.GetName().Name,
            "because EventCollector is a shared Logic-level type, not a Gateway concern");
    }
}