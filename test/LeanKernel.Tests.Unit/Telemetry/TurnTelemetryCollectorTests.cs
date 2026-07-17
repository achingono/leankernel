using FluentAssertions;
using LeanKernel.Logic.Telemetry;
using Xunit;

namespace LeanKernel.Tests.Unit.Telemetry;

public class TurnTelemetryCollectorTests
{
    [Fact]
    public void CaptureAndConsume_ReturnsTelemetryAndResetsSlot()
    {
        var collector = new TurnTelemetryCollector();
        var telemetry = new TurnTelemetry { ServedModel = "gpt-4o-mini", PromptTokens = 11 };

        collector.Capture(telemetry);

        var consumed = collector.Consume();
        var consumedAgain = collector.Consume();

        consumed.Should().NotBeNull();
        consumed!.ServedModel.Should().Be("gpt-4o-mini");
        consumed.PromptTokens.Should().Be(11);
        consumedAgain.Should().BeNull();
    }

    [Fact]
    public void Capture_WhenCalledTwice_UsesMostRecentTelemetry()
    {
        var collector = new TurnTelemetryCollector();

        collector.Capture(new TurnTelemetry { ServedModel = "first" });
        collector.Capture(new TurnTelemetry { ServedModel = "second" });

        var consumed = collector.Consume();

        consumed.Should().NotBeNull();
        consumed!.ServedModel.Should().Be("second");
    }
}
