using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace LeanKernel.Tests.Integration;

/// <summary>
/// Configures the gateway host for integration tests.
/// </summary>
public class GatewayTestApplicationFactory : WebApplicationFactory<Program>
{
    /// <inheritdoc />
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((context, config) =>
        {
            config.Sources.Clear();
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Sqlite"] = $"Data Source=:memory:",
                ["OpenAI:ApiKey"] = "test-key",
                ["OpenAI:BaseUrl"] = "http://localhost:1",
                ["OpenAI:DefaultModel"] = "test-model",
                ["Agents:DefaultName"] = "leankernel",
                ["Agents:DefaultDescription"] = "Test agent",
                ["Agents:DefaultInstructions"] = "You are a test assistant."
            });
        });

        builder.ConfigureTestServices(services =>
        {
            // Remove ALL EF Core services registered by AddEntityContext for EntityContext,
            // including factory-related and options-configuration services that carry the
            // SQLite provider extension and would conflict with the InMemory replacement.
            var entityType = typeof(LeanKernel.Data.EntityContext);
            var optionsConfigType = typeof(Microsoft.EntityFrameworkCore.Infrastructure.IDbContextOptionsConfiguration<>).MakeGenericType(entityType);
            var toRemove = services.Where(d =>
                d.ServiceType == typeof(DbContextOptions<LeanKernel.Data.EntityContext>) ||
                d.ServiceType == entityType ||
                d.ServiceType == typeof(IDbContextFactory<LeanKernel.Data.EntityContext>) ||
                d.ServiceType == optionsConfigType ||
                d.ServiceType == typeof(Microsoft.EntityFrameworkCore.Infrastructure.ServiceProviderAccessor)).ToList();

            foreach (var descriptor in toRemove)
            {
                services.Remove(descriptor);
            }

            services.AddDbContext<LeanKernel.Data.EntityContext>(options =>
                options.UseInMemoryDatabase($"IntegrationTests_{Guid.NewGuid():N}"));

            // Remove external health checks that depend on services unavailable in tests
            services.Configure<HealthCheckServiceOptions>(opts =>
            {
                var external = opts.Registrations
                    .Where(r => r.Name is "litellm" or "gbrain")
                    .ToList();
                foreach (var r in external)
                {
                    opts.Registrations.Remove(r);
                }
            });
        });
    }
}
