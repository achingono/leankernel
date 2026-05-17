using LeanKernel.Core.Models;
using LeanKernel.Thinker;

namespace LeanKernel.Tests.Unit.Thinker;

public class ChatExecutionContextAccessorTests
{
    [Fact]
    public void BeginScope_SetsAndResetsCurrentContext()
    {
        var accessor = new ChatExecutionContextAccessor();
        Assert.Null(accessor.Current);

        using (accessor.BeginScope(new ChatExecutionContext
        {
            UserId = "user-a",
            ChannelId = "signal",
            SessionId = "sess-a",
            IsAdmin = false
        }))
        {
            Assert.NotNull(accessor.Current);
            Assert.Equal("user-a", accessor.Current!.UserId);
        }

        Assert.Null(accessor.Current);
    }
}
