using FluentAssertions;
using LeanKernel.Abstractions.Configuration;
using LeanKernel.Abstractions.Interfaces;
using LeanKernel.Abstractions.Models;
using LeanKernel.Agents;
using LeanKernel.Channels;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace LeanKernel.Tests.Unit.Channels;

public class ChannelRouterTests
{
    [Fact]
    public async Task RouteInboundAsync_routes_authorized_message_through_runtime_and_sends_response()
    {
        var runtime = new Mock<IAgentRuntime>(MockBehavior.Strict);
        var sessions = new Mock<ISessionStore>(MockBehavior.Strict);
        var channel = new TestChannel("signal");
        var message = new ChannelMessage
        {
            ChannelId = "signal",
            SenderId = "+15550001",
            Content = "hello"
        };

        runtime
            .Setup(candidate => candidate.RunTurnDetailedAsync(
                It.Is<LeanKernelMessage>(payload =>
                    payload.ChannelId == "signal"
                    && payload.SenderId == "+15550001"
                    && payload.SessionId == "session-1"
                    && payload.Content == "hello"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentResponse { Content = "hi back" });

        sessions
            .Setup(store => store.GetOrCreateSessionIdAsync("signal", "+15550001", It.IsAny<CancellationToken>()))
            .ReturnsAsync("session-1");

        var router = CreateRouter(runtime.Object, sessions.Object, channel, new ChannelsConfig
        {
            ChannelAuth =
            [
                new ChannelAuthConfig
                {
                    ChannelId = "signal",
                    RequireAuth = true,
                    AllowedSenders = ["+15550001"]
                }
            ]
        });

        await router.RouteInboundAsync(message);

        runtime.VerifyAll();
        sessions.VerifyAll();
        channel.SentMessages.Should().ContainSingle();
        channel.SentMessages[0].RecipientId.Should().Be("+15550001");
        channel.SentMessages[0].Message.Should().Be("hi back");
        channel.TypingStarts.Should().BeGreaterThanOrEqualTo(1);
        channel.TypingStops.Should().Be(1);
    }

    [Fact]
    public async Task RouteInboundAsync_uses_the_first_registered_channel_when_duplicates_are_present()
    {
        var runtime = new Mock<IAgentRuntime>(MockBehavior.Strict);
        var sessions = new Mock<ISessionStore>(MockBehavior.Strict);
        var primaryChannel = new TestChannel("signal");
        var duplicateChannel = new TestChannel("signal");

        runtime
            .Setup(candidate => candidate.RunTurnDetailedAsync(It.IsAny<LeanKernelMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentResponse { Content = "hi back" });

        sessions
            .Setup(store => store.GetOrCreateSessionIdAsync("signal", "+15550001", It.IsAny<CancellationToken>()))
            .ReturnsAsync("session-1");

        var router = CreateRouter(
            runtime.Object,
            sessions.Object,
            [primaryChannel, duplicateChannel],
            new ChannelsConfig
            {
                ChannelAuth =
                [
                    new ChannelAuthConfig
                    {
                        ChannelId = "signal",
                        RequireAuth = false
                    }
                ]
            });

        await router.RouteInboundAsync(new ChannelMessage
        {
            ChannelId = "signal",
            SenderId = "+15550001",
            Content = "hello"
        });

        primaryChannel.SentMessages.Should().ContainSingle();
        duplicateChannel.SentMessages.Should().BeEmpty();
    }

    [Fact]
    public async Task RouteInboundAsync_rejects_unauthorized_messages_before_the_runtime_is_called()
    {
        var runtime = new Mock<IAgentRuntime>(MockBehavior.Strict);
        var sessions = new Mock<ISessionStore>(MockBehavior.Strict);
        var channel = new TestChannel("signal");
        var router = CreateRouter(runtime.Object, sessions.Object, channel, new ChannelsConfig
        {
            ChannelAuth =
            [
                new ChannelAuthConfig
                {
                    ChannelId = "signal",
                    RequireAuth = true,
                    AllowedSenders = ["+15550001"]
                }
            ]
        });

        await router.RouteInboundAsync(new ChannelMessage
        {
            ChannelId = "signal",
            SenderId = "+15550002",
            Content = "hello"
        });

        runtime.Verify(candidate => candidate.RunTurnDetailedAsync(It.IsAny<LeanKernelMessage>(), It.IsAny<CancellationToken>()), Times.Never);
        sessions.Verify(store => store.GetOrCreateSessionIdAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        channel.SentMessages.Should().BeEmpty();
        channel.TypingStarts.Should().Be(0);
        channel.TypingStops.Should().Be(0);
    }

    [Fact]
    public async Task RouteInboundAsync_forwards_runtime_attachments_to_channel_send()
    {
        var runtime = new Mock<IAgentRuntime>(MockBehavior.Strict);
        var sessions = new Mock<ISessionStore>(MockBehavior.Strict);
        var channel = new TestChannel("signal");
        var attachment = new Attachment
        {
            FileName = "sample.txt",
            ContentType = "text/plain",
            Data = [1, 2, 3]
        };

        runtime
            .Setup(candidate => candidate.RunTurnDetailedAsync(It.IsAny<LeanKernelMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentResponse
            {
                Content = "with attachment",
                Attachments = [attachment]
            });

        sessions
            .Setup(store => store.GetOrCreateSessionIdAsync("signal", "+15550001", It.IsAny<CancellationToken>()))
            .ReturnsAsync("session-1");

        var router = CreateRouter(runtime.Object, sessions.Object, channel, new ChannelsConfig
        {
            ChannelAuth =
            [
                new ChannelAuthConfig
                {
                    ChannelId = "signal",
                    RequireAuth = false
                }
            ]
        });

        await router.RouteInboundAsync(new ChannelMessage
        {
            ChannelId = "signal",
            SenderId = "+15550001",
            Content = "hello"
        });

        channel.SentMessages.Should().ContainSingle();
        channel.SentMessages[0].Attachments.Should().NotBeNull();
        channel.SentMessages[0].Attachments!.Should().ContainSingle();
        channel.SentMessages[0].Attachments![0].FileName.Should().Be("sample.txt");
        sessions.VerifyAll();
    }

    [Fact]
    public async Task RouteInboundAsync_ignores_messages_for_unknown_channel_ids()
    {
        var runtime = new Mock<IAgentRuntime>(MockBehavior.Strict);
        var sessions = new Mock<ISessionStore>(MockBehavior.Strict);
        var channel = new TestChannel("signal");
        var router = CreateRouter(runtime.Object, sessions.Object, channel, new ChannelsConfig
        {
            ChannelAuth =
            [
                new ChannelAuthConfig
                {
                    ChannelId = "discord",
                    RequireAuth = false
                }
            ]
        });

        await router.RouteInboundAsync(new ChannelMessage
        {
            ChannelId = "discord",
            SenderId = "user-1",
            Content = "hello"
        });

        runtime.Verify(candidate => candidate.RunTurnDetailedAsync(It.IsAny<LeanKernelMessage>(), It.IsAny<CancellationToken>()), Times.Never);
        sessions.Verify(store => store.GetOrCreateSessionIdAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        channel.SentMessages.Should().BeEmpty();
        channel.TypingStarts.Should().Be(0);
        channel.TypingStops.Should().Be(0);
    }

    [Fact]
    public async Task RouteInboundAsync_publishes_progress_updates_while_the_turn_is_in_flight()
    {
        var runtime = new Mock<IAgentRuntime>(MockBehavior.Strict);
        var sessions = new Mock<ISessionStore>(MockBehavior.Strict);
        var channel = new TestChannel("signal");
        var progressBroker = new TurnProgressBroker();
        var timeProvider = new MutableTimeProvider(DateTimeOffset.Parse("2025-05-20T10:00:00Z"));
        var turnStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseTurn = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        string? runtimeTurnId = null;

        sessions
            .Setup(store => store.GetOrCreateSessionIdAsync("signal", "+15550001", It.IsAny<CancellationToken>()))
            .ReturnsAsync("session-1");

        runtime
            .Setup(candidate => candidate.RunTurnDetailedAsync(It.IsAny<LeanKernelMessage>(), It.IsAny<CancellationToken>()))
            .Returns<LeanKernelMessage, CancellationToken>(async (payload, _) =>
            {
                runtimeTurnId = payload.Metadata!["turn_id"];
                turnStarted.TrySetResult();
                await releaseTurn.Task.ConfigureAwait(false);
                return new AgentResponse { Content = "final response" };
            });

        var router = CreateRouter(
            runtime.Object,
            sessions.Object,
            channel,
            new ChannelsConfig
            {
                ChannelAuth =
                [
                    new ChannelAuthConfig
                    {
                        ChannelId = "signal",
                        RequireAuth = false
                    }
                ]
            },
            new LeanKernelConfig
            {
                Continuation = new ContinuationConfig
                {
                    Progress = new ContinuationProgressConfig
                    {
                        Enabled = true,
                        InitialSilenceSeconds = 0,
                        MinIntervalSeconds = 0,
                        HeartbeatSeconds = 1
                    }
                }
            },
            progressBroker,
            timeProvider: timeProvider);

        var routeTask = router.RouteInboundAsync(new ChannelMessage
        {
            ChannelId = "signal",
            SenderId = "+15550001",
            Content = "hello"
        });

        await turnStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
        runtimeTurnId.Should().NotBeNullOrWhiteSpace();
        await progressBroker.PublishAsync(
            new TurnProgressUpdate(
                "session-1",
                runtimeTurnId!,
                TurnProgressKind.ToolStarted,
                "inspect",
                null,
                DateTimeOffset.UtcNow));
        timeProvider.Advance(TimeSpan.FromSeconds(2));
        await progressBroker.PublishAsync(
            new TurnProgressUpdate(
                "session-1",
                runtimeTurnId!,
                TurnProgressKind.ToolCompleted,
                "inspect complete",
                "inspect complete",
                DateTimeOffset.UtcNow));
        timeProvider.Advance(TimeSpan.FromSeconds(2));
        await progressBroker.PublishAsync(
            new TurnProgressUpdate(
                "session-1",
                runtimeTurnId!,
                TurnProgressKind.ContinuationStarted,
                null,
                "keep going",
                DateTimeOffset.UtcNow));
        timeProvider.Advance(TimeSpan.FromSeconds(2));
        await progressBroker.PublishAsync(
            new TurnProgressUpdate(
                "session-1",
                runtimeTurnId!,
                TurnProgressKind.StatusNote,
                null,
                "still refining",
                DateTimeOffset.UtcNow));
        await progressBroker.PublishAsync(
            new TurnProgressUpdate(
                "session-1",
                "turn-not-active",
                TurnProgressKind.ToolStarted,
                "other-tool",
                null,
                DateTimeOffset.UtcNow));
        releaseTurn.TrySetResult();
        await routeTask;

        channel.SentMessages.Should().Contain(message => message.Message == "Working with inspect...");
        channel.SentMessages.Should().Contain(message => message.Message == "inspect complete");
        channel.SentMessages.Should().Contain(message => message.Message == "keep going");
        channel.SentMessages.Should().Contain(message => message.Message == "still refining");
        channel.SentMessages.Should().NotContain(message => message.Message == "Working with other-tool...");
        channel.SentMessages.Should().Contain(message => message.Message == "final response");
        channel.TypingStarts.Should().BeGreaterThanOrEqualTo(1);
        channel.TypingStops.Should().Be(1);
    }

    [Fact]
    public async Task RouteInboundAsync_skips_progress_dispatch_when_progress_updates_are_disabled()
    {
        var runtime = new Mock<IAgentRuntime>(MockBehavior.Strict);
        var sessions = new Mock<ISessionStore>(MockBehavior.Strict);
        var channel = new TestChannel("signal");
        var progressBroker = new TurnProgressBroker();
        var turnStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseTurn = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        string? runtimeTurnId = null;

        sessions
            .Setup(store => store.GetOrCreateSessionIdAsync("signal", "+15550001", It.IsAny<CancellationToken>()))
            .ReturnsAsync("session-1");

        runtime
            .Setup(candidate => candidate.RunTurnDetailedAsync(It.IsAny<LeanKernelMessage>(), It.IsAny<CancellationToken>()))
            .Returns<LeanKernelMessage, CancellationToken>(async (payload, _) =>
            {
                runtimeTurnId = payload.Metadata!["turn_id"];
                turnStarted.TrySetResult();
                await releaseTurn.Task.ConfigureAwait(false);
                return new AgentResponse { Content = "final response" };
            });

        var router = CreateRouter(
            runtime.Object,
            sessions.Object,
            channel,
            new ChannelsConfig
            {
                ChannelAuth =
                [
                    new ChannelAuthConfig
                    {
                        ChannelId = "signal",
                        RequireAuth = false
                    }
                ]
            },
            new LeanKernelConfig
            {
                Continuation = new ContinuationConfig
                {
                    Progress = new ContinuationProgressConfig
                    {
                        Enabled = false,
                        InitialSilenceSeconds = 0,
                        MinIntervalSeconds = 0,
                        HeartbeatSeconds = 1
                    }
                }
            },
            progressBroker);

        var routeTask = router.RouteInboundAsync(new ChannelMessage
        {
            ChannelId = "signal",
            SenderId = "+15550001",
            Content = "hello"
        });

        await turnStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
        runtimeTurnId.Should().NotBeNullOrWhiteSpace();
        await progressBroker.PublishAsync(
            new TurnProgressUpdate(
                "session-1",
                runtimeTurnId!,
                TurnProgressKind.ToolStarted,
                "inspect",
                null,
                DateTimeOffset.UtcNow));
        releaseTurn.TrySetResult();
        await routeTask;

        channel.SentMessages.Should().ContainSingle(message => message.Message == "final response");
        channel.SentMessages.Should().NotContain(message => message.Message == "Working with inspect...");
        channel.TypingStarts.Should().BeGreaterThanOrEqualTo(1);
        channel.TypingStops.Should().Be(1);
    }

    [Fact]
    public async Task RouteInboundAsync_suppresses_progress_messages_before_initial_silence_expires()
    {
        var runtime = new Mock<IAgentRuntime>(MockBehavior.Strict);
        var sessions = new Mock<ISessionStore>(MockBehavior.Strict);
        var channel = new TestChannel("signal");
        var progressBroker = new TurnProgressBroker();
        var turnStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseTurn = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        string? runtimeTurnId = null;

        sessions
            .Setup(store => store.GetOrCreateSessionIdAsync("signal", "+15550001", It.IsAny<CancellationToken>()))
            .ReturnsAsync("session-1");

        runtime
            .Setup(candidate => candidate.RunTurnDetailedAsync(It.IsAny<LeanKernelMessage>(), It.IsAny<CancellationToken>()))
            .Returns<LeanKernelMessage, CancellationToken>(async (payload, _) =>
            {
                runtimeTurnId = payload.Metadata!["turn_id"];
                turnStarted.TrySetResult();
                await releaseTurn.Task.ConfigureAwait(false);
                return new AgentResponse { Content = "final response" };
            });

        var router = CreateRouter(
            runtime.Object,
            sessions.Object,
            channel,
            new ChannelsConfig
            {
                ChannelAuth =
                [
                    new ChannelAuthConfig
                    {
                        ChannelId = "signal",
                        RequireAuth = false
                    }
                ]
            },
            new LeanKernelConfig
            {
                Continuation = new ContinuationConfig
                {
                    Progress = new ContinuationProgressConfig
                    {
                        Enabled = true,
                        InitialSilenceSeconds = 60,
                        MinIntervalSeconds = 0,
                        HeartbeatSeconds = 1
                    }
                }
            },
            progressBroker);

        var routeTask = router.RouteInboundAsync(new ChannelMessage
        {
            ChannelId = "signal",
            SenderId = "+15550001",
            Content = "hello"
        });

        await turnStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
        runtimeTurnId.Should().NotBeNullOrWhiteSpace();
        await progressBroker.PublishAsync(
            new TurnProgressUpdate(
                "session-1",
                runtimeTurnId!,
                TurnProgressKind.ToolStarted,
                "inspect",
                null,
                DateTimeOffset.UtcNow));
        releaseTurn.TrySetResult();
        await routeTask;

        channel.SentMessages.Should().ContainSingle(message => message.Message == "final response");
        channel.SentMessages.Should().NotContain(message => message.Message == "Working with inspect...");
    }

    [Fact]
    public async Task RouteInboundAsync_keeps_only_one_progress_message_when_updates_arrive_inside_the_minimum_interval()
    {
        var runtime = new Mock<IAgentRuntime>(MockBehavior.Strict);
        var sessions = new Mock<ISessionStore>(MockBehavior.Strict);
        var channel = new TestChannel("signal");
        var progressBroker = new TurnProgressBroker();
        var turnStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseTurn = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        string? runtimeTurnId = null;

        sessions
            .Setup(store => store.GetOrCreateSessionIdAsync("signal", "+15550001", It.IsAny<CancellationToken>()))
            .ReturnsAsync("session-1");

        runtime
            .Setup(candidate => candidate.RunTurnDetailedAsync(It.IsAny<LeanKernelMessage>(), It.IsAny<CancellationToken>()))
            .Returns<LeanKernelMessage, CancellationToken>(async (payload, _) =>
            {
                runtimeTurnId = payload.Metadata!["turn_id"];
                turnStarted.TrySetResult();
                await releaseTurn.Task.ConfigureAwait(false);
                return new AgentResponse { Content = "final response" };
            });

        var router = CreateRouter(
            runtime.Object,
            sessions.Object,
            channel,
            new ChannelsConfig
            {
                ChannelAuth =
                [
                    new ChannelAuthConfig
                    {
                        ChannelId = "signal",
                        RequireAuth = false
                    }
                ]
            },
            new LeanKernelConfig
            {
                Continuation = new ContinuationConfig
                {
                    Progress = new ContinuationProgressConfig
                    {
                        Enabled = true,
                        InitialSilenceSeconds = 0,
                        MinIntervalSeconds = 60,
                        HeartbeatSeconds = 1
                    }
                }
            },
            progressBroker);

        var routeTask = router.RouteInboundAsync(new ChannelMessage
        {
            ChannelId = "signal",
            SenderId = "+15550001",
            Content = "hello"
        });

        await turnStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
        runtimeTurnId.Should().NotBeNullOrWhiteSpace();
        await progressBroker.PublishAsync(
            new TurnProgressUpdate(
                "session-1",
                runtimeTurnId!,
                TurnProgressKind.ToolStarted,
                "inspect",
                null,
                DateTimeOffset.UtcNow));
        await progressBroker.PublishAsync(
            new TurnProgressUpdate(
                "session-1",
                runtimeTurnId!,
                TurnProgressKind.ToolStarted,
                "inspect",
                null,
                DateTimeOffset.UtcNow));
        releaseTurn.TrySetResult();
        await routeTask;

        channel.SentMessages.Should().Contain(message => message.Message == "Working with inspect...");
        channel.SentMessages.Count(message => message.Message == "Working with inspect...").Should().Be(1);
        channel.SentMessages.Should().ContainSingle(message => message.Message == "final response");
    }

    [Fact]
    public async Task RouteInboundAsync_continues_when_progress_delivery_throws()
    {
        var runtime = new Mock<IAgentRuntime>(MockBehavior.Strict);
        var sessions = new Mock<ISessionStore>(MockBehavior.Strict);
        var channel = new TestChannel("signal")
        {
            ThrowOnSendWhenMessageContains = "Working with inspect..."
        };
        var progressBroker = new TurnProgressBroker();
        var turnStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseTurn = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        string? runtimeTurnId = null;

        sessions
            .Setup(store => store.GetOrCreateSessionIdAsync("signal", "+15550001", It.IsAny<CancellationToken>()))
            .ReturnsAsync("session-1");

        runtime
            .Setup(candidate => candidate.RunTurnDetailedAsync(It.IsAny<LeanKernelMessage>(), It.IsAny<CancellationToken>()))
            .Returns<LeanKernelMessage, CancellationToken>(async (payload, _) =>
            {
                runtimeTurnId = payload.Metadata!["turn_id"];
                turnStarted.TrySetResult();
                await releaseTurn.Task.ConfigureAwait(false);
                return new AgentResponse { Content = "final response" };
            });

        var router = CreateRouter(
            runtime.Object,
            sessions.Object,
            channel,
            new ChannelsConfig
            {
                ChannelAuth =
                [
                    new ChannelAuthConfig
                    {
                        ChannelId = "signal",
                        RequireAuth = false
                    }
                ]
            },
            new LeanKernelConfig
            {
                Continuation = new ContinuationConfig
                {
                    Progress = new ContinuationProgressConfig
                    {
                        Enabled = true,
                        InitialSilenceSeconds = 0,
                        MinIntervalSeconds = 0,
                        HeartbeatSeconds = 1
                    }
                }
            },
            progressBroker);

        var routeTask = router.RouteInboundAsync(new ChannelMessage
        {
            ChannelId = "signal",
            SenderId = "+15550001",
            Content = "hello"
        });

        await turnStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
        runtimeTurnId.Should().NotBeNullOrWhiteSpace();
        await progressBroker.PublishAsync(
            new TurnProgressUpdate(
                "session-1",
                runtimeTurnId!,
                TurnProgressKind.ToolStarted,
                "inspect",
                null,
                DateTimeOffset.UtcNow));
        releaseTurn.TrySetResult();
        await routeTask;

        channel.SentMessages.Should().ContainSingle(message => message.Message == "final response");
    }

    [Fact]
    public async Task RouteInboundAsync_emits_a_heartbeat_when_the_turn_runs_long_enough()
    {
        var runtime = new Mock<IAgentRuntime>(MockBehavior.Strict);
        var sessions = new Mock<ISessionStore>(MockBehavior.Strict);
        var channel = new TestChannel("signal");
        var progressBroker = new TurnProgressBroker();
        var timeProvider = new MutableTimeProvider(DateTimeOffset.Parse("2025-05-20T10:00:00Z"));
        var turnStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseTurn = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        sessions
            .Setup(store => store.GetOrCreateSessionIdAsync("signal", "+15550001", It.IsAny<CancellationToken>()))
            .ReturnsAsync("session-1");

        runtime
            .Setup(candidate => candidate.RunTurnDetailedAsync(It.IsAny<LeanKernelMessage>(), It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                turnStarted.TrySetResult();
                await releaseTurn.Task.ConfigureAwait(false);
                return new AgentResponse { Content = "final response" };
            });

        var router = CreateRouter(
            runtime.Object,
            sessions.Object,
            channel,
            new ChannelsConfig
            {
                ChannelAuth =
                [
                    new ChannelAuthConfig
                    {
                        ChannelId = "signal",
                        RequireAuth = false
                    }
                ]
            },
            new LeanKernelConfig
            {
                Continuation = new ContinuationConfig
                {
                    Progress = new ContinuationProgressConfig
                    {
                        Enabled = true,
                        InitialSilenceSeconds = 0,
                        MinIntervalSeconds = 0,
                        HeartbeatSeconds = 1
                    }
                }
            },
            progressBroker,
            timeProvider: timeProvider);

        var routeTask = router.RouteInboundAsync(new ChannelMessage
        {
            ChannelId = "signal",
            SenderId = "+15550001",
            Content = "hello"
        });

        await turnStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
        timeProvider.Advance(TimeSpan.FromSeconds(2));
        await WaitForMessageAsync(channel, "⏳ Still working...", TimeSpan.FromSeconds(2));
        releaseTurn.TrySetResult();
        await routeTask;

        channel.SentMessages.Should().Contain(message => message.Message == "⏳ Still working...");
        channel.SentMessages.Should().Contain(message => message.Message == "final response");
    }

    private static async Task WaitForMessageAsync(TestChannel channel, string expectedMessage, TimeSpan timeout)
    {
        var startedAt = DateTimeOffset.UtcNow;
        while (DateTimeOffset.UtcNow - startedAt < timeout)
        {
            if (channel.SentMessages.Any(entry => entry.Message == expectedMessage))
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(20));
        }

        throw new TimeoutException($"Timed out waiting for message '{expectedMessage}'.");
    }

    private static ChannelRouter CreateRouter(
        IAgentRuntime runtime,
        ISessionStore sessions,
        IChannel channel,
        ChannelsConfig config,
        LeanKernelConfig? leanKernelConfig = null,
        ITurnProgressBroker? progressBroker = null,
        TimeProvider? timeProvider = null)
        => CreateRouter(runtime, sessions, [channel], config, leanKernelConfig, progressBroker, timeProvider);

    private static ChannelRouter CreateRouter(
        IAgentRuntime runtime,
        ISessionStore sessions,
        IEnumerable<IChannel> channels,
        ChannelsConfig config,
        LeanKernelConfig? leanKernelConfig = null,
        ITurnProgressBroker? progressBroker = null,
        TimeProvider? timeProvider = null)
        => new(
            runtime,
            new ChannelAuthenticator(NullLogger<ChannelAuthenticator>.Instance, Options.Create(config)),
            channels,
            Options.Create(config),
            Options.Create(leanKernelConfig ?? new LeanKernelConfig()),
            sessions,
            NullLogger<ChannelRouter>.Instance,
            progressBroker,
            timeProvider: timeProvider);

    private sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;

        public void Advance(TimeSpan delta) => _utcNow = _utcNow.Add(delta);

        public override DateTimeOffset GetUtcNow() => _utcNow;
    }

    private sealed class TestChannel(string channelId) : IChannel
    {
        public string ChannelId { get; } = channelId;

        public bool IsConnected { get; private set; }

        public List<(string RecipientId, string Message, IReadOnlyList<Attachment>? Attachments)> SentMessages { get; } = [];

        public int TypingStarts { get; private set; }

        public int TypingStops { get; private set; }

        public string? ThrowOnSendWhenMessageContains { get; init; }

        public event Func<ChannelMessage, Task>? MessageReceived
        {
            add { }
            remove { }
        }

        public Task StartAsync(CancellationToken ct = default)
        {
            IsConnected = true;
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken ct = default)
        {
            IsConnected = false;
            return Task.CompletedTask;
        }

        public Task StartTypingAsync(string recipientId, CancellationToken ct = default)
        {
            TypingStarts++;
            return Task.CompletedTask;
        }

        public Task StopTypingAsync(string recipientId, CancellationToken ct = default)
        {
            TypingStops++;
            return Task.CompletedTask;
        }

        public Task SendAsync(string recipientId, string message, IReadOnlyList<Attachment>? attachments = null, CancellationToken ct = default)
        {
            if (!string.IsNullOrWhiteSpace(ThrowOnSendWhenMessageContains)
                && message.Contains(ThrowOnSendWhenMessageContains, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("send failed");
            }

            SentMessages.Add((recipientId, message, attachments));
            return Task.CompletedTask;
        }
    }
}
