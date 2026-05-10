using LeanKernel.Core.Models;

namespace LeanKernel.Core.Interfaces;

/// <summary>
/// Provides active-hour and quiet-hour status for agent actions.
/// </summary>
public interface ITimeBoundaryService
{
    /// <summary>
    /// Gets whether the current time is inside the configured active window.
    /// </summary>
    /// <returns><see langword="true" /> when actions may run immediately; otherwise <see langword="false" />.</returns>
    bool IsInActiveHours();

    /// <summary>
    /// Gets the next active window start time.
    /// </summary>
    /// <returns>The next active window start time in UTC.</returns>
    DateTime GetNextActiveWindow();

    /// <summary>
    /// Gets the current time-boundary diagnostic status.
    /// </summary>
    /// <returns>The current time-boundary status.</returns>
    TimeBoundaryStatus GetStatus();
}
