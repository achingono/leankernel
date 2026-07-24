using LeanKernel.Data;
using LeanKernel.Logic.Memory;

using Microsoft.EntityFrameworkCore;

namespace LeanKernel.Services.Learning.Onboarding;

/// <summary>
/// Detects gaps in the user onboarding experience.
/// </summary>
public sealed class OnboardingGapDetector
{
    private readonly IDbContextFactory<EntityContext> _contextFactory;
    private readonly IMemoryService _memoryService;
    private readonly ILogger<OnboardingGapDetector> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="OnboardingGapDetector"/> class.
    /// </summary>
    /// <param name="contextFactory">The context factory.</param>
    /// <param name="memoryService">The memory service.</param>
    /// <param name="logger">The logger.</param>
    public OnboardingGapDetector(
        IDbContextFactory<EntityContext> contextFactory,
        IMemoryService memoryService,
        ILogger<OnboardingGapDetector> logger)
    {
        _contextFactory = contextFactory;
        _memoryService = memoryService;
        _logger = logger;
    }

    /// <summary>
    /// Detects onboarding gaps for the specified user.
    /// </summary>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="userId">The user identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A list of detected onboarding gaps.</returns>
    public async Task<IReadOnlyList<OnboardingGap>> DetectGapsAsync(Guid tenantId, Guid userId, CancellationToken ct = default)
    {
        var gaps = new List<OnboardingGap>();

        _logger.LogDebug("Checking onboarding gaps for {TenantId}/{UserId}", tenantId, userId);

        await using var context = await _contextFactory.CreateDbContextAsync(ct);
        var user = await context.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId, ct);

        if (user is null)
        {
            gaps.Add(new OnboardingGap(
                "UserProfileMissing",
                "I don't have a profile for this user yet. Ask the user to confirm their name and contact details.",
                100));
            return gaps;
        }

        if (string.IsNullOrWhiteSpace(user.FullName)
            && string.IsNullOrWhiteSpace(user.FirstName)
            && string.IsNullOrWhiteSpace(user.PreferredUserName))
        {
            gaps.Add(new OnboardingGap(
                "FullNameMissing",
                "Ask the user what name they prefer to be addressed by and save it.",
                95));
        }

        if (string.IsNullOrWhiteSpace(user.Email))
        {
            gaps.Add(new OnboardingGap(
                "EmailMissing",
                "Ask the user for a preferred email address for reminders and notifications.",
                90));
        }

        if (string.IsNullOrWhiteSpace(user.TimeZone))
        {
            gaps.Add(new OnboardingGap(
                "TimeZoneMissing",
                "Ask the user for their time zone so scheduling and reminders happen at the right local time.",
                85));
        }

        if (string.IsNullOrWhiteSpace(user.Locale))
        {
            gaps.Add(new OnboardingGap(
                "LocaleMissing",
                "Ask the user for preferred language/locale so dates and phrasing match expectations.",
                70));
        }

        try
        {
            var intentQuery = $"identity intent preferences tenant {tenantId} user {userId}";
            var intentSignals = await _memoryService.SearchAsync(intentQuery, maxResults: 3, ct);
            if (intentSignals.Count == 0)
            {
                gaps.Add(new OnboardingGap(
                    "PreferencesUnknown",
                    "Ask the user about key working preferences (communication style, planning cadence, and priorities).",
                    60));
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Skipping preference-intent gap check because memory search failed.");
        }

        return gaps
            .OrderByDescending(g => g.Priority)
            .ToList();
    }
}
