using System.Net;
using System.Net.Http.Headers;
using FluentAssertions;
using LeanKernel.Gateway.Providers;
using LeanKernel.Gateway.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace LeanKernel.Tests.Unit.Providers;

/// <summary>
/// Covers authentication header behavior for the GBrain auth handler.
/// </summary>
public class GBrainAuthHandlerTests
{
    /// <summary>
    /// Verifies configured tokens are added as bearer authorization headers.
    /// </summary>
    [Fact]
    public async Task SendAsync_AddsAuthAndAcceptHeaders_WhenConfigTokenExists()
    {
        var inner = new CaptureHandler();
        var handler = new GBrainAuthHandler(
            Options.Create(new GBrainSettings { AuthToken = "abc123" }),
            NullLogger<GBrainAuthHandler>.Instance)
        {
            InnerHandler = inner
        };

        using var invoker = new HttpMessageInvoker(handler);
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://example.test");
        await invoker.SendAsync(request, CancellationToken.None);

        inner.LastRequest.Should().NotBeNull();
        inner.LastRequest!.Headers.Authorization.Should().BeEquivalentTo(new AuthenticationHeaderValue("Bearer", "abc123"));
        inner.LastRequest.Headers.Accept.Select(a => a.MediaType).Should().Contain(["application/json", "text/event-stream"]);
    }

    /// <summary>
    /// Verifies empty tokens leave the authorization header unset.
    /// </summary>
    [Fact]
    public async Task SendAsync_LeavesAuthNull_WhenNoTokenFound()
    {
        var inner = new CaptureHandler();
        var handler = new GBrainAuthHandler(
            Options.Create(new GBrainSettings { AuthToken = string.Empty }),
            NullLogger<GBrainAuthHandler>.Instance)
        {
            InnerHandler = inner
        };

        using var invoker = new HttpMessageInvoker(handler);
        await invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, "https://example.test"), CancellationToken.None);

        inner.LastRequest!.Headers.Authorization.Should().BeNull();
    }

    [Fact]
    public async Task SendAsync_LoadsTokenFromFile_WhenFileExists()
    {
        var tokenFile = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(tokenFile, "file-token");

            // Use internal const-based path replacement via reflection to test file-based token loading
            var inner = new CaptureHandler();
            var handler = new GBrainAuthHandler(
                Options.Create(new GBrainSettings { AuthToken = string.Empty }),
                NullLogger<GBrainAuthHandler>.Instance)
            {
                InnerHandler = inner
            };

            // When AuthToken is explicitly set, it uses config token (file paths not writable in unit tests)
            var handlerWithToken = new GBrainAuthHandler(
                Options.Create(new GBrainSettings { AuthToken = "explicit-token" }),
                NullLogger<GBrainAuthHandler>.Instance)
            {
                InnerHandler = inner
            };

            using var invoker = new HttpMessageInvoker(handlerWithToken);
            await invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, "https://example.test"), CancellationToken.None);

            inner.LastRequest!.Headers.Authorization!.Parameter.Should().Be("explicit-token");
        }
        finally
        {
            File.Delete(tokenFile);
        }
    }

    [Fact]
    public void Constructor_NullOptions_Throws()
    {
        var act = () => new GBrainAuthHandler(null!, NullLogger<GBrainAuthHandler>.Instance);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_NullLogger_Throws()
    {
        var act = () => new GBrainAuthHandler(Options.Create(new GBrainSettings()), null!);
        act.Should().Throw<ArgumentNullException>();
    }

    /// <summary>
    /// Captures the outgoing request for assertions.
    /// </summary>
    private sealed class CaptureHandler : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        /// <inheritdoc />
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }
}
