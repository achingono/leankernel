using Serilog.Core;
using Serilog.Events;

namespace LeanKernel.Diagnostics;

/// <summary>
/// Serilog enricher that adds LeanKernel context to log events.
/// </summary>
public sealed class LeanKernelLogEnricher : ILogEventEnricher
{
    private readonly string _serviceName;

    public LeanKernelLogEnricher(string serviceName = "leankernel")
    {
        _serviceName = serviceName;
    }

    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("ServiceName", _serviceName));
    }
}
