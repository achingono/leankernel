namespace LeanKernel.Logic.Configuration;

/// <summary>
/// Configures file storage settings for the gateway and filesystem tools.
/// </summary>
public class FileSettings
{
    /// <summary>
    /// Gets or sets the root path used for gateway-managed files and filesystem tool boundary.
    /// </summary>
    public string RootPath { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the root directory used for temporary or scratch file operations.
    /// </summary>
    public string ScratchRoot { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the maximum size in bytes for a single file download.
    /// </summary>
    public long MaxDownloadBytes { get; set; } = 10_000_000;

    /// <summary>
    /// Gets or sets the maximum number of characters to extract from a file.
    /// </summary>
    public int MaxExtractedCharacters { get; set; } = 20_000;

    /// <summary>
    /// Gets or sets the name or path of the Python executable used for advanced file processing.
    /// </summary>
    public string PythonExecutable { get; set; } = "python3";
}