namespace LeanKernel.Thinker.Enhancement;

/// <summary>
/// Classifies natural-language user feedback into engagement-file update intent.
/// </summary>
public interface IEngagementIntentClassifier
{
    /// <summary>
    /// Determines whether a user message implies an engagement identity update.
    /// </summary>
    Task<EngagementIntentClassification> ClassifyAsync(string userMessage, CancellationToken ct);
}
