using FluentAssertions;

using LeanKernel.Logic.Memory;
using LeanKernel.Services.Learning;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using Xunit;

namespace LeanKernel.Tests.Unit.Learning;

public sealed class KnowledgePageUpdateCoordinatorTests
{
    [Fact]
    public async Task WriteFactsAsync_WithFacts_PersistsMemoryPage()
    {
        var memoryService = new Mock<IMemoryService>();
        var sut = new KnowledgePageUpdateCoordinator(memoryService.Object, NullLogger<KnowledgePageUpdateCoordinator>.Instance);

        await sut.WriteFactsAsync("tenant/user", ["Fact one", "Fact two"]);

        memoryService.Verify(x => x.PutPageAsync(It.Is<string>(k => k.StartsWith("learning/facts/tenant/user/")), It.Is<string>(v => v.Contains("Fact one") && v.Contains("Fact two")), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task WriteFactsAsync_NoFacts_DoesNothing()
    {
        var memoryService = new Mock<IMemoryService>();
        var sut = new KnowledgePageUpdateCoordinator(memoryService.Object, NullLogger<KnowledgePageUpdateCoordinator>.Instance);

        await sut.WriteFactsAsync("tenant/user", []);

        memoryService.Verify(x => x.PutPageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
