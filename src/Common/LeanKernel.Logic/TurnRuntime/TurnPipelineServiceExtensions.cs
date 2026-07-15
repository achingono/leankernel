using LeanKernel.Logic.Configuration;
using LeanKernel.Logic.TurnRuntime;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Extension methods for registering turn pipeline services.
/// </summary>
public static class TurnPipelineServiceExtensions
{
    /// <summary>
    /// Registers the turn pipeline and its default stages.
    /// </summary>
    public static IServiceCollection AddTurnPipeline(this IServiceCollection services)
    {
        services.Configure<TurnPipelineSettings>(_ => { });

        services.AddScoped<ContextGatekeeper>();
        services.AddScoped<HistoryShaper>();
        services.AddScoped<PromptAssembler>();
        services.AddSingleton<TurnProgressBroker>();

        services.AddScoped<ITurnStage>(sp => sp.GetRequiredService<ContextGatekeeper>());
        services.AddScoped<ITurnStage>(sp => sp.GetRequiredService<HistoryShaper>());
        services.AddScoped<ITurnStage>(sp => sp.GetRequiredService<PromptAssembler>());

        services.AddScoped<TurnPipeline>();

        return services;
    }
}
