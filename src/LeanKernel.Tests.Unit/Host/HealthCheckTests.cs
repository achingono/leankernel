using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using LeanKernel.Core.Configuration;
using LeanKernel.Host;

namespace LeanKernel.Tests.Unit.Host;

public class HealthCheckTests : IDisposable
{
    private readonly string _tmpDir;

    public HealthCheckTests()
    {
        _tmpDir = Path.Combine(Path.GetTempPath(), $"LEANKERNEL_hc_{Guid.NewGuid():N}");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tmpDir))
            Directory.Delete(_tmpDir, recursive: true);
    }

    private IOptions<LeanKernelConfig> MakeConfig(string wikiPath) =>
        Options.Create(new LeanKernelConfig
        {
            Wiki = new WikiConfig { BasePath = wikiPath },
            LiteLlm = new LiteLlmConfig { BaseUrl = "http://localhost:4000", ApiKey = "k" },
            Qdrant = new QdrantConfig { Host = "localhost" }
        });

    [Fact]
    public async Task CheckHealthAsync_WikiExists_ReturnsHealthy()
    {
        var wikiPath = Path.Combine(_tmpDir, "wiki");
        Directory.CreateDirectory(wikiPath);

        var hc = new LeanKernelHealthCheck(MakeConfig(wikiPath));
        var result = await hc.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);

        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Contains("operational", result.Description!);
    }

    [Fact]
    public async Task CheckHealthAsync_WikiMissing_ReturnsDegraded()
    {
        var wikiPath = Path.Combine(_tmpDir, "nonexistent_wiki");
        // Don't create directory

        var hc = new LeanKernelHealthCheck(MakeConfig(wikiPath));
        var result = await hc.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);

        Assert.Equal(HealthStatus.Degraded, result.Status);
        Assert.Contains("not found", result.Description!);
    }

    [Fact]
    public async Task CheckHealthAsync_IncludesDataKeys()
    {
        var wikiPath = Path.Combine(_tmpDir, "wiki");
        Directory.CreateDirectory(wikiPath);

        var hc = new LeanKernelHealthCheck(MakeConfig(wikiPath));
        var result = await hc.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);

        Assert.True(result.Data.ContainsKey("wiki_path"));
        Assert.True(result.Data.ContainsKey("litellm_url"));
        Assert.True(result.Data.ContainsKey("qdrant_host"));
        Assert.True(result.Data.ContainsKey("uptime"));
    }
}
