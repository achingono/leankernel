using LeanKernel.Commander;

namespace LeanKernel.Tests.Unit.Commander;

public class MessageNormalizerTests
{
    [Fact]
    public void Normalize_CreatesValidLeanKernelMessage()
    {
        var msg = MessageNormalizer.Normalize("signal", "user123", "  Hello world  ");

        Assert.Equal("signal", msg.ChannelId);
        Assert.Equal("user123", msg.SenderId);
        Assert.Equal("Hello world", msg.Content); // Trimmed
        Assert.NotNull(msg.Id);
        Assert.NotEqual(default, msg.Timestamp);
    }

    [Fact]
    public void Normalize_IncludesOptionalMetadata()
    {
        var meta = new Dictionary<string, string> { ["source"] = "test" };
        var msg = MessageNormalizer.Normalize("signal", "u1", "hi", metadata: meta);

        Assert.Equal("test", msg.Metadata["source"]);
    }

    [Fact]
    public void Normalize_SetsReplyToId()
    {
        var msg = MessageNormalizer.Normalize("signal", "u1", "reply", replyToId: "orig-123");
        Assert.Equal("orig-123", msg.ReplyToId);
    }
}
