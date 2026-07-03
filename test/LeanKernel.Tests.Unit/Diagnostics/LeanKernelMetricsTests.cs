using FluentAssertions;
using System.Diagnostics.Metrics;
using LeanKernel.Abstractions.Enums;
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
            metrics.RecordContinuationRound();
            metrics.RecordContinuationTermination("max_rounds");
            metrics.RecordProgressMessageSent("tool_started");
            metrics.RecordTypingRefresh("signal");
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

    [Fact]
    public void Metrics_observable_gauges_report_spend_and_provider_health_values()
    {
        var spendValues = new List<double>();
        var providerValues = new List<int>();

        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == "LeanKernel")
            {
                meterListener.EnableMeasurementEvents(instrument);
            }
        };

        listener.SetMeasurementEventCallback<double>((instrument, measurement, tags, state) =>
        {
            if (instrument.Name == "leankernel.spend.total_usd")
            {
                spendValues.Add(measurement);
            }
        });

        listener.SetMeasurementEventCallback<int>((instrument, measurement, tags, state) =>
        {
            if (instrument.Name == "leankernel.providers.health")
            {
                providerValues.Add(measurement);
            }
        });

        listener.Start();

        using var metrics = new LeanKernelMetrics();
        metrics.SetSpendTotals(1.23m, 4.56m);
        metrics.SetProviderHealth(new Dictionary<string, ProviderHealthStatus>(StringComparer.OrdinalIgnoreCase)
        {
            [ProviderNames.Database] = new ProviderHealthStatus
            {
                ProviderName = ProviderNames.Database,
                State = ProviderHealthState.Healthy,
                Description = "ok",
            },
            [ProviderNames.LiteLlm] = new ProviderHealthStatus
            {
                ProviderName = ProviderNames.LiteLlm,
                State = ProviderHealthState.Unhealthy,
                Description = "down",
            }
        });

        listener.RecordObservableInstruments();

        spendValues.Should().ContainEquivalentOf(1.23, options => options.Using<double>(ctx => ctx.Subject.Should().BeApproximately(ctx.Expectation, 0.0001)).WhenTypeIs<double>());
        spendValues.Should().ContainEquivalentOf(4.56, options => options.Using<double>(ctx => ctx.Subject.Should().BeApproximately(ctx.Expectation, 0.0001)).WhenTypeIs<double>());
        providerValues.Should().Contain(1);
        providerValues.Should().Contain(0);
    }
}
