using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using LeanKernel.Core.Configuration;
using LeanKernel.Core.Enums;
using LeanKernel.Core.Interfaces;
using LeanKernel.Core.Models;
using LeanKernel.Host.Controllers;
using NSubstitute;
using Xunit;

namespace LeanKernel.Tests.Unit.Host;

public class StatsControllerTests
{
    [Fact]
    public async Task GetStats_ReturnsOk()
    {
        var sessions = Substitute.For<ISessionStore>();
        sessions.ListSessionsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<string>>(["s1"]));

        var wiki = Substitute.For<IWikiStore>();
        foreach (var dim in Enum.GetValues<WikiDimension>())
            wiki.ListByDimensionAsync(dim, Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<IReadOnlyList<WikiEntry>>([]));

        var config = Options.Create(new LeanKernelConfig());
        var controller = new StatsController(sessions, wiki, config);

        var result = await controller.GetStats(CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task GetStats_IncludesSessionCount()
    {
        var sessions = Substitute.For<ISessionStore>();
        sessions.ListSessionsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<string>>(["s1", "s2", "s3"]));

        var wiki = Substitute.For<IWikiStore>();
        foreach (var dim in Enum.GetValues<WikiDimension>())
            wiki.ListByDimensionAsync(dim, Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<IReadOnlyList<WikiEntry>>([]));

        var controller = new StatsController(sessions, wiki, Options.Create(new LeanKernelConfig()));
        var result = await controller.GetStats(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var json = System.Text.Json.JsonSerializer.Serialize(ok.Value);
        Assert.Contains("3", json); // 3 sessions
    }
}
