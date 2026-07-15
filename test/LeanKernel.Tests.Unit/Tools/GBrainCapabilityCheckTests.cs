using System.Text.Json;
using FluentAssertions;
using LeanKernel.Gateway.Memory;
using LeanKernel.Logic.Memory;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace LeanKernel.Tests.Unit.Tools;

public class GBrainCapabilityCheckTests
{
    private readonly Mock<IGBrainMcpClient> _mockClient = new();
    private readonly ILogger<GBrainCapabilityCheck> _logger =
        new Mock<ILogger<GBrainCapabilityCheck>>().Object;

    private GBrainCapabilityCheck CreateCheck() =>
        new(_mockClient.Object, _logger);

    private static JsonElement EmptyJson()
    {
        using var doc = JsonDocument.Parse("{}");
        return doc.RootElement.Clone();
    }

    [Fact]
    public async Task ProbeAsync_AllOperationsAvailable_ReturnsFullStatus()
    {
        _mockClient.Setup(c => c.CallToolAsync(It.IsAny<string>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(EmptyJson());

        var check = CreateCheck();
        var result = await check.ProbeAsync();

        result.Status.Should().Be(MemoryCapabilityStatus.Full);
        result.CanSearch.Should().BeTrue();
        result.CanRead.Should().BeTrue();
        result.CanWrite.Should().BeTrue();
    }

    [Fact]
    public async Task ProbeAsync_SearchFails_ReturnsUnavailable()
    {
        _mockClient.Setup(c => c.CallToolAsync("search", It.IsAny<object?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Connection refused"));

        var check = CreateCheck();
        var result = await check.ProbeAsync();

        result.Status.Should().Be(MemoryCapabilityStatus.Unavailable);
        result.CanSearch.Should().BeFalse();
    }

    [Fact]
    public async Task ProbeAsync_GetPageToolNotFound_ReturnsDegradedWithoutRead()
    {
        _mockClient.Setup(c => c.CallToolAsync("search", It.IsAny<object?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(EmptyJson());
        _mockClient.Setup(c => c.CallToolAsync("get_page", It.IsAny<object?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new GBrainException("Method not found", -32601));
        _mockClient.Setup(c => c.CallToolAsync("put_page", It.IsAny<object?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(EmptyJson());

        var check = CreateCheck();
        var result = await check.ProbeAsync();

        result.Status.Should().Be(MemoryCapabilityStatus.Degraded);
        result.CanSearch.Should().BeTrue();
        result.CanRead.Should().BeFalse();
        result.CanWrite.Should().BeTrue();
    }

    [Fact]
    public async Task ProbeAsync_GBrainNonMethodNotFoundError_TreatsAsSupported()
    {
        // Non-404 GBrain errors (e.g. page not found for probe key) count as "tool supported"
        _mockClient.Setup(c => c.CallToolAsync(It.IsAny<string>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new GBrainException("Page not found", 404));

        var check = CreateCheck();
        var result = await check.ProbeAsync();

        result.Status.Should().Be(MemoryCapabilityStatus.Full);
        result.CanSearch.Should().BeTrue();
        result.CanRead.Should().BeTrue();
        result.CanWrite.Should().BeTrue();
    }

    [Fact]
    public void Constructor_NullClient_Throws()
    {
        var act = () => new GBrainCapabilityCheck(null!, _logger);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_NullLogger_Throws()
    {
        var act = () => new GBrainCapabilityCheck(_mockClient.Object, null!);
        act.Should().Throw<ArgumentNullException>();
    }
}
