using FluentAssertions;
using LeanKernel.Logic.Configuration;
using LeanKernel.Logic.Telemetry;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using Xunit;

namespace LeanKernel.Tests.Unit.Telemetry;

public sealed class TelemetryCapturingChatClientTests
{
    [Fact]
    public async Task GetResponseAsync_CapturesReportedCostTelemetry()
    {
        var collector = new TurnTelemetryCollector();
        var response = new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok"))
        {
            ModelId = "gpt-4o-mini",
            Usage = new UsageDetails { InputTokenCount = 120, OutputTokenCount = 30, TotalTokenCount = 150 },
            AdditionalProperties = new AdditionalPropertiesDictionary
            {
                ["x-litellm-response-cost"] = "0.0125",
                ["api_base"] = "https://api.example.test"
            }
        };
        var inner = new StubChatClient(response, []);
        var client = CreateSut(inner, collector, new CostEstimateTable(), new TelemetrySettings { Currency = "USD", UseCostEstimate = true });

        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hello")], new ChatOptions { ModelId = "requested-model" });

        var telemetry = collector.Consume();
        telemetry.Should().NotBeNull();
        telemetry!.RequestedModel.Should().Be("requested-model");
        telemetry.ServedModel.Should().Be("gpt-4o-mini");
        telemetry.Provider.Should().Be("openai");
        telemetry.ApiBase.Should().Be("https://api.example.test");
        telemetry.PromptTokens.Should().Be(120);
        telemetry.CompletionTokens.Should().Be(30);
        telemetry.TotalTokens.Should().Be(150);
        telemetry.ResponseCost.Should().Be(0.0125m);
        telemetry.CostIsEstimated.Should().BeFalse();
    }

    [Fact]
    public async Task GetResponseAsync_UsesEstimatedCostWhenMissingReportedCost()
    {
        var collector = new TurnTelemetryCollector();
        var response = new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok"))
        {
            ModelId = "gpt-4o-mini",
            Usage = new UsageDetails { InputTokenCount = 1000, OutputTokenCount = 1000, TotalTokenCount = 2000 },
            AdditionalProperties = new AdditionalPropertiesDictionary()
        };
        var table = new CostEstimateTable
        {
            CostPer1kInputTokens = { ["gpt-4o-mini"] = 0.001m },
            CostPer1kOutputTokens = { ["gpt-4o-mini"] = 0.002m }
        };
        var inner = new StubChatClient(response, []);
        var client = CreateSut(inner, collector, table, new TelemetrySettings { Currency = "USD", UseCostEstimate = true });

        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "hello")], new ChatOptions { ModelId = "gpt-4o-mini" });

        var telemetry = collector.Consume();
        telemetry.Should().NotBeNull();
        telemetry!.ResponseCost.Should().Be(0.003m);
        telemetry.CostIsEstimated.Should().BeTrue();
    }

    [Fact]
    public async Task GetStreamingResponseAsync_CapturesFinalStreamingTelemetry()
    {
        var collector = new TurnTelemetryCollector();
        var updates = new[]
        {
            new ChatResponseUpdate(ChatRole.Assistant, "hello") { ModelId = "claude-3-sonnet" },
            new ChatResponseUpdate(ChatRole.Assistant, " world") { ModelId = "claude-3-sonnet" },
            new ChatResponseUpdate(ChatRole.Assistant, string.Empty) { ModelId = "claude-3-sonnet" }
        };
        var inner = new StubChatClient(new ChatResponse(new ChatMessage(ChatRole.Assistant, "ignored")), updates);
        var client = CreateSut(inner, collector, new CostEstimateTable(), new TelemetrySettings { UseCostEstimate = false });

        var emitted = new List<ChatResponseUpdate>();
        await foreach (var update in client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "hello")]))
        {
            emitted.Add(update);
        }

        emitted.Should().HaveCount(3);

        var telemetry = collector.Consume();
        telemetry.Should().NotBeNull();
        telemetry!.ServedModel.Should().Be("claude-3-sonnet");
        telemetry.Provider.Should().Be("anthropic");
    }

    [Fact]
    public async Task GetResponseAsync_WhenCollectorThrows_DoesNotPropagate()
    {
        var response = new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok"))
        {
            ModelId = "gpt-4o-mini"
        };
        var inner = new StubChatClient(response, []);
        var client = CreateSut(inner, new ThrowingCollector(), new CostEstimateTable(), new TelemetrySettings());

        var act = () => client.GetResponseAsync([new ChatMessage(ChatRole.User, "hello")]);

        await act.Should().NotThrowAsync();
    }

    private static IChatClient CreateSut(
        IChatClient inner,
        ITurnTelemetryCollector collector,
        CostEstimateTable table,
        TelemetrySettings settings)
    {
        var telemetryClientType = typeof(TurnTelemetryCollector).Assembly
            .GetType("LeanKernel.Logic.Telemetry.TelemetryCapturingChatClient", throwOnError: true)!;

        return (IChatClient)Activator.CreateInstance(
            telemetryClientType,
            inner,
            collector,
            table,
            Options.Create(settings),
            null)!
            ;
    }

    private sealed class StubChatClient(ChatResponse response, IReadOnlyList<ChatResponseUpdate> updates) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(response);
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            foreach (var update in updates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return update;
                await Task.Yield();
            }
        }

        public object? GetService(Type serviceType, object? serviceKey = null)
        {
            return null;
        }

        public void Dispose()
        {
            GC.SuppressFinalize(this);
        }
    }

    private sealed class ThrowingCollector : ITurnTelemetryCollector
    {
        public void Capture(TurnTelemetry telemetry)
        {
            throw new InvalidOperationException("capture failed");
        }

        public TurnTelemetry? Consume()
        {
            return null;
        }
    }
}
