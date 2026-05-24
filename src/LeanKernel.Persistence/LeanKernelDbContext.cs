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

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.HasDefaultSchema("engine");

        modelBuilder.Entity<SessionEntity>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.ChannelId, x.UserId }).IsUnique();
            entity.HasMany(x => x.Turns)
                .WithOne(x => x.Session)
                .HasForeignKey(x => x.SessionId);
        });

        modelBuilder.Entity<TurnEntity>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.SessionId);
            entity.HasIndex(x => x.Timestamp);
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

        base.OnModelCreating(modelBuilder);
    }
}
