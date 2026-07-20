namespace LeanKernel.Data;

using LeanKernel.Entities;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// EF Core database context for persisted tenants, users, channels, sessions, turns, and agent state.
/// </summary>
public class EntityContext : DbContext
{
    /// <summary>
    /// Initializes a new instance of the <see cref="EntityContext"/> class.
    /// </summary>
    /// <param name="options">The configured EF Core options for this context.</param>
    public EntityContext(DbContextOptions<EntityContext> options)
        : base(options)
    {
    }

    /// <summary>
    /// Gets the persisted sessions.
    /// </summary>
    public DbSet<SessionEntity> Sessions => this.Set<SessionEntity>();

    /// <summary>
    /// Gets the persisted conversation turns.
    /// </summary>
    public DbSet<TurnEntity> Turns => this.Set<TurnEntity>();

    /// <summary>
    /// Gets the persisted users.
    /// </summary>
    public DbSet<UserEntity> Users => this.Set<UserEntity>();

    /// <summary>
    /// Gets the persisted channels.
    /// </summary>
    public DbSet<ChannelEntity> Channels => this.Set<ChannelEntity>();

    /// <summary>
    /// Gets the persisted tenants.
    /// </summary>
    public DbSet<TenantEntity> Tenants => this.Set<TenantEntity>();

    /// <summary>
    /// Gets the persisted channel sender bindings.
    /// </summary>
    public DbSet<ChannelSenderBindingEntity> ChannelSenderBindings => this.Set<ChannelSenderBindingEntity>();

    /// <summary>
    /// Gets the persisted channel memory policy overrides.
    /// </summary>
    public DbSet<ChannelMemoryPolicyEntity> ChannelMemoryPolicies => this.Set<ChannelMemoryPolicyEntity>();

    /// <summary>
    /// Gets the persisted agent session state blobs.
    /// </summary>
    public DbSet<AgentStateEntity> AgentStates => this.Set<AgentStateEntity>();

    /// <summary>
    /// Gets the persisted assistant turn telemetry records.
    /// </summary>
    public DbSet<TurnTelemetryEntity> TurnTelemetry => this.Set<TurnTelemetryEntity>();

    /// <summary>
    /// Applies pending migrations and ensures the default tenant and OpenAI channel records exist.
    /// </summary>
    /// <param name="hostName">The host name to seed for the default tenant if needed.</param>
    /// <returns>A task that completes when migrations and seed data have been applied.</returns>
    public async Task ApplyMigrationsAndSeedAsync(string hostName)
    {
        if (this.Database.IsRelational())
        {
            await this.Database.MigrateAsync();
        }
        else
        {
            await this.Database.EnsureCreatedAsync();
        }

        await this.EnsureDefaultTenantAsync(hostName);
        await this.EnsureKnownChannelsAsync();
    }

    /// <summary>
    /// Configures the entity mappings, indexes, relationships, and query filters.
    /// </summary>
    /// <param name="modelBuilder">The model builder used to configure the EF Core model.</param>
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

            // Ignore the Sessions collection navigation to prevent EF from inferring a duplicate
            // shadow FK alongside the explicitly mapped TenantId FK on SessionEntity (M2).
            entity.Ignore(e => e.Sessions);
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
            entity.Property(e => e.PersonId);
            entity.HasIndex(e => e.Email);
            entity.HasIndex(e => e.PersonId);
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

        // ChannelSenderBindingEntity
        modelBuilder.Entity<ChannelSenderBindingEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Issuer).HasMaxLength(200);
            entity.Property(e => e.Subject).HasMaxLength(500);
            entity.Property(e => e.BearerToken).HasColumnType("text");
            entity.HasIndex(e => new { e.TenantId, e.ChannelId, e.Issuer, e.Subject }).IsUnique();
            entity.HasIndex(e => new { e.TenantId, e.ChannelId, e.UserId });
            entity.HasOne(e => e.Tenant)
                .WithMany(t => t.ChannelSenderBindings)
                .HasForeignKey(e => e.TenantId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.User)
                .WithMany(u => u.ChannelSenderBindings)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.Channel)
                .WithMany(c => c.SenderBindings)
                .HasForeignKey(e => e.ChannelId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // ChannelMemoryPolicyEntity
        modelBuilder.Entity<ChannelMemoryPolicyEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.ShareList).HasColumnType("text");
            entity.Property(e => e.AccessList).HasColumnType("text");
            entity.HasIndex(e => new { e.TenantId, e.ChannelId }).IsUnique();
            entity.HasOne(e => e.Tenant)
                .WithMany(t => t.ChannelMemoryPolicies)
                .HasForeignKey(e => e.TenantId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.Channel)
                .WithMany(c => c.MemoryPolicies)
                .HasForeignKey(e => e.ChannelId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // TurnTelemetryEntity
        modelBuilder.Entity<TurnTelemetryEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasMaxLength(50);
            entity.Property(e => e.TurnId).HasMaxLength(50);
            entity.Property(e => e.SchemaVersion).HasMaxLength(10);
            entity.Property(e => e.Currency).HasMaxLength(10);
            entity.HasIndex(e => e.TurnId).IsUnique();
            entity.HasIndex(e => e.ServedModel);
            entity.HasIndex(e => e.Provider);
            entity.HasIndex(e => e.CapturedAt);
            entity.HasOne(e => e.Turn)
                .WithMany()
                .HasForeignKey(e => e.TurnId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasQueryFilter(e => !e.IsDeleted && !e.Turn.IsDeleted);
        });
    }

    /// <summary>
    /// Ensures a default tenant exists for the supplied host name.
    /// </summary>
    /// <param name="hostName">The host name to associate with the default tenant.</param>
    /// <returns>A task that completes when the tenant check finishes.</returns>
    private async Task EnsureDefaultTenantAsync(string hostName)
    {
        if (await this.Tenants.AnyAsync(tenant => tenant.HostName == hostName))
        {
            return;
        }

        this.Tenants.Add(new TenantEntity
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
                Email = "system@leankernel.local",
            },
        });

        try
        {
            await this.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            this.ChangeTracker.Clear();
        }
    }

    /// <summary>
    /// Ensures the well-known OpenAI HTTP channel exists.
    /// </summary>
    /// <returns>A task that completes when the channel check finishes.</returns>
    private async Task EnsureKnownChannelsAsync()
    {
        var knownChannelNames = new[]
        {
            ChannelEntity.OpenAiHttpName,
            ChannelEntity.SignalName,
            ChannelEntity.TeamsName,
        };

        var existing = await this.Channels
            .AsNoTracking()
            .Select(channel => channel.Name)
            .ToListAsync();

        var missingNames = knownChannelNames
            .Except(existing, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (missingNames.Count == 0)
        {
            return;
        }

        foreach (var missingName in missingNames)
        {
            this.Channels.Add(new ChannelEntity
            {
                Id = Guid.NewGuid(),
                Name = missingName,
            });
        }

        try
        {
            await this.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            this.ChangeTracker.Clear();
        }
    }
}