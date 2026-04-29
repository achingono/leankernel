using Microsoft.Extensions.Logging.Abstractions;
using LeanKernel.Host.Services;

namespace LeanKernel.Tests.Unit.Host;

public class OnboardingStateStoreTests : IDisposable
{
    private readonly string _tempDir;
    private readonly OnboardingStateStore _store;

    public OnboardingStateStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"LEANKERNEL_onboarding_{Guid.NewGuid():N}");
        _store = new OnboardingStateStore(
            new LeanKernelHostPaths
            {
                DataDirectory = _tempDir,
                RuntimeConfigPath = Path.Combine(_tempDir, "runtime-settings.json"),
                OnboardingStatePath = Path.Combine(_tempDir, "onboarding-state.json")
            },
            NullLogger<OnboardingStateStore>.Instance);
    }

    [Fact]
    public async Task GetAsync_NoStateFile_ReturnsNotCompleted()
    {
        var state = await _store.GetAsync(CancellationToken.None);
        Assert.False(state.Completed);
        Assert.Null(state.CompletedAt);
    }

    [Fact]
    public async Task MarkCompletedAsync_PersistsCompletedState()
    {
        await _store.MarkCompletedAsync(CancellationToken.None);
        var completed = await _store.IsCompletedAsync(CancellationToken.None);
        Assert.True(completed);
    }

    [Fact]
    public async Task MarkInProgressAsync_AfterComplete_DoesNotReset()
    {
        await _store.MarkCompletedAsync(CancellationToken.None);
        await _store.MarkInProgressAsync(CancellationToken.None);
        var state = await _store.GetAsync(CancellationToken.None);
        Assert.True(state.Completed);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }
}
