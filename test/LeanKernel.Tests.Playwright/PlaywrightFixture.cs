using Microsoft.Playwright;

using Xunit;

namespace LeanKernel.Tests.Playwright;

/// <summary>
/// Manages the shared Playwright instance for endpoint smoke tests.
/// </summary>
public class PlaywrightFixture : IAsyncLifetime
{
    public string BaseUrl { get; } = BuildBaseUrl();

    /// <summary>Whether the gateway server was reachable during initialization.</summary>
    public bool ServerAvailable { get; private set; }

    public IPlaywright Instance => _instance ?? throw new InvalidOperationException("Playwright not initialized.");

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        _instance = await Microsoft.Playwright.Playwright.CreateAsync();

        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            using var response = await http.GetAsync($"{BaseUrl}/health");
            ServerAvailable = response.IsSuccessStatusCode;
        }
        catch
        {
            ServerAvailable = false;
        }
    }

    /// <inheritdoc />
    public Task DisposeAsync()
    {
        _instance?.Dispose();
        return Task.CompletedTask;
    }

    private IPlaywright? _instance;

    private static string BuildBaseUrl()
    {
        var explicitUrl = Environment.GetEnvironmentVariable("LEANKERNEL_BASE_URL");
        if (!string.IsNullOrWhiteSpace(explicitUrl))
        {
            return explicitUrl;
        }

        var port = Environment.GetEnvironmentVariable("LEANKERNEL_GATEWAY_PORT") ?? "8080";
        return $"http://localhost:{port}";
    }
}