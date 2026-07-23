using FluentAssertions;

using LeanKernel.Logic.Events;

using Xunit;

namespace LeanKernel.Tests.Unit.Events;

public class NullEventStoreTests
{
    [Fact]
    public async Task AppendAsync_CompletesWithoutThrowing()
    {
        var store = new NullEventStore();

        var act = async () => await store.AppendAsync(new { Kind = "turn" });

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task AppendBatchAsync_CompletesWithoutThrowing()
    {
        var store = new NullEventStore();

        var act = async () => await store.AppendBatchAsync([
            new { Kind = "turn" },
            new { Kind = "telemetry" },
        ]);

        await act.Should().NotThrowAsync();
    }
}
