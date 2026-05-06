using System.Text;
using LeanKernel.Core.Models;

namespace LeanKernel.Tests.Unit.Commander;

public sealed class SignalAttachmentProcessingTests
{
    [Fact]
    public void TextExtractor_ReadsUtf8PlainText()
    {
        var bytes = Encoding.UTF8.GetBytes("Line one\nLine two");

        var text = InboundAttachmentTextExtractor.TryExtractText("text/plain", "notes.txt", bytes);

        Assert.Equal("Line one\nLine two", text);
    }

    [Fact]
    public void TextExtractor_RejectsBinaryContent()
    {
        var bytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x00, 0x01, 0x02, 0x03 };

        var text = InboundAttachmentTextExtractor.TryExtractText("image/png", "diagram.png", bytes);

        Assert.Null(text);
    }

    [Fact]
    public void Formatter_EmbedsExtractedAttachmentTextAndMetadata()
    {
        var attachments = new[]
        {
            new InboundAttachment
            {
                Id = "att-1",
                FileName = "meeting-notes.md",
                ContentType = "text/markdown",
                Size = 42,
                ExtractedText = "# Notes\n- Budget approved"
            },
            new InboundAttachment
            {
                Id = "att-2",
                FileName = "photo.png",
                ContentType = "image/png",
                Size = 1024
            }
        };

        var content = InboundMessageContentFormatter.FormatContent(
            "Here are the notes from my meeting with Phil yesterday.",
            attachments);
        var metadata = InboundMessageContentFormatter.BuildMetadata("signal", attachments);

        Assert.Contains("Received 2 attachments:", content);
        Assert.Contains("meeting-notes.md", content);
        Assert.Contains("--- Begin attachment: meeting-notes.md ---", content);
        Assert.Contains("# Notes", content);
        Assert.Contains("photo.png", content);
        Assert.Contains("could not be extracted automatically", content);
        Assert.Equal("2", metadata["signal:attachment_count"]);
        Assert.Equal("1", metadata["signal:attachment_text_count"]);
    }
}
