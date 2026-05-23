using FluentAssertions;
using LeanKernel.Abstractions.Configuration;
using LeanKernel.Abstractions.Enums;
using LeanKernel.Abstractions.Interfaces;
using LeanKernel.Abstractions.Models;
using LeanKernel.Agents;
using LeanKernel.Agents.Routing;
using LeanKernel.Agents.Strategies;
using LeanKernel.Context;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace LeanKernel.Tests.Unit.Agents.Routing;

public class ShadowRoutingStrategyTests
{
    [Fact]
    public async Task InvokeAsync_runs_primary_and_shadow_in_parallel_and_returns_primary_response()
    {
        var primaryStarted = CreateSignal();
        var primaryRelease = CreateSignal();
        var shadowStarted = CreateSignal();
        var shadowRelease = CreateSignal();
        var inner = new ControlledAgentStrategy("primary-model", "primary answer", 12, primaryStarted, primaryRelease);
        var shadowClient = new ControlledChatClient(
            new ChatResponse(new ChatMessage(ChatRole.Assistant, "shadow answer")),
            shadowStarted,
            shadowRelease);
        var sink = new RecordingDiagnosticsSink();
        var strategy = CreateStrategy(inner, shadowClient, sink: sink);
        var context = CreateContext();

        var invocationTask = strategy.InvokeAsync(context);
        var startedTask = Task.WhenAll(primaryStarted.Task, shadowStarted.Task);

        (await Task.WhenAny(startedTask, Task.Delay(TimeSpan.FromSeconds(1)))).Should().Be(startedTask);
        invocationTask.IsCompleted.Should().BeFalse();

        shadowRelease.SetResult(true);
        primaryRelease.SetResult(true);

        var response = await invocationTask;

        response.Should().Be("primary answer");
        context.ModelUsed.Should().Be("primary-model");
        context.TokensUsed.Should().Be(12);
        sink.Entries.Should().ContainSingle();
        sink.Entries[0].Category.Should().Be(DiagnosticCategory.Shadow.ToString());
        var result = sink.Entries[0].Payload.Should().BeOfType<ShadowRoutingResult>().Subject;
        result.PrimaryModel.Should().Be("primary-model");
        result.ShadowModel.Should().Be("shadow-model");
        result.PrimaryResponse.Should().Be("primary answer");
        result.ShadowResponse.Should().Be("shadow answer");
    }

    [Fact]
    public async Task InvokeAsync_returns_primary_response_when_shadow_invocation_fails()
    {
        var inner = new FixedAgentStrategy("primary-model", "primary answer", 7);
        var shadowClient = new ThrowingChatClient(new InvalidOperationException("shadow exploded"));
        var sink = new RecordingDiagnosticsSink();
        var strategy = CreateStrategy(inner, shadowClient, sink: sink);

        var response = await strategy.InvokeAsync(CreateContext());

        response.Should().Be("primary answer");
        sink.Entries.Should().ContainSingle();
        var result = sink.Entries[0].Payload.Should().BeOfType<ShadowRoutingResult>().Subject;
        result.ShadowResponse.Should().BeEmpty();
        result.Comparison.Should().NotBeNull();
        result.Comparison!.Notes.Should().Contain("shadow invocation failed: shadow exploded");
    }

    [Fact]
    public async Task InvokeAsync_returns_primary_response_when_shadow_is_canceled_after_primary_completes()
    {
        var shadowStarted = CreateSignal();
        var shadowRelease = CreateSignal();
        var inner = new FixedAgentStrategy("primary-model", "primary answer", 5);
        var shadowClient = new ControlledChatClient(
            new ChatResponse(new ChatMessage(ChatRole.Assistant, "shadow answer")),
            shadowStarted,
            shadowRelease);
        var sink = new RecordingDiagnosticsSink();
        var strategy = CreateStrategy(inner, shadowClient, sink: sink);
        using var cts = new CancellationTokenSource();

        var invocationTask = strategy.InvokeAsync(CreateContext(), cts.Token);
        (await Task.WhenAny(shadowStarted.Task, Task.Delay(TimeSpan.FromSeconds(1)))).Should().Be(shadowStarted.Task);
        cts.Cancel();

        var response = await invocationTask;

        response.Should().Be("primary answer");
        sink.Entries.Should().ContainSingle();
        var result = sink.Entries[0].Payload.Should().BeOfType<ShadowRoutingResult>().Subject;
        result.Comparison!.Notes.Should().Contain("shadow invocation failed: canceled");
    }

    [Fact]
    public async Task InvokeAsync_ignores_diagnostics_sink_failures()
    {
        var inner = new FixedAgentStrategy("primary-model", "primary answer", 5);
        var shadowClient = new FixedChatClient(new ChatResponse(new ChatMessage(ChatRole.Assistant, "shadow answer")));
        var sink = new Mock<IDiagnosticsSink>(MockBehavior.Strict);
        sink
            .Setup(item => item.RecordAsync(It.IsAny<DiagnosticEntry>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("sink unavailable"));
        var strategy = CreateStrategy(inner, shadowClient, sink: sink.Object);

        var response = await strategy.InvokeAsync(CreateContext());

        response.Should().Be("primary answer");
        sink.Verify(item => item.RecordAsync(It.IsAny<DiagnosticEntry>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void AddLeanKernelAgents_wraps_the_primary_strategy_when_shadow_routing_is_enabled()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ITokenEstimator, SimpleTokenEstimator>();
        services.AddSingleton(Options.Create(new LeanKernelConfig
        {
            LiteLlm = new LiteLlmConfig
            {
                ApiKey = "test-key"
            },
            Routing = new RoutingConfig
            {
                Enabled = false,
                ShadowRoutingEnabled = true,
                ShadowModel = "shadow-model"
            }
        }));
        services.AddLeanKernelAgents();

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IAgentStrategy>().Should().BeOfType<ShadowRoutingStrategy>();
    }

    private static ShadowRoutingStrategy CreateStrategy(
        IAgentStrategy inner,
        IChatClient shadowClient,
        IDiagnosticsSink? sink = null)
    {
        var factory = new AgentFactory(
            new FixedChatClient(new ChatResponse(new ChatMessage(ChatRole.Assistant, "unused default"))),
            NullLogger<AgentFactory>.Instance,
            new Dictionary<string, IChatClient>(StringComparer.OrdinalIgnoreCase)
            {
                ["shadow-model"] = shadowClient
            });
        var config = Options.Create(new LeanKernelConfig
        {
            Routing = new RoutingConfig
            {
                ShadowRoutingEnabled = true,
                ShadowModel = "shadow-model",
                RefusalPatterns = ["I cannot", "I'm sorry, I can't"]
            }
        });

        return new ShadowRoutingStrategy(
            inner,
            factory,
            new ShadowComparer(config),
            config,
            NullLogger<ShadowRoutingStrategy>.Instance,
            sink);
    }

    private static AgentStrategyContext CreateContext()
        => new()
        {
            SessionId = "session-1",
            TurnId = "turn-1",
            UserMessage = "Summarize the project status.",
            SystemMessage = "You are a helpful assistant.",
            History = []
        };

    private static TaskCompletionSource<bool> CreateSignal()
        => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private sealed class ControlledAgentStrategy(
        string modelUsed,
        string response,
        int tokensUsed,
        TaskCompletionSource<bool> started,
        TaskCompletionSource<bool> release) : IAgentStrategy
    {
        public string Name => "controlled";

        public async Task<string> InvokeAsync(AgentStrategyContext context, CancellationToken ct = default)
        {
            context.ModelUsed = modelUsed;
            context.TokensUsed = tokensUsed;
            started.TrySetResult(true);
            await release.Task.WaitAsync(ct);
            return response;
        }
    }

    private sealed class FixedAgentStrategy(string modelUsed, string response, int tokensUsed) : IAgentStrategy
    {
        public string Name => "fixed";

        public Task<string> InvokeAsync(AgentStrategyContext context, CancellationToken ct = default)
        {
            context.ModelUsed = modelUsed;
            context.TokensUsed = tokensUsed;
            return Task.FromResult(response);
        }
    }

    private sealed class ControlledChatClient(
        ChatResponse response,
        TaskCompletionSource<bool> started,
        TaskCompletionSource<bool> release) : IChatClient
    {
        public async Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            started.TrySetResult(true);
            await release.Task.WaitAsync(cancellationToken);
            return response;
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    private sealed class FixedChatClient(ChatResponse response) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(response);

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    private sealed class ThrowingChatClient(Exception exception) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => Task.FromException<ChatResponse>(exception);

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    private sealed class RecordingDiagnosticsSink : IDiagnosticsSink
    {
        public List<DiagnosticEntry> Entries { get; } = [];

        public Task RecordAsync(DiagnosticEntry entry, CancellationToken ct = default)
        {
            Entries.Add(entry);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<DiagnosticEntry>> GetEntriesAsync(string sessionId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<DiagnosticEntry>>(Entries.Where(entry => entry.SessionId == sessionId).ToList());
    }
}
