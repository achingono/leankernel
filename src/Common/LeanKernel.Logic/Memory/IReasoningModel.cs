namespace LeanKernel.Logic.Memory;

public interface IReasoningModel
{
    bool Enabled { get; }

    Task<string?> CompleteAsync(
        string systemPrompt,
        string userPrompt,
        int maxOutputTokens,
        CancellationToken cancellationToken = default);
}
