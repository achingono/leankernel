using FluentAssertions;
using LeanKernel.Abstractions.Configuration;
using LeanKernel.Abstractions.Interfaces;
using LeanKernel.Agents;
using LeanKernel.Agents.Strategies;
using LeanKernel.Agents.ToolSelection;
using Microsoft.Extensions.DependencyInjection;

namespace LeanKernel.Tests.Unit.Agents;

public class AgentsServiceCollectionExtensionsTests
{
    [Fact]
    public void AddLeanKernelAgents_throws_on_null_services()
    {
        var act = () => ((IServiceCollection)null!).AddLeanKernelAgents(new LeanKernelConfig());
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void AddLeanKernelAgents_throws_on_null_config()
    {
        var services = new ServiceCollection();
        var act = () => services.AddLeanKernelAgents(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void AddLeanKernelAgents_registers_core_services_as_singletons()
    {
        var services = new ServiceCollection();
        services.AddLeanKernelAgents(new LeanKernelConfig());

        services.Should().Contain(sd =>
            sd.ServiceType == typeof(IAgentStrategy) &&
            sd.Lifetime == ServiceLifetime.Singleton);

        services.Should().Contain(sd =>
            sd.ServiceType == typeof(IToolSelector) &&
            sd.Lifetime == ServiceLifetime.Singleton);

        services.Should().Contain(sd =>
            sd.ServiceType == typeof(ITurnProgressBroker) &&
            sd.Lifetime == ServiceLifetime.Singleton);

        services.Should().Contain(sd =>
            sd.ServiceType == typeof(ISessionTurnCoordinator) &&
            sd.Lifetime == ServiceLifetime.Singleton);

        services.Should().Contain(sd =>
            sd.ServiceType == typeof(IResponseQualityGate) &&
            sd.Lifetime == ServiceLifetime.Singleton);
    }

    [Fact]
    public void AddLeanKernelAgents_registers_scoped_pipeline_services()
    {
        var services = new ServiceCollection();
        services.AddLeanKernelAgents(new LeanKernelConfig());

        services.Should().Contain(sd =>
            sd.ServiceType == typeof(ITurnPipeline) &&
            sd.Lifetime == ServiceLifetime.Scoped);

        services.Should().Contain(sd =>
            sd.ServiceType == typeof(IAgentRuntime) &&
            sd.Lifetime == ServiceLifetime.Scoped);
    }

    [Fact]
    public void AddLeanKernelAgents_registers_response_enhancement_when_enabled()
    {
        var services = new ServiceCollection();
        services.AddLeanKernelAgents(new LeanKernelConfig
        {
            Enhancement = new EnhancementConfig
            {
                Enabled = true,
                KnowledgeSynthesisEnabled = false,
                RefusalInterceptionEnabled = false,
                CitationInjectionEnabled = false
            }
        });

        // Even with no steps, the pipeline itself should be registered
        services.Should().Contain(sd =>
            sd.ServiceType == typeof(IResponseEnhancer) &&
            sd.Lifetime == ServiceLifetime.Singleton);
    }

    [Fact]
    public void AddLeanKernelAgents_registers_enhancement_steps_when_enabled()
    {
        var services = new ServiceCollection();
        services.AddLeanKernelAgents(new LeanKernelConfig
        {
            Enhancement = new EnhancementConfig
            {
                Enabled = true,
                KnowledgeSynthesisEnabled = true,
                RefusalInterceptionEnabled = true,
                CitationInjectionEnabled = true
            }
        });

        services.Should().Contain(sd =>
            sd.ServiceType == typeof(IEnhancementStep) &&
            sd.Lifetime == ServiceLifetime.Singleton);
    }

    [Fact]
    public void AddLeanKernelAgents_does_not_register_enhancement_when_disabled()
    {
        var services = new ServiceCollection();
        services.AddLeanKernelAgents(new LeanKernelConfig
        {
            Enhancement = new EnhancementConfig { Enabled = false }
        });

        services.Should().NotContain(sd =>
            sd.ServiceType == typeof(IResponseEnhancer));
    }

    [Fact]
    public void AddLeanKernelAgents_registers_static_strategy_when_orchestration_and_routing_disabled()
    {
        var services = new ServiceCollection();
        services.AddLeanKernelAgents(new LeanKernelConfig
        {
            Orchestration = new OrchestrationConfig { Enabled = false },
            Routing = new RoutingConfig { Enabled = false }
        });

        // IAgentStrategy should resolve to StaticAgentStrategy
        services.Should().Contain(sd =>
            sd.ServiceType == typeof(IAgentStrategy) &&
            sd.Lifetime == ServiceLifetime.Singleton);
    }

    [Fact]
    public void AddLeanKernelAgents_registers_orchestrated_strategy_when_orchestration_enabled()
    {
        var services = new ServiceCollection();
        services.AddLeanKernelAgents(new LeanKernelConfig
        {
            Orchestration = new OrchestrationConfig { Enabled = true },
            Routing = new RoutingConfig { Enabled = false }
        });

        services.Should().Contain(sd =>
            sd.ServiceType == typeof(IAgentStrategy) &&
            sd.Lifetime == ServiceLifetime.Singleton);
    }

    [Fact]
    public void AddLeanKernelAgents_registers_routed_strategy_when_routing_enabled()
    {
        var services = new ServiceCollection();
        services.AddLeanKernelAgents(new LeanKernelConfig
        {
            Orchestration = new OrchestrationConfig { Enabled = false },
            Routing = new RoutingConfig { Enabled = true }
        });

        services.Should().Contain(sd =>
            sd.ServiceType == typeof(IAgentStrategy) &&
            sd.Lifetime == ServiceLifetime.Singleton);
    }
}
