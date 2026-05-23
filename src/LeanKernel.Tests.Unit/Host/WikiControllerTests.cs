using Microsoft.AspNetCore.Mvc;
using LeanKernel.Core.Enums;
using LeanKernel.Core.Interfaces;
using LeanKernel.Core.Models;
using LeanKernel.Host.Controllers;
using NSubstitute;
using Xunit;

namespace LeanKernel.Tests.Unit.Host;

public class WikiControllerTests
{
    [Fact]
    public async Task GetDimensions_ReturnsSummary()
    {
        var wiki = Substitute.For<IWikiStore>();
        var migration = Substitute.For<IWikiMigrationService>();
        foreach (var dim in Enum.GetValues<WikiDimension>())
            wiki.ListByDimensionAsync(dim, Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<IReadOnlyList<WikiEntry>>([]));

        var controller = new WikiController(wiki, migration);
        var result = await controller.GetDimensions(CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task ListEntries_ByDimension_ReturnsFiltered()
    {
        var wiki = Substitute.For<IWikiStore>();
        var migration = Substitute.For<IWikiMigrationService>();
        var entry = new WikiEntry
        {
            Id = "who-alice", Dimension = WikiDimension.Who, Subject = "Alice",
            Facts = [new WikiFact { Claim = "Dev" }]
        };
        wiki.ListByDimensionAsync(WikiDimension.Who, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<WikiEntry>>([entry]));

        var controller = new WikiController(wiki, migration);
        var result = await controller.ListEntries("who", ct: CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var entries = Assert.IsAssignableFrom<IReadOnlyList<WikiEntry>>(ok.Value);
        Assert.Single(entries);
    }

    [Fact]
    public async Task ListEntries_WithSearch_FiltersResults()
    {
        var wiki = Substitute.For<IWikiStore>();
        var migration = Substitute.For<IWikiMigrationService>();
        var alice = new WikiEntry { Id = "who-alice", Dimension = WikiDimension.Who, Subject = "Alice", Facts = [new WikiFact { Claim = "Dev" }] };
        var bob = new WikiEntry { Id = "who-bob", Dimension = WikiDimension.Who, Subject = "Bob", Facts = [new WikiFact { Claim = "PM" }] };

        wiki.ListByDimensionAsync(WikiDimension.Who, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<WikiEntry>>([alice, bob]));

        var controller = new WikiController(wiki, migration);
        var result = await controller.ListEntries("who", "alice", CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var entries = Assert.IsAssignableFrom<IEnumerable<WikiEntry>>(ok.Value);
        Assert.Single(entries);
    }

    [Fact]
    public async Task ListEntries_NoFilters_ListsAll()
    {
        var wiki = Substitute.For<IWikiStore>();
        var migration = Substitute.For<IWikiMigrationService>();
        foreach (var dim in Enum.GetValues<WikiDimension>())
            wiki.ListByDimensionAsync(dim, Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<IReadOnlyList<WikiEntry>>([]));

        var controller = new WikiController(wiki, migration);
        var result = await controller.ListEntries(ct: CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task ListEntries_SearchOnly_QueriesWiki()
    {
        var wiki = Substitute.For<IWikiStore>();
        var migration = Substitute.For<IWikiMigrationService>();
        wiki.QueryAsync(Arg.Any<WikiQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<WikiEntry>>([]));

        var controller = new WikiController(wiki, migration);
        var result = await controller.ListEntries(q: "test", ct: CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        await wiki.Received(1).QueryAsync(
            Arg.Is<WikiQuery>(q => q.TextQuery == "test"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetEntry_Exists_ReturnsOk()
    {
        var wiki = Substitute.For<IWikiStore>();
        var migration = Substitute.For<IWikiMigrationService>();
        wiki.GetAsync("who-alice", Arg.Any<CancellationToken>())
            .Returns(new WikiEntry { Id = "who-alice", Dimension = WikiDimension.Who, Subject = "Alice" });

        var controller = new WikiController(wiki, migration);
        var result = await controller.GetEntry("who-alice", CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task GetEntry_NotFound_Returns404()
    {
        var wiki = Substitute.For<IWikiStore>();
        var migration = Substitute.For<IWikiMigrationService>();
        wiki.GetAsync("who-nobody", Arg.Any<CancellationToken>())
            .Returns((WikiEntry?)null);

        var controller = new WikiController(wiki, migration);
        var result = await controller.GetEntry("who-nobody", CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Migrate_ReturnsSummary()
    {
        var wiki = Substitute.For<IWikiStore>();
        var migration = Substitute.For<IWikiMigrationService>();
        migration.MigrateAsync(Arg.Any<CancellationToken>())
            .Returns(new WikiMigrationResult(1, 0, 0, "/tmp/migration.completed"));

        var controller = new WikiController(wiki, migration);
        var result = await controller.Migrate(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var payload = Assert.IsType<WikiMigrationResult>(ok.Value);
        Assert.Equal(1, payload.Migrated);
    }
}
