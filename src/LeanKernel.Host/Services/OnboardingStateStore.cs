using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace LeanKernel.Host.Services;

/// <summary>
/// Represents the onboarding state store.
/// </summary>
public sealed class OnboardingStateStore : IOnboardingStateStore
{
    private readonly LeanKernelHostPaths _paths;
    private readonly ILogger<OnboardingStateStore> _logger;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    /// <summary>
    /// Initializes a new instance of the <see cref="OnboardingStateStore" /> class.
    /// </summary>
    /// <param name="paths">The paths.</param>
    /// <param name="logger">The logger.</param>
    public OnboardingStateStore(LeanKernelHostPaths paths, ILogger<OnboardingStateStore> logger)
    {
        _paths = paths;
        _logger = logger;
        Directory.CreateDirectory(_paths.DataDirectory);
    }

    /// <summary>
    /// Executes the get async operation.
    /// </summary>
    /// <param name="ct">The ct.</param>
    /// <returns>A task that represents the asynchronous operation and contains the result.</returns>
    public async Task<OnboardingStateDocument> GetAsync(CancellationToken ct)
    {
        if (!File.Exists(_paths.OnboardingStatePath))
        {
            return new OnboardingStateDocument
            {
                Completed = false,
                UpdatedAt = DateTimeOffset.UtcNow
            };
        }

        try
        {
            await using var stream = File.OpenRead(_paths.OnboardingStatePath);
            var state = await JsonSerializer.DeserializeAsync<OnboardingStateDocument>(stream, JsonOptions, ct);
            return state ?? new OnboardingStateDocument { UpdatedAt = DateTimeOffset.UtcNow };
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Onboarding state file is invalid JSON; resetting state");
            return new OnboardingStateDocument
            {
                Completed = false,
                UpdatedAt = DateTimeOffset.UtcNow
            };
        }
    }

    /// <summary>
    /// Executes the is completed async operation.
    /// </summary>
    /// <param name="ct">The ct.</param>
    /// <returns>A task that represents the asynchronous operation and contains the result.</returns>
    public async Task<bool> IsCompletedAsync(CancellationToken ct)
    {
        var state = await GetAsync(ct);
        return state.Completed;
    }

    /// <summary>
    /// Executes the mark in progress async operation.
    /// </summary>
    /// <param name="ct">The ct.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public async Task MarkInProgressAsync(CancellationToken ct)
    {
        var existing = await GetAsync(ct);
        if (existing.Completed)
            return;

        await SaveAsync(new OnboardingStateDocument
        {
            Completed = false,
            CompletedAt = null,
            UpdatedAt = DateTimeOffset.UtcNow,
            Version = existing.Version
        }, ct);
    }

    /// <summary>
    /// Executes the mark completed async operation.
    /// </summary>
    /// <param name="ct">The ct.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public async Task MarkCompletedAsync(CancellationToken ct)
    {
        await SaveAsync(new OnboardingStateDocument
        {
            Completed = true,
            CompletedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        }, ct);
    }

    private async Task SaveAsync(OnboardingStateDocument state, CancellationToken ct)
    {
        Directory.CreateDirectory(_paths.DataDirectory);

        var tempPath = _paths.OnboardingStatePath + ".tmp";
        await using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(stream, state, JsonOptions, ct);
        }

        File.Move(tempPath, _paths.OnboardingStatePath, overwrite: true);
    }
}
