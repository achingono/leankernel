namespace LeanKernel.Logic.Configuration;

/// <summary>
/// Calculation/aggregation helper settings.
/// </summary>
public sealed class CalculationSettings
{
    /// <summary>
    /// Gets or sets a value indicating whether calculation/aggregation helpers are enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Gets or sets the upper bound for aggregate/group/count inputs.
    /// </summary>
    public int MaxInputItems { get; set; } = 1000;
}
