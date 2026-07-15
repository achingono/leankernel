using LeanKernel.Logic.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LeanKernel.Logic.TurnRuntime;

/// <summary>
/// Deny-by-default context gatekeeper. All context items start as rejected;
/// admission requires passing both the minimum-score threshold and the token budget.
/// Items are scored and admitted in descending score order until the budget is exhausted.
/// </summary>
public sealed class ContextGatekeeper(
    IOptions<TurnPipelineSettings> settings,
    ILogger<ContextGatekeeper> logger) : ITurnStage
{
    private readonly TurnPipelineSettings _settings = settings.Value;

    /// <inheritdoc />
    public string Name => "ContextGatekeeper";

    /// <inheritdoc />
    public Task ExecuteAsync(TurnContext context, CancellationToken cancellationToken = default)
    {
        context.RemainingBudget = _settings.MaxContextTokens;

        // Allocate system/identity budget first
        var systemItems = context.Candidates
            .Where(c => c.Source is "system" or "identity")
            .OrderByDescending(c => c.Score)
            .ToList();

        var systemBudgetUsed = 0;
        foreach (var item in systemItems)
        {
            if (item.EstimatedTokens <= context.RemainingBudget)
            {
                context.Admitted.Add(item);
                context.RemainingBudget -= item.EstimatedTokens;
                systemBudgetUsed += item.EstimatedTokens;

                context.AdmissionTrace.Add(new AdmissionRecord
                {
                    Source = item.Source,
                    Admitted = true,
                    Reason = "system_context",
                    TokenCost = item.EstimatedTokens,
                    RemainingBudget = context.RemainingBudget
                });
            }
            else
            {
                context.AdmissionTrace.Add(new AdmissionRecord
                {
                    Source = item.Source,
                    Admitted = false,
                    Reason = "budget_exhausted",
                    TokenCost = item.EstimatedTokens,
                    RemainingBudget = context.RemainingBudget
                });

                logger.LogWarning(
                    "System context item from {Source} rejected: {TokenCost} tokens exceeds remaining budget {Budget}.",
                    item.Source, item.EstimatedTokens, context.RemainingBudget);
            }
        }

        // Cap system budget usage
        if (systemBudgetUsed > _settings.SystemContextTokenBudget)
        {
            logger.LogWarning(
                "System context used {Used} tokens, exceeding budget of {Budget}.",
                systemBudgetUsed, _settings.SystemContextTokenBudget);
        }

        // Allocate retrieval/memory budget
        var retrievalBudget = Math.Min(_settings.RetrievalTokenBudget, context.RemainingBudget);
        var retrievalItems = context.Candidates
            .Where(c => c.Source is "memory" or "retrieval")
            .OrderByDescending(c => c.Score)
            .ToList();

        var retrievalCount = 0;
        foreach (var item in retrievalItems)
        {
            if (retrievalCount >= _settings.MaxRetrievalCandidates)
            {
                context.AdmissionTrace.Add(new AdmissionRecord
                {
                    Source = item.Source,
                    Admitted = false,
                    Reason = "max_candidates_reached",
                    TokenCost = item.EstimatedTokens,
                    RemainingBudget = context.RemainingBudget
                });
                continue;
            }

            if (item.Score < _settings.MinRetrievalScore)
            {
                context.AdmissionTrace.Add(new AdmissionRecord
                {
                    Source = item.Source,
                    Admitted = false,
                    Reason = "low_score",
                    TokenCost = item.EstimatedTokens,
                    RemainingBudget = context.RemainingBudget
                });
                continue;
            }

            if (item.EstimatedTokens <= context.RemainingBudget && retrievalBudget > 0)
            {
                context.Admitted.Add(item);
                context.RemainingBudget -= item.EstimatedTokens;
                retrievalBudget -= item.EstimatedTokens;
                retrievalCount++;

                context.AdmissionTrace.Add(new AdmissionRecord
                {
                    Source = item.Source,
                    Admitted = true,
                    Reason = "admitted",
                    TokenCost = item.EstimatedTokens,
                    RemainingBudget = context.RemainingBudget
                });
            }
            else
            {
                context.AdmissionTrace.Add(new AdmissionRecord
                {
                    Source = item.Source,
                    Admitted = false,
                    Reason = "budget_exhausted",
                    TokenCost = item.EstimatedTokens,
                    RemainingBudget = context.RemainingBudget
                });
            }
        }

        // Admit all remaining non-retrieval, non-system items if budget allows
        var remainingItems = context.Candidates
            .Where(c => c.Source is not ("system" or "identity" or "memory" or "retrieval"))
            .OrderByDescending(c => c.Score)
            .ToList();

        foreach (var item in remainingItems)
        {
            if (item.EstimatedTokens <= context.RemainingBudget)
            {
                context.Admitted.Add(item);
                context.RemainingBudget -= item.EstimatedTokens;

                context.AdmissionTrace.Add(new AdmissionRecord
                {
                    Source = item.Source,
                    Admitted = true,
                    Reason = "admitted",
                    TokenCost = item.EstimatedTokens,
                    RemainingBudget = context.RemainingBudget
                });
            }
            else
            {
                context.AdmissionTrace.Add(new AdmissionRecord
                {
                    Source = item.Source,
                    Admitted = false,
                    Reason = "budget_exhausted",
                    TokenCost = item.EstimatedTokens,
                    RemainingBudget = context.RemainingBudget
                });
            }
        }

        logger.LogDebug(
            "Gatekeeping complete: {Admitted}/{Total} items admitted, {RemainingBudget} tokens remaining.",
            context.Admitted.Count, context.Candidates.Count, context.RemainingBudget);

        return Task.CompletedTask;
    }
}
