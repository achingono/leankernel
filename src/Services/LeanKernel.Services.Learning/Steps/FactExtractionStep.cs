using LeanKernel.Events;
using LeanKernel.Logic.Memory;

using Microsoft.Extensions.AI;

namespace LeanKernel.Services.Learning.Steps;

/// <summary>
/// Extracts facts from a completed conversation turn.
/// </summary>
public sealed class FactExtractionStep
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<FactExtractionStep> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="FactExtractionStep"/> class.
    /// </summary>
    /// <param name="scopeFactory">The service scope factory.</param>
    /// <param name="logger">The logger.</param>
    public FactExtractionStep(IServiceScopeFactory scopeFactory, ILogger<FactExtractionStep> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <summary>
    /// Extracts facts from the given turn event.
    /// </summary>
    /// <param name="turnEvent">The turn completed event.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A list of extracted fact strings.</returns>
    public async Task<IReadOnlyList<string>> ExecuteAsync(TurnCompletedEvent turnEvent, CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var factService = scope.ServiceProvider.GetRequiredService<FactExtractionService>();

        var facts = await factService.ExtractFactsAsync(
            turnEvent.UserMessage,
            turnEvent.AssistantResponse,
            [],
            ct);

        _logger.LogDebug("Extracted {Count} facts from turn {TurnId}", facts.Count, turnEvent.TurnId);
        return facts;
    }
}