using LeanKernel.Logic.Configuration;
using LeanKernel.Logic.TurnRuntime;
using Microsoft.Extensions.Options;

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
        services
            .AddOptions<TurnPipelineSettings>()
            .BindConfiguration("TurnPipeline")
            .Validate(
                settings =>
                    settings.MaxContextTokens > 0 &&
                    settings.SystemContextTokenBudget >= 0 &&
                    settings.RetrievalTokenBudget >= 0 &&
                    settings.SystemContextTokenBudget <= settings.MaxContextTokens &&
                    settings.RetrievalTokenBudget <= settings.MaxContextTokens &&
                    settings.RecentTurnsVerbatim >= 0 &&
                    settings.CompactedTurnsMax >= 0 &&
                    settings.SummarizedTurnsMax >= 0 &&
                    settings.SummarizationTemperature is >= 0 and <= 1 &&
                    settings.SummarizationMaxOutputTokens > 0 &&
                    settings.MaxRetrievalCandidates >= 0 &&
                    settings.MinRetrievalScore is >= 0 and <= 1 &&
                    settings.MaxContinuationRounds >= 1 &&
                    settings.MaxPipelineDuration > TimeSpan.Zero,
                "Invalid TurnPipeline settings.")
            .ValidateOnStart();

        services.AddScoped<ContextGatekeeper>();
        services.AddScoped<IHistorySummarizer, HistorySummarizer>();
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
