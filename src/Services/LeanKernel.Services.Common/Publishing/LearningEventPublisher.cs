using System.Net.Http.Json;

using LeanKernel.Services.Common.Contracts;

namespace LeanKernel.Services.Common.Publishing;

/// <summary>
/// HTTP implementation of <see cref="ILearningEventPublisher"/>.
/// </summary>
/// <param name="httpClient">Client configured for the learning service endpoint.</param>
public sealed class LearningEventPublisher(HttpClient httpClient) : ILearningEventPublisher
{
    /// <inheritdoc />
    public async Task PublishAsync(CompletedTurnEvent completedTurn, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(completedTurn);

        using var response = await httpClient.PostAsJsonAsync(
            LearningServiceRoutes.TurnEventsPath,
            completedTurn,
            cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }
}
