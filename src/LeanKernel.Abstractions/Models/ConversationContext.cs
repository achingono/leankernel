namespace LeanKernel.Abstractions.Models;

public sealed class ConversationContext
{
    public required string SystemPrompt { get; init; }
    public string? SessionId { get; init; }
    public IReadOnlyList<ConversationTurn> History { get; init; } = [];
    public IReadOnlyList<RetrievalCandidate> WikiFacts { get; init; } = [];
    public IReadOnlyList<RetrievalCandidate> RetrievedKnowledge { get; init; } = [];
    public IdentityContext? Identity { get; init; }
    public OnboardingResult? Onboarding { get; init; }
    public IReadOnlyList<string> ActiveToolNames { get; init; } = [];
    public ContextBudgetUsage? BudgetUsage { get; init; }
    public IReadOnlyList<ContextAdmissionRecord> AdmissionLog { get; init; } = [];
    public HistoryShapingDiagnostics? HistoryDiagnostics { get; init; }
    public RetrievalDiagnostics? RetrievalDiagnostics { get; init; }
}
