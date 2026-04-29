using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using LeanKernel.Archivist.Wiki;
using LeanKernel.Core.Configuration;
using LeanKernel.Core.Interfaces;
using LeanKernel.Scheduler.Jobs;
using NSubstitute;
using Xunit;

namespace LeanKernel.Tests.Unit.Scheduler;

public class WikiMaintenanceJobTests
{
    [Fact]
    public async Task ExecuteAsync_CallsCompiler()
    {
        var wiki = Substitute.For<IWikiStore>();
        foreach (var dim in Enum.GetValues<LeanKernel.Core.Enums.WikiDimension>())
            wiki.ListByDimensionAsync(dim, Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<IReadOnlyList<LeanKernel.Core.Models.WikiEntry>>([]));

        var compiler = new WikiCompiler(wiki, Options.Create(new LeanKernelConfig()), NullLogger<WikiCompiler>.Instance);
        var job = new WikiMaintenanceJob(compiler, NullLogger<WikiMaintenanceJob>.Instance);

        await job.ExecuteAsync(CancellationToken.None);

        // Compiler should have queried all dimensions
        foreach (var dim in Enum.GetValues<LeanKernel.Core.Enums.WikiDimension>())
            await wiki.Received(1).ListByDimensionAsync(dim, Arg.Any<CancellationToken>());
    }
}
