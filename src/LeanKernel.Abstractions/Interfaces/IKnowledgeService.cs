using LeanKernel.Abstractions.Models;

namespace LeanKernel.Abstractions.Interfaces;

public interface IKnowledgeService
{
    Task<IReadOnlyList<RetrievalCandidate>> SearchAsync(string query, int maxResults = 10, CancellationToken ct = default);
    Task<KnowledgePage?> GetPageAsync(string key, CancellationToken ct = default);
    Task PutPageAsync(string key, string content, CancellationToken ct = default);
}
