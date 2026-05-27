namespace LeanKernel.Abstractions.Configuration;

public sealed class FileSystemConfig
{
    public string AllowedRoot { get; set; } = "/app/data";

    public string ScratchRoot { get; set; } = "/app/data/.scratch";

    public long MaxDownloadBytes { get; set; } = 10_000_000;

    public int MaxExtractedCharacters { get; set; } = 20_000;

    public string PythonExecutable { get; set; } = "python3";

    public string PaddleOcrModule { get; set; } = "paddleocr";

    public string PdfToImageModule { get; set; } = "pdf2image";
}
