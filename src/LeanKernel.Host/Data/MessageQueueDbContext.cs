using Microsoft.EntityFrameworkCore;

namespace LeanKernel.Host.Data;

/// <summary>
/// Database context for message queue persistence.
/// </summary>
public class MessageQueueDbContext : DbContext
{
    public DbSet<QueuedMessageEntity> QueuedMessages => Set<QueuedMessageEntity>();

    public MessageQueueDbContext(DbContextOptions<MessageQueueDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<QueuedMessageEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Channel).IsRequired();
            entity.Property(e => e.Recipient).IsRequired();
            entity.Property(e => e.Content).IsRequired();
            entity.Property(e => e.EnqueuedAt).IsRequired();
            entity.Property(e => e.IsUrgent).IsRequired();
            entity.Property(e => e.IsDelivered).IsRequired().HasDefaultValue(false);
            entity.Property(e => e.CreatedAt).IsRequired().HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.UpdatedAt).IsRequired().HasDefaultValueSql("CURRENT_TIMESTAMP");

            // Indices for efficient querying
            entity.HasIndex(e => new { e.IsDelivered, e.EnqueuedAt });
            entity.HasIndex(e => new { e.IsUrgent, e.IsDelivered });
            entity.HasIndex(e => e.DeliveredAt);
            entity.HasIndex(e => e.NextRetryAt);
        });
    }
}
