using LeanKernel;

namespace LeanKernel.Logic.Providers;

/// <summary>
/// Parameter object derived from job identity fields and used as input
/// to <see cref="IDocumentStoreClient"/> methods. Carries all identity
/// dimensions and the availability scope.
/// </summary>
/// <param name="TenantId">The tenant identifier.</param>
/// <param name="UserId">The user identifier.</param>
/// <param name="PersonId">The person identifier.</param>
/// <param name="ChannelId">The channel identifier.</param>
/// <param name="AvailabilityScope">The document availability scope.</param>
public sealed record DocumentScopeContext(
    Guid TenantId,
    Guid UserId,
    Guid PersonId,
    Guid ChannelId,
    DocumentAvailabilityScope AvailabilityScope);
