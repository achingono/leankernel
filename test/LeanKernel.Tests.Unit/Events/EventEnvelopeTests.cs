using FluentAssertions;

using Xunit;

namespace LeanKernel.Tests.Unit.Events;

public class EventEnvelopeTests
{
    [Fact]
    public void NewEnvelope_HasUniqueEventId()
    {
        var envelope1 = new EventEnvelope();
        var envelope2 = new EventEnvelope();

        envelope1.EventId.Should().NotBe(envelope2.EventId);
    }

    [Fact]
    public void NewEnvelope_HasDefaultSchemaVersion()
    {
        var envelope = new EventEnvelope();

        envelope.SchemaVersion.Should().Be("1.0");
    }

    [Fact]
    public void NewEnvelope_HasUtcNowTimestamp()
    {
        var before = DateTimeOffset.UtcNow.AddSeconds(-1);
        var envelope = new EventEnvelope();
        var after = DateTimeOffset.UtcNow.AddSeconds(1);

        envelope.Timestamp.Should().BeOnOrAfter(before);
        envelope.Timestamp.Should().BeOnOrBefore(after);
    }
}