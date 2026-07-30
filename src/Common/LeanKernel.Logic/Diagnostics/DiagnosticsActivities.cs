namespace LeanKernel.Logic.Diagnostics;

using System.Diagnostics;

/// <summary>
/// Provides shared OpenTelemetry activity sources for diagnostics instrumentation.
/// </summary>
public static class DiagnosticsActivities
{
    /// <summary>
    /// Gets the OpenTelemetry activity source used for diagnostics operations.
    /// </summary>
    public static readonly ActivitySource Source = new("LeanKernel.Diagnostics");
}