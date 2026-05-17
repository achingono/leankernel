using System.Text.Json;
using Microsoft.Extensions.Logging;
using LeanKernel.Core.Interfaces;
using LeanKernel.Core.Models;

namespace LeanKernel.Scheduler;

/// <summary>
/// File-backed store for scheduled jobs and job state.
/// </summary>
public sealed class FileScheduledJobStore : IScheduledJobStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly string _directoryPath;
    private readonly string _jobsPath;
    private readonly string _statesPath;
    private readonly ILogger<FileScheduledJobStore> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="FileScheduledJobStore" /> class.
    /// </summary>
    public FileScheduledJobStore(string dataDirectory, ILogger<FileScheduledJobStore> logger)
    {
        _directoryPath = Path.Combine(dataDirectory, "scheduler");
        _jobsPath = Path.Combine(_directoryPath, "jobs.json");
        _statesPath = Path.Combine(_directoryPath, "jobs-state.json");
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ScheduledJobStoreSnapshot> LoadAsync(CancellationToken ct)
    {
        Directory.CreateDirectory(_directoryPath);

        var snapshot = new ScheduledJobStoreSnapshot();

        if (File.Exists(_jobsPath))
        {
            try
            {
                await using var jobsStream = File.OpenRead(_jobsPath);
                var jobsDoc = await JsonSerializer.DeserializeAsync<JobsDocument>(jobsStream, JsonOptions, ct);
                if (jobsDoc?.Jobs is not null)
                    snapshot.Jobs = jobsDoc.Jobs;
                snapshot.Version = jobsDoc?.Version ?? snapshot.Version;
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Scheduled job definition file is invalid JSON: {Path}", _jobsPath);
            }
        }

        if (File.Exists(_statesPath))
        {
            try
            {
                await using var statesStream = File.OpenRead(_statesPath);
                var stateDoc = await JsonSerializer.DeserializeAsync<StateDocument>(statesStream, JsonOptions, ct);
                if (stateDoc?.Jobs is not null)
                    snapshot.States = stateDoc.Jobs;
                snapshot.Version = Math.Max(snapshot.Version, stateDoc?.Version ?? snapshot.Version);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Scheduled job state file is invalid JSON: {Path}", _statesPath);
            }
        }

        return snapshot;
    }

    /// <inheritdoc />
    public async Task SaveAsync(ScheduledJobStoreSnapshot snapshot, CancellationToken ct)
    {
        Directory.CreateDirectory(_directoryPath);

        var jobsDoc = new JobsDocument
        {
            Version = snapshot.Version,
            Jobs = snapshot.Jobs
        };

        var stateDoc = new StateDocument
        {
            Version = snapshot.Version,
            Jobs = snapshot.States
        };

        await WriteAtomicallyAsync(_jobsPath, jobsDoc, ct);
        await WriteAtomicallyAsync(_statesPath, stateDoc, ct);
    }

    private static async Task WriteAtomicallyAsync<T>(string path, T payload, CancellationToken ct)
    {
        var tempPath = $"{path}.tmp";
        await using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(stream, payload, JsonOptions, ct);
        }

        File.Move(tempPath, path, overwrite: true);
    }

    private sealed class JobsDocument
    {
        public int Version { get; init; } = 1;
        public List<ScheduledJobDefinition> Jobs { get; init; } = [];
    }

    private sealed class StateDocument
    {
        public int Version { get; init; } = 1;
        public Dictionary<string, ScheduledJobState> Jobs { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
