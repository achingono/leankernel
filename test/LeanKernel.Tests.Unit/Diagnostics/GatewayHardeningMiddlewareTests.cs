using FluentAssertions;

using LeanKernel.Services.Gateway.Configuration;
using LeanKernel.Services.Gateway.Providers;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Moq;

using Xunit;

namespace LeanKernel.Tests.Unit.Diagnostics;

public class GatewayHardeningMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_AddsCorrelationId()
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Path = "/v1/responses";
        ctx.Response.Body = new MemoryStream();
        var nextInvoked = false;
        var middleware = new HardeningMiddleware(_ =>
        {
            nextInvoked = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(
            ctx,
            Options.Create(new HardeningSettings()),
            Mock.Of<ILogger<HardeningMiddleware>>());

        nextInvoked.Should().BeTrue();
        ctx.Response.Headers["X-Correlation-ID"].ToString().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task InvokeAsync_ProtectsDiagnosticsWithoutApiKey()
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Path = "/v1/diagnostics/entries";
        ctx.Response.Body = new MemoryStream();
        var middleware = new HardeningMiddleware(_ => Task.CompletedTask);

        await middleware.InvokeAsync(
            ctx,
            Options.Create(new HardeningSettings { RequireApiKey = true, ApiKey = "secret" }),
            Mock.Of<ILogger<HardeningMiddleware>>());

        ctx.Response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
    }

    [Fact]
    public async Task InvokeAsync_WithValidApiKey_PassesThrough()
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Path = "/v1/diagnostics/entries";
        ctx.Request.Headers["X-Api-Key"] = "valid-key";
        ctx.Response.Body = new MemoryStream();
        var nextCalled = false;
        var middleware = new HardeningMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(
            ctx,
            Options.Create(new HardeningSettings
            {
                RequireApiKey = true,
                ApiKey = "valid-key",
            }),
            Mock.Of<ILogger<HardeningMiddleware>>());

        nextCalled.Should().BeTrue();
        ctx.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
    }

    [Fact]
    public async Task InvokeAsync_NonProtectedPath_AlwaysPassesThrough()
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Path = "/v1/responses";
        ctx.Response.Body = new MemoryStream();
        var nextCalled = false;
        var middleware = new HardeningMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(
            ctx,
            Options.Create(new HardeningSettings { RequireApiKey = true, ApiKey = "secret" }),
            Mock.Of<ILogger<HardeningMiddleware>>());

        nextCalled.Should().BeTrue();
        ctx.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
    }

    [Fact]
    public async Task InvokeAsync_PreservesIncomingCorrelationId()
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Path = "/v1/responses";
        ctx.Request.Headers["X-Correlation-ID"] = "incoming-id";
        ctx.Response.Body = new MemoryStream();
        var middleware = new HardeningMiddleware(_ => Task.CompletedTask);

        await middleware.InvokeAsync(
            ctx,
            Options.Create(new HardeningSettings()),
            Mock.Of<ILogger<HardeningMiddleware>>());

        ctx.Response.Headers["X-Correlation-ID"].ToString().Should().Be("incoming-id");
    }

    [Fact]
    public async Task InvokeAsync_WhenApiKeyMissing_StillSetsCorrelationId()
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Path = "/v1/diagnostics/entries";
        ctx.Response.Body = new MemoryStream();
        var middleware = new HardeningMiddleware(_ => Task.CompletedTask);

        await middleware.InvokeAsync(
            ctx,
            Options.Create(new HardeningSettings { RequireApiKey = true, ApiKey = "secret" }),
            Mock.Of<ILogger<HardeningMiddleware>>());

        ctx.Response.Headers["X-Correlation-ID"].ToString().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task InvokeAsync_WhenApiKeyEmpty_RejectsRequest()
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Path = "/v1/diagnostics/entries";
        ctx.Response.Body = new MemoryStream();
        var nextCalled = false;
        var middleware = new HardeningMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(
            ctx,
            Options.Create(new HardeningSettings { RequireApiKey = true, ApiKey = string.Empty }),
            Mock.Of<ILogger<HardeningMiddleware>>());

        ctx.Response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
        nextCalled.Should().BeFalse();
    }

    [Fact]
    public async Task InvokeAsync_WhenApiKeyWhitespace_RejectsRequest()
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Path = "/v1/diagnostics/entries";
        ctx.Response.Body = new MemoryStream();
        var nextCalled = false;
        var middleware = new HardeningMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(
            ctx,
            Options.Create(new HardeningSettings { RequireApiKey = true, ApiKey = "   " }),
            Mock.Of<ILogger<HardeningMiddleware>>());

        ctx.Response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
        nextCalled.Should().BeFalse();
    }
}