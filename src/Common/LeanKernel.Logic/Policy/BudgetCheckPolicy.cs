namespace LeanKernel.Logic.Policy;

/// <summary>
/// Ensures authenticated identity is present for budget-sensitive operations.
/// </summary>
public sealed class BudgetCheckPolicy : IPolicy<object>
{
    /// <inheritdoc />
    public string Name => "BudgetCheck";

    /// <inheritdoc />
    public PolicyResult Evaluate(object entity, IPolicyContext context)
    {
        if (!context.Identity.IsAuthenticated)
        {
            return PolicyResult.Deny("Budget policies require authenticated identity.");
        }

        return PolicyResult.Allow();
    }
}