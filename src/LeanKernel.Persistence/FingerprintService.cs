using LeanKernel.Abstractions.Interfaces;
using LeanKernel.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace LeanKernel.Persistence;

public sealed class FingerprintService : IDocumentFingerprintService
{
    private readonly IDbContextFactory<LeanKernelDbContext> _dbContextFactory;
    private readonly ILogger<FingerprintService> _logger;

    public FingerprintService(
        IDbContextFactory<LeanKernelDbContext> dbContextFactory,
        ILogger<FingerprintService> logger)
    {
        _dbContextFactory = dbContextFactory ?? throw new ArgumentNullException(nameof(dbContextFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<bool> IsKnownFingerprintAsync(string fingerprint, CancellationToken ct = default)
    {
        await using var ctx = await _dbContextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        return await ctx.DocumentFingerprints.AnyAsync(x => x.Fingerprint == fingerprint, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Records a fingerprint for deduplication. Uses check-then-insert with the AnyAsync
    /// guard as an optimization to avoid round-trips in the common case.
    /// The DbUpdateException catch handles the TOCTOU race when two writers both pass
    /// the check and collide on the PK constraint — the losing writer treats the
    /// duplicate as a no-op (idempotent).
    /// </summary>
    public async Task RecordFingerprintAsync(string fingerprint, string filePath, long fileSize, CancellationToken ct = default)
    {
        await using var ctx = await _dbContextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        if (await ctx.DocumentFingerprints.AnyAsync(x => x.Fingerprint == fingerprint, ct).ConfigureAwait(false))
        {
            return;
        }

        ctx.DocumentFingerprints.Add(new DocumentFingerprintEntity
        {
            Fingerprint = fingerprint,
            FilePath = filePath,
            FileSize = fileSize,
            CreatedAt = DateTimeOffset.UtcNow
        });

        try
        {
            await ctx.SaveChangesAsync(ct).ConfigureAwait(false);
            _logger.LogDebug("Recorded fingerprint {Fingerprint} for {FilePath}", fingerprint, filePath);
        }
        catch (DbUpdateException)
        {
            _logger.LogDebug("Fingerprint {Fingerprint} already recorded (concurrent write)", fingerprint);
        }
    }

    public string ComputeFingerprint(string filePath)
    {
        var normalizedPath = Path.GetFullPath(filePath).Replace(Path.DirectorySeparatorChar, '/');

        try
        {
            var fileInfo = new FileInfo(filePath);
            if (!fileInfo.Exists)
            {
                return $"{normalizedPath}|0|0";
            }

            return $"{normalizedPath}|{fileInfo.Length}|{fileInfo.LastWriteTimeUtc.Ticks}";
        }
        catch
        {
            return $"{normalizedPath}|0|0";
        }
    }

    public async Task RemoveFingerprintAsync(string fingerprint, CancellationToken ct = default)
    {
        await using var ctx = await _dbContextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var entity = await ctx.DocumentFingerprints.FindAsync(new object[] { fingerprint }, ct).ConfigureAwait(false);
        if (entity is not null)
        {
            ctx.DocumentFingerprints.Remove(entity);
            await ctx.SaveChangesAsync(ct).ConfigureAwait(false);
            _logger.LogInformation("Removed fingerprint {Fingerprint}", fingerprint);
        }
    }
}