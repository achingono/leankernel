namespace LeanKernel.Channels.Signal;

/// <summary>
/// Represents an opened markdown style delimiter with its position in the output text.
/// </summary>
/// <param name="Delimiter">The markdown delimiter string (e.g., "**", "*", "`").</param>
/// <param name="Style">The text style name corresponding to the delimiter.</param>
/// <param name="Start">The starting position of the style in the output text.</param>
public sealed record OpenStyle(string Delimiter, string Style, int Start);
