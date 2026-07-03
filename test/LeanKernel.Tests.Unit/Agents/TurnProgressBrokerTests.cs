using FluentAssertions;
using LeanKernel.Abstractions.Models;
using LeanKernel.Agents;
using Xunit.Sdk;

namespace LeanKernel.Tests.Unit.Agents;

public class TurnProgressBrokerTests
{
    [Fact]
    public async Task PublishAsync_delivers_to_subscribers_and_unsubscribes_disposed_handlers()
    {
        var broker = new TurnProgressBroker();
        var firstCalls = 0;
        var secondCalls = 0;

        var first = broker.Subscribe("session-1", _ =>
        {
            firstCalls++;
            return Task.CompletedTask;
        });
        var second = broker.Subscribe("session-1", _ =>
        {
            secondCalls++;
            return Task.CompletedTask;
        });

        await broker.PublishAsync(new TurnProgressUpdate("session-1", "turn-1", TurnProgressKind.ToolStarted, "wiki_read", null, DateTimeOffset.UtcNow));
        firstCalls.Should().Be(1);
        secondCalls.Should().Be(1);

        first.Dispose();
        await broker.PublishAsync(new TurnProgressUpdate("session-1", "turn-2", TurnProgressKind.StatusNote, null, "Working", DateTimeOffset.UtcNow));
        firstCalls.Should().Be(1);
        secondCalls.Should().Be(2);

        second.Dispose();
        await broker.PublishAsync(new TurnProgressUpdate("session-1", "turn-3", TurnProgressKind.Heartbeat, null, "Still working", DateTimeOffset.UtcNow));
        firstCalls.Should().Be(1);
        secondCalls.Should().Be(2);
    }

    [Fact]
    public async Task PublishAsync_keeps_publishing_when_one_subscriber_throws()
    {
        var broker = new TurnProgressBroker();
        var delivered = false;

        broker.Subscribe("session-1", _ => throw new InvalidOperationException("boom"));
        broker.Subscribe("session-1", _ =>
        {
            delivered = true;
            return Task.CompletedTask;
        });

        await broker.PublishAsync(new TurnProgressUpdate("session-1", "turn-1", TurnProgressKind.ContinuationStarted, null, "Continuing", DateTimeOffset.UtcNow));

        delivered.Should().BeTrue();
    }

    [Fact]
    public async Task PublishAsync_dispatches_subscribers_concurrently()
    {
        var broker = new TurnProgressBroker();
        var startedCount = 0;
        var bothStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        broker.Subscribe("session-1", async _ =>
        {
            if (Interlocked.Increment(ref startedCount) == 2)
            {
                bothStarted.TrySetResult();
            }

            await release.Task.ConfigureAwait(false);
        });

        broker.Subscribe("session-1", async _ =>
        {
            if (Interlocked.Increment(ref startedCount) == 2)
            {
                bothStarted.TrySetResult();
            }

            await release.Task.ConfigureAwait(false);
        });

        var publishTask = broker.PublishAsync(new TurnProgressUpdate("session-1", "turn-1", TurnProgressKind.ToolStarted, "tool", null, DateTimeOffset.UtcNow));

        await WaitWithTimeoutAsync(
            bothStarted.Task,
            TimeSpan.FromSeconds(1),
            "Expected both subscriber handlers to start before publish completed.");
        startedCount.Should().Be(2);

        release.TrySetResult();
        await WaitWithTimeoutAsync(
            publishTask,
            TimeSpan.FromSeconds(1),
            "PublishAsync did not complete after releasing subscriber handlers.");
    }

    [Fact]
    public async Task PublishAsync_returns_without_invoking_handlers_when_pre_canceled()
    {
        var broker = new TurnProgressBroker();
        var called = 0;

        broker.Subscribe("session-1", _ =>
        {
            Interlocked.Increment(ref called);
            return Task.CompletedTask;
        });

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await broker.PublishAsync(
            new TurnProgressUpdate("session-1", "turn-1", TurnProgressKind.ToolStarted, "tool", null, DateTimeOffset.UtcNow),
            cts.Token);

        called.Should().Be(0);
    }

    private static async Task WaitWithTimeoutAsync(Task task, TimeSpan timeout, string failureMessage)
    {
        try
        {
            await task.WaitAsync(timeout);
        }
        catch (TimeoutException)
        {
            throw new XunitException(failureMessage);
        }
    }
}
