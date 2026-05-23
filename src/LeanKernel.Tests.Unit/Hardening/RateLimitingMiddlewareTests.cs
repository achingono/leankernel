using System.Net;
using FluentAssertions;
using LeanKernel.Abstractions.Configuration;
using LeanKernel.Diagnostics;
using LeanKernel.Gateway.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace LeanKernel.Tests.Unit.Hardening;

public class RateLimitingMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_enforces_sliding_minute_window_and_allows_requests_after_window_expires()
    {
        // Arrange
        var timeProvider = new AdjustableTimeProvider(DateTimeOffset.Parse("2025-05-20T10:00:00Z"));
        using var metrics = new LeanKernelMetrics();
        var middleware = new RateLimitingMiddleware(
            _ => Task.CompletedTask,
            Options.Create(new HardeningConfig
            {
                RateLimit = new RateLimitConfig
                {
                    Enabled = true,
                    RequestsPerMinute = 1,
                    RequestsPerHour = 10,
                    ConcurrentRequests = 1,
                }
            }),
            metrics,
            NullLogger<RateLimitingMiddleware>.Instance,
            timeProvider);
        var firstContext = CreateHttpContext();
        var secondContext = CreateHttpContext();
        var thirdContext = CreateHttpContext();

        // Act
        await middleware.InvokeAsync(firstContext);
        await middleware.InvokeAsync(secondContext);
        timeProvider.Advance(TimeSpan.FromSeconds(61));
        await middleware.InvokeAsync(thirdContext);

        // Assert
        firstContext.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
        secondContext.Response.StatusCode.Should().Be(StatusCodes.Status429TooManyRequests);
        thirdContext.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
    }

    [Fact]
    public async Task InvokeAsync_enforces_hourly_limit()
    {
        // Arrange
        using var metrics = new LeanKernelMetrics();
        var middleware = new RateLimitingMiddleware(
            _ => Task.CompletedTask,
            Options.Create(new HardeningConfig
            {
                RateLimit = new RateLimitConfig
                {
                    Enabled = true,
                    RequestsPerMinute = 10,
                    RequestsPerHour = 2,
                    ConcurrentRequests = 1,
                }
            }),
            metrics,
            NullLogger<RateLimitingMiddleware>.Instance,
            new AdjustableTimeProvider(DateTimeOffset.Parse("2025-05-20T10:00:00Z")));

        // Act
        var first = CreateHttpContext();
        var second = CreateHttpContext();
        var third = CreateHttpContext();
        await middleware.InvokeAsync(first);
        await middleware.InvokeAsync(second);
        await middleware.InvokeAsync(third);

        // Assert
        third.Response.StatusCode.Should().Be(StatusCodes.Status429TooManyRequests);
    }

    [Fact]
    public async Task InvokeAsync_enforces_concurrent_request_limit()
    {
        // Arrange
        var releaseRequest = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var requestEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var metrics = new LeanKernelMetrics();
        var middleware = new RateLimitingMiddleware(
            async _ =>
            {
                requestEntered.TrySetResult();
                await releaseRequest.Task;
            },
            Options.Create(new HardeningConfig
            {
                RateLimit = new RateLimitConfig
                {
                    Enabled = true,
                    RequestsPerMinute = 10,
                    RequestsPerHour = 10,
                    ConcurrentRequests = 1,
                }
            }),
            metrics,
            NullLogger<RateLimitingMiddleware>.Instance,
            new AdjustableTimeProvider(DateTimeOffset.Parse("2025-05-20T10:00:00Z")));
        var firstContext = CreateHttpContext();
        var secondContext = CreateHttpContext();

        // Act
        var firstRequest = middleware.InvokeAsync(firstContext);
        await requestEntered.Task.WaitAsync(TimeSpan.FromSeconds(1));
        await middleware.InvokeAsync(secondContext);
        releaseRequest.SetResult();
        await firstRequest;

        // Assert
        secondContext.Response.StatusCode.Should().Be(StatusCodes.Status429TooManyRequests);
    }

    private static DefaultHttpContext CreateHttpContext()
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/chat";
        context.Connection.RemoteIpAddress = IPAddress.Parse("127.0.0.1");
        context.Response.Body = new MemoryStream();
        return context;
    }

    private sealed class AdjustableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan delta) => _utcNow = _utcNow.Add(delta);
    }
}
