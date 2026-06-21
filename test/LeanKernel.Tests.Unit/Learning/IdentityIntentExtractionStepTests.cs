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

public class IdentityIntentExtractionStepTests
{
    [Fact]
    public async Task ProcessAsync_persists_behavior_intent_update_to_identity_page()
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
                            content = "{\"hasBehaviorIntent\":true,\"updates\":[{\"field\":\"autonomy_level\",\"value\":\"be proactive and only ask for destructive changes\",\"confidence\":0.91,\"reason\":\"explicit user preference\"}]}"
                        }
                    }
                }
            })
        });

        var step = CreateStep(handler, new RecordingKnowledgeService(), new LeanKernelConfig
        {
            LiteLlm = new LiteLlmConfig
            {
                BaseUrl = "http://localhost",
                DefaultModel = "small",
            },
            Learning = new LearningConfig
            {
                IntentExtractionEnabled = true,
                IntentExtractionModel = "small",
                IntentExtractionMinConfidence = 0.72,
            },
            Identity = new IdentityConfig(),
        });

        var result = await step.ProcessAsync(CreateTurnEvent("Please be proactive and only ask before destructive actions."));

        result.Success.Should().BeTrue();
        result.ItemsLearned.Should().Be(1);
        result.LearnedFacts.Should().Contain("autonomy_level");

        using var requestDoc = JsonDocument.Parse(handler.RequestBodies.Single());
        requestDoc.RootElement.GetProperty("model").GetString().Should().Be("small");
    }

    [Fact]
    public async Task ProcessAsync_does_not_override_higher_confidence_existing_field()
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
                            content = "{\"hasBehaviorIntent\":true,\"updates\":[{\"field\":\"autonomy_level\",\"value\":\"high autonomy\",\"confidence\":0.75,\"reason\":\"explicit\"}]}"
                        }
                    }
                }
            })
        });

        var knowledge = new RecordingKnowledgeService();
        knowledge.Pages["identity-user-default"] =
            "---\nautonomy_level:\n  value: 'high'\n  confidence: 0.95\n  last_updated: '2026-06-20T10:00:00.0000000+00:00'\n  source: 'manual'\n---\n";

        var step = CreateStep(handler, knowledge, new LeanKernelConfig
        {
            LiteLlm = new LiteLlmConfig
            {
                BaseUrl = "http://localhost",
                DefaultModel = "small",
            },
            Learning = new LearningConfig
            {
                IntentExtractionEnabled = true,
                IntentExtractionMinConfidence = 0.72,
            },
            Identity = new IdentityConfig(),
        });

        var result = await step.ProcessAsync(CreateTurnEvent("Take more initiative."));

        result.ItemsLearned.Should().Be(0);
        knowledge.PutCalls.Should().Be(0);
    }

    private static IdentityIntentExtractionStep CreateStep(
        RecordingHttpMessageHandler handler,
        RecordingKnowledgeService knowledge,
        LeanKernelConfig config)
    {
        var clientFactory = new TestHttpClientFactory(new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost/")
        });

        return new IdentityIntentExtractionStep(
            clientFactory,
            knowledge,
            new KnowledgePageUpdateCoordinator(),
            Options.Create(config),
            NullLogger<IdentityIntentExtractionStep>.Instance);
    }

    private static TurnEvent CreateTurnEvent(string userMessage)
        => new()
        {
            SessionId = "session-1",
            TurnId = "turn-1",
            Role = "assistant",
            Content = "Got it.",
            UserMessage = userMessage,
            AssistantResponse = "Got it.",
        };

    private sealed class TestHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class RecordingKnowledgeService : IKnowledgeService
    {
        public Dictionary<string, string> Pages { get; } = new(StringComparer.OrdinalIgnoreCase);

        public int PutCalls { get; private set; }

        public Task<IReadOnlyList<RetrievalCandidate>> SearchAsync(string query, int maxResults = 10, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<RetrievalCandidate>>([]);

        public Task<KnowledgePage?> GetPageAsync(string key, CancellationToken ct = default)
            => Task.FromResult<KnowledgePage?>(Pages.TryGetValue(key, out var content)
                ? new KnowledgePage { Key = key, Content = content }
                : null);

        public Task PutPageAsync(string key, string content, CancellationToken ct = default)
        {
            PutCalls++;
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
