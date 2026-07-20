namespace LeanKernel.Logic.Configuration;

/// <summary>
/// Filesystem tool configuration nested under <c>Agents:Tools:FileSystem</c>.
/// </summary>
public sealed class FileSystemToolSettings
{
    /// <summary>
    /// Gets or sets a value indicating whether filesystem tools are enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;
}
