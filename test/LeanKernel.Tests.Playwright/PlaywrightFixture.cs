namespace LeanKernel.Tests.Playwright;

/// <summary>
/// Shared fixture that manages Playwright browser lifecycle for all UI tests.
/// Tests expect the application to be running at the configured base URL.
/// </summary>
public class PlaywrightFixture : IAsyncLifetime
{
    private IPlaywright? _playwright;
    private IBrowser? _browser;
    private IBrowserContext? _context;

    public IBrowserContext Context => _context ?? throw new InvalidOperationException("Context not initialized");

    public string BaseUrl => Environment.GetEnvironmentVariable("LEANKERNEL_BASE_URL") ?? "http://localhost:5080";

    public async Task InitializeAsync()
    {
        _playwright = await Microsoft.Playwright.Playwright.CreateAsync();
        _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true
        });
        _context = await _browser.NewContextAsync();
    }

    public async Task DisposeAsync()
    {
        if (_context is not null)
        {
            await _context.CloseAsync();
        }

        if (_browser is not null)
        {
            await _browser.CloseAsync();
        }

        _playwright?.Dispose();
    }
}
