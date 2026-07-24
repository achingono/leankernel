using LeanKernel.Logic.Memory;

namespace LeanKernel.Services.Learning.Onboarding;

/// <summary>
/// Builds and persists onboarding directives based on detected gaps.
/// </summary>
public sealed class OnboardingDirectiveBuilder
{
    private readonly IMemoryService _memoryService;
    private readonly ILogger<OnboardingDirectiveBuilder> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="OnboardingDirectiveBuilder"/> class.
    /// </summary>
    /// <param name="memoryService">The memory service for persisting directives.</param>
    /// <param name="logger">The logger.</param>
    public OnboardingDirectiveBuilder(
        IMemoryService memoryService,
        ILogger<OnboardingDirectiveBuilder> logger)
    {
        _memoryService = memoryService;
        _logger = logger;
    }

    /// <summary>
    /// Builds and persists onboarding directives for the given gaps.
    /// </summary>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="userId">The user identifier.</param>
    /// <param name="gaps">The detected onboarding gaps.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    public async Task BuildAndPersistDirectivesAsync(
        Guid tenantId, Guid userId, IReadOnlyList<OnboardingGap> gaps, CancellationToken ct = default)
    {
        foreach (var gap in gaps)
        {
            var key = $"onboarding/directive/{gap.GapType}";
            await _memoryService.PutPageAsync(key, gap.SuggestedDirective, ct);
            _logger.LogDebug("Persisted onboarding directive for gap {GapType}", gap.GapType);
        }
    }
}