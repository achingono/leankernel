using Microsoft.Extensions.Logging;
using LeanKernel.Host.Services;
using LeanKernel.Scheduler;

namespace LeanKernel.Host.Services.Jobs;

/// <summary>
/// Periodic user profile sync job that updates USER.md from extracted wiki facts.
/// Runs via cron (default: 4 AM UTC daily, configurable in SchedulerConfig).
/// Learns user preferences, expertise areas, and communication patterns from conversations.
/// </summary>
public sealed class UserProfileSyncJob : IAsyncJob
{
    private readonly SelfConfigurationStep _selfConfig;
    private readonly UserConfigurationStep _userConfig;
    private readonly ILogger<UserProfileSyncJob> _logger;

    public UserProfileSyncJob(
        SelfConfigurationStep selfConfig,
        UserConfigurationStep userConfig,
        ILogger<UserProfileSyncJob> logger)
    {
        _selfConfig = selfConfig;
        _userConfig = userConfig;
        _logger = logger;
    }

    /// <summary>
    /// Execute the user profile sync from wiki facts.
    /// </summary>
    public async Task ExecuteAsync(CancellationToken ct)
    {
        try
        {
            _logger.LogInformation("User profile sync job starting...");

            var selfInitResult = await _selfConfig.InitializeAsync(ct);
            if (!selfInitResult.Success)
            {
                _logger.LogWarning("SELF.md initialization failed during nightly sync: {Message}", selfInitResult.Message);
            }

            var userInitResult = await _userConfig.InitializeAsync(ct);
            if (!userInitResult.Success)
            {
                _logger.LogWarning("USER.md initialization failed during nightly sync: {Message}", userInitResult.Message);
            }

            var result = await _userConfig.SyncFromWikiAsync(ct);
            
            if (result.Success)
            {
                _logger.LogInformation("User profile sync completed successfully");
            }
            else
            {
                _logger.LogWarning("User profile sync completed with issues: {Message}", result.Message);
            }

            if (result.Errors.Any())
            {
                foreach (var error in result.Errors)
                {
                    _logger.LogWarning("Sync error: {Error}", error);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "User profile sync job failed");
        }
    }
}
