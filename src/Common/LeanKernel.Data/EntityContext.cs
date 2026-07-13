using LeanKernel.Entities;
using Microsoft.EntityFrameworkCore;

namespace LeanKernel.Data;

public class EntityContext : DbContext
{
    public EntityContext(DbContextOptions<EntityContext> options) : base(options)
    {
    }

    /// <summary>
    /// Gets the persisted sessions.
    /// </summary>
    public DbSet<SessionEntity> Sessions => Set<SessionEntity>();

    /// <summary>
    /// Gets the persisted conversation turns.
    /// </summary>
    public DbSet<TurnEntity> Turns => Set<TurnEntity>();

    /// <summary>
    /// Gets the persisted users.
    /// </summary>
    public DbSet<UserEntity> Users => Set<UserEntity>();

    /// <summary>
    /// Gets the persisted channels.
    /// </summary>
    public DbSet<ChannelEntity> Channels => Set<ChannelEntity>();

    /// <summary>
    /// Gets the persisted tenants.
    /// </summary>
    public DbSet<TenantEntity> Tenants => Set<TenantEntity>();

    /// <summary>
    /// Gets the persisted agent session state blobs.
    /// </summary>
    public DbSet<AgentStateEntity> AgentStates => Set<AgentStateEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // SessionEntity
        modelBuilder.Entity<SessionEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.ConversationId).HasMaxLength(500);
            entity.HasQueryFilter(e => !e.IsDeleted);
            entity.HasIndex(e => new { e.TenantId, e.UserId, e.ChannelId, e.ConversationId })
                .IsUnique()
                .HasFilter("\"ConversationId\" IS NOT NULL");
            entity.HasIndex(e => new { e.TenantId, e.UserId, e.ChannelId });
            entity.HasOne(e => e.User)
                .WithMany(u => u.Sessions)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.Channel)
                .WithMany(c => c.Sessions)
                .HasForeignKey(e => e.ChannelId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.Tenant)
                .WithMany()
                .HasForeignKey(e => e.TenantId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // TurnEntity
        modelBuilder.Entity<TurnEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasMaxLength(50);
            entity.Property(e => e.SessionId).HasMaxLength(50);
            entity.Property(e => e.Role).HasMaxLength(50);
            entity.Property(e => e.AuthorName).HasMaxLength(200);
            entity.HasOne(e => e.Session)
                .WithMany(s => s.Turns)
                .HasForeignKey(e => e.SessionId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(e => new { e.SessionId, e.Timestamp });
            entity.HasQueryFilter(e => !e.IsDeleted && !e.Session.IsDeleted);
        });

        // AgentStateEntity
        modelBuilder.Entity<AgentStateEntity>(entity =>
        {
            entity.HasKey(e => e.ScopedConversationId);
            entity.Property(e => e.ScopedConversationId).HasMaxLength(500);
            entity.Property(e => e.StateJson).HasColumnType("text");
            entity.Property(e => e.RowVersion).IsRowVersion();
            entity.HasIndex(e => new { e.TenantId, e.UserId, e.ChannelId });
        });

        // TenantEntity
        modelBuilder.Entity<TenantEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).HasMaxLength(100);
            entity.Property(e => e.Description).HasMaxLength(500);
            entity.Property(e => e.HostName).HasMaxLength(200);
            entity.HasIndex(e => e.HostName).IsUnique();
            entity.HasQueryFilter(e => !e.IsDeleted);
            entity.OwnsOne(e => e.CreatedBy);
            entity.OwnsOne(e => e.UpdatedBy);
        });

        // UserEntity
        modelBuilder.Entity<UserEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Email).HasMaxLength(200);
            entity.Property(e => e.UserName).HasMaxLength(100);
            entity.Property(e => e.FirstName).HasMaxLength(100);
            entity.Property(e => e.LastName).HasMaxLength(100);
            entity.Property(e => e.FullName).HasMaxLength(200);
            entity.Property(e => e.Issuer).HasMaxLength(500);
            entity.Property(e => e.Subject).HasMaxLength(500);
            entity.HasIndex(e => e.Email);
            entity.HasIndex(e => new { e.Issuer, e.Subject }).IsUnique();
            entity.HasQueryFilter(e => !e.IsDeleted);
            entity.OwnsOne(e => e.CreatedBy);
            entity.OwnsOne(e => e.UpdatedBy);
        });

        // ChannelEntity
        modelBuilder.Entity<ChannelEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).HasMaxLength(100);
            entity.HasIndex(e => e.Name).IsUnique();
        });
    }

    public async Task ApplyMigrationsAndSeedAsync(string hostName)
    {
        await Database.MigrateAsync();

        await EnsureDefaultTenantAsync(hostName);
        await EnsureOpenAiChannelAsync();
    }

    private async Task EnsureDefaultTenantAsync(string hostName)
    {
        if (await Tenants.AnyAsync(tenant => tenant.HostName == hostName))
        {
            return;
        }

        Tenants.Add(new TenantEntity
        {
            Id = Guid.NewGuid(),
            Name = "Default Tenant",
            Description = "Default tenant created at startup",
            HostName = hostName,
            IsActive = true,
            CreatedOn = DateTime.UtcNow,
            CreatedBy = new Badge
            {
                Id = Guid.Empty,
                FullName = "System",
                Email = "system@leankernel.local"
            }
        });

        try
        {
            await SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            ChangeTracker.Clear();
        }
    }

    private async Task EnsureOpenAiChannelAsync()
    {
        if (await Channels.AnyAsync(channel => channel.Name == ChannelEntity.OpenAiHttpName))
        {
            return;
        }

        Channels.Add(new ChannelEntity
        {
            Id = Guid.NewGuid(),
            Name = ChannelEntity.OpenAiHttpName
        });

        try
        {
            await SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            ChangeTracker.Clear();
        }
    }
}
