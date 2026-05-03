using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using LeanKernel.Core.Configuration;

namespace LeanKernel.Scheduler.Jobs;

/// <summary>
/// Phase 4 — Scheduled job that syncs model context-window and max-token limits
/// from live provider APIs into the LiteLLM config.yaml.
/// Shells out to <c>scripts/sync_litellm_model_limits.py</c> with <c>--write</c>
/// so the running LiteLLM proxy picks up the changes on its next hot-reload cycle.
/// </summary>
public sealed class ModelLimitSyncJob
{
    private readonly LeanKernelConfig _config;
    private readonly ILogger<ModelLimitSyncJob> _logger;

    public ModelLimitSyncJob(IOptions<LeanKernelConfig> config, ILogger<ModelLimitSyncJob> logger)
    {
        _config = config.Value;
        _logger = logger;
    }

    public async Task ExecuteAsync(CancellationToken ct)
    {
        if (!_config.Routing.Enabled)
        {
            _logger.LogDebug("ModelLimitSyncJob: routing disabled, skipping sync");
            return;
        }

        _logger.LogInformation("ModelLimitSyncJob: starting model-limit sync");

        // Locate the Python script relative to the application base directory.
        // In Docker the working directory is /app, and the scripts folder is mounted there.
        var appBase = AppContext.BaseDirectory;
        var scriptPath = Path.GetFullPath(Path.Combine(appBase, "../../../../scripts/sync_litellm_model_limits.py"));

        // Fallback: try the Docker path.
        if (!File.Exists(scriptPath))
            scriptPath = "/app/scripts/sync_litellm_model_limits.py";

        if (!File.Exists(scriptPath))
        {
            _logger.LogWarning("ModelLimitSyncJob: script not found at '{Path}', skipping", scriptPath);
            return;
        }

        var driftReportPath = Path.Combine(
            Path.GetDirectoryName(scriptPath) ?? "/tmp",
            $"drift-{DateTimeOffset.UtcNow:yyyy-MM-dd}.json");

        var psi = new ProcessStartInfo
        {
            FileName = "python3",
            ArgumentList = { scriptPath, "--write", "--drift-report", driftReportPath },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = psi };
        process.Start();

        var stdout = process.StandardOutput.ReadToEndAsync(ct);
        var stderr = process.StandardError.ReadToEndAsync(ct);

        await process.WaitForExitAsync(ct);
        await Task.WhenAll(stdout, stderr);

        if (process.ExitCode == 0)
        {
            _logger.LogInformation(
                "ModelLimitSyncJob: completed (exit 0). Output: {Output}",
                (await stdout).Trim());

            if (File.Exists(driftReportPath))
                _logger.LogInformation("ModelLimitSyncJob: drift report written to '{Path}'", driftReportPath);
        }
        else
        {
            _logger.LogWarning(
                "ModelLimitSyncJob: script exited with {Code}. Stderr: {Stderr}",
                process.ExitCode, (await stderr).Trim());
        }
    }
}
