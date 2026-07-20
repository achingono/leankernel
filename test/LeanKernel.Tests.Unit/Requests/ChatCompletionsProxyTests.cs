using System.Text;
using System.Text.Json;

using FluentAssertions;

using LeanKernel.Gateway.Requests;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging;

using Moq;

using Xunit;

namespace LeanKernel.Tests.Unit.Requests;

/// <summary>
/// Unit tests for <see cref="IEndpointRouteBuilderExtensions"/> covering
/// <see cref="IEndpointRouteBuilderExtensions.ReconstructMessage"/> and
/// <see cref="IEndpointRouteBuilderExtensions.HandleChatCompletionsRequestAsync"/>.
/// </summary>
public class ChatCompletionsProxyTests
{
    /// <summary>
    /// When <c>role</c> is already the first property, the output order is unchanged.
    /// </summary>
    [Fact]
    public void ReconstructMessage_RoleFirst_Unchanged()
    {
        var input = """{"messages":[{"role":"user","content":"hello"}]}""";
        var result = IEndpointRouteBuilderExtensions.ReconstructMessage(input);

        using var doc = JsonDocument.Parse(result);
        var msg = doc.RootElement.GetProperty("messages")[0];
        var props = new List<string>();
        foreach (var p in msg.EnumerateObject())
            props.Add(p.Name);

        props[0].Should().Be("role");
        props[1].Should().Be("content");
        msg.GetProperty("role").GetString().Should().Be("user");
        msg.GetProperty("content").GetString().Should().Be("hello");
    }

    /// <summary>
    /// When <c>content</c> appears before <c>role</c>, the output has <c>role</c> moved first.
    /// </summary>
    [Fact]
    public void ReconstructMessage_ContentFirst_RoleMovedFirst()
    {
        var input = """{"messages":[{"content":"hello","role":"user"}]}""";
        var result = IEndpointRouteBuilderExtensions.ReconstructMessage(input);

        using var doc = JsonDocument.Parse(result);
        var msg = doc.RootElement.GetProperty("messages")[0];
        var props = new List<string>();
        foreach (var p in msg.EnumerateObject())
            props.Add(p.Name);

        props[0].Should().Be("role");
        props[1].Should().Be("content");
        msg.GetProperty("role").GetString().Should().Be("user");
        msg.GetProperty("content").GetString().Should().Be("hello");
    }

    /// <summary>
    /// When no <c>messages</c> property exists, the payload passes through unchanged.
    /// </summary>
    [Fact]
    public void ReconstructMessage_NoMessages_Passthrough()
    {
        var input = """{"model":"leankernel"}""";
        var result = IEndpointRouteBuilderExtensions.ReconstructMessage(input);

        using var doc = JsonDocument.Parse(result);
        doc.RootElement.GetProperty("model").GetString().Should().Be("leankernel");
        doc.RootElement.TryGetProperty("messages", out _).Should().BeFalse();
    }

    /// <summary>
    /// Multiple messages in the array are each independently rewritten.
    /// </summary>
    [Fact]
    public void ReconstructMessage_MultipleMessages_EachRewritten()
    {
        var input = """{"messages":[{"content":"first","role":"user"},{"content":"second","role":"assistant"}]}""";
        var result = IEndpointRouteBuilderExtensions.ReconstructMessage(input);

        using var doc = JsonDocument.Parse(result);
        var messages = doc.RootElement.GetProperty("messages");

        messages.GetArrayLength().Should().Be(2);

        var props0 = messages[0].EnumerateObject().Select(p => p.Name).ToList();
        props0[0].Should().Be("role");
        messages[0].GetProperty("role").GetString().Should().Be("user");
        messages[0].GetProperty("content").GetString().Should().Be("first");

        var props1 = messages[1].EnumerateObject().Select(p => p.Name).ToList();
        props1[0].Should().Be("role");
        messages[1].GetProperty("role").GetString().Should().Be("assistant");
        messages[1].GetProperty("content").GetString().Should().Be("second");
    }

    /// <summary>
    /// Extra properties beyond <c>role</c> and <c>content</c> (e.g. <c>name</c>, <c>tool_calls</c>)
    /// are preserved in the rewritten message.
    /// </summary>
    [Fact]
    public void ReconstructMessage_ExtraProperties_Preserved()
    {
        var input = """{"messages":[{"role":"user","content":"hello","name":"John","tool_calls":[]}]}""";
        var result = IEndpointRouteBuilderExtensions.ReconstructMessage(input);

        using var doc = JsonDocument.Parse(result);
        var msg = doc.RootElement.GetProperty("messages")[0];

        msg.GetProperty("role").GetString().Should().Be("user");
        msg.GetProperty("content").GetString().Should().Be("hello");
        msg.GetProperty("name").GetString().Should().Be("John");
        msg.TryGetProperty("tool_calls", out _).Should().BeTrue();
    }

    /// <summary>
    /// When a message omits <c>content</c>, the output defaults it to an empty string.
    /// </summary>
    [Fact]
    public void ReconstructMessage_MissingContent_DefaultsEmpty()
    {
        var input = """{"messages":[{"role":"user"}]}""";
        var result = IEndpointRouteBuilderExtensions.ReconstructMessage(input);

        using var doc = JsonDocument.Parse(result);
        var msg = doc.RootElement.GetProperty("messages")[0];

        msg.GetProperty("role").GetString().Should().Be("user");
        msg.GetProperty("content").GetString().Should().Be("");
    }

    /// <summary>
    /// When a message omits <c>role</c>, the output defaults it to <c>"user"</c>.
    /// </summary>
    [Fact]
    public void ReconstructMessage_MissingRole_DefaultsUser()
    {
        var input = """{"messages":[{"content":"hello"}]}""";
        var result = IEndpointRouteBuilderExtensions.ReconstructMessage(input);

        using var doc = JsonDocument.Parse(result);
        var msg = doc.RootElement.GetProperty("messages")[0];

        msg.GetProperty("role").GetString().Should().Be("user");
        msg.GetProperty("content").GetString().Should().Be("hello");
    }

    /// <summary>
    /// When <c>content</c> is explicitly <c>null</c>, the output defaults it to an empty string.
    /// </summary>
    [Fact]
    public void ReconstructMessage_NullContent_DefaultsEmpty()
    {
        var input = """{"messages":[{"role":"user","content":null}]}""";
        var result = IEndpointRouteBuilderExtensions.ReconstructMessage(input);

        using var doc = JsonDocument.Parse(result);
        var msg = doc.RootElement.GetProperty("messages")[0];

        msg.GetProperty("role").GetString().Should().Be("user");
        msg.GetProperty("content").GetString().Should().Be("");
    }

    /// <summary>
    /// An empty <c>messages</c> array produces an empty array in the output.
    /// </summary>
    [Fact]
    public void ReconstructMessage_EmptyMessagesArray_EmptyArray()
    {
        var input = """{"messages":[]}""";
        var result = IEndpointRouteBuilderExtensions.ReconstructMessage(input);

        using var doc = JsonDocument.Parse(result);
        doc.RootElement.GetProperty("messages").GetArrayLength().Should().Be(0);
    }

    /// <summary>
    /// Top-level properties outside <c>messages</c> (e.g. <c>model</c>, <c>temperature</c>, <c>stream</c>)
    /// are preserved unchanged.
    /// </summary>
    [Fact]
    public void ReconstructMessage_TopLevelPropertiesPreserved()
    {
        var input = """{"model":"test","messages":[{"role":"user","content":"hi"}],"temperature":0.7,"stream":true}""";
        var result = IEndpointRouteBuilderExtensions.ReconstructMessage(input);

        using var doc = JsonDocument.Parse(result);
        doc.RootElement.GetProperty("model").GetString().Should().Be("test");
        doc.RootElement.GetProperty("temperature").GetDouble().Should().Be(0.7);
        doc.RootElement.GetProperty("stream").GetBoolean().Should().BeTrue();
        doc.RootElement.GetProperty("messages").GetArrayLength().Should().Be(1);
    }

    /// <summary>
    /// An empty request body returns <see cref="BadRequest{T}"/>.
    /// </summary>
    [Fact]
    public async Task HandleChatCompletionsRequestAsync_EmptyPayload_ReturnsBadRequest()
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Body = new MemoryStream();

        var result = await IEndpointRouteBuilderExtensions.HandleChatCompletionsRequestAsync(
            "/v1/internal/completions", ctx, Mock.Of<IHttpClientFactory>(), Mock.Of<ILogger<HttpContext>>());

        result.Should().BeOfType<BadRequest<string>>();
    }

    /// <summary>
    /// Malformed JSON returns <see cref="BadRequest{T}"/>.
    /// </summary>
    [Fact]
    public async Task HandleChatCompletionsRequestAsync_InvalidJson_ReturnsBadRequest()
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("{"));

        var result = await IEndpointRouteBuilderExtensions.HandleChatCompletionsRequestAsync(
            "/v1/internal/completions", ctx, Mock.Of<IHttpClientFactory>(), Mock.Of<ILogger<HttpContext>>());

        result.Should().BeOfType<BadRequest<string>>();
    }
}