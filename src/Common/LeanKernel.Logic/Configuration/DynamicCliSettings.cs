namespace LeanKernel.Logic.Configuration;

/// <summary>
/// Global settings for dynamic CLI skill tools, nested under <c>Agents:Tools:DynamicCli</c>.
/// </summary>
public sealed class DynamicCliSettings
{
    /// <summary>
    /// Gets or sets the maximum number of characters of tool output returned to the model.
    /// Output is truncated at this length. Defaults to 12 KB.
    /// </summary>
    public int MaxOutputChars { get; set; } = 12_000;
}