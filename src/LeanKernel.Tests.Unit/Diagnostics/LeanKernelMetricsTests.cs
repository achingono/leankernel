using FluentAssertions;
using LeanKernel.Abstractions.Models;
using LeanKernel.Diagnostics;

namespace LeanKernel.Tests.Unit.Diagnostics;

public class LeanKernelMetricsTests
{
    [Fact]
    public void Metrics_recording_methods_can_be_called_without_throwing()
    {
        using var metrics = new LeanKernelMetrics();

        var act = () =>
        {
            metrics.RecordTurnProcessed("gpt-4o-mini");
            metrics.RecordTokensUsed(42, "gpt-4o-mini");
            metrics.RecordTurnLatency(123.4);
            metrics.RecordQualityGateFailure("policy");
            metrics.RecordEscalation("gpt-4o-mini", "gpt-4.1");
            metrics.RecordBudgetUtilization(0.75);
            metrics.RecordRequestTotal("/api/chat", "POST");
            metrics.RecordRequestDuration("/api/chat", "POST", 200, 45.6);
            metrics.RecordRequestError("/api/chat", "POST", 500);
            metrics.RecordRateLimitRejected("ip:127.0.0.1");
            metrics.SetSpendTotals(1.23m, 45.67m);
            metrics.SetProviderHealth(new Dictionary<string, ProviderHealthStatus>(StringComparer.OrdinalIgnoreCase)
            {
                [ProviderNames.Database] = new ProviderHealthStatus
                {
                    ProviderName = ProviderNames.Database,
                    Description = "ok",
                    LastCheckedAt = DateTimeOffset.UtcNow,
                }
            });
        };

        act.Should().NotThrow();
    }
}
