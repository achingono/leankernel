using FluentAssertions;
using LeanKernel.Gateway.Channels;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace LeanKernel.Tests.Unit.Channels;

/// <summary>
/// Tests for <see cref="SignalAttachmentParser"/> — Signal-specific attachment directive parsing.
/// </summary>
public sealed class SignalAttachmentParserTests
{
    private readonly SignalAttachmentParser _parser;

    public SignalAttachmentParserTests()
    {
        _parser = new SignalAttachmentParser(Mock.Of<ILogger<SignalAttachmentParser>>());
    }

    [Fact]
    public void TryParseAndRemoveDirective_NoDirective_ReturnsFalse()
    {
        var content = "Hello, no attachments here.";
        var incoming = new List<Attachment>();

        var found = _parser.TryParseAndRemoveDirective(ref content, out var attachments, incoming);

        found.Should().BeFalse();
        attachments.Should().BeNull();
        content.Should().Be("Hello, no attachments here.");
    }

    [Fact]
    public void TryParseAndRemoveDirective_DirectiveFound_ResolvesAttachments()
    {
        var content = "Here is the file:\n```signal-attachments\n{\"attachments\":[{\"source\":\"incoming\",\"index\":1}]}\n```";
        var incoming = new List<Attachment>
        {
            new() { FileName = "doc.pdf", ContentType = "application/pdf", Data = [1, 2, 3] }
        };

        var found = _parser.TryParseAndRemoveDirective(ref content, out var attachments, incoming);

        found.Should().BeTrue();
        attachments.Should().HaveCount(1);
        attachments![0].FileName.Should().Be("doc.pdf");
        content.Should().NotContain("signal-attachments");
    }

    [Fact]
    public void TryParseAndRemoveDirective_NoIncomingAttachments_ReturnsTrueButNullAttachments()
    {
        var content = "```signal-attachments\n{\"attachments\":[{\"source\":\"incoming\",\"index\":1}]}\n```";

        var found = _parser.TryParseAndRemoveDirective(ref content, out var attachments, null);

        found.Should().BeTrue();
        attachments.Should().BeNull("because no incoming attachments were present");
    }

    [Fact]
    public void TryParseAndRemoveDirective_InvalidJson_ReturnsTrueButNullAttachments()
    {
        var content = "```signal-attachments\n{not json}\n```";

        var found = _parser.TryParseAndRemoveDirective(ref content, out var attachments, []);

        found.Should().BeTrue();
        attachments.Should().BeNull("because JSON was malformed");
    }

    [Fact]
    public void TryParseAndRemoveDirective_OutOfRangeIndex_SkipsInvalidEntry()
    {
        var content = "```signal-attachments\n{\"attachments\":[{\"source\":\"incoming\",\"index\":99}]}\n```";
        var incoming = new List<Attachment>
        {
            new() { FileName = "file.txt", Data = [1] }
        };

        var found = _parser.TryParseAndRemoveDirective(ref content, out var attachments, incoming);

        found.Should().BeTrue();
        attachments.Should().BeEmpty("because index 99 is out of range");
    }

    [Fact]
    public void TryParseAndRemoveDirective_NonIncomingSource_SkipsEntry()
    {
        var content = "```signal-attachments\n{\"attachments\":[{\"source\":\"generated\",\"index\":1}]}\n```";
        var incoming = new List<Attachment>
        {
            new() { FileName = "file.txt", Data = [1] }
        };

        var found = _parser.TryParseAndRemoveDirective(ref content, out var attachments, incoming);

        found.Should().BeTrue();
        attachments.Should().BeEmpty("because only 'incoming' source is resolved");
    }

    [Fact]
    public void TryParseAndRemoveDirective_MultipleAttachments_ResolvesAll()
    {
        var content = "```signal-attachments\n{\"attachments\":[{\"source\":\"incoming\",\"index\":1},{\"source\":\"incoming\",\"index\":2}]}\n```";
        var incoming = new List<Attachment>
        {
            new() { FileName = "a.pdf", Data = [1] },
            new() { FileName = "b.pdf", Data = [2] }
        };

        var found = _parser.TryParseAndRemoveDirective(ref content, out var attachments, incoming);

        found.Should().BeTrue();
        attachments.Should().HaveCount(2);
        attachments![0].FileName.Should().Be("a.pdf");
        attachments[1].FileName.Should().Be("b.pdf");
    }

    [Fact]
    public void TryParseAndRemoveDirective_EmptyDirective_NoIncoming_ReturnsTrueWithNullAttachments()
    {
        var content = "```signal-attachments\n{}\n```";

        var found = _parser.TryParseAndRemoveDirective(ref content, out var attachments, []);

        found.Should().BeTrue();
        attachments.Should().BeNull();
    }
}
