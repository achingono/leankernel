namespace LeanKernel.Abstractions.Configuration;

/// <summary>
/// Configuration settings for the file system operations used by the kernel.
/// </summary>
public sealed class FileSystemConfig
{
    /// <summary>
    /// The root directory allowed for file operations.
    /// </summary>
    public string AllowedRoot { get; set; } = "/app/data";

    /// <summary>
    /// The root directory used for temporary or scratch file operations.
    /// </summary>
    public string ScratchRoot { get; set; } = "/app/data/.scratch";

    /// <summary>
    /// The maximum size in bytes for a single file download.
    /// </summary>
    public long MaxDownloadBytes { get; set; } = 10_000_000;

    /// <summary>
    /// The maximum number of characters to extract from a file.
    /// </summary>
    public int MaxExtractedCharacters { get; set; } = 20_000;

    /// <summary>
    /// The name or path of the Python executable used for advanced file processing.
    /// </summary>
    public string PythonExecutable { get; set; } = "python3";

    /// <summary>
    /// The module name used for PaddleOCR operations.
    /// </summary>
    public string PaddleOcrModule { get; set; } = "paddleocr";

    /// <summary>
    /// The module name used for converting PDF to image.
    /// </summary>
    public string PdfToImageModule { get; set; } = "pdf2image";
}
