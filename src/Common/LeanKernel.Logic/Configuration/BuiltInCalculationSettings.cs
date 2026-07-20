namespace LeanKernel.Logic.Configuration;

/// <summary>
/// Configuration for built-in calculation and aggregation helpers,
/// nested under <c>Agents:Tools:BuiltIns:Calculation</c>.
/// </summary>
public sealed class BuiltInCalculationSettings
{
    /// <summary>
    /// Gets or sets the calculation/aggregation helper configuration.
    /// </summary>
    public CalculationSettings Calculation { get; set; } = new();
}
