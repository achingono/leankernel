using System.Text.Json;
using FluentAssertions;
using LeanKernel.Logic.Configuration;
using Xunit;
using LeanKernel.Logic.Tools;
using LeanKernel.Logic.Tools.BuiltIn.Browser;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace LeanKernel.Tests.Unit.Tools;

public class BrowserToolTests
{
    [Fact]
    public async Task BrowserRunTask_returns_error_when_task_is_missing()
    {
        var fake = new FakeWebwrightClient();
        var tool = BrowserToolDefinitions.CreateRunTaskTool(CreateScopeFactory(fake));

        var result = await tool.Handler!(new Dictionary<string, object?>(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("Task is required");
    }

    [Fact]
    public async Task BrowserRunTask_returns_error_when_start_url_is_not_http()
    {
        var fake = new FakeWebwrightClient();
        var tool = BrowserToolDefinitions.CreateRunTaskTool(CreateScopeFactory(fake));

        var result = await tool.Handler!(
            new Dictionary<string, object?> { ["task"] = "do something", ["start_url"] = "ftp://example.com" },
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("start_url must be an absolute HTTP or HTTPS URL");
    }

    [Fact]
    public async Task BrowserRunTask_submits_run_and_returns_run_id()
    {
        var fake = new FakeWebwrightClient
        {
            SubmitResult = new BrowserRunSubmissionResponse("run-abc", "queued", DateTimeOffset.UtcNow, null)
        };
        var tool = BrowserToolDefinitions.CreateRunTaskTool(CreateScopeFactory(fake));

        var result = await tool.Handler!(
            new Dictionary<string, object?> { ["task"] = "click the button" },
            CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Output.Should().Contain("run-abc");
        result.Output.Should().Contain("queued");
        fake.SubmitCalls.Should().HaveCount(1);
        fake.SubmitCalls[0].Task.Should().Be("click the button");
    }

    [Fact]
    public async Task BrowserGetRun_rejects_wait_seconds_parameter()
    {
        var fake = new FakeWebwrightClient();
        var tool = BrowserToolDefinitions.CreateGetRunTool(CreateScopeFactory(fake));

        var result = await tool.Handler!(
            new Dictionary<string, object?> { ["run_id"] = "run-1", ["wait_seconds"] = 5 },
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("wait_seconds is reserved for future long-polling");
    }

    [Fact]
    public async Task BrowserGetRun_returns_error_when_run_id_is_missing()
    {
        var fake = new FakeWebwrightClient();
        var tool = BrowserToolDefinitions.CreateGetRunTool(CreateScopeFactory(fake));

        var result = await tool.Handler!(new Dictionary<string, object?>(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("run_id is required");
    }

    [Fact]
    public async Task BrowserGetRun_polls_run_and_serializes_status()
    {
        var fake = new FakeWebwrightClient
        {
            GetRunResult = new BrowserRunStatusResponse(
                "run-xyz", "completed", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, 0,
                "final data",
                [new("art-1", "screenshot", "screenshot.png", "image/png", 1024)],
                null)
        };
        var tool = BrowserToolDefinitions.CreateGetRunTool(CreateScopeFactory(fake));

        var result = await tool.Handler!(
            new Dictionary<string, object?> { ["run_id"] = "run-xyz" },
            CancellationToken.None);

        result.Success.Should().BeTrue();
        using var doc = JsonDocument.Parse(result.Output!);
        doc.RootElement.GetProperty("runId").GetString().Should().Be("run-xyz");
        doc.RootElement.GetProperty("status").GetString().Should().Be("completed");
        fake.GetRunCalls.Should().ContainSingle().Which.Should().Be("run-xyz");
    }

    [Fact]
    public async Task BrowserGetArtifact_returns_base64_encoded_bytes()
    {
        var bytes = new byte[] { 0x89, 0x50, 0x4E, 0x47 };
        var fake = new FakeWebwrightClient
        {
            ArtifactResult = new BrowserArtifactContent("run-1", "art-1", "image/png", bytes, Truncated: false)
        };
        var tool = BrowserToolDefinitions.CreateGetArtifactTool(CreateScopeFactory(fake));

        var result = await tool.Handler!(
            new Dictionary<string, object?> { ["run_id"] = "run-1", ["artifact_id"] = "art-1" },
            CancellationToken.None);

        result.Success.Should().BeTrue();
        using var doc = JsonDocument.Parse(result.Output!);
        doc.RootElement.GetProperty("base64").GetString().Should().Be(Convert.ToBase64String(bytes));
        doc.RootElement.GetProperty("contentType").GetString().Should().Be("image/png");
        doc.RootElement.GetProperty("truncated").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task BrowserGetArtifact_returns_error_when_run_id_is_missing()
    {
        var fake = new FakeWebwrightClient();
        var tool = BrowserToolDefinitions.CreateGetArtifactTool(CreateScopeFactory(fake));

        var result = await tool.Handler!(
            new Dictionary<string, object?> { ["artifact_id"] = "art-1" },
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("run_id is required");
    }

    [Fact]
    public async Task BrowserGetArtifact_returns_error_when_artifact_id_is_missing()
    {
        var fake = new FakeWebwrightClient();
        var tool = BrowserToolDefinitions.CreateGetArtifactTool(CreateScopeFactory(fake));

        var result = await tool.Handler!(
            new Dictionary<string, object?> { ["run_id"] = "run-1" },
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("artifact_id is required");
    }

    [Fact]
    public async Task BrowserCancelRun_returns_error_when_run_id_is_missing()
    {
        var fake = new FakeWebwrightClient();
        var tool = BrowserToolDefinitions.CreateCancelRunTool(CreateScopeFactory(fake));

        var result = await tool.Handler!(new Dictionary<string, object?>(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("run_id is required");
    }

    [Fact]
    public async Task BrowserCancelRun_cancels_and_returns_status()
    {
        var fake = new FakeWebwrightClient
        {
            CancelResult = new BrowserCancelRunResponse("run-abc", "cancelling", "Cancellation requested.")
        };
        var tool = BrowserToolDefinitions.CreateCancelRunTool(CreateScopeFactory(fake));

        var result = await tool.Handler!(
            new Dictionary<string, object?> { ["run_id"] = "run-abc" },
            CancellationToken.None);

        result.Success.Should().BeTrue();
        using var doc = JsonDocument.Parse(result.Output!);
        doc.RootElement.GetProperty("status").GetString().Should().Be("cancelling");
        fake.CancelCalls.Should().ContainSingle().Which.Should().Be("run-abc");
    }

    [Fact]
    public void BrowserTools_are_registered_in_tool_registry()
    {
        var registry = new ToolRegistry();
        var scopeFactory = CreateScopeFactory(new FakeWebwrightClient());

        registry.Register(BrowserToolDefinitions.CreateRunTaskTool(scopeFactory));
        registry.Register(BrowserToolDefinitions.CreateGetRunTool(scopeFactory));
        registry.Register(BrowserToolDefinitions.CreateGetArtifactTool(scopeFactory));
        registry.Register(BrowserToolDefinitions.CreateCancelRunTool(scopeFactory));

        registry.GetTool("browser_run_task").Should().NotBeNull();
        registry.GetTool("browser_get_run").Should().NotBeNull();
        registry.GetTool("browser_get_artifact").Should().NotBeNull();
        registry.GetTool("browser_cancel_run").Should().NotBeNull();
        registry.Tools.Should().HaveCount(4);
    }

    [Fact]
    public async Task BrowserRunTask_returns_error_when_webwright_exception_is_thrown()
    {
        var fake = new FakeWebwrightClient
        {
            SubmitException = new WebwrightException("SERVICE_UNAVAILABLE", "Sidecar is down", 503)
        };
        var tool = BrowserToolDefinitions.CreateRunTaskTool(CreateScopeFactory(fake));

        var result = await tool.Handler!(
            new Dictionary<string, object?> { ["task"] = "do something" },
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("SERVICE_UNAVAILABLE");
    }

    private static IServiceScopeFactory CreateScopeFactory(FakeWebwrightClient fakeClient)
    {
        var services = new ServiceCollection();
        services.Configure<AgentSettings>(config => config.Tools.Webwright.Enabled = true);
        services.AddSingleton<IWebwrightClient>(fakeClient);
        var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IServiceScopeFactory>();
    }

    private sealed class FakeWebwrightClient : IWebwrightClient
    {
        public BrowserRunSubmissionResponse SubmitResult { get; set; } = new("run-0", "queued", DateTimeOffset.UtcNow, null);
        public WebwrightException? SubmitException { get; set; }
        public List<BrowserRunTaskRequest> SubmitCalls { get; } = [];

        public BrowserRunStatusResponse GetRunResult { get; set; } = new("run-0", "pending", null, null, null, null, [], null);
        public List<string> GetRunCalls { get; } = [];

        public BrowserArtifactContent ArtifactResult { get; set; } = new("run-0", "art-0", "text/plain", [], Truncated: false);
        public List<(string RunId, string ArtifactId, int MaxBytes)> GetArtifactCalls { get; } = [];

        public BrowserCancelRunResponse CancelResult { get; set; } = new("run-0", "cancelling", "ok");
        public List<string> CancelCalls { get; } = [];

        public Task<BrowserRunSubmissionResponse> SubmitRunAsync(BrowserRunTaskRequest request, CancellationToken ct = default)
        {
            SubmitCalls.Add(request);
            if (SubmitException is not null)
            {
                throw SubmitException;
            }

            return Task.FromResult(SubmitResult);
        }

        public Task<BrowserRunStatusResponse> GetRunAsync(string runId, CancellationToken ct = default)
        {
            GetRunCalls.Add(runId);
            return Task.FromResult(GetRunResult);
        }

        public Task<BrowserArtifactContent> GetArtifactAsync(string runId, string artifactId, int maxBytes, CancellationToken ct = default)
        {
            GetArtifactCalls.Add((runId, artifactId, maxBytes));
            return Task.FromResult(ArtifactResult);
        }

        public Task<BrowserCancelRunResponse> CancelRunAsync(string runId, CancellationToken ct = default)
        {
            CancelCalls.Add(runId);
            return Task.FromResult(CancelResult);
        }
    }
}
