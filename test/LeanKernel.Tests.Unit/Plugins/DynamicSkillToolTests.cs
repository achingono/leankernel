using System.Net;
using System.Text.Json;
using FluentAssertions;
using LeanKernel.Abstractions.Models;
using LeanKernel.Plugins.BuiltIn.Skills;
using Moq;

namespace LeanKernel.Tests.Unit.Plugins;

public class DynamicSkillToolHttpTests
{
    private sealed class CaptureHandler : DelegatingHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }
        public string? CapturedRequestBody { get; private set; }
        public HttpResponseMessage Response { get; set; } = new(HttpStatusCode.OK) { Content = new StringContent("{}") };

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastRequest = request;
            if (request.Content is not null)
            {
                CapturedRequestBody = await request.Content.ReadAsStringAsync(cancellationToken);
            }
            return Response;
        }
    }

    private static (ToolDefinition Tool, CaptureHandler Handler) CreateHttpTool(
        SkillDefinition? skill = null,
        SkillOperation? operation = null)
    {
        var handler = new CaptureHandler();
        var client = new HttpClient(handler);
        var factory = Mock.Of<IHttpClientFactory>(f => f.CreateClient("SkillHttp") == client);

        var defaultSkill = skill ?? new SkillDefinition
        {
            Name = "test_skill",
            Description = "Test skill description",
            Runtime = new SkillRuntimeConfig
            {
                Type = "http",
                BaseUrl = "https://api.example.com"
            },
            Operations = [operation ?? new SkillOperation
            {
                Id = "get_data",
                Summary = "Get data from API",
                Invoke = new SkillInvokeConfig
                {
                    HttpMethod = "GET",
                    HttpPath = "/data"
                }
            }]
        };

        var op = operation ?? defaultSkill.Operations[0];
        var tool = DynamicSkillTool.CreateTool(defaultSkill, op, factory);
        return (tool, handler);
    }

    [Fact]
    public void CreateTool_returns_ToolDefinition_with_correct_name_and_description()
    {
        var (tool, _) = CreateHttpTool();

        tool.Name.Should().Be("test_skill_get_data");
        tool.Description.Should().Be("Test skill description — Get data from API");
    }

    [Fact]
    public void CreateTool_sets_category_from_metadata()
    {
        var skill = new SkillDefinition
        {
            Name = "test",
            Description = "desc",
            Metadata = new Dictionary<string, object?> { ["category"] = "data" },
            Runtime = new SkillRuntimeConfig { Type = "http", BaseUrl = "https://api.example.com" },
            Operations = [new SkillOperation { Id = "op", Summary = "op", Invoke = new SkillInvokeConfig { HttpMethod = "GET" } }]
        };
        var (tool, _) = CreateHttpTool(skill);

        tool.Category.Should().Be("data");
    }

    [Fact]
    public void CreateTool_builds_parameters_from_ParametersRaw()
    {
        var op = new SkillOperation
        {
            Id = "op",
            Summary = "op",
            Invoke = new SkillInvokeConfig { HttpMethod = "GET" },
            ParametersRaw = new Dictionary<string, object?>
            {
                ["properties"] = new Dictionary<object, object?>
                {
                    ["name"] = new Dictionary<object, object?>
                    {
                        ["type"] = "string",
                        ["description"] = "The name"
                    },
                    ["count"] = new Dictionary<object, object?>
                    {
                        ["type"] = "integer"
                    }
                },
                ["required"] = new List<object> { "name" }
            }
        };

        var skill = new SkillDefinition
        {
            Name = "test", Description = "desc",
            Runtime = new SkillRuntimeConfig { Type = "http", BaseUrl = "https://api.example.com" },
            Operations = [op]
        };
        var (tool, _) = CreateHttpTool(skill, op);

        tool.Parameters.Should().HaveCount(2);
        tool.Parameters.Should().Contain(p => p.Name == "name" && p.Type == "string" && p.Description == "The name" && p.Required);
        tool.Parameters.Should().Contain(p => p.Name == "count" && p.Type == "integer" && p.Description == string.Empty && !p.Required);
    }

    [Fact]
    public void CreateTool_throws_ArgumentNullException_for_null_parameters()
    {
        var skill = new SkillDefinition
        {
            Name = "test", Description = "desc",
            Runtime = new SkillRuntimeConfig { Type = "http", BaseUrl = "https://example.com" },
            Operations = [new SkillOperation { Id = "op", Summary = "op", Invoke = new SkillInvokeConfig { HttpMethod = "GET" } }]
        };
        var op = skill.Operations[0];
        var factory = Mock.Of<IHttpClientFactory>();

        Action act1 = () => DynamicSkillTool.CreateTool(null!, op, factory);
        Action act2 = () => DynamicSkillTool.CreateTool(skill, null!, factory);
        Action act3 = () => DynamicSkillTool.CreateTool(skill, op, null!);

        act1.Should().Throw<ArgumentNullException>();
        act2.Should().Throw<ArgumentNullException>();
        act3.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task Handler_sends_GET_request_to_correct_url()
    {
        var (tool, handler) = CreateHttpTool();

        var result = await tool.Handler!(new Dictionary<string, object?>(), CancellationToken.None);

        result.Success.Should().BeTrue();
        handler.LastRequest.Should().NotBeNull();
        handler.LastRequest!.Method.Should().Be(HttpMethod.Get);
        handler.LastRequest.RequestUri!.ToString().Should().Be("https://api.example.com/data");
    }

    [Fact]
    public async Task Handler_sends_POST_with_json_body()
    {
        var op = new SkillOperation
        {
            Id = "create",
            Summary = "Create item",
            Invoke = new SkillInvokeConfig
            {
                HttpMethod = "POST",
                HttpPath = "/items",
                Flags = new Dictionary<string, string>
                {
                    ["name"] = "name",
                    ["value"] = "value"
                }
            }
        };
        var (tool, handler) = CreateHttpTool(operation: op);

        var result = await tool.Handler!(new Dictionary<string, object?>
        {
            ["name"] = "test-item",
            ["value"] = "42"
        }, CancellationToken.None);

        result.Success.Should().BeTrue();
        handler.LastRequest!.Method.Should().Be(HttpMethod.Post);
        handler.LastRequest.RequestUri!.ToString().Should().Be("https://api.example.com/items");
        handler.CapturedRequestBody.Should().NotBeNull();
        using var json = JsonDocument.Parse(handler.CapturedRequestBody);
        json.RootElement.GetProperty("name").GetString().Should().Be("test-item");
        json.RootElement.GetProperty("value").GetString().Should().Be("42");
    }

    [Fact]
    public async Task Handler_with_query_parameters_appends_to_url()
    {
        var op = new SkillOperation
        {
            Id = "search",
            Summary = "Search items",
            Invoke = new SkillInvokeConfig
            {
                HttpMethod = "GET",
                HttpPath = "/search",
                Flags = new Dictionary<string, string>
                {
                    ["q"] = "q",
                    ["limit"] = "limit"
                }
            }
        };
        var (tool, handler) = CreateHttpTool(operation: op);

        var result = await tool.Handler!(new Dictionary<string, object?>
        {
            ["q"] = "hello world",
            ["limit"] = "10"
        }, CancellationToken.None);

        result.Success.Should().BeTrue();
        var query = handler.LastRequest!.RequestUri!.Query;
        query.Should().Contain("q=hello%20world");
        query.Should().Contain("limit=10");
    }

    [Fact]
    public async Task Handler_uses_query_string_for_DELETE_requests()
    {
        var op = new SkillOperation
        {
            Id = "delete_item",
            Summary = "Delete item",
            Invoke = new SkillInvokeConfig
            {
                HttpMethod = "DELETE",
                HttpPath = "/items",
                Flags = new Dictionary<string, string> { ["id"] = "id" }
            }
        };
        var (tool, handler) = CreateHttpTool(operation: op);

        var result = await tool.Handler!(new Dictionary<string, object?>
        {
            ["id"] = "42"
        }, CancellationToken.None);

        result.Success.Should().BeTrue();
        handler.LastRequest!.Method.Should().Be(HttpMethod.Delete);
        handler.LastRequest.RequestUri!.Query.Should().Be("?id=42");
        handler.LastRequest.Content.Should().BeNull();
    }

    [Fact]
    public async Task Handler_with_missing_baseUrl_returns_error()
    {
        var skill = new SkillDefinition
        {
            Name = "test", Description = "desc",
            Runtime = new SkillRuntimeConfig { Type = "http" },
            Operations = [new SkillOperation { Id = "op", Summary = "op", Invoke = new SkillInvokeConfig { HttpMethod = "GET" } }]
        };
        var (tool, _) = CreateHttpTool(skill);

        var result = await tool.Handler!(new Dictionary<string, object?>(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ToolName.Should().Be("op");
        result.Error.Should().Contain("baseUrl");
    }

    [Fact]
    public async Task Handler_with_bearer_auth_includes_authorization_header()
    {
        const string secretKey = "LEANKERNEL_TEST_SKILL_TOKEN";
        var skill = new SkillDefinition
        {
            Name = "test", Description = "desc",
            Runtime = new SkillRuntimeConfig
            {
                Type = "http",
                BaseUrl = "https://api.example.com",
                Auth = new SkillAuthConfig
                {
                    Type = "bearer",
                    SecretRef = secretKey
                }
            },
            Operations = [new SkillOperation { Id = "op", Summary = "op", Invoke = new SkillInvokeConfig { HttpMethod = "GET" } }]
        };

        Environment.SetEnvironmentVariable(secretKey, "test-token-value");
        try
        {
            var (tool, handler) = CreateHttpTool(skill);

            var result = await tool.Handler!(new Dictionary<string, object?>(), CancellationToken.None);

            result.Success.Should().BeTrue();
            handler.LastRequest!.Headers.Authorization.Should().NotBeNull();
            handler.LastRequest.Headers.Authorization!.Scheme.Should().Be("Bearer");
            handler.LastRequest.Headers.Authorization.Parameter.Should().Be("test-token-value");
        }
        finally
        {
            Environment.SetEnvironmentVariable(secretKey, null);
        }
    }

    [Fact]
    public async Task Handler_with_unsupported_auth_type_returns_error()
    {
        var skill = new SkillDefinition
        {
            Name = "test", Description = "desc",
            Runtime = new SkillRuntimeConfig
            {
                Type = "http",
                BaseUrl = "https://api.example.com",
                Auth = new SkillAuthConfig { Type = "basic" }
            },
            Operations = [new SkillOperation { Id = "op", Summary = "op", Invoke = new SkillInvokeConfig { HttpMethod = "GET" } }]
        };
        var (tool, _) = CreateHttpTool(skill);

        var result = await tool.Handler!(new Dictionary<string, object?>(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("auth");
    }

    [Fact]
    public async Task Handler_with_missing_secret_ref_returns_error()
    {
        var skill = new SkillDefinition
        {
            Name = "test", Description = "desc",
            Runtime = new SkillRuntimeConfig
            {
                Type = "http",
                BaseUrl = "https://api.example.com",
                Auth = new SkillAuthConfig { Type = "bearer" }
            },
            Operations = [new SkillOperation { Id = "op", Summary = "op", Invoke = new SkillInvokeConfig { HttpMethod = "GET" } }]
        };
        var (tool, _) = CreateHttpTool(skill);

        var result = await tool.Handler!(new Dictionary<string, object?>(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("secretRef");
    }

    [Fact]
    public async Task Handler_non_success_status_code_returns_error()
    {
        var (tool, handler) = CreateHttpTool();
        handler.Response = new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("Server error")
        };

        var result = await tool.Handler!(new Dictionary<string, object?>(), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("500");
        result.Error.Should().Contain("Server error");
    }

    [Fact]
    public async Task Handler_resolves_url_path_parameters()
    {
        var op = new SkillOperation
        {
            Id = "get_item",
            Summary = "Get item by ID",
            Invoke = new SkillInvokeConfig
            {
                HttpMethod = "GET",
                HttpPath = "/items/{id}"
            }
        };
        var (tool, handler) = CreateHttpTool(operation: op);

        var result = await tool.Handler!(new Dictionary<string, object?>
        {
            ["id"] = "42"
        }, CancellationToken.None);

        result.Success.Should().BeTrue();
        handler.LastRequest!.RequestUri!.ToString().Should().Be("https://api.example.com/items/42");
    }

    [Fact]
    public async Task Handler_cancellation_returns_cancelled_result()
    {
        var (tool, handler) = CreateHttpTool();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await tool.Handler!(new Dictionary<string, object?>(), cts.Token);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("cancelled");
    }

    [Fact]
    public async Task Handler_with_null_arg_values_skips_them()
    {
        var op = new SkillOperation
        {
            Id = "search",
            Summary = "Search",
            Invoke = new SkillInvokeConfig
            {
                HttpMethod = "GET",
                HttpPath = "/search",
                Flags = new Dictionary<string, string>
                {
                    ["q"] = "q",
                    ["unused"] = "unused"
                }
            }
        };
        var (tool, handler) = CreateHttpTool(operation: op);

        var result = await tool.Handler!(new Dictionary<string, object?>
        {
            ["q"] = "test",
            ["unused"] = null
        }, CancellationToken.None);

        result.Success.Should().BeTrue();
        handler.LastRequest!.RequestUri!.Query.Should().Be("?q=test");
    }

    [Fact]
    public async Task Handler_truncates_long_output()
    {
        var (tool, handler) = CreateHttpTool();
        var longContent = new string('x', 15_000);
        handler.Response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(longContent)
        };

        var result = await tool.Handler!(new Dictionary<string, object?>(), CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Output!.Length.Should().Be(12_000);
    }
}
