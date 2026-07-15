namespace LeanKernel.Logic.Memory;

public interface IMemoryCapabilityCheck
{
    /// <summary>
    /// Performs the Memory capability pre-check at startup per Appendix D.
    /// Probes for search, get_page, and put_page support and returns a structured result.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A <see cref="MemoryCapabilityResult"/> describing the outcome of the probe.</returns>
    Task<MemoryCapabilityResult> ProbeAsync(CancellationToken cancellationToken = default);
}