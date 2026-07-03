using FluentAssertions;
using LeanKernel.Abstractions.Interfaces;
using LeanKernel.Agents;

namespace LeanKernel.Tests.Unit.Agents;

public class SessionTurnCoordinatorTests
{
    [Fact]
    public async Task BeginTurnAsync_serializes_execution_per_session()
    {
        ISessionTurnCoordinator coordinator = new SessionTurnCoordinator();

        await using var firstLease = await coordinator.BeginTurnAsync("session-1");
        var secondLeaseTask = coordinator.BeginTurnAsync("session-1").AsTask();

        secondLeaseTask.IsCompleted.Should().BeFalse();

        await firstLease.DisposeAsync();
        var secondLease = await secondLeaseTask;
        await secondLease.DisposeAsync();
    }

    [Fact]
    public async Task NotifyInbound_sets_preemption_on_active_lease()
    {
        ISessionTurnCoordinator coordinator = new SessionTurnCoordinator();

        await using var lease = await coordinator.BeginTurnAsync("session-1");
        lease.PreemptionRequested.Should().BeFalse();

        coordinator.NotifyInbound("session-1");

        lease.PreemptionRequested.Should().BeTrue();
    }
}
