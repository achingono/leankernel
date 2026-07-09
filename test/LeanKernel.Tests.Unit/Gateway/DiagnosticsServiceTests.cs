using FluentAssertions;
using LeanKernel.Gateway.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;

namespace LeanKernel.Tests.Unit.Gateway;

public class DiagnosticsServiceTests
{
    private readonly Mock<ILogger<DiagnosticsService>> _loggerMock = new();

    [Fact]
    public void Constructor_throws_on_null_navigationManager()
    {
        var act = () => new DiagnosticsService(null!, new ConfigurationBuilder().Build(), _loggerMock.Object);
        act.Should().Throw<ArgumentNullException>().WithParameterName("navigationManager");
    }

    [Fact]
    public void Constructor_throws_on_null_configuration()
    {
        var nav = new TestNavigationManager();
        var act = () => new DiagnosticsService(nav, null!, _loggerMock.Object);
        act.Should().Throw<ArgumentNullException>().WithParameterName("configuration");
    }

    [Fact]
    public void Constructor_throws_on_null_logger()
    {
        var nav = new TestNavigationManager();
        var act = () => new DiagnosticsService(nav, new ConfigurationBuilder().Build(), null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    [Fact]
    public void Constructor_with_single_api_key_adds_header()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["LeanKernel:Gateway:ApiKey"] = "test-key-123"
            })
            .Build();

        using var service = new DiagnosticsService(new TestNavigationManager(), config, _loggerMock.Object);

        var httpClient = GetHttpClient(service);
        httpClient.DefaultRequestHeaders.Contains("X-Api-Key").Should().BeTrue();
        httpClient.DefaultRequestHeaders.GetValues("X-Api-Key").Should().ContainSingle().Which.Should().Be("test-key-123");
    }

    [Fact]
    public void Constructor_with_api_keys_array_uses_first_non_empty()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["LeanKernel:Gateway:ApiKeys:0"] = "",
                ["LeanKernel:Gateway:ApiKeys:1"] = "array-key-456"
            })
            .Build();

        using var service = new DiagnosticsService(new TestNavigationManager(), config, _loggerMock.Object);

        var httpClient = GetHttpClient(service);
        httpClient.DefaultRequestHeaders.GetValues("X-Api-Key").Should().ContainSingle().Which.Should().Be("array-key-456");
    }

    [Fact]
    public void Constructor_without_api_key_does_not_add_header()
    {
        var config = new ConfigurationBuilder().Build();

        using var service = new DiagnosticsService(new TestNavigationManager(), config, _loggerMock.Object);

        var httpClient = GetHttpClient(service);
        httpClient.DefaultRequestHeaders.Contains("X-Api-Key").Should().BeFalse();
    }

    [Fact]
    public void Constructor_with_whitespace_api_key_does_not_add_header()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["LeanKernel:Gateway:ApiKey"] = "   "
            })
            .Build();

        using var service = new DiagnosticsService(new TestNavigationManager(), config, _loggerMock.Object);

        var httpClient = GetHttpClient(service);
        httpClient.DefaultRequestHeaders.Contains("X-Api-Key").Should().BeFalse();
    }

    [Fact]
    public void Constructor_sets_base_address_from_navigation_manager()
    {
        var config = new ConfigurationBuilder().Build();

        using var service = new DiagnosticsService(new TestNavigationManager("https://custom-host.example.com/"), config, _loggerMock.Object);

        var httpClient = GetHttpClient(service);
        httpClient.BaseAddress.Should().Be(new Uri("https://custom-host.example.com/"));
    }

    [Fact]
    public async Task LoadAsync_throws_on_null_sessionId()
    {
        using var service = new DiagnosticsService(new TestNavigationManager(), new ConfigurationBuilder().Build(), _loggerMock.Object);

        var act = () => service.LoadAsync(null!);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task LoadAsync_throws_on_empty_sessionId()
    {
        using var service = new DiagnosticsService(new TestNavigationManager(), new ConfigurationBuilder().Build(), _loggerMock.Object);

        var act = () => service.LoadAsync("");
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task LoadAsync_throws_on_whitespace_sessionId()
    {
        using var service = new DiagnosticsService(new TestNavigationManager(), new ConfigurationBuilder().Build(), _loggerMock.Object);

        var act = () => service.LoadAsync("   ");
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task LoadAsync_with_unreachable_uri_returns_error_result()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var service = new DiagnosticsService(
            new TestNavigationManager("http://127.0.0.1:1/"),
            new ConfigurationBuilder().Build(),
            _loggerMock.Object);

        var result = await service.LoadAsync("test-session", cts.Token);

        result.Data.Should().BeNull();
        result.ErrorMessage.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task LoadAsync_with_api_key_sends_header_on_unreachable_uri()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["LeanKernel:Gateway:ApiKey"] = "secret-key"
            })
            .Build();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var service = new DiagnosticsService(
            new TestNavigationManager("http://127.0.0.1:1/"),
            config,
            _loggerMock.Object);

        var result = await service.LoadAsync("test-session", cts.Token);

        result.ErrorMessage.Should().NotBeNullOrWhiteSpace();
        GetHttpClient(service).DefaultRequestHeaders.Contains("X-Api-Key").Should().BeTrue();
    }

    [Fact]
    public void Dispose_completes_without_error()
    {
        var service = new DiagnosticsService(new TestNavigationManager(), new ConfigurationBuilder().Build(), _loggerMock.Object);

        var act = () => service.Dispose();
        act.Should().NotThrow();
    }

    [Fact]
    public void Dispose_can_be_called_multiple_times()
    {
        var service = new DiagnosticsService(new TestNavigationManager(), new ConfigurationBuilder().Build(), _loggerMock.Object);

        var act = () =>
        {
            service.Dispose();
            service.Dispose();
        };
        act.Should().NotThrow();
    }

    private static HttpClient GetHttpClient(DiagnosticsService service)
    {
        var field = typeof(DiagnosticsService).GetField("_httpClient", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        return (HttpClient)field.GetValue(service)!;
    }

    private sealed class TestNavigationManager : NavigationManager
    {
        public TestNavigationManager(string baseUri = "http://localhost:5080/")
        {
            Initialize(baseUri, baseUri);
        }

        protected override void NavigateToCore(string uri, NavigationOptions options)
        {
            throw new NotSupportedException();
        }
    }
}
