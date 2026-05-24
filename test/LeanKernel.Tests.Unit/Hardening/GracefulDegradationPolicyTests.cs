using FluentAssertions;
using LeanKernel.Abstractions.Enums;
using LeanKernel.Abstractions.Interfaces;
using LeanKernel.Abstractions.Models;
using LeanKernel.Agents.Resilience;
using Microsoft.Extensions.Logging.Abstractions;

namespace LeanKernel.Tests.Unit.Hardening;

public class GracefulDegradationPolicyTests
{
    [Fact]
    public void Evaluate_blocks_model_execution_when_litellm_is_unhealthy()
    {
        // Arrange
        var tracker = new StubProviderHealthTracker(new Dictionary<string, ProviderHealthStatus>(StringComparer.OrdinalIgnoreCase)
        {
            [ProviderNames.Database] = CreateStatus(ProviderNames.Database, ProviderHealthState.Healthy),
            [ProviderNames.GBrain] = CreateStatus(ProviderNames.GBrain, ProviderHealthState.Healthy),
            [ProviderNames.LiteLlm] = CreateStatus(ProviderNames.LiteLlm, ProviderHealthState.Unhealthy),
        });
        var policy = new GracefulDegradationPolicy(tracker, NullLogger<GracefulDegradationPolicy>.Instance);

        // Act
        var decision = policy.Evaluate();

        // Assert
        decision.AllowModelExecution.Should().BeFalse();
        decision.UserMessage.Should().Contain("model provider");
    }

    [Fact]
    public void Evaluate_marks_gbrain_and_database_as_degraded_without_throwing()
    {
        // Arrange
        var tracker = new StubProviderHealthTracker(new Dictionary<string, ProviderHealthStatus>(StringComparer.OrdinalIgnoreCase)
        {
            [ProviderNames.Database] = CreateStatus(ProviderNames.Database, ProviderHealthState.Unhealthy),
            [ProviderNames.GBrain] = CreateStatus(ProviderNames.GBrain, ProviderHealthState.Unhealthy),
            [ProviderNames.LiteLlm] = CreateStatus(ProviderNames.LiteLlm, ProviderHealthState.Healthy),
        });
        var policy = new GracefulDegradationPolicy(tracker, NullLogger<GracefulDegradationPolicy>.Instance);

        // Act
        var decision = policy.Evaluate();

        // Assert
        decision.AllowModelExecution.Should().BeTrue();
        decision.SkipKnowledgeRetrieval.Should().BeTrue();
        decision.PersistenceDegraded.Should().BeTrue();
        decision.Warnings.Should().HaveCount(2);
    }

    private static ProviderHealthStatus CreateStatus(string providerName, ProviderHealthState state)
        => new()
        {
            ProviderName = providerName,
            State = state,
            Description = state == ProviderHealthState.Healthy ? "healthy" : "unhealthy",
            LastCheckedAt = DateTimeOffset.UtcNow,
        };

    private sealed class StubProviderHealthTracker(IReadOnlyDictionary<string, ProviderHealthStatus> statuses) : IProviderHealthTracker
    {
        private readonly IReadOnlyDictionary<string, ProviderHealthStatus> _statuses = statuses;

        public ProviderHealthSnapshot GetSnapshot() => new() { Providers = _statuses };

        public ProviderHealthStatus GetStatus(string providerName) => _statuses[providerName];

        public void RecordProbeResult(string providerName, ProviderProbeResult result) => throw new NotSupportedException();

        public Task RefreshAsync(CancellationToken ct = default) => Task.CompletedTask;
    }
}
