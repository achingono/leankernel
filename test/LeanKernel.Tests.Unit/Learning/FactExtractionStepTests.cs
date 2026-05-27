using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using LeanKernel.Abstractions.Configuration;
using LeanKernel.Abstractions.Interfaces;
using LeanKernel.Abstractions.Models;
using LeanKernel.Learning;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace LeanKernel.Tests.Unit.Learning;

public class FactExtractionStepTests
{
    [Fact]
    public async Task ProcessAsync_extracts_facts_and_persists_them()
    {
        var handler = new RecordingHttpMessageHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new
            {
                choices = new[]
                {
                    new
                    {
                        message = new
                        {
                            content = "[\"Atlas shipped diagnostics improvements\",\"Atlas uses pgvector\"]"
                        }
                    }
                }
            })
        });
        var httpClientFactory = new TestHttpClientFactory(new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost/")
        });
        var knowledgeService = new RecordingKnowledgeService();
        var step = new FactExtractionStep(
            httpClientFactory,
            knowledgeService,
            Options.Create(new LeanKernelConfig
            {
                LiteLlm = new LiteLlmConfig
                {
                    BaseUrl = "http://localhost",
                    DefaultModel = "gpt-4o-mini"
                },
                Learning = new LearningConfig
                {
                    ExtractionModel = "gpt-4o-mini",
                    ExtractionTemperature = 0.1,
                    MinTurnLengthForExtraction = 10
                }
            }),
            NullLogger<FactExtractionStep>.Instance);

        var result = await step.ProcessAsync(CreateTurnEvent());

        result.Success.Should().BeTrue();
        result.ItemsLearned.Should().Be(2);
        knowledgeService.Pages.Keys.Should().Contain(new[]
        {
            "learning/facts/session-1/turn-1/01",
            "learning/facts/session-1/turn-1/02"
        });
        knowledgeService.Pages.Values.Should().Contain(value => value.Contains("Atlas shipped diagnostics improvements", StringComparison.Ordinal));

        using var document = JsonDocument.Parse(handler.RequestBodies.Single());
        document.RootElement.GetProperty("model").GetString().Should().Be("gpt-4o-mini");
        document.RootElement.GetProperty("temperature").GetDouble().Should().Be(0.1);
        document.RootElement.GetProperty("messages")[0].GetProperty("content").GetString().Should().Contain("Extract any new factual information");
    }

    [Fact]
    public async Task ProcessAsync_skips_short_turns()
    {
        var httpClientFactory = new TestHttpClientFactory(new HttpClient(new RecordingHttpMessageHandler((_, _) => throw new InvalidOperationException("should not be called")))
        {
            BaseAddress = new Uri("http://localhost/")
        });
        var knowledgeService = new RecordingKnowledgeService();
        var step = new FactExtractionStep(
            httpClientFactory,
            knowledgeService,
            Options.Create(new LeanKernelConfig
            {
                LiteLlm = new LiteLlmConfig(),
                Learning = new LearningConfig
                {
                    MinTurnLengthForExtraction = 500
                }
            }),
            NullLogger<FactExtractionStep>.Instance);

        var result = await step.ProcessAsync(CreateTurnEvent());

        result.ItemsLearned.Should().Be(0);
        knowledgeService.Pages.Should().BeEmpty();
    }

    private static TurnEvent CreateTurnEvent()
        => new()
        {
            SessionId = "session-1",
            TurnId = "turn-1",
            Role = "assistant",
            Content = "Atlas shipped diagnostics improvements and now uses pgvector for storage.",
            UserMessage = "What changed in Atlas storage?",
            AssistantResponse = "Atlas shipped diagnostics improvements and now uses pgvector for storage.",
        };

    private sealed class TestHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class RecordingKnowledgeService : IKnowledgeService
    {
        public Dictionary<string, string> Pages { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Task<IReadOnlyList<RetrievalCandidate>> SearchAsync(string query, int maxResults = 10, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<RetrievalCandidate>>([]);

        public Task<KnowledgePage?> GetPageAsync(string key, CancellationToken ct = default)
            => Task.FromResult<KnowledgePage?>(Pages.TryGetValue(key, out var content)
                ? new KnowledgePage { Key = key, Content = content }
                : null);

        public Task PutPageAsync(string key, string content, CancellationToken ct = default)
        {
            Pages[key] = content;
            return Task.CompletedTask;
        }

        public Task DeletePageAsync(string key, CancellationToken ct = default)
        {
            Pages.Remove(key);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> _handler;

        public RecordingHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> handler)
        {
            _handler = handler;
        }

        public List<string> RequestBodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestBodies.Add(request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync().ConfigureAwait(false));
            return _handler(request, cancellationToken);
        }
    }
}
