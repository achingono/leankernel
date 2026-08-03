using System.Collections.Concurrent;
using System.Net;
using System.Text;

using FluentAssertions;

using LeanKernel.Channels.Common.Configuration;
using LeanKernel.Channels.Signal;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Xunit;

namespace LeanKernel.Tests.Unit.Signal;

public sealed class SignalChannelTransportTests
{
    [Fact]
    public async Task TerminalService_Continues_WhenFirstSendFails()
    {
        var inboundMessages = new Queue<InboundMessage>([
            new InboundMessage("+10000000001", "+15550000001", "first", "token-a", []),
            new InboundMessage("+10000000002", "+15550000002", "second", "token-b", [])
        ]);

        var transport = new FakeTransport(inboundMessages, failFirstSend: true);
        var gatewayClient = CreateGatewayClient(_ =>
            JsonResponse("""
                {
                  "output": [
                    {
                      "content": [
                        { "type": "output_text", "text": "ok" }
                      ]
                    }
                  ]
                }
                """));

        var service = new TerminalService(
            NullLogger<TerminalService>.Instance,
            transport,
            gatewayClient,
            Options.Create(new SignalSettings()));

        using var stopCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await service.StartAsync(stopCts.Token);

        await transport.WaitForSendCountAsync(expectedCount: 2, timeout: TimeSpan.FromSeconds(3));

        await service.StopAsync(CancellationToken.None);

        transport.SendAttempts.Should().Be(2);
    }

    [Fact]
    public async Task SocketTransportClient_SendAsync_ReturnsFalse_WhenApiReturnsFailure()
    {
        var httpClient = CreateHttpClient(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        httpClient.BaseAddress = new Uri("http://localhost:8080");

        var client = new SocketTransportClient(
            new StubHttpClientFactory(httpClient),
            Options.Create(new SignalSettings()),
            new StubCredentialProvider(),
            new StubReceiveClient(),
            NullLogger<SocketTransportClient>.Instance);

        var sent = await client.SendAsync("+10000000001", "+15550000001", "hello", [], CancellationToken.None);

        sent.Should().BeFalse();
    }

    [Fact]
    public async Task SocketTransportClient_ReceivesFromSecondAccount_WhenFirstAccountStalls()
    {
        var signalClient = new ScriptedReceiveClient(async (account, ct) =>
        {
            if (account == "+10000000001")
            {
                await Task.Delay(TimeSpan.FromSeconds(3), ct);
                return null;
            }

            return """
                {
                  "envelope": {
                    "sourceNumber": "+15550000002",
                    "dataMessage": {
                      "message": "hello from second"
                    }
                  }
                }
                """;
        });

        var transport = CreateSocketTransportClient(
            signalClient,
            _ => JsonResponse("[\"+10000000001\",\"+10000000002\"]"),
            settings: new SignalSettings
            {
                AccountRefreshSeconds = 1,
                ReconnectDelaySeconds = 1,
                ReceiveClientDeadlineSeconds = 5,
                InboundQueueCapacity = 100
            });

        await transport.StartAsync(CancellationToken.None);
        using var receiveCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var inbound = await transport.ReceiveAsync(receiveCts.Token);
        await transport.StopAsync(CancellationToken.None);

        inbound.Should().NotBeNull();
        inbound!.Account.Should().Be("+10000000002");
        inbound.Sender.Should().Be("+15550000002");
    }

    [Fact]
    public async Task SocketTransportClient_DropsNewest_WhenInboundQueueIsFull()
    {
        var emitted = 0;
        var signalClient = new ScriptedReceiveClient(async (_, ct) =>
        {
            if (Interlocked.Increment(ref emitted) == 1)
            {
                return """
                    [
                      {
                        "envelope": {
                          "sourceNumber": "+15550000001",
                          "dataMessage": {
                            "message": "first"
                          }
                        }
                      },
                      {
                        "envelope": {
                          "sourceNumber": "+15550000002",
                          "dataMessage": {
                            "message": "second"
                          }
                        }
                      }
                    ]
                    """;
            }

            await Task.Delay(TimeSpan.FromSeconds(1), ct);
            return null;
        });

        var transport = CreateSocketTransportClient(
            signalClient,
            _ => JsonResponse("[\"+10000000001\"]"),
            settings: new SignalSettings
            {
                InboundQueueCapacity = 1,
                AccountRefreshSeconds = 30,
                ReceiveClientDeadlineSeconds = 2
            });

        await transport.StartAsync(CancellationToken.None);

        using var firstReceiveCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var firstInbound = await transport.ReceiveAsync(firstReceiveCts.Token);

        using var secondReceiveCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(400));
        var secondRead = async () => await transport.ReceiveAsync(secondReceiveCts.Token);

        await secondRead.Should().ThrowAsync<OperationCanceledException>();
        await transport.StopAsync(CancellationToken.None);

        firstInbound.Should().NotBeNull();
        firstInbound!.Sender.Should().Be("+15550000001");
    }

    [Fact]
    public async Task SocketTransportClient_RecyclesReceiveLoop_WhenClientDeadlineExpires()
    {
        var signalClient = new ScriptedReceiveClient(async (account, ct) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(5), ct);
            return null;
        });

        var transport = CreateSocketTransportClient(
            signalClient,
            _ => JsonResponse("[\"+10000000001\"]"),
            settings: new SignalSettings
            {
                ReceiveClientDeadlineSeconds = 1,
                AccountRefreshSeconds = 30,
                ReconnectDelaySeconds = 1
            });

        await transport.StartAsync(CancellationToken.None);
        await Task.Delay(TimeSpan.FromMilliseconds(2600));
        await transport.StopAsync(CancellationToken.None);

        signalClient.GetCallCount("+10000000001").Should().BeGreaterThanOrEqualTo(2);
    }

    private static GatewayChannelClient CreateGatewayClient(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        var httpClient = CreateHttpClient(responder);
        httpClient.BaseAddress = new Uri("http://localhost:5088");

        return new GatewayChannelClient(
            httpClient,
            Options.Create(new GatewaySettings { BaseUrl = "http://localhost:5088", Model = "test-model" }),
            NullLogger<GatewayChannelClient>.Instance);
    }

    private static HttpClient CreateHttpClient(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        return new HttpClient(new StubHttpMessageHandler(responder));
    }

    private static SocketTransportClient CreateSocketTransportClient(
        ISignalReceiveClient receiveClient,
        Func<HttpRequestMessage, HttpResponseMessage> signalApiResponder,
        SignalSettings settings)
    {
        var httpClient = CreateHttpClient(signalApiResponder);
        httpClient.BaseAddress = new Uri("http://localhost:8080");

        return new SocketTransportClient(
            new StubHttpClientFactory(httpClient),
            Options.Create(settings),
            new StubCredentialProvider(),
            receiveClient,
            NullLogger<SocketTransportClient>.Instance);
    }

    private static HttpResponseMessage JsonResponse(string body)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
    }

    private sealed class FakeTransport : ITransportClient
    {
        private readonly Queue<InboundMessage> _inbound;
        private readonly TaskCompletionSource<bool> _sendTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly bool _failFirstSend;
        private int _sendAttempts;

        public FakeTransport(Queue<InboundMessage> inbound, bool failFirstSend)
        {
            _inbound = inbound;
            _failFirstSend = failFirstSend;
        }

        public int SendAttempts => Volatile.Read(ref _sendAttempts);

        public async Task<InboundMessage?> ReceiveAsync(CancellationToken ct)
        {
            if (_inbound.Count > 0)
            {
                return _inbound.Dequeue();
            }

            await Task.Delay(TimeSpan.FromMilliseconds(25), ct);
            return null;
        }

        public Task<bool> SendAsync(string account, string recipient, string text, IReadOnlyList<SignalTextStyle> textStyles, CancellationToken ct)
        {
            var attempt = Interlocked.Increment(ref _sendAttempts);
            if (attempt >= 2)
            {
                _sendTcs.TrySetResult(true);
            }

            var success = !_failFirstSend || attempt > 1;
            return Task.FromResult(success);
        }

        public Task StartTypingAsync(string account, string recipient, CancellationToken ct) => Task.CompletedTask;

        public Task StopTypingAsync(string account, string recipient, CancellationToken ct) => Task.CompletedTask;

        public async Task WaitForSendCountAsync(int expectedCount, TimeSpan timeout)
        {
            if (SendAttempts >= expectedCount)
            {
                return;
            }

            using var timeoutCts = new CancellationTokenSource(timeout);
            await _sendTcs.Task.WaitAsync(timeoutCts.Token);
        }
    }

    private sealed class StubHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class StubCredentialProvider : IChannelCredentialProvider
    {
        public Task<string> ResolveBearerTokenAsync(string sender, CancellationToken ct) => Task.FromResult("token");
    }

    private sealed class StubReceiveClient : ISignalReceiveClient
    {
        public Task<string?> ReceiveAsync(string account, Uri receiveUri, CancellationToken ct) => Task.FromResult<string?>(null);
    }

    private sealed class ScriptedReceiveClient(Func<string, CancellationToken, Task<string?>> handler) : ISignalReceiveClient
    {
        private readonly ConcurrentDictionary<string, int> _callCounts = new(StringComparer.OrdinalIgnoreCase);

        public async Task<string?> ReceiveAsync(string account, Uri receiveUri, CancellationToken ct)
        {
            _callCounts.AddOrUpdate(account, 1, static (_, current) => current + 1);
            return await handler(account, ct);
        }

        public int GetCallCount(string account) => _callCounts.TryGetValue(account, out var count) ? count : 0;
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(responder(request));
        }
    }
}