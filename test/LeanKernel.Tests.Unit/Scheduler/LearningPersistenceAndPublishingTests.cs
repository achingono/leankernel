using System.Net;
using System.Net.Http;
using System.Text;

using FluentAssertions;

using LeanKernel.Logic.Providers;
using LeanKernel.Services.Common.Contracts;
using LeanKernel.Services.Common.Publishing;
using LeanKernel.Services.Learning.Learning;

using Moq;

using Xunit;

namespace LeanKernel.Tests.Unit.Scheduler;

public sealed class LearningPersistenceAndPublishingTests
{
    [Fact]
    public async Task KnowledgePageUpdateCoordinator_WritesFactWithScopeRelativeKey()
    {
        var memoryClient = new Mock<IMemoryClient>();
        var coordinator = new KnowledgePageUpdateCoordinator(memoryClient.Object);
        var turn = CreateTurnEvent("turn-fact");

        await coordinator.WriteFactAsync(turn, "Ada likes tea", CancellationToken.None);

        memoryClient.Verify(
            client => client.SaveMemoryAsync(
                It.Is<MemoryScope>(scope =>
                    scope.TenantId == turn.TenantId
                    && scope.PersonId == turn.PersonId
                    && scope.ChannelId == turn.ChannelId),
                It.Is<string>(key => key.StartsWith("facts/what/learned/turn-fact/", StringComparison.Ordinal)),
                It.Is<string>(content => content.Contains("Ada likes tea", StringComparison.Ordinal)),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task MemoryOnboardingDirectivePublisher_WritesDirectivePage()
    {
        var memoryClient = new Mock<IMemoryClient>();
        var publisher = new MemoryOnboardingDirectivePublisher(memoryClient.Object);
        var turn = CreateTurnEvent("turn-directive");

        await publisher.PublishAsync(turn, "Ask for email.", CancellationToken.None);

        memoryClient.Verify(
            client => client.SaveMemoryAsync(
                It.IsAny<MemoryScope>(),
                It.Is<string>(key => key.StartsWith("onboarding/directives/turn-directive/", StringComparison.Ordinal)),
                It.Is<string>(content => content.Contains("Ask for email.", StringComparison.Ordinal)),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task LearningEventPublisher_PostsTurnEventToInternalRoute()
    {
        var handler = new CaptureHandler();
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost:5168")
        };

        var publisher = new LearningEventPublisher(httpClient);
        var turn = CreateTurnEvent("turn-publish");

        await publisher.PublishAsync(turn, CancellationToken.None);

        handler.LastRequest.Should().NotBeNull();
        handler.LastRequest!.Method.Should().Be(HttpMethod.Post);
        handler.LastRequest.RequestUri!.AbsolutePath.Should().Be("/internal/learning/turn-events");
    }

    private static CompletedTurnEvent CreateTurnEvent(string turnId)
    {
        return new CompletedTurnEvent(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "session-learning",
            turnId,
            DateTimeOffset.UtcNow,
            [new TurnMessage("user", "hello")],
            [new TurnMessage("assistant", "world")]);
    }

    private sealed class CaptureHandler : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Accepted)
            {
                Content = new StringContent(string.Empty, Encoding.UTF8, "application/json")
            });
        }
    }
}
