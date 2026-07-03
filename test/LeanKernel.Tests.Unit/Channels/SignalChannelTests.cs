using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using LeanKernel.Abstractions.Configuration;
using LeanKernel.Abstractions.Interfaces;
using LeanKernel.Abstractions.Models;
using LeanKernel.Channels;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace LeanKernel.Tests.Unit.Channels;

public class SignalChannelTests
{
    [Fact]
    public async Task StartAsync_polls_the_daemon_and_raises_received_messages()
    {
        await using var server = await TestWebSocketServer.CreateAsync(port => port);

        var receivedMessage = new TaskCompletionSource<ChannelMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        server.OnConnectAsync = async (webSocket, _reqPath) =>
        {
            var payload = new
            {
                envelope = new
                {
                    sourceNumber = "+15550002",
                    timestamp = 1720000000000L,
                    dataMessage = new { message = "hello from signal" }
                },
                account = "+15550001"
            };

            var json = JsonSerializer.Serialize(payload);
            var bytes = System.Text.Encoding.UTF8.GetBytes(json);
            await webSocket.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, cancellationToken: CancellationToken.None);
            await Task.Delay(50);
            await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None);
        };

        var channel = CreateChannel(new RecordingHttpMessageHandler((_, _) => CreateJsonResponse(new { timestamp = 0L })), new ChannelsConfig
        {
            Signal = new SignalChannelConfig
            {
                Enabled = true,
                PhoneNumber = "+15550001",
                PollIntervalSeconds = 0,
                ReconnectDelaySeconds = 0,
                MaxReconnectAttempts = 3,
                DaemonUrl = server.DaemonUrl
            }
        });

        channel.MessageReceived += message =>
        {
            receivedMessage.TrySetResult(message);
            return Task.CompletedTask;
        };

        await channel.StartAsync();
        ChannelMessage message;
        try
        {
            message = await receivedMessage.Task.WaitAsync(TimeSpan.FromSeconds(30));
        }
        finally
        {
            await channel.StopAsync();
        }

        message.ChannelId.Should().Be("signal");
        message.SenderId.Should().Be("+15550002");
        message.Content.Should().Be("hello from signal");
        message.Timestamp.Should().Be(DateTimeOffset.FromUnixTimeMilliseconds(1720000000000L));
    }

    [Fact]
    public async Task StartAsync_maps_attachment_only_messages_and_downloads_attachment_data()
    {
        await using var server = await TestWebSocketServer.CreateAsync(port => port);

        var attachmentBytes = Encoding.UTF8.GetBytes("image-bytes");
        var receivedMessage = new TaskCompletionSource<ChannelMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        server.OnConnectAsync = async (webSocket, _reqPath) =>
        {
            var payload = new
            {
                envelope = new
                {
                    sourceNumber = "+15550002",
                    timestamp = 1720000000001L,
                    dataMessage = new
                    {
                        attachments = new[]
                        {
                            new
                            {
                                id = "attachment-1",
                                filename = "invoice.pdf",
                                contentType = "application/pdf"
                            }
                        }
                    }
                },
                account = "+15550001"
            };

            var json = JsonSerializer.Serialize(payload);
            var bytes = Encoding.UTF8.GetBytes(json);
            await webSocket.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, cancellationToken: CancellationToken.None);
            await Task.Delay(50);
            await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None);
        };

        var handler = new RecordingHttpMessageHandler((request, _) =>
        {
            if (request.RequestUri?.AbsolutePath == "/v1/attachments/attachment-1")
            {
                var response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(attachmentBytes)
                };
                response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/pdf");
                return response;
            }

            return CreateJsonResponse(new { timestamp = 0L });
        });

        var channel = CreateChannel(handler, new ChannelsConfig
        {
            Signal = new SignalChannelConfig
            {
                Enabled = true,
                PhoneNumber = "+15550001",
                PollIntervalSeconds = 0,
                ReconnectDelaySeconds = 0,
                MaxReconnectAttempts = 3,
                DaemonUrl = server.DaemonUrl
            }
        });

        channel.MessageReceived += message =>
        {
            receivedMessage.TrySetResult(message);
            return Task.CompletedTask;
        };

        await channel.StartAsync();
        ChannelMessage message;
        try
        {
            message = await receivedMessage.Task.WaitAsync(TimeSpan.FromSeconds(30));
        }
        finally
        {
            await channel.StopAsync();
        }

        message.Content.Should().Be("[Received attachment: invoice.pdf]");
        message.Attachments.Should().NotBeNull();
        message.Attachments!.Should().ContainSingle();
        message.Attachments[0].FileName.Should().Be("invoice.pdf");
        message.Attachments[0].ContentType.Should().Be("application/pdf");
        message.Attachments[0].Data.Should().Equal(attachmentBytes);
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
    public async Task SendAsync_includes_base64_attachments_when_provided()
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

        await channel.SendAsync(
            "+15550002",
            "reply with file",
            [
                new Attachment
                {
                    FileName = "report.pdf",
                    ContentType = "application/pdf",
                    Data = Encoding.UTF8.GetBytes("file-bytes")
                }
            ]);

        handler.RequestUris.Should().ContainSingle();
        handler.RequestUris[0].AbsolutePath.Should().Be("/v2/send");
        using var document = JsonDocument.Parse(handler.RequestBodies.Single());
        document.RootElement.GetProperty("base64_attachments").GetArrayLength().Should().Be(1);
        var encodedAttachment = document.RootElement.GetProperty("base64_attachments")[0].GetString();
        encodedAttachment.Should().NotBeNull();
        encodedAttachment!.Should().StartWith("data:application/pdf;filename=report.pdf;base64,");
    }

    [Fact]
    public async Task SendAsync_throws_when_the_signal_daemon_returns_an_error_response()
    {
        var handler = new RecordingHttpMessageHandler((_, _) => new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("failure")
        });
        var channel = CreateChannel(handler, new ChannelsConfig
        {
            Signal = new SignalChannelConfig
            {
                Enabled = true,
                PhoneNumber = "+15550001"
            }
        });

        var act = () => channel.SendAsync("+15550002", "reply message");

        await act.Should().ThrowAsync<HttpRequestException>();
        handler.RequestUris.Should().ContainSingle();
        handler.RequestUris[0].AbsolutePath.Should().Be("/v2/send");
    }

    [Fact]
    public async Task StartTypingAsync_and_StopTypingAsync_use_the_signal_typing_indicator_endpoint()
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

        await channel.StartTypingAsync("+15550002");
        await channel.StopTypingAsync("+15550002");

        handler.RequestUris.Should().HaveCount(2);
        handler.RequestMethods[0].Should().Be(HttpMethod.Put);
        handler.RequestMethods[1].Should().Be(HttpMethod.Delete);
        handler.RequestUris[0].AbsolutePath.Should().Be("/v1/typing-indicator/%2B15550001");
        handler.RequestUris[1].AbsolutePath.Should().Be("/v1/typing-indicator/%2B15550001");

        using var firstBody = JsonDocument.Parse(handler.RequestBodies[0]);
        using var secondBody = JsonDocument.Parse(handler.RequestBodies[1]);
        firstBody.RootElement.GetProperty("recipient").GetString().Should().Be("+15550002");
        secondBody.RootElement.GetProperty("recipient").GetString().Should().Be("+15550002");
    }

    [Fact]
    public async Task SendAsync_splits_oversized_messages_before_posting_to_signal()
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

        var oversizedMessage = new string('x', 5742);

        await channel.SendAsync("+15550002", oversizedMessage);

        handler.RequestUris.Should().HaveCount(2);
        handler.RequestBodies.Should().HaveCount(2);

        var firstChunk = JsonDocument.Parse(handler.RequestBodies[0]).RootElement.GetProperty("message").GetString();
        var secondChunk = JsonDocument.Parse(handler.RequestBodies[1]).RootElement.GetProperty("message").GetString();

        firstChunk.Should().HaveLength(3500);
        secondChunk.Should().HaveLength(2242);
        firstChunk.Should().Be(new string('x', 3500));
        secondChunk.Should().Be(new string('x', 2242));
    }

    [Fact]
    public async Task StartAsync_recovers_after_a_transient_poll_failure()
    {
        var requestCount = 0;

        await using var server = await TestWebSocketServer.CreateAsync(port => port);
        server.OnConnectAsync = async (webSocket, _reqPath) =>
        {
            var attempt = Interlocked.Increment(ref requestCount);
            if (attempt == 1)
            {
                await webSocket.CloseAsync(WebSocketCloseStatus.InternalServerError, "transient", CancellationToken.None);
                return;
            }

            // Second connection: keep it alive briefly to allow the client loop to consider itself connected.
            await Task.Delay(100);
            await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "ok", CancellationToken.None);
        };

        var channel = CreateChannel(new RecordingHttpMessageHandler((_, _) => CreateJsonResponse(new { timestamp = 0L })), new ChannelsConfig
        {
            Signal = new SignalChannelConfig
            {
                Enabled = true,
                PhoneNumber = "+15550001",
                PollIntervalSeconds = 0,
                ReconnectDelaySeconds = 0,
                MaxReconnectAttempts = 3,
                DaemonUrl = server.DaemonUrl
            }
        });

        await channel.StartAsync();
        await WaitForAsync(() => Volatile.Read(ref requestCount) >= 2 && channel.IsConnected, TimeSpan.FromSeconds(2));
        await channel.StopAsync();

        channel.IsConnected.Should().BeFalse();
        Volatile.Read(ref requestCount).Should().BeGreaterThanOrEqualTo(2);
    }

    private static SignalChannel CreateChannel(HttpMessageHandler handler, ChannelsConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.Signal.DaemonUrl))
        {
            config.Signal.DaemonUrl = "http://localhost:8080";
        }

        return new SignalChannel(
            new StubHttpClientFactory(new HttpClient(handler) { BaseAddress = new Uri(config.Signal.DaemonUrl) }),
            Options.Create(config),
            NullLogger<SignalChannel>.Instance);
    }

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
        public ConcurrentQueue<HttpMethod> RecordedMethods { get; } = new();

        public List<string> RequestBodies => RecordedBodies.ToList();
        public List<Uri> RequestUris => RecordedUris.ToList();
        public List<HttpMethod> RequestMethods => RecordedMethods.ToList();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri is not null)
            {
                RecordedUris.Enqueue(request.RequestUri);
            }

            RecordedMethods.Enqueue(request.Method);

            RecordedBodies.Enqueue(request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync().ConfigureAwait(false));

            return _handler(request, cancellationToken);
        }
    }

    private sealed class TestWebSocketServer : IAsyncDisposable
    {
        private readonly System.Net.HttpListener _listener;
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _acceptLoop;

        public string DaemonUrl { get; }

        public Func<WebSocket, string, Task> OnConnectAsync { get; set; } = (_, _) => Task.CompletedTask;

        private TestWebSocketServer(System.Net.HttpListener listener, string daemonUrl)
        {
            _listener = listener;
            DaemonUrl = daemonUrl;
            _acceptLoop = Task.Run(AcceptLoopAsync);
        }

        public static Task<TestWebSocketServer> CreateAsync(Func<int, int> portSelector)
        {
            // Simple approach: pick an ephemeral port by binding to port 0.
            var listener = new System.Net.HttpListener();
            listener.Prefixes.Add("http://127.0.0.1:0/");
            // HttpListener doesn't expose the chosen port until start; use TcpListener to allocate.
            return Task.FromResult(new TestWebSocketServer(CreateBoundListener(out var daemonUrl), daemonUrl));
        }

        private static System.Net.HttpListener CreateBoundListener(out string daemonUrl)
        {
            var tcp = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
            tcp.Start();
            var port = ((IPEndPoint)tcp.LocalEndpoint).Port;
            tcp.Stop();

            daemonUrl = $"http://127.0.0.1:{port}";
            var listener = new System.Net.HttpListener();
            listener.Prefixes.Add($"{daemonUrl}/");
            listener.Start();
            return listener;
        }

        private async Task AcceptLoopAsync()
        {
            while (!_cts.IsCancellationRequested)
            {
                HttpListenerContext context;
                try
                {
                    context = await _listener.GetContextAsync().ConfigureAwait(false);
                }
                catch
                {
                    if (_cts.IsCancellationRequested) return;
                    continue;
                }

                if (!context.Request.IsWebSocketRequest)
                {
                    context.Response.StatusCode = 400;
                    context.Response.Close();
                    continue;
                }

                _ = Task.Run(async () =>
                {
                    WebSocket? ws = null;
                    try
                    {
                        var wsContext = await context.AcceptWebSocketAsync(subProtocol: null).ConfigureAwait(false);
                        ws = wsContext.WebSocket;
                        await OnConnectAsync(ws, context.Request.Url?.AbsolutePath ?? string.Empty).ConfigureAwait(false);
                    }
                    catch
                    {
                        // Ignore; tests rely on client-side reconnect behavior.
                    }
                    finally
                    {
                        try { ws?.Dispose(); } catch { }
                        try { context.Response.Close(); } catch { }
                    }
                });
            }
        }

        public async ValueTask DisposeAsync()
        {
            _cts.Cancel();
            try { _listener.Stop(); } catch { }
            try { await _acceptLoop.ConfigureAwait(false); } catch { }
            _cts.Dispose();
        }
    }
}
