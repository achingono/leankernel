using FluentAssertions;

using LeanKernel.Services.Common.Interfaces;
using LeanKernel.Services.Common.Memory;

using Microsoft.Extensions.Logging;

using Moq;

using Xunit;

namespace LeanKernel.Tests.Unit.Providers;

public sealed class GBrainDreamServiceTests
{
    [Fact]
    public async Task RunDreamAsync_ParsesStructuredResult()
    {
        var clientMock = new Mock<IGBrainMcpClient>();
        var loggerMock = new Mock<ILogger<GBrainDreamService>>();

        using var json = System.Text.Json.JsonDocument.Parse(
            """
            {
              "status": "Completed",
              "total_pages": 12,
              "failed_pages": 1,
              "phase_status": { "consolidation": "ok" }
            }
            """);

        clientMock
            .Setup(c => c.CallToolAsync("dream", It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(json.RootElement);

        var service = new GBrainDreamService(clientMock.Object, loggerMock.Object);
        var result = await service.RunDreamAsync("tenant/user", "targeted");

        result.Status.Should().Be("Completed");
        result.TotalPages.Should().Be(12);
        result.FailedPages.Should().Be(1);
        result.PhaseStatusJson.Should().Contain("consolidation");
    }

    [Fact]
    public async Task RunDreamAsync_WhenToolFails_ReturnsFailedResult()
    {
        var clientMock = new Mock<IGBrainMcpClient>();
        var loggerMock = new Mock<ILogger<GBrainDreamService>>();

        clientMock
            .Setup(c => c.CallToolAsync("dream", It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        var service = new GBrainDreamService(clientMock.Object, loggerMock.Object);
        var result = await service.RunDreamAsync("tenant/user", "full");

        result.Status.Should().Be("Failed");
        result.TotalPages.Should().Be(0);
        result.FailedPages.Should().Be(0);
    }
}