using System.Text.Json;
using LeanKernel.Abstractions.Interfaces;
using LeanKernel.Abstractions.Models;
using LeanKernel.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace LeanKernel.Persistence;

/// <summary>
/// Provides PostgreSQL-backed diagnostic persistence.
/// </summary>
public sealed class PostgresDiagnosticsSink(
    IDbContextFactory<LeanKernelDbContext> dbFactory,
    ILogger<PostgresDiagnosticsSink> logger) : IDiagnosticsSink
{
    private readonly IDbContextFactory<LeanKernelDbContext> _dbFactory = dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));
    private readonly ILogger<PostgresDiagnosticsSink> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>
    /// Records a diagnostic entry.
    /// </summary>
    /// <param name="entry">The diagnostic entry to store.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A task that completes when the entry is stored.</returns>
    public async Task RecordAsync(DiagnosticEntry entry, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);

        var entity = new DiagnosticEntryEntity
        {
            Id = entry.Id,
            SessionId = entry.SessionId,
            TurnId = entry.TurnId,
            Category = entry.Category,
            Payload = JsonSerializer.Serialize(entry.Payload),
            Timestamp = entry.Timestamp,
        };

        db.DiagnosticEntries.Add(entity);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        _logger.LogDebug(
            "Recorded diagnostic entry {DiagnosticEntryId} for session {SessionId}",
            entity.Id,
            entity.SessionId);
    }

    /// <summary>
    /// Gets diagnostic entries for a session ordered by timestamp.
    /// </summary>
    /// <param name="sessionId">The session identifier.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The ordered diagnostic entries for the session.</returns>
    public async Task<IReadOnlyList<DiagnosticEntry>> GetEntriesAsync(string sessionId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        await using var db = await _dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);

        var entries = await db.DiagnosticEntries
            .AsNoTracking()
            .Where(e => e.SessionId == sessionId)
            .OrderBy(e => e.Timestamp)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return entries
            .Select(e => new DiagnosticEntry
            {
                Id = e.Id,
                SessionId = e.SessionId,
                TurnId = e.TurnId,
                Category = e.Category,
                Payload = JsonSerializer.Deserialize<JsonElement>(e.Payload),
                Timestamp = e.Timestamp,
            })
            .ToList();
    }
}
