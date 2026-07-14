using System.Net;
using System.Net.Http;
using FluentAssertions;
using LeanKernel.Gateway.Tools.Dynamic;
using LeanKernel.Logic.Configuration;
using LeanKernel.Logic.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
using Xunit;

namespace LeanKernel.Tests.Unit.Tools;

public class DynamicSkillToolTests
{
    private static SkillDefinition MakeSkill(string name, string baseUrl, IReadOnlyList<string>? allowedHosts = null) =>
        new()
        {
            Name = name,
            Description = $"{name} skill",
            Runtime = new SkillRuntimeConfig
            {
                Type = "http",
                BaseUrl = baseUrl,
                TimeoutSeconds = 10
            },
            AllowedHosts = allowedHosts ?? ["api.example.com"],
            Operations = []
        };

    private static SkillOperation MakeOperation(string id, string method = "GET", string path = "/test") =>
        new()
        {
            Id = id,
            Summary = $"Test {id}",
            HttpMethod = method,
            HttpPath = path,
            Parameters = []
        };

    private IServiceScopeFactory BuildScopeFactory(
        HttpMessageHandler? handler = null,
        IReadOnlyList<string>? globalAllowHosts = null)
    {
        var services = new ServiceCollection();
        services.Configure<AgentSettings>(opts =>
        {
            opts.Tools.DynamicHttp.AllowHosts = globalAllowHosts ?? [];
        });

        if (handler != null)
        {
            services.AddHttpClient("dynamic-skill")
                .ConfigurePrimaryHttpMessageHandler(() => handler);
        }
        else
        {
            services.AddHttpClient("dynamic-skill");
        }

        var sp = services.BuildServiceProvider();

        var mockFactory = new Mock<IServiceScopeFactory>();
        mockFactory.Setup(f => f.CreateScope())
            .Returns(() =>
            {
                var mockScope = new Mock<IServiceScope>();
                mockScope.Setup(s => s.ServiceProvider).Returns(sp);
                return mockScope.Object;
            });

        return mockFactory.Object;
    }

    [Fact]
    public void Create_ValidInputs_ReturnsToolDefinition()
    {
        var skill = MakeSkill("weather", "https://api.example.com");
        var op = MakeOperation("current");
        var scopeFactory = BuildScopeFactory();

        var tool = DynamicSkillTool.Create(skill, op, scopeFactory);

        tool.Name.Should().Be("weather_current");
        tool.Description.Should().Contain("weather");
        tool.Description.Should().Contain("Test current");
    }

    [Fact]
    public void Create_NullSkill_Throws()
    {
        var act = () => DynamicSkillTool.Create(null!, MakeOperation("op"), BuildScopeFactory());
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Create_NullOperation_Throws()
    {
        var act = () => DynamicSkillTool.Create(MakeSkill("s", "https://api.example.com"), null!, BuildScopeFactory());
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task Handler_LoopbackTarget_ReturnsEgressError()
    {
        var skill = MakeSkill("local", "http://localhost", ["localhost"]);
        var op = MakeOperation("test", "GET", "/api");
        var tool = DynamicSkillTool.Create(skill, op, BuildScopeFactory());

        var result = await tool.Handler(new Dictionary<string, object?>(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("localhost");
    }

    [Fact]
    public async Task Handler_HostNotInAllowlist_ReturnsEgressError()
    {
        var skill = MakeSkill("unlisted", "https://unlisted.com", []);
        var op = MakeOperation("test", "GET", "/api");
        var tool = DynamicSkillTool.Create(skill, op, BuildScopeFactory());

        var result = await tool.Handler(new Dictionary<string, object?>(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("allowlist");
    }

    [Fact]
    public async Task Handler_WithMockedHttpClient_SuccessResponse()
    {
        var mockHandler = new Mock<HttpMessageHandler>();
        mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"result":"ok"}""")
            });

        var skill = MakeSkill("weather", "https://api.example.com");
        var op = MakeOperation("current", "GET", "/weather");
        var tool = DynamicSkillTool.Create(skill, op, BuildScopeFactory(mockHandler.Object));

        var result = await tool.Handler(new Dictionary<string, object?>(), CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Output.Should().Contain("ok");
    }

    [Fact]
    public async Task Handler_PathPlaceholder_ExpandsCorrectly()
    {
        var requests = new List<HttpRequestMessage>();
        var mockHandler = new Mock<HttpMessageHandler>();
        mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((req, _) => requests.Add(req))
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}")
            });

        var skill = MakeSkill("weather", "https://api.example.com");
        var op = new SkillOperation
        {
            Id = "current",
            Summary = "Get weather",
            HttpMethod = "GET",
            HttpPath = "/v1/{city}/weather",
            Parameters =
            [
                new SkillOperationParameter { Name = "city", Type = "string", Required = true }
            ]
        };

        var tool = DynamicSkillTool.Create(skill, op, BuildScopeFactory(mockHandler.Object));
        await tool.Handler(new Dictionary<string, object?> { ["city"] = "London" }, CancellationToken.None);

        requests.Should().HaveCount(1);
        requests[0].RequestUri!.PathAndQuery.Should().Contain("London");
    }

    [Fact]
    public async Task Handler_BearerAuthNoSecretRef_ReturnsError()
    {
        var skill = new SkillDefinition
        {
            Name = "secure",
            Description = "Secured",
            Runtime = new SkillRuntimeConfig
            {
                Type = "http",
                BaseUrl = "https://api.example.com",
                Auth = new SkillAuthConfig { Type = "bearer", SecretRef = null }
            },
            AllowedHosts = ["api.example.com"],
            Operations = []
        };
        var op = MakeOperation("get");
        var tool = DynamicSkillTool.Create(skill, op, BuildScopeFactory());

        var result = await tool.Handler(new Dictionary<string, object?>(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("secretRef");
    }

    [Fact]
    public async Task Handler_GlobalAllowlistIntersection_BlocksNotInGlobal()
    {
        var skill = MakeSkill("weather", "https://api.example.com");
        var op = MakeOperation("current");
        // Global list doesn't include api.example.com
        var tool = DynamicSkillTool.Create(
            skill, op,
            BuildScopeFactory(globalAllowHosts: ["other.com"]));

        var result = await tool.Handler(new Dictionary<string, object?>(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("allowlist");
    }

    [Fact]
    public async Task Handler_PostMethod_SendsJsonBody()
    {
        string? capturedBody = null;
        var mockHandler = new Mock<HttpMessageHandler>();
        mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .Returns<HttpRequestMessage, CancellationToken>(async (req, ct) =>
            {
                capturedBody = await req.Content!.ReadAsStringAsync(ct);
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") };
            });

        var skill = MakeSkill("svc", "https://api.example.com");
        var op = new SkillOperation
        {
            Id = "create",
            Summary = "Create item",
            HttpMethod = "POST",
            HttpPath = "/items",
            Parameters = [new SkillOperationParameter { Name = "name", Type = "string", Required = true }]
        };

        var tool = DynamicSkillTool.Create(skill, op, BuildScopeFactory(mockHandler.Object));
        var result = await tool.Handler(new Dictionary<string, object?> { ["name"] = "test-item" }, CancellationToken.None);

        result.Success.Should().BeTrue();
        capturedBody.Should().Contain("test-item");
    }

    [Fact]
    public async Task Handler_GetWithRemainingParams_AppendsQueryString()
    {
        HttpRequestMessage? captured = null;
        var mockHandler = new Mock<HttpMessageHandler>();
        mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((req, _) => captured = req)
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") });

        var skill = MakeSkill("svc", "https://api.example.com");
        var op = new SkillOperation
        {
            Id = "search",
            Summary = "Search",
            HttpMethod = "GET",
            HttpPath = "/search",
            Parameters = [new SkillOperationParameter { Name = "q", Type = "string", Required = true }]
        };

        var tool = DynamicSkillTool.Create(skill, op, BuildScopeFactory(mockHandler.Object));
        await tool.Handler(new Dictionary<string, object?> { ["q"] = "hello world" }, CancellationToken.None);

        captured!.RequestUri!.Query.Should().Contain("hello%20world");
    }

    [Fact]
    public async Task Handler_BearerAuthWithEnvVar_SendsToken()
    {
        Environment.SetEnvironmentVariable("SKILL__TEST_SECRET", "env-token-value");
        HttpRequestMessage? captured = null;
        var mockHandler = new Mock<HttpMessageHandler>();
        mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((req, _) => captured = req)
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") });

        var skill = new SkillDefinition
        {
            Name = "svc",
            Description = "Secure svc",
            Runtime = new SkillRuntimeConfig
            {
                Type = "http",
                BaseUrl = "https://api.example.com",
                Auth = new SkillAuthConfig { Type = "bearer", SecretRef = "test-secret" }
            },
            AllowedHosts = ["api.example.com"],
            Operations = []
        };
        var op = MakeOperation("get");
        var tool = DynamicSkillTool.Create(skill, op, BuildScopeFactory(mockHandler.Object));

        await tool.Handler(new Dictionary<string, object?>(), CancellationToken.None);

        captured!.Headers.Authorization!.Scheme.Should().Be("Bearer");
        captured.Headers.Authorization.Parameter.Should().Be("env-token-value");
        Environment.SetEnvironmentVariable("SKILL__TEST_SECRET", null);
    }

    [Fact]
    public async Task Handler_SecretNotFound_ReturnsError()
    {
        var skill = new SkillDefinition
        {
            Name = "svc",
            Description = "Secure svc",
            Runtime = new SkillRuntimeConfig
            {
                Type = "http",
                BaseUrl = "https://api.example.com",
                Auth = new SkillAuthConfig { Type = "bearer", SecretRef = "nonexistent-secret-xyz" }
            },
            AllowedHosts = ["api.example.com"],
            Operations = []
        };
        var op = MakeOperation("get");
        var tool = DynamicSkillTool.Create(skill, op, BuildScopeFactory());

        var result = await tool.Handler(new Dictionary<string, object?>(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("nonexistent-secret-xyz");
    }

    [Fact]
    public async Task Handler_HttpError_ReturnsFailure()
    {
        var mockHandler = new Mock<HttpMessageHandler>();
        mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = new StringContent("error") });

        var skill = MakeSkill("svc", "https://api.example.com");
        var op = MakeOperation("get");
        var tool = DynamicSkillTool.Create(skill, op, BuildScopeFactory(mockHandler.Object));

        var result = await tool.Handler(new Dictionary<string, object?>(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("500");
    }
}
