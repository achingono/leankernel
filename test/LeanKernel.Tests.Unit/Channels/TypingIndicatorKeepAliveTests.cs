using FluentAssertions;
using System.Reflection;
using LeanKernel.Abstractions.Configuration;
using LeanKernel.Abstractions.Interfaces;
using LeanKernel.Abstractions.Models;
using LeanKernel.Channels;
using Microsoft.Extensions.Logging.Abstractions;

namespace LeanKernel.Tests.Unit.Channels;

public class TypingIndicatorKeepAliveTests
{
    [Fact]
    public async Task StopAsync_is_idempotent_and_sends_stop_once()
    {
        var channel = new TestChannel();
        await using var keepAlive = TypingIndicatorKeepAlive.Start(
            channel,
            "+15550001",
            new TypingConfig
            {
                Enabled = true,
                KeepAliveSeconds = 1,
                StopTimeoutSeconds = 2,
            },
            TimeProvider.System,
            NullLogger.Instance);

        var started = await channel.WaitForStartTypingAsync(TimeSpan.FromSeconds(1));
        started.Should().BeTrue("the initial typing signal should be emitted before stopping");
        await keepAlive.StopAsync();
        await keepAlive.StopAsync();

        channel.TypingStarts.Should().BeGreaterThanOrEqualTo(1);
        channel.TypingStops.Should().Be(1);
    }

    [Fact]
    public async Task StartAsync_still_sends_the_initial_typing_indicator_when_refresh_is_disabled()
    {
        var channel = new TestChannel();
        await using var keepAlive = TypingIndicatorKeepAlive.Start(
            channel,
            "+15550002",
            new TypingConfig
            {
                Enabled = false,
                KeepAliveSeconds = 1,
                StopTimeoutSeconds = 2,
            },
            TimeProvider.System,
            NullLogger.Instance);

        var started = await channel.WaitForStartTypingAsync(TimeSpan.FromSeconds(1));
        started.Should().BeTrue("the initial typing signal should be emitted even when refresh is disabled");
        await keepAlive.StopAsync();

        channel.TypingStarts.Should().Be(1);
        channel.TypingStops.Should().Be(1);
    }

    [Fact]
    public async Task StartAsync_swallows_refresh_and_stop_failures()
    {
        var channel = new TestChannel
        {
            ThrowOnStartTyping = true,
            ThrowOnStopTyping = true,
        };

        await using var keepAlive = TypingIndicatorKeepAlive.Start(
            channel,
            "+15550003",
            new TypingConfig
            {
                Enabled = true,
                KeepAliveSeconds = 1,
                StopTimeoutSeconds = 2,
            },
            TimeProvider.System,
            NullLogger.Instance);

        var started = await channel.WaitForStartTypingAsync(TimeSpan.FromSeconds(1));
        started.Should().BeTrue("the start attempt should be observed even when it fails");
        await keepAlive.StopAsync();

        channel.TypingStarts.Should().Be(1);
        channel.TypingStops.Should().Be(1);
    }

    [Fact]
    public async Task StartAsync_swallows_cancellation_during_the_initial_typing_refresh()
    {
        var channel = new TestChannel
        {
            CancelOnStartTyping = true
        };

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await using var keepAlive = TypingIndicatorKeepAlive.Start(
            channel,
            "+15550004",
            new TypingConfig
            {
                Enabled = true,
                KeepAliveSeconds = 1,
                StopTimeoutSeconds = 2,
            },
            TimeProvider.System,
            NullLogger.Instance,
            cts.Token);

        var started = await channel.WaitForStartTypingAsync(TimeSpan.FromSeconds(1));
        started.Should().BeTrue("the canceled start attempt should still be observed");
        await keepAlive.StopAsync();

        channel.TypingStarts.Should().Be(1);
        channel.TypingStops.Should().Be(1);
    }

    [Fact]
    public async Task RunLoopAsync_returns_immediately_when_no_timer_is_configured()
    {
        var channel = new TestChannel();
        await using var keepAlive = TypingIndicatorKeepAlive.Start(
            channel,
            "+15550005",
            new TypingConfig
            {
                Enabled = false,
                KeepAliveSeconds = 1,
                StopTimeoutSeconds = 2,
            },
            TimeProvider.System,
            NullLogger.Instance);

        var method = typeof(TypingIndicatorKeepAlive).GetMethod("RunLoopAsync", BindingFlags.Instance | BindingFlags.NonPublic);
        method.Should().NotBeNull();
        var task = (Task?)method!.Invoke(keepAlive, [CancellationToken.None]);
        task.Should().NotBeNull();
        await task!;
        await keepAlive.StopAsync();

        channel.TypingStarts.Should().Be(1);
        channel.TypingStops.Should().Be(1);
    }

    private sealed class TestChannel : IChannel
    {
        private readonly TaskCompletionSource<bool> _startTypingObserved = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _typingStarts;
        private int _typingStops;

        public string ChannelId => "signal";

        public bool IsConnected => true;

        public int TypingStarts => Volatile.Read(ref _typingStarts);

        public int TypingStops => Volatile.Read(ref _typingStops);

        public bool ThrowOnStartTyping { get; init; }

        public bool ThrowOnStopTyping { get; init; }

        public bool CancelOnStartTyping { get; init; }

        public event Func<ChannelMessage, Task>? MessageReceived
        {
            add { }
            remove { }
        }

        public Task StartAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task StopAsync(CancellationToken ct = default) => Task.CompletedTask;

        public async Task<bool> WaitForStartTypingAsync(TimeSpan timeout, CancellationToken ct = default)
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(timeout);
            try
            {
                await _startTypingObserved.Task.WaitAsync(timeoutCts.Token);
                return true;
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                return false;
            }
        }

        public Task StartTypingAsync(string recipientId, CancellationToken ct = default)
        {
            Interlocked.Increment(ref _typingStarts);
            _startTypingObserved.TrySetResult(true);
            if (CancelOnStartTyping)
            {
                return Task.FromCanceled(ct);
            }

            if (ThrowOnStartTyping)
            {
                throw new InvalidOperationException("start failed");
            }

            return Task.CompletedTask;
        }

        public Task StopTypingAsync(string recipientId, CancellationToken ct = default)
        {
            Interlocked.Increment(ref _typingStops);
            if (ThrowOnStopTyping)
            {
                throw new InvalidOperationException("stop failed");
            }

            return Task.CompletedTask;
        }

        public Task SendAsync(string recipientId, string message, IReadOnlyList<Attachment>? attachments = null, CancellationToken ct = default)
            => Task.CompletedTask;
    }
}
