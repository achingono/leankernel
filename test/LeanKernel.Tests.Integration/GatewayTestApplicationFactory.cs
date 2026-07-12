using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace LeanKernel.Tests.Integration;

public class GatewayTestApplicationFactory : WebApplicationFactory<Program>
{
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
            // Replace EntityContext with InMemory
            services.RemoveAll<DbContextOptions<LeanKernel.Data.EntityContext>>();
            services.RemoveAll<LeanKernel.Data.EntityContext>();
            services.AddDbContext<LeanKernel.Data.EntityContext>(options =>
                options.UseInMemoryDatabase($"IntegrationTests_{Guid.NewGuid():N}"));
        });
    }
}
