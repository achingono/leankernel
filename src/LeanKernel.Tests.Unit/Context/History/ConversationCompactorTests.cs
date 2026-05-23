using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using LeanKernel.Abstractions.Configuration;
using LeanKernel.Abstractions.Models;
using LeanKernel.Context.History;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace LeanKernel.Tests.Unit.Context.History;

public class ConversationCompactorTests
{
    [Fact]
    public async Task CompactAsync_posts_expected_chat_completion_request()
    {
        var handler = new RecordingHttpMessageHandler((_, _) => CreateJsonResponse(new
        {
            choices = new[]
            {
                new
                {
                    message = new
                    {
                        content = "Key facts"
                    }
                }
            }
        }));
        var compactor = CreateCompactor(handler);

        var result = await compactor.CompactAsync(
        [
            new ConversationTurn { Role = "user", Content = "Need status", Timestamp = DateTimeOffset.Parse("2025-05-20T10:00:00Z") },
            new ConversationTurn { Role = "assistant", Content = "Atlas shipped", Timestamp = DateTimeOffset.Parse("2025-05-20T10:01:00Z") },
        ]);

        result.Should().Be("Key facts");
        handler.Requests.Should().ContainSingle();
        handler.Requests[0].Headers.Authorization!.Scheme.Should().Be("Bearer");
        handler.Requests[0].Headers.Authorization.Parameter.Should().Be("test-key");

        using var document = JsonDocument.Parse(handler.RequestBodies.Single());
        document.RootElement.GetProperty("model").GetString().Should().Be("gpt-4o-mini");
        document.RootElement.GetProperty("temperature").GetDouble().Should().Be(0.1d);
        document.RootElement.GetProperty("max_tokens").GetInt32().Should().Be(200);
        document.RootElement.GetProperty("messages")[0].GetProperty("content").GetString().Should().Contain("Extract the key facts");
        document.RootElement.GetProperty("messages")[1].GetProperty("content").GetString().Should().Contain("Need status");
    }

    [Fact]
    public async Task SummarizeAsync_throws_when_litellm_returns_empty_content()
    {
        var handler = new RecordingHttpMessageHandler((_, _) => CreateJsonResponse(new
        {
            choices = new[]
            {
                new
                {
                    message = new
                    {
                        content = ""
                    }
                }
            }
        }));
        var compactor = CreateCompactor(handler);

        var act = () => compactor.SummarizeAsync(
        [
            new ConversationTurn { Role = "user", Content = "Need status", Timestamp = DateTimeOffset.Parse("2025-05-20T10:00:00Z") }
        ]);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*no compaction content*");
    }

    private static ConversationCompactor CreateCompactor(HttpMessageHandler handler)
        => new(
            new HttpClient(handler) { BaseAddress = new Uri("http://localhost") },
            Options.Create(new LiteLlmConfig
            {
                BaseUrl = "http://localhost",
                ApiKey = "test-key",
                DefaultModel = "gpt-4o-mini"
            }),
            Options.Create(new HistoryConfig
            {
                CompactionModel = "gpt-4o-mini",
                CompactionTemperature = 0.1,
                MaxSummaryTokens = 200
            }),
            NullLogger<ConversationCompactor>.Instance);

    private static HttpResponseMessage CreateJsonResponse(object payload)
        => new(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(payload)
        };

    private sealed class RecordingHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> _handler;

        public RecordingHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> handler)
        {
            _handler = handler;
        }

        public List<HttpRequestMessage> Requests { get; } = [];

        public List<string> RequestBodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            RequestBodies.Add(request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync().ConfigureAwait(false));
            return _handler(request, cancellationToken);
        }
    }
}
