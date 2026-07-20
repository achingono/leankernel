namespace LeanKernel.Services.Common;

/// <summary>
/// Shared route constants used by learning service producers and consumers.
/// </summary>
public static class LearningServiceRoutes
{
    /// <summary>
    /// Internal route that ingests completed turn events.
    /// </summary>
    public const string TurnEventsPath = "/internal/learning/turn-events";
}
