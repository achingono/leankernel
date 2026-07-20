namespace LeanKernel.Channels.Signal;

/// <summary>
/// Represents a text style applied to a range of characters in a message.
/// </summary>
public sealed class SignalTextStyle
{
    /// <summary>
    /// Gets or sets the starting position of the styled range.
    /// </summary>
    public int Start { get; set; }

    /// <summary>
    /// Gets or sets the length of the styled range.
    /// </summary>
    public int Length { get; set; }

    /// <summary>
    /// Gets or sets the style name (e.g., "BOLD", "ITALIC", "MONOSPACE").
    /// </summary>
    public string Style { get; set; } = string.Empty;
}