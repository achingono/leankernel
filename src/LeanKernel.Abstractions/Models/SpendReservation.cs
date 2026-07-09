namespace LeanKernel.Abstractions.Models;

/// <summary>
/// Represents a reserved spend budget for one in-flight execution.
/// </summary>
public sealed record SpendReservation
{
    /// <summary>
    /// Gets the reservation identifier.
    /// </summary>
    public required Guid ReservationId { get; init; }

    /// <summary>
    /// Gets the session identifier.
    /// </summary>
    public required string SessionId { get; init; }

    /// <summary>
    /// Gets the turn identifier.
    /// </summary>
    public required string TurnId { get; init; }

    /// <summary>
    /// Gets the reserved amount in USD.
    /// </summary>
    public decimal ReservedAmountUsd { get; init; }
}
