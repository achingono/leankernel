using LeanKernel.Gateway.Configuration;
using LeanKernel.Logic.Configuration;
using LeanKernel.Logic.Providers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;

namespace LeanKernel.Gateway;

internal static class ProgramSetup
{
    private static readonly string[] ConnectionStringNames = ["SqlServer", "Sqlite", "Postgres"];

    internal static void ConfigureEntityContext(WebApplicationBuilder builder, DbContextOptionsBuilder options)
    {
        var (connectionStringName, connectionString) = ResolveConnectionString(builder.Configuration);

        if (string.IsNullOrWhiteSpace(connectionString) && builder.Environment.EnvironmentName != "Testing")
        {
            throw new InvalidOperationException(
                $"Connection string is missing. Specify any of '{string.Join(",", ConnectionStringNames)}'.");
        }

        switch (connectionStringName)
        {
            case "SqlServer":
                options.UseSqlServer(connectionString, sqlOptions => sqlOptions.EnableRetryOnFailure());
                break;
            case "Sqlite":
                options.UseSqlite(connectionString);
                break;
            case "Postgres":
                options.UseNpgsql(connectionString);
                break;
        }

        if (builder.Environment.IsDevelopment())
        {
            options.EnableDetailedErrors(true)
                   .EnableSensitiveDataLogging(true);
        }
    }

    internal static void ConfigureMemoryClient(IServiceCollection services, GBrainConfig? gbrainConfig)
    {
        if (gbrainConfig is { BaseUrl: not null } && !string.IsNullOrWhiteSpace(gbrainConfig.BaseUrl))
        {
            services.AddLeanKernelKnowledge(gbrainConfig);
            return;
        }

        services.AddScoped<IMemoryClient, StubMemoryClient>();
    }

    internal static async Task ApplyMigrationsAndSeedAsync(WebApplication app)
    {
        if (app.Environment.EnvironmentName == "Testing")
        {
            return;
        }

        using var scope = app.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<LeanKernel.Data.EntityContext>();
        await dbContext.Database.MigrateAsync();

        await EnsureDefaultTenantAsync(app, dbContext);
        await EnsureOpenAiChannelAsync(dbContext);
    }

    private static (string? Name, string? Value) ResolveConnectionString(IConfiguration configuration)
    {
        foreach (var connectionStringName in ConnectionStringNames)
        {
            var connectionString = configuration.GetConnectionString(connectionStringName);
            if (!string.IsNullOrWhiteSpace(connectionString))
            {
                return (connectionStringName, connectionString);
            }
        }

        return (null, null);
    }

    private static async Task EnsureDefaultTenantAsync(WebApplication app, LeanKernel.Data.EntityContext dbContext)
    {
        var hostName = app.Configuration["WebHost:HostName"] ?? "localhost";
        if (await dbContext.Tenants.AnyAsync(tenant => tenant.HostName == hostName))
        {
            return;
        }

        dbContext.Tenants.Add(new LeanKernel.Entities.TenantEntity
        {
            Id = Guid.NewGuid(),
            Name = "Default Tenant",
            Description = "Default tenant created at startup",
            HostName = hostName,
            IsActive = true,
            CreatedOn = DateTime.UtcNow,
            CreatedBy = new LeanKernel.Entities.Badge
            {
                Id = Guid.Empty,
                FullName = "System",
                Email = "system@leankernel.local"
            }
        });
        await dbContext.SaveChangesAsync();
    }

    private static async Task EnsureOpenAiChannelAsync(LeanKernel.Data.EntityContext dbContext)
    {
        if (await dbContext.Channels.AnyAsync(channel => channel.Name == LeanKernel.Entities.ChannelEntity.OpenAiHttpName))
        {
            return;
        }

        dbContext.Channels.Add(new LeanKernel.Entities.ChannelEntity
        {
            Id = Guid.NewGuid(),
            Name = LeanKernel.Entities.ChannelEntity.OpenAiHttpName
        });
        await dbContext.SaveChangesAsync();
    }
}
