using FluentAssertions;

using LeanKernel.Entities;
using LeanKernel.Logic.Diagnostics;

using Xunit;

namespace LeanKernel.Tests.Unit.Diagnostics;

public class DiagnosticsCollectorTests
{
    [Fact]
    public void Capture_ThenConsume_ReturnsEntries()
    {
        var collector = new DiagnosticsCollector();
        var entry = new DiagnosticEntry
        {
            Source = "gateway",
            Category = "turn",
            PayloadJson = "{}",
        };

        collector.Capture(entry);

        var consumed = collector.Consume();

        consumed.Should().ContainSingle();
        consumed[0].Source.Should().Be("gateway");
        collector.Consume().Should().BeEmpty();
    }

    [Fact]
    public void Capture_NullEntry_ThrowsArgumentNullException()
    {
        var collector = new DiagnosticsCollector();

        Action act = () => collector.Capture(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("entry");
    }

    [Fact]
    public void Capture_MultipleEntries_ConsumeReturnsAll()
    {
        var collector = new DiagnosticsCollector();

        collector.Capture(new DiagnosticEntry { Source = "a", Category = "c1", PayloadJson = "{}" });
        collector.Capture(new DiagnosticEntry { Source = "b", Category = "c2", PayloadJson = "{}" });
        collector.Capture(new DiagnosticEntry { Source = "c", Category = "c3", PayloadJson = "{}" });

        var consumed = collector.Consume();

        consumed.Should().HaveCount(3);
        consumed.Select(e => e.Source).Should().BeEquivalentTo("a", "b", "c");
    }
}