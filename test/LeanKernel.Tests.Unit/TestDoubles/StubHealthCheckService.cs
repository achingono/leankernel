namespace LeanKernel.Tests.Unit.TestDoubles;

using Microsoft.Extensions.Diagnostics.HealthChecks;

public sealed class StubHealthCheckService : HealthCheckService
{
    private readonly HealthReport _report;

    public StubHealthCheckService(HealthReport report)
    {
        _report = report;
    }

    public override Task<HealthReport> CheckHealthAsync(
        Func<HealthCheckRegistration, bool>? predicate,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_report);
    }
}