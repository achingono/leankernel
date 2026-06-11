using System.Diagnostics;
using LeanKernel.Abstractions.Configuration;
using LeanKernel.Abstractions.Interfaces;
using LeanKernel.Abstractions.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LeanKernel.Agents.Enhancement;

/// <summary>
/// Runs configured enhancement steps in deterministic order before response delivery.
/// </summary>
public sealed class ResponseEnhancementPipeline(
    IEnumerable<IEnhancementStep> steps,
    IOptions<LeanKernelConfig> config,
    ILogger<ResponseEnhancementPipeline> logger) : IResponseEnhancer
{
    private readonly IReadOnlyList<IEnhancementStep> _steps = (steps ?? throw new ArgumentNullException(nameof(steps)))
        .OrderBy(step => step.Order)
        .ThenBy(step => step.Name, StringComparer.Ordinal)
        .ToArray();
    private readonly EnhancementConfig _config = (config ?? throw new ArgumentNullException(nameof(config))).Value.Enhancement;
    private readonly ILogger<ResponseEnhancementPipeline> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <inheritdoc />
    public async Task<EnhancementResult> EnhanceAsync(EnhancementStepInput input, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        var stopwatch = Stopwatch.StartNew();
        if (!_config.Enabled || _steps.Count == 0)
        {
            stopwatch.Stop();
            return CreateResult(input.Response, input.Response, [], stopwatch.Elapsed);
        }

        var currentResponse = input.Response;
        var stepResults = new List<EnhancementStepResult>(_steps.Count);
        var timeoutMs = Math.Max(1, _config.MaxEnhancementTimeMs);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromMilliseconds(timeoutMs));

        foreach (var step in _steps)
        {
            if (timeoutCts.IsCancellationRequested)
            {
                stopwatch.Stop();
                _logger.LogWarning(
                    "Response enhancement timed out after {DurationMs:0}ms before step {StepName} could start",
                    stopwatch.Elapsed.TotalMilliseconds,
                    step.Name);
                return CreateResult(input.Response, input.Response, stepResults, stopwatch.Elapsed);
            }

            var stepStopwatch = Stopwatch.StartNew();

            try
            {
                var output = await step.ExecuteAsync(input with { Response = currentResponse }, timeoutCts.Token).ConfigureAwait(false);
                stepStopwatch.Stop();

                currentResponse = output.Response;
                stepResults.Add(new EnhancementStepResult
                {
                    StepName = step.Name,
                    Applied = true,
                    Modified = output.Modified,
                    Reason = output.Reason,
                    Duration = stepStopwatch.Elapsed
                });
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                stepStopwatch.Stop();
                stopwatch.Stop();

                // If the pipeline's internal timeout token triggered, treat this as a timeout and
                // discard any partial step output.
                if (timeoutCts.IsCancellationRequested)
                {
                    stepResults.Add(new EnhancementStepResult
                    {
                        StepName = step.Name,
                        Applied = false,
                        Modified = false,
                        Reason = "Enhancement timed out before completion.",
                        Duration = stepStopwatch.Elapsed
                    });

                    _logger.LogWarning(
                        "Response enhancement timed out after {DurationMs:0}ms on step {StepName}",
                        stopwatch.Elapsed.TotalMilliseconds,
                        step.Name);

                    return CreateResult(input.Response, input.Response, stepResults, stopwatch.Elapsed);
                }

                stepResults.Add(new EnhancementStepResult
                {
                    StepName = step.Name,
                    Applied = false,
                    Modified = false,
                    Reason = "Enhancement timed out before completion.",
                    Duration = stepStopwatch.Elapsed
                });
                _logger.LogWarning(
                    "Response enhancement timed out after {DurationMs:0}ms on step {StepName}",
                    stopwatch.Elapsed.TotalMilliseconds,
                    step.Name);
                return CreateResult(input.Response, input.Response, stepResults, stopwatch.Elapsed);
            }
            catch (Exception ex)
            {
                stepStopwatch.Stop();
                stopwatch.Stop();
                stepResults.Add(new EnhancementStepResult
                {
                    StepName = step.Name,
                    Applied = false,
                    Modified = false,
                    Reason = $"Enhancement failed: {ex.GetType().Name}.",
                    Duration = stepStopwatch.Elapsed
                });
                _logger.LogWarning(ex, "Response enhancement failed on step {StepName}", step.Name);
                return CreateResult(input.Response, input.Response, stepResults, stopwatch.Elapsed);
            }
        }

        stopwatch.Stop();
        return CreateResult(input.Response, currentResponse, stepResults, stopwatch.Elapsed);
    }

    private static EnhancementResult CreateResult(
        string originalResponse,
        string enhancedResponse,
        IReadOnlyList<EnhancementStepResult> steps,
        TimeSpan totalDuration)
        => new()
        {
            OriginalResponse = originalResponse,
            EnhancedResponse = enhancedResponse,
            WasModified = !string.Equals(originalResponse, enhancedResponse, StringComparison.Ordinal),
            Steps = steps,
            TotalDuration = totalDuration
        };
}
