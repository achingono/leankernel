using LeanKernel.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace LeanKernel.Persistence;

/// <summary>
/// Provides EF Core access to LeanKernel persistence data.
/// </summary>
public sealed class LeanKernelDbContext(DbContextOptions<LeanKernelDbContext> options) : DbContext(options)
{
    /// <summary>
    /// Gets the persisted sessions.
    /// </summary>
    public DbSet<SessionEntity> Sessions => Set<SessionEntity>();

    /// <summary>
    /// Gets the persisted conversation turns.
    /// </summary>
    public DbSet<TurnEntity> Turns => Set<TurnEntity>();

    /// <summary>
    /// Gets the persisted capability gap records.
    /// </summary>
    public DbSet<CapabilityGapEntity> CapabilityGaps => Set<CapabilityGapEntity>();

    /// <summary>
    /// Gets the persisted diagnostic entries.
    /// </summary>
    public DbSet<DiagnosticEntryEntity> DiagnosticEntries => Set<DiagnosticEntryEntity>();

    /// <summary>
    /// Gets the persisted compaction markers.
    /// </summary>
    public DbSet<CompactionMarkerEntity> CompactionMarkers => Set<CompactionMarkerEntity>();

    /// <summary>
    /// Gets the persisted scheduled job executions.
    /// </summary>
    public DbSet<ScheduledJobEntity> ScheduledJobExecutions => Set<ScheduledJobEntity>();

    /// <summary>
    /// Gets the persisted document ingestion jobs.
    /// </summary>
    public DbSet<DocumentIngestionJobEntity> DocumentIngestionJobs => Set<DocumentIngestionJobEntity>();

    /// <summary>
    /// Gets the persisted document fingerprints for deduplication.
    /// </summary>
    public DbSet<DocumentFingerprintEntity> DocumentFingerprints => Set<DocumentFingerprintEntity>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.HasDefaultSchema("engine");

        modelBuilder.Entity<SessionEntity>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.ChannelId, x.UserId }).IsUnique();
            entity.Property(x => x.UpdatedAt).IsConcurrencyToken();
            entity.HasMany(x => x.Turns)
                .WithOne(x => x.Session)
                .HasForeignKey(x => x.SessionId);
        });

        modelBuilder.Entity<TurnEntity>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.SessionId);
            entity.HasIndex(x => x.Timestamp);
            entity.Property(x => x.Metadata).HasColumnType("jsonb");
        });

        modelBuilder.Entity<CapabilityGapEntity>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.SessionId);
        });

        modelBuilder.Entity<DiagnosticEntryEntity>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.SessionId);
            entity.HasIndex(x => x.Timestamp);
        });

        modelBuilder.Entity<CompactionMarkerEntity>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.SessionId);
            entity.HasIndex(x => x.CompactedAt);
        });

        modelBuilder.Entity<ScheduledJobEntity>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.JobName);
            entity.HasIndex(x => new { x.JobName, x.ScheduledAt });
            entity.HasIndex(x => x.StartedAt);
        });

        modelBuilder.Entity<DocumentIngestionJobEntity>(entity =>
        {
            entity.HasKey(x => x.JobId);
            entity.HasIndex(x => x.Status);
            entity.HasIndex(x => x.CreatedAt);
            entity.HasIndex(x => x.CompletedAt);
        });

        modelBuilder.Entity<DocumentFingerprintEntity>(entity =>
        {
            entity.HasKey(x => x.Fingerprint);
            entity.HasIndex(x => x.FilePath);
            entity.Property(x => x.FilePath).IsRequired();
        });

        base.OnModelCreating(modelBuilder);
    }
}
