using System.Text.Json;
using FluentAssertions;
using LeanKernel.Abstractions.Configuration;
using LeanKernel.Abstractions.Interfaces;
using LeanKernel.Abstractions.Models;
using LeanKernel.Tools;
using LeanKernel.Tools.BuiltIn.Browser;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace LeanKernel.Tests.Unit.Tools;

public class BrowserToolTests
{
    [Fact]
    public void AddLeanKernelTools_does_not_register_browser_tools_by_default()
    {
        var services = CreateToolServices(browserEnabled: false);

        using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<IToolRegistry>();

        registry.GetTool("browser_run_task").Should().BeNull();
    }

    [Fact]
    public void AddLeanKernelTools_registers_browser_tools_when_enabled()
    {
        var services = CreateToolServices(browserEnabled: true);

        using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<IToolRegistry>();

        registry.GetTool("browser_run_task").Should().NotBeNull();
        registry.GetTool("browser_get_run").Should().NotBeNull();
        registry.GetTool("browser_get_artifact").Should().NotBeNull();
        registry.GetTool("browser_cancel_run").Should().NotBeNull();
    }

    [Fact]
    public async Task BrowserRunTask_rejects_missing_task()
    {
        var fakeClient = new FakeBrowserServiceClient();
        var tool = BrowserToolDefinitions.CreateRunTaskTool(CreateScopeFactory(fakeClient));

        var result = await tool.Handler!(new Dictionary<string, object?>(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Be("Task is required");
    }

    [Fact]
    public async Task BrowserRunTask_rejects_non_http_start_url()
    {
        var fakeClient = new FakeBrowserServiceClient();
        var tool = BrowserToolDefinitions.CreateRunTaskTool(CreateScopeFactory(fakeClient));

        var result = await tool.Handler!(new Dictionary<string, object?>
        {
            ["task"] = "Read the page",
            ["start_url"] = "file:///etc/passwd"
        }, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Be("start_url must be an absolute HTTP or HTTPS URL");
    }

    [Fact]
    public async Task BrowserRunTask_submits_request_and_serializes_run_id()
    {
        var fakeClient = new FakeBrowserServiceClient
        {
            Submission = new BrowserRunSubmissionResponse("run-123", "queued", DateTimeOffset.Parse("2026-05-28T09:00:00Z"), 1)
        };
        var tool = BrowserToolDefinitions.CreateRunTaskTool(CreateScopeFactory(fakeClient));

        var result = await tool.Handler!(new Dictionary<string, object?>
        {
            ["task"] = "Find the title",
            ["start_url"] = "https://example.com",
            ["request_id"] = "idem-1"
        }, CancellationToken.None);

        result.Success.Should().BeTrue();
        fakeClient.LastRunRequest.Should().NotBeNull();
        fakeClient.LastRunRequest!.Task.Should().Be("Find the title");
        fakeClient.LastRunRequest.StartUrl.Should().Be("https://example.com");
        fakeClient.LastRunRequest.RequestId.Should().Be("idem-1");

        using var output = JsonDocument.Parse(result.Output!);
        output.RootElement.GetProperty("runId").GetString().Should().Be("run-123");
        output.RootElement.GetProperty("status").GetString().Should().Be("queued");
    }

    [Fact]
    public async Task BrowserGetRun_rejects_wait_seconds_in_v1()
    {
        var fakeClient = new FakeBrowserServiceClient();
        var tool = BrowserToolDefinitions.CreateGetRunTool(CreateScopeFactory(fakeClient));

        var result = await tool.Handler!(new Dictionary<string, object?>
        {
            ["run_id"] = "run-123",
            ["wait_seconds"] = 5
        }, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Be("wait_seconds is reserved for future long-polling and is not supported in v1.");
    }

    [Fact]
    public async Task BrowserGetArtifact_returns_base64_content()
    {
        var fakeClient = new FakeBrowserServiceClient
        {
            Artifact = new BrowserArtifactContent("run-123", "script-abc", "text/x-python", "print('ok')"u8.ToArray(), false)
        };
        var tool = BrowserToolDefinitions.CreateGetArtifactTool(CreateScopeFactory(fakeClient));

        var result = await tool.Handler!(new Dictionary<string, object?>
        {
            ["run_id"] = "run-123",
            ["artifact_id"] = "script-abc"
        }, CancellationToken.None);

        result.Success.Should().BeTrue();
        using var output = JsonDocument.Parse(result.Output!);
        output.RootElement.GetProperty("runId").GetString().Should().Be("run-123");
        output.RootElement.GetProperty("base64").GetString().Should().Be(Convert.ToBase64String("print('ok')"u8.ToArray()));
    }

    [Fact]
    public async Task BrowserCancelRun_calls_client_delete_contract()
    {
        var fakeClient = new FakeBrowserServiceClient
        {
            CancelResponse = new BrowserCancelRunResponse("run-123", "cancelled", "Cancellation requested.")
        };
        var tool = BrowserToolDefinitions.CreateCancelRunTool(CreateScopeFactory(fakeClient));

        var result = await tool.Handler!(new Dictionary<string, object?> { ["run_id"] = "run-123" }, CancellationToken.None);

        result.Success.Should().BeTrue();
        fakeClient.CancelledRunId.Should().Be("run-123");
    }

    private static ServiceCollection CreateToolServices(bool browserEnabled)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOptions();
        services.Configure<LeanKernelConfig>(config => config.BrowserService.Enabled = browserEnabled);
        services.AddSingleton(Mock.Of<IKnowledgeService>());
        services.AddSingleton<IBrowserServiceClient, FakeBrowserServiceClient>();
        services.AddLeanKernelTools();
        return services;
    }

    private static IServiceScopeFactory CreateScopeFactory(FakeBrowserServiceClient fakeClient)
    {
        var services = new ServiceCollection();
        services.AddOptions();
        services.Configure<LeanKernelConfig>(config =>
        {
            config.BrowserService.Enabled = true;
            config.BrowserService.MaxArtifactBytes = 128;
            config.BrowserService.DefaultModel = "gpt-4o";
        });
        services.AddSingleton<IBrowserServiceClient>(fakeClient);
        return services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
    }

    private sealed class FakeBrowserServiceClient : IBrowserServiceClient
    {
        public BrowserRunTaskRequest? LastRunRequest { get; private set; }
        public string? CancelledRunId { get; private set; }
        public BrowserRunSubmissionResponse Submission { get; set; } = new("run-default", "queued", DateTimeOffset.UtcNow, null);
        public BrowserRunStatusResponse Status { get; set; } = new("run-default", "queued", null, null, null, null, [], null);
        public BrowserArtifactContent Artifact { get; set; } = new("run-default", "log", "text/plain", [], false);
        public BrowserCancelRunResponse CancelResponse { get; set; } = new("run-default", "cancelled", "Cancellation requested.");

        public Task<BrowserRunSubmissionResponse> SubmitRunAsync(BrowserRunTaskRequest request, CancellationToken ct = default)
        {
            LastRunRequest = request;
            return Task.FromResult(Submission);
        }

        public Task<BrowserRunStatusResponse> GetRunAsync(string runId, CancellationToken ct = default)
        {
            return Task.FromResult(Status with { RunId = runId });
        }

        public Task<BrowserArtifactContent> GetArtifactAsync(string runId, string artifactId, int maxBytes, CancellationToken ct = default)
        {
            return Task.FromResult(Artifact);
        }

        public Task<BrowserCancelRunResponse> CancelRunAsync(string runId, CancellationToken ct = default)
        {
            CancelledRunId = runId;
            return Task.FromResult(CancelResponse);
        }
    }
}
