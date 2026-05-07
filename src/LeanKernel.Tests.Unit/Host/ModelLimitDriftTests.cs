using System.Diagnostics.CodeAnalysis;
using LeanKernel.Host.Controllers;
using LeanKernel.Host.Models.Routing;
using LeanKernel.Host.Services;

namespace LeanKernel.Tests.Unit.Host;

public sealed class ModelLimitDriftTests
{
    // ---------------------------------------------------------------------------
    // ModelLimitDriftService — script-not-found path
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task PreviewDrift_ScriptNotFound_ReturnsError()
    {
        var svc = new ModelLimitDriftService("/nonexistent/path/drift.py", "/tmp/config.yaml");
        var result = await svc.PreviewDriftAsync();

        Assert.NotNull(result.Error);
        Assert.Contains("not found", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, result.TotalChanges);
        Assert.Empty(result.Changes);
    }

    // ---------------------------------------------------------------------------
    // ModelLimitDriftController
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task GetDrift_ReturnsServiceResult()
    {
        var stub = new StubDriftService(new DriftReport("2025-01-01T00:00:00Z", 2,
        [
            new DriftEntry("groq", "llama-3.1", "llama-3.1", "max_tokens", 4096, 8192),
            new DriftEntry("groq", "llama-3.1", "llama-3.1", "context_window", 128000, 131072)
        ]));

        var controller = new ModelLimitDriftController(stub);
        var result = await controller.Preview(CancellationToken.None);

        Assert.Equal(2, result.TotalChanges);
        Assert.Equal(2, result.Changes.Count);
        Assert.Null(result.Error);
    }

    [Fact]
    public async Task GetDrift_WhenServiceReturnsError_PropagatesError()
    {
        var stub = new StubDriftService(new DriftReport("2025-01-01T00:00:00Z", 0, []) { Error = "script not found" });
        var controller = new ModelLimitDriftController(stub);
        var result = await controller.Preview(CancellationToken.None);

        Assert.Equal(0, result.TotalChanges);
        Assert.Equal("script not found", result.Error);
    }

    [Fact]
    public async Task GetDrift_NoChanges_ReturnsEmpty()
    {
        var stub = new StubDriftService(new DriftReport("2025-01-01T00:00:00Z", 0, []));
        var controller = new ModelLimitDriftController(stub);
        var result = await controller.Preview(CancellationToken.None);

        Assert.Equal(0, result.TotalChanges);
        Assert.Empty(result.Changes);
        Assert.Null(result.Error);
    }

    [Fact]
    public async Task DriftEntry_RecordsAllFields()
    {
        var entry = new DriftEntry("azure", "gpt-4o", "gpt-4o", "context_window", 128000, 200000);

        Assert.Equal("azure", entry.Provider);
        Assert.Equal("gpt-4o", entry.ModelId);
        Assert.Equal("gpt-4o", entry.ModelName);
        Assert.Equal("context_window", entry.Field);
        Assert.Equal(128000, entry.OldValue);
        Assert.Equal(200000, entry.NewValue);
    }

    [Fact]
    public async Task DriftReport_ExposesError()
    {
        var report = new DriftReport("2025-01-01T00:00:00Z", 0, []) { Error = "no python3" };
        Assert.Equal("no python3", report.Error);
        await Task.CompletedTask;
    }

    // ---------------------------------------------------------------------------
    // Stub
    // ---------------------------------------------------------------------------

    [ExcludeFromCodeCoverage]
    private sealed class StubDriftService(DriftReport result) : IModelLimitDriftService
    {
        public Task<DriftReport> PreviewDriftAsync(CancellationToken ct = default) =>
            Task.FromResult(result);
    }
}
