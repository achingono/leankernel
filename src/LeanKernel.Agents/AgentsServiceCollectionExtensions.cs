using LeanKernel.Abstractions.Configuration;
using LeanKernel.Abstractions.Interfaces;
using LeanKernel.Agents.Enhancement;
using LeanKernel.Agents.Orchestration;
using LeanKernel.Agents.Quality;
using LeanKernel.Agents.Routing;
using LeanKernel.Agents.Strategies;
using LeanKernel.Agents.ToolSelection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LeanKernel.Agents;

public static class AgentsServiceCollectionExtensions
{
    public static IServiceCollection AddLeanKernelAgents(this IServiceCollection services)
        => services.AddLeanKernelAgents(new LeanKernelConfig());

    public static IServiceCollection AddLeanKernelAgents(this IServiceCollection services, LeanKernelConfig config)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(config);

        services.TryAddSingleton<IOptions<LeanKernelConfig>>(Options.Create(config));
        services.AddSingleton<AgentFactory>();
        services.AddSingleton<TaskComplexityScorer>();
        services.AddSingleton<PolicyModelSelector>();
        services.AddSingleton<EscalationPolicy>();
        services.AddSingleton<EmptyResponseCheck>();
        services.AddSingleton<MinLengthCheck>();
        services.AddSingleton<RefusalDetectionCheck>();
        services.AddSingleton<ConstraintCoverageCheck>();
        services.AddSingleton<IResponseQualityGate, ResponseQualityGate>();
        RegisterResponseEnhancement(services, config);
        services.AddSingleton<StaticAgentStrategy>();
        services.AddSingleton<RoutedAgentStrategy>();
        services.AddSingleton<OrchestrationDecider>();
        services.AddSingleton<IReadOnlyList<WorkerAgent>>(provider =>
        {
            var resolvedConfig = provider.GetRequiredService<IOptions<LeanKernelConfig>>().Value;
            return resolvedConfig.Orchestration.Workers
                .Select(worker => ActivatorUtilities.CreateInstance<WorkerAgent>(provider, worker))
                .ToArray();
        });
        services.AddSingleton<OrchestratedAgentStrategy>();
        services.AddSingleton<ShadowComparer>();
        services.AddSingleton<IAgentStrategy>(provider =>
        {
            var resolvedConfig = provider.GetRequiredService<IOptions<LeanKernelConfig>>().Value;
            IAgentStrategy primaryStrategy = resolvedConfig.Orchestration.Enabled
                ? provider.GetRequiredService<OrchestratedAgentStrategy>()
                : resolvedConfig.Routing.Enabled
                    ? provider.GetRequiredService<RoutedAgentStrategy>()
                    : provider.GetRequiredService<StaticAgentStrategy>();

            if (!resolvedConfig.Routing.ShadowRoutingEnabled || string.IsNullOrWhiteSpace(resolvedConfig.Routing.ShadowModel))
            {
                return primaryStrategy;
            }

            return new ShadowRoutingStrategy(
                primaryStrategy,
                provider.GetRequiredService<AgentFactory>(),
                provider.GetRequiredService<ShadowComparer>(),
                provider.GetRequiredService<IOptions<LeanKernelConfig>>(),
                provider.GetRequiredService<ILogger<ShadowRoutingStrategy>>(),
                provider.GetService<IDiagnosticsSink>());
        });
        services.AddSingleton<IToolSelector, ToolSelector>();
        services.AddScoped<ITurnPipeline, TurnPipeline>();
        services.AddScoped<IAgentRuntime, AgentRuntime>();

        return services;
    }

    private static void RegisterResponseEnhancement(IServiceCollection services, LeanKernelConfig config)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(config);

        if (!config.Enhancement.Enabled)
        {
            return;
        }

        if (config.Enhancement.KnowledgeSynthesisEnabled)
        {
            services.AddSingleton<IEnhancementStep, KnowledgeSynthesisStep>();
        }

        if (config.Enhancement.RefusalInterceptionEnabled)
        {
            services.AddSingleton<IEnhancementStep, RefusalInterceptionStep>();
        }

        if (config.Enhancement.CitationInjectionEnabled)
        {
            services.AddSingleton<IEnhancementStep, CitationInjectionStep>();
        }

        services.AddSingleton<IResponseEnhancer, ResponseEnhancementPipeline>();
    }
}
