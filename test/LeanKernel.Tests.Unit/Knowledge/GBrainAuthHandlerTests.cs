using System.Net;
using System.Net.Http.Headers;
using FluentAssertions;
using LeanKernel.Abstractions.Configuration;
using LeanKernel.Knowledge;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace LeanKernel.Tests.Unit.Knowledge;

public class GBrainAuthHandlerTests
{
    [Fact]
    public void Constructor_throws_when_config_is_null()
    {
        var act = () => new GBrainAuthHandler(null!, NullLogger<GBrainAuthHandler>.Instance);
        act.Should().Throw<ArgumentNullException>().WithParameterName("config");
    }

    [Fact]
    public void Constructor_throws_when_logger_is_null()
    {
        var act = () => new GBrainAuthHandler(Options.Create(new GBrainConfig()), null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    [Fact]
    public async Task SendAsync_sets_Bearer_header_from_config_token()
    {
        var capture = new CaptureHandler();
        var handler = new GBrainAuthHandler(
            Options.Create(new GBrainConfig { AuthToken = "test-token" }),
            NullLogger<GBrainAuthHandler>.Instance)
        {
            InnerHandler = capture
        };

        var client = new HttpClient(handler);
        await client.GetAsync("http://localhost/");

        capture.LastRequest!.Headers.Authorization.Should().NotBeNull();
        capture.LastRequest!.Headers.Authorization!.Scheme.Should().Be("Bearer");
        capture.LastRequest!.Headers.Authorization!.Parameter.Should().Be("test-token");
    }

    [Fact]
    public async Task SendAsync_sets_no_Authorization_when_no_token()
    {
        var capture = new CaptureHandler();
        var handler = new GBrainAuthHandler(
            Options.Create(new GBrainConfig()),
            NullLogger<GBrainAuthHandler>.Instance)
        {
            InnerHandler = capture
        };

        var client = new HttpClient(handler);
        await client.GetAsync("http://localhost/");

        capture.LastRequest!.Headers.Authorization.Should().BeNull();
    }

    [Fact]
    public async Task SendAsync_sets_no_Authorization_when_token_is_empty_string()
    {
        var capture = new CaptureHandler();
        var handler = new GBrainAuthHandler(
            Options.Create(new GBrainConfig { AuthToken = "" }),
            NullLogger<GBrainAuthHandler>.Instance)
        {
            InnerHandler = capture
        };

        var client = new HttpClient(handler);
        await client.GetAsync("http://localhost/");

        capture.LastRequest!.Headers.Authorization.Should().BeNull();
    }

    [Fact]
    public async Task SendAsync_sets_no_Authorization_when_token_is_whitespace()
    {
        var capture = new CaptureHandler();
        var handler = new GBrainAuthHandler(
            Options.Create(new GBrainConfig { AuthToken = "   " }),
            NullLogger<GBrainAuthHandler>.Instance)
        {
            InnerHandler = capture
        };

        var client = new HttpClient(handler);
        await client.GetAsync("http://localhost/");

        capture.LastRequest!.Headers.Authorization.Should().BeNull();
    }

    [Fact]
    public async Task SendAsync_always_sets_Accept_headers()
    {
        var capture = new CaptureHandler();
        var handler = new GBrainAuthHandler(
            Options.Create(new GBrainConfig()),
            NullLogger<GBrainAuthHandler>.Instance)
        {
            InnerHandler = capture
        };

        var client = new HttpClient(handler);
        await client.GetAsync("http://localhost/");

        capture.LastRequest!.Headers.Accept.Should().HaveCount(2);
        capture.LastRequest!.Headers.Accept.Should().Contain(x => x.MediaType == "application/json");
        capture.LastRequest!.Headers.Accept.Should().Contain(x => x.MediaType == "text/event-stream");
    }

    [Fact]
    public async Task SendAsync_clears_existing_Accept_headers()
    {
        var capture = new CaptureHandler();
        var handler = new GBrainAuthHandler(
            Options.Create(new GBrainConfig()),
            NullLogger<GBrainAuthHandler>.Instance)
        {
            InnerHandler = capture
        };

        var client = new HttpClient(handler);
        var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/xml"));

        await client.SendAsync(request);

        capture.LastRequest!.Headers.Accept.Should().HaveCount(2);
        capture.LastRequest!.Headers.Accept.Should().NotContain(x => x.MediaType == "application/xml");
        capture.LastRequest!.Headers.Accept.Should().Contain(x => x.MediaType == "application/json");
        capture.LastRequest!.Headers.Accept.Should().Contain(x => x.MediaType == "text/event-stream");
    }

    [Fact]
    public async Task SendAsync_returns_response_from_inner_handler()
    {
        var capture = new CaptureHandler
        {
            Response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"result\":\"ok\"}")
            }
        };
        var handler = new GBrainAuthHandler(
            Options.Create(new GBrainConfig()),
            NullLogger<GBrainAuthHandler>.Instance)
        {
            InnerHandler = capture
        };

        var client = new HttpClient(handler);
        var response = await client.GetAsync("http://localhost/");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Be("{\"result\":\"ok\"}");
    }

    [Fact]
    public async Task SendAsync_resolves_token_from_config_when_token_files_do_not_exist()
    {
        var capture = new CaptureHandler();
        var handler = new GBrainAuthHandler(
            Options.Create(new GBrainConfig { AuthToken = "config-token" }),
            NullLogger<GBrainAuthHandler>.Instance)
        {
            InnerHandler = capture
        };

        var client = new HttpClient(handler);
        await client.GetAsync("http://localhost/");

        capture.LastRequest!.Headers.Authorization!.Parameter.Should().Be("config-token");
    }

    [Fact]
    public async Task SendAsync_forwards_cancellation_token()
    {
        var capture = new CaptureHandler();
        var handler = new GBrainAuthHandler(
            Options.Create(new GBrainConfig()),
            NullLogger<GBrainAuthHandler>.Instance)
        {
            InnerHandler = capture
        };

        using var cts = new CancellationTokenSource();
        var client = new HttpClient(handler);
        var response = await client.GetAsync("http://localhost/", cts.Token);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private sealed class CaptureHandler : DelegatingHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }
        public HttpResponseMessage Response { get; set; } = new(HttpStatusCode.OK);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(Response);
        }
    }
}
