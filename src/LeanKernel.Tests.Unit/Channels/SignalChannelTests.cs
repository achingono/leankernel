using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using LeanKernel.Abstractions.Configuration;
using LeanKernel.Abstractions.Interfaces;
using LeanKernel.Channels;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace LeanKernel.Tests.Unit.Channels;

public class SignalChannelTests
{
    [Fact]
    public async Task StartAsync_polls_the_daemon_and_raises_received_messages()
    {
        var receivedMessage = new TaskCompletionSource<ChannelMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new RecordingHttpMessageHandler((request, _) =>
        {
            if (request.Method == HttpMethod.Get)
            {
                return CreateJsonResponse(new[]
                {
                    new
                    {
                        envelope = new
                        {
                            sourceNumber = "+15550002",
                            timestamp = 1720000000000L,
                            dataMessage = new
                            {
                                message = "hello from signal"
                            }
                        },
                        account = "+15550001"
                    }
                });
            }

            return CreateJsonResponse(new { timestamp = 0 });
        });

        var channel = CreateChannel(handler, new ChannelsConfig
        {
            Signal = new SignalChannelConfig
            {
                Enabled = true,
                PhoneNumber = "+15550001",
                PollIntervalSeconds = 0,
                ReconnectDelaySeconds = 0,
                MaxReconnectAttempts = 3
            }
        });

        channel.MessageReceived += message =>
        {
            receivedMessage.TrySetResult(message);
            return Task.CompletedTask;
        };

        await channel.StartAsync();
        var message = await receivedMessage.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await channel.StopAsync();

        message.ChannelId.Should().Be("signal");
        message.SenderId.Should().Be("+15550002");
        message.Content.Should().Be("hello from signal");
        message.Timestamp.Should().Be(DateTimeOffset.FromUnixTimeMilliseconds(1720000000000L));
    }

    [Fact]
    public async Task SendAsync_posts_the_expected_payload_to_the_signal_daemon()
    {
        var handler = new RecordingHttpMessageHandler((_, _) => CreateJsonResponse(new { timestamp = 123L }));
        var channel = CreateChannel(handler, new ChannelsConfig
        {
            Signal = new SignalChannelConfig
            {
                Enabled = true,
                PhoneNumber = "+15550001"
            }
        });

        await channel.SendAsync("+15550002", "reply message");

        handler.RequestUris.Should().ContainSingle();
        handler.RequestUris[0].AbsolutePath.Should().Be("/v2/send");
        using var document = JsonDocument.Parse(handler.RequestBodies.Single());
        document.RootElement.GetProperty("number").GetString().Should().Be("+15550001");
        document.RootElement.GetProperty("recipients")[0].GetString().Should().Be("+15550002");
        document.RootElement.GetProperty("message").GetString().Should().Be("reply message");
    }

    [Fact]
    public async Task StartAsync_recovers_after_a_transient_poll_failure()
    {
        var requestCount = 0;
        var handler = new RecordingHttpMessageHandler((request, _) =>
        {
            if (request.Method != HttpMethod.Get)
            {
                return CreateJsonResponse(new { timestamp = 0L });
            }

            var attempt = Interlocked.Increment(ref requestCount);
            return attempt == 1
                ? throw new HttpRequestException("boom")
                : CreateJsonResponse(Array.Empty<object>());
        });

        var channel = CreateChannel(handler, new ChannelsConfig
        {
            Signal = new SignalChannelConfig
            {
                Enabled = true,
                PhoneNumber = "+15550001",
                PollIntervalSeconds = 0,
                ReconnectDelaySeconds = 0,
                MaxReconnectAttempts = 3
            }
        });

        await channel.StartAsync();
        await WaitForAsync(() => Volatile.Read(ref requestCount) >= 2 && channel.IsConnected, TimeSpan.FromSeconds(2));
        await channel.StopAsync();

        channel.IsConnected.Should().BeFalse();
        Volatile.Read(ref requestCount).Should().BeGreaterThanOrEqualTo(2);
    }

    private static SignalChannel CreateChannel(HttpMessageHandler handler, ChannelsConfig config)
        => new(
            new StubHttpClientFactory(new HttpClient(handler) { BaseAddress = new Uri(config.Signal.DaemonUrl) }),
            Options.Create(config),
            NullLogger<SignalChannel>.Instance);

    private static HttpResponseMessage CreateJsonResponse(object payload)
        => new(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(payload)
        };

    private static async Task WaitForAsync(Func<bool> condition, TimeSpan timeout)
    {
        var startedAt = DateTime.UtcNow;
        while (!condition())
        {
            if (DateTime.UtcNow - startedAt > timeout)
            {
                throw new TimeoutException("Condition was not met in time.");
            }

            await Task.Delay(25).ConfigureAwait(false);
        }
    }

    private sealed class StubHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class RecordingHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> _handler;

        public RecordingHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> handler)
        {
            _handler = handler;
        }

        public ConcurrentQueue<string> RecordedBodies { get; } = new();
        public ConcurrentQueue<Uri> RecordedUris { get; } = new();

        public List<string> RequestBodies => RecordedBodies.ToList();
        public List<Uri> RequestUris => RecordedUris.ToList();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri is not null)
            {
                RecordedUris.Enqueue(request.RequestUri);
            }

            RecordedBodies.Enqueue(request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync().ConfigureAwait(false));

            return _handler(request, cancellationToken);
        }
    }
}
