using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using LeanKernel.Core.Configuration;
using LeanKernel.Host.Services;

namespace LeanKernel.Tests.Unit.Host;

public class RuntimeLeanKernelConfigStoreTests : IDisposable
{
    private readonly string _tempDir;
    private readonly LeanKernelHostPaths _paths;

    public RuntimeLeanKernelConfigStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"LEANKERNEL_runtimecfg_{Guid.NewGuid():N}");
        _paths = new LeanKernelHostPaths
        {
            DataDirectory = _tempDir,
            RuntimeConfigPath = Path.Combine(_tempDir, "runtime-settings.json"),
            OnboardingStatePath = Path.Combine(_tempDir, "onboarding-state.json")
        };
    }

    [Fact]
    public async Task SaveAsync_WritesRuntimeSettingsFile()
    {
        var current = new LeanKernelConfig
        {
            LiteLlm = new LiteLlmConfig { BaseUrl = "http://litellm:4000", ApiKey = "key" },
            Qdrant = new QdrantConfig { Host = "qdrant", Port = 6334 },
            Signal = new SignalConfig { Enabled = false, CliPath = "/usr/local/bin/signal-cli", Account = "" },
            Wiki = new WikiConfig { BasePath = "/app/data/wiki" },
            Context = new ContextConfig(),
            Scheduler = new SchedulerConfig()
        };

        var store = new RuntimeLeanKernelConfigStore(
            new TestOptionsMonitor(current),
            _paths,
            NullLogger<RuntimeLeanKernelConfigStore>.Instance);

        await store.SaveAsync(current, CancellationToken.None);

        Assert.True(File.Exists(_paths.RuntimeConfigPath));
        var json = await File.ReadAllTextAsync(_paths.RuntimeConfigPath);
        using var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.TryGetProperty("LeanKernel", out _));
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private sealed class TestOptionsMonitor : IOptionsMonitor<LeanKernelConfig>
    {
        private readonly LeanKernelConfig _value;

        public TestOptionsMonitor(LeanKernelConfig value)
        {
            _value = value;
        }

        public LeanKernelConfig CurrentValue => _value;
        public LeanKernelConfig Get(string? name) => _value;
        public IDisposable? OnChange(Action<LeanKernelConfig, string?> listener) => null;
    }
}
