namespace LeanKernel.Tests.Playwright;

/// <summary>
/// Collection definition that shares a single Playwright browser instance across all UI tests.
/// Parallelization is disabled to prevent concurrent page connections from causing failures.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public class PlaywrightCollection : ICollectionFixture<PlaywrightFixture>
{
    public const string Name = "Playwright";
}
