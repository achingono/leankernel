using LeanKernel.Logic.Events;

using Xunit;

namespace LeanKernel.Tests.Unit.Events;

public class NullEventStoreTests
{
    [Fact]
    public async Task AppendAsync_CompletesWithoutThrowing()
    {
        var store = new NullEventStore();

        await store.AppendAsync(new { Kind = "turn" });
    }

    [Fact]
    public async Task AppendBatchAsync_CompletesWithoutThrowing()
    {
        var store = new NullEventStore();

        await store.AppendBatchAsync([
            new { Kind = "turn" },
            new { Kind = "telemetry" },
        ]);
    }
}
