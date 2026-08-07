using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

using FluentAssertions;

using LeanKernel.Channels.Signal;
using LeanKernel.Channels.Signal.HealthChecks;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Xunit;

namespace LeanKernel.Tests.Unit.Signal;

public sealed class SocketWorkerHealthCheckTests
{
    private const string AccountA = "+10000000001";
    private const string AccountB = "+10000000002";

    [Fact]
    public async Task CheckHealth_ReturnsHealthy_WhenNoAccountsConfigured()
    {
        var provider = new FakeSocketWorkerHealthProvider();

        var result = await RunCheckAsync(provider);

        result.Status.Should().Be(HealthStatus.Healthy);
    }

    [Fact]
    public async Task CheckHealth_ReturnsHealthy_WhenWorkerProgressing()
    {
        var provider = new FakeSocketWorkerHealthProvider
        {
            WorkerStates = new Dictionary<string, SocketWorkerHealthState>
            {
                [AccountA] = CreateState(lastTick: DateTime.UtcNow.AddSeconds(-5))
            }
        };

        var result = await RunCheckAsync(provider);

        result.Status.Should().Be(HealthStatus.Healthy);
    }

    [Fact]
    public async Task CheckHealth_ReturnsDegraded_WhenWorkerStalledBeyondDegradedThreshold()
    {
        var provider = new FakeSocketWorkerHealthProvider
        {
            WorkerStates = new Dictionary<string, SocketWorkerHealthState>
            {
                [AccountA] = CreateState(lastTick: DateTime.UtcNow.AddSeconds(-70))
            }
        };

        var result = await RunCheckAsync(provider);

        result.Status.Should().Be(HealthStatus.Degraded);
    }

    [Fact]
    public async Task CheckHealth_ReturnsUnhealthy_WhenWorkerStalledBeyondUnhealthyThreshold()
    {
        var provider = new FakeSocketWorkerHealthProvider
        {
            WorkerStates = new Dictionary<string, SocketWorkerHealthState>
            {
                [AccountA] = CreateState(lastTick: DateTime.UtcNow.AddSeconds(-190))
            }
        };

        var result = await RunCheckAsync(provider);

        result.Status.Should().Be(HealthStatus.Unhealthy);
    }

    [Fact]
    public async Task CheckHealth_ReturnsUnhealthy_WhenWorkerIsFaulted()
    {
        var provider = new FakeSocketWorkerHealthProvider
        {
            WorkerStates = new Dictionary<string, SocketWorkerHealthState>
            {
                [AccountA] = CreateState(state: SocketWorkerState.Faulted, lastTick: DateTime.UtcNow)
            }
        };

        var result = await RunCheckAsync(provider);

        result.Status.Should().Be(HealthStatus.Unhealthy);
    }

    [Fact]
    public async Task CheckHealth_ReturnsUnhealthy_WhenConsecutiveErrorsReachUnhealthyThreshold()
    {
        var provider = new FakeSocketWorkerHealthProvider
        {
            WorkerStates = new Dictionary<string, SocketWorkerHealthState>
            {
                [AccountA] = CreateState(lastTick: DateTime.UtcNow, consecutiveErrors: 10)
            }
        };

        var result = await RunCheckAsync(provider);

        result.Status.Should().Be(HealthStatus.Unhealthy);
    }

    [Fact]
    public async Task CheckHealth_ReturnsDegraded_WhenConsecutiveErrorsReachDegradedThreshold()
    {
        var provider = new FakeSocketWorkerHealthProvider
        {
            WorkerStates = new Dictionary<string, SocketWorkerHealthState>
            {
                [AccountA] = CreateState(lastTick: DateTime.UtcNow, consecutiveErrors: 3)
            }
        };

        var result = await RunCheckAsync(provider);

        result.Status.Should().Be(HealthStatus.Degraded);
    }

    [Fact]
    public async Task CheckHealth_ReturnsHealthy_WhenStartingWorkerIsFresh()
    {
        var provider = new FakeSocketWorkerHealthProvider
        {
            WorkerStates = new Dictionary<string, SocketWorkerHealthState>
            {
                [AccountA] = CreateState(state: SocketWorkerState.Starting, startedUtc: DateTime.UtcNow)
            }
        };

        var result = await RunCheckAsync(provider);

        result.Status.Should().Be(HealthStatus.Healthy);
    }

    [Fact]
    public async Task CheckHealth_ReturnsHealthy_WhenStartingWorkerWithinStartupTimeout()
    {
        var provider = new FakeSocketWorkerHealthProvider
        {
            WorkerStates = new Dictionary<string, SocketWorkerHealthState>
            {
                [AccountA] = CreateState(state: SocketWorkerState.Starting, startedUtc: DateTime.UtcNow.AddSeconds(-30))
            }
        };

        var result = await RunCheckAsync(provider);

        result.Status.Should().Be(HealthStatus.Healthy);
    }

    [Fact]
    public async Task CheckHealth_ReturnsDegraded_WhenStartingWorkerExceedsStartupTimeout()
    {
        var provider = new FakeSocketWorkerHealthProvider
        {
            WorkerStates = new Dictionary<string, SocketWorkerHealthState>
            {
                [AccountA] = CreateState(state: SocketWorkerState.Starting, startedUtc: DateTime.UtcNow.AddSeconds(-70))
            }
        };

        var result = await RunCheckAsync(provider);

        result.Status.Should().Be(HealthStatus.Degraded);
    }

    [Fact]
    public async Task CheckHealth_ReturnsUnhealthy_WhenStartingWorkerExceedsUnhealthyTimeout()
    {
        var provider = new FakeSocketWorkerHealthProvider
        {
            WorkerStates = new Dictionary<string, SocketWorkerHealthState>
            {
                [AccountA] = CreateState(state: SocketWorkerState.Starting, startedUtc: DateTime.UtcNow.AddSeconds(-190))
            }
        };

        var result = await RunCheckAsync(provider);

        result.Status.Should().Be(HealthStatus.Unhealthy);
    }

    [Fact]
    public async Task CheckHealth_ReturnsUnhealthy_WhenManagerTaskCrashed()
    {
        var provider = new FakeSocketWorkerHealthProvider
        {
            IsManagerRunning = false,
            IsTransportStarted = true
        };

        var result = await RunCheckAsync(provider);

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Contain("manager");
    }

    [Fact]
    public async Task CheckHealth_ReturnsHealthy_WhenFeatureFlagDisabled()
    {
        var provider = new FakeSocketWorkerHealthProvider
        {
            IsManagerRunning = false,
            WorkerStates = new Dictionary<string, SocketWorkerHealthState>
            {
                [AccountA] = CreateState(state: SocketWorkerState.Faulted)
            }
        };

        var result = await RunCheckAsync(provider, new SignalSettings { EnableWorkerHealthCheck = false });

        result.Status.Should().Be(HealthStatus.Healthy);
    }

    [Fact]
    public async Task CheckHealth_ReturnsHealthy_WhenInitialDiscoveryInProgress()
    {
        var provider = new FakeSocketWorkerHealthProvider
        {
            IsInitialDiscoveryCompleted = false
        };

        var result = await RunCheckAsync(provider);

        result.Status.Should().Be(HealthStatus.Healthy);
    }

    [Fact]
    public async Task CheckHealth_ReturnsDegraded_WhenInitialDiscoveryInProgressBeyondGrace()
    {
        var provider = new FakeSocketWorkerHealthProvider
        {
            IsInitialDiscoveryCompleted = false,
            StartedUtc = DateTime.UtcNow - TimeSpan.FromSeconds(120)
        };

        var result = await RunCheckAsync(provider, new SignalSettings { AccountRefreshSeconds = 30 });

        result.Status.Should().Be(HealthStatus.Degraded);
    }

    [Fact]
    public async Task CheckHealth_ReturnsHealthy_WhenTransportNotStarted()
    {
        var provider = new FakeSocketWorkerHealthProvider
        {
            IsTransportStarted = false,
            IsManagerRunning = false
        };

        var result = await RunCheckAsync(provider);

        result.Status.Should().Be(HealthStatus.Healthy);
    }

    [Fact]
    public async Task CheckHealth_ReturnsUnhealthy_WhenAnyAccountIsFaulted()
    {
        var provider = new FakeSocketWorkerHealthProvider
        {
            WorkerStates = new Dictionary<string, SocketWorkerHealthState>
            {
                [AccountA] = CreateState(lastTick: DateTime.UtcNow),
                [AccountB] = CreateState(AccountB, state: SocketWorkerState.Faulted, lastTick: DateTime.UtcNow)
            }
        };

        var result = await RunCheckAsync(provider);

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Contain(AccountB);
    }

    [Fact]
    public async Task CheckHealth_ReturnsDegraded_WhenAnyAccountStalled()
    {
        var provider = new FakeSocketWorkerHealthProvider
        {
            WorkerStates = new Dictionary<string, SocketWorkerHealthState>
            {
                [AccountA] = CreateState(lastTick: DateTime.UtcNow),
                [AccountB] = CreateState(AccountB, lastTick: DateTime.UtcNow.AddSeconds(-70))
            }
        };

        var result = await RunCheckAsync(provider);

        result.Status.Should().Be(HealthStatus.Degraded);
    }

    [Fact]
    public async Task CheckHealth_IncludesPerAccountStateInData()
    {
        var worker = CreateState(lastTick: DateTime.UtcNow);
        var provider = new FakeSocketWorkerHealthProvider
        {
            WorkerStates = new Dictionary<string, SocketWorkerHealthState> { [AccountA] = worker }
        };

        var result = await RunCheckAsync(provider);

        result.Status.Should().Be(HealthStatus.Healthy);
        result.Data.Should().ContainKey(AccountA);
        result.Data[AccountA].Should().BeSameAs(worker);
        result.Data.Should().ContainKey("managerRunning");
    }

    [Fact]
    public async Task Transport_TracksSuccessfulReceive_OnValidPayload()
    {
        var signalClient = new ScriptedReceiveClient(async (_, _) =>
            """{"envelope":{"sourceNumber":"+15550000001","dataMessage":{"message":"hi"}}}""");

        var transport = CreateTransport(
            signalClient,
            _ => JsonResponse(AccountsPayload(AccountA)),
            new SignalSettings
            {
                AccountRefreshSeconds = 30,
                ReconnectDelaySeconds = 1,
                ReceiveClientDeadlineSeconds = 5
            });

        await transport.StartAsync(CancellationToken.None);

        await WaitUntilAsync(
            () => transport.GetWorkerStates().TryGetValue(AccountA, out var state)
                && state.LastSuccessfulReceiveUtc.HasValue,
            TimeSpan.FromSeconds(5));

        var workerState = transport.GetWorkerStates()[AccountA];
        await transport.StopAsync(CancellationToken.None);

        workerState.State.Should().Be(SocketWorkerState.Running);
        workerState.ConsecutiveErrors.Should().Be(0);
        workerState.LastWorkerLoopTickUtc.Should().NotBeNull();
        workerState.LastErrorUtc.Should().BeNull();
    }

    [Fact]
    public async Task Transport_TracksLoopTick_WhenIdleWithoutMessages()
    {
        var signalClient = new ScriptedReceiveClient(async (_, ct) =>
        {
            await Task.Delay(TimeSpan.FromMilliseconds(100), ct);
            return null;
        });

        var transport = CreateTransport(
            signalClient,
            _ => JsonResponse(AccountsPayload(AccountA)),
            new SignalSettings
            {
                AccountRefreshSeconds = 30,
                ReconnectDelaySeconds = 1,
                ReceiveClientDeadlineSeconds = 5
            });

        await transport.StartAsync(CancellationToken.None);

        await WaitUntilAsync(
            () => transport.GetWorkerStates().TryGetValue(AccountA, out var state)
                && state.LastWorkerLoopTickUtc.HasValue
                && state.State == SocketWorkerState.Running,
            TimeSpan.FromSeconds(5));

        var workerState = transport.GetWorkerStates()[AccountA];
        await transport.StopAsync(CancellationToken.None);

        workerState.LastWorkerLoopTickUtc.Should().NotBeNull();
        workerState.LastSuccessfulReceiveUtc.Should().BeNull();
    }

    [Fact]
    public async Task Transport_AccumulatesConsecutiveErrors_AndTransitionsToFaulted()
    {
        var signalClient = new ScriptedReceiveClient(async (_, _) => throw new InvalidOperationException("receive failed"));

        var transport = CreateTransport(
            signalClient,
            _ => JsonResponse(AccountsPayload(AccountA)),
            new SignalSettings
            {
                AccountRefreshSeconds = 30,
                ReconnectDelaySeconds = 1,
                ReceiveClientDeadlineSeconds = 2,
                WorkerConsecutiveErrorThreshold = 1,
                WorkerUnhealthyErrorThreshold = 2
            });

        await transport.StartAsync(CancellationToken.None);

        await WaitUntilAsync(
            () => transport.GetWorkerStates().TryGetValue(AccountA, out var state)
                && state.ConsecutiveErrors >= 2
                && state.State == SocketWorkerState.Faulted,
            TimeSpan.FromSeconds(10));

        var workerState = transport.GetWorkerStates()[AccountA];
        await transport.StopAsync(CancellationToken.None);

        workerState.ConsecutiveErrors.Should().BeGreaterThanOrEqualTo(2);
        workerState.State.Should().Be(SocketWorkerState.Faulted);
        workerState.LastErrorUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task Transport_ResetsErrorsAndRecovers_AfterSuccessfulRoundTrip()
    {
        var calls = 0;
        var signalClient = new ScriptedReceiveClient(async (_, _) =>
        {
            if (Interlocked.Increment(ref calls) <= 2)
            {
                throw new InvalidOperationException("transient");
            }

            return """{"envelope":{"sourceNumber":"+15550000001","dataMessage":{"message":"hi"}}}""";
        });

        var transport = CreateTransport(
            signalClient,
            _ => JsonResponse(AccountsPayload(AccountA)),
            new SignalSettings
            {
                AccountRefreshSeconds = 30,
                ReconnectDelaySeconds = 1,
                ReceiveClientDeadlineSeconds = 2,
                WorkerConsecutiveErrorThreshold = 1,
                WorkerUnhealthyErrorThreshold = 2
            });

        await transport.StartAsync(CancellationToken.None);

        await WaitUntilAsync(
            () => transport.GetWorkerStates().TryGetValue(AccountA, out var state)
                && state.State == SocketWorkerState.Running
                && state.ConsecutiveErrors == 0,
            TimeSpan.FromSeconds(10));

        var workerState = transport.GetWorkerStates()[AccountA];
        await transport.StopAsync(CancellationToken.None);

        workerState.State.Should().Be(SocketWorkerState.Running);
        workerState.ConsecutiveErrors.Should().Be(0);
        workerState.LastSuccessfulReceiveUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task Transport_FaultedWorker_RequiresConfiguredSuccesses_ToRecover()
    {
        var calls = 0;
        var allowSecondSuccess = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var signalClient = new ScriptedReceiveClient(async (_, ct) =>
        {
            var current = Interlocked.Increment(ref calls);
            if (current <= 3)
            {
                throw new InvalidOperationException("transient");
            }

            if (current >= 5)
            {
                await allowSecondSuccess.Task.WaitAsync(ct);
            }

            return """{"envelope":{"sourceNumber":"+15550000001","dataMessage":{"message":"hi"}}}""";
        });

        var transport = CreateTransport(
            signalClient,
            _ => JsonResponse(AccountsPayload(AccountA)),
            new SignalSettings
            {
                AccountRefreshSeconds = 30,
                ReconnectDelaySeconds = 1,
                ReceiveClientDeadlineSeconds = 2,
                WorkerConsecutiveErrorThreshold = 2,
                WorkerUnhealthyErrorThreshold = 3
            });

        await transport.StartAsync(CancellationToken.None);

        await WaitUntilAsync(
            () => transport.GetWorkerStates().TryGetValue(AccountA, out var state)
                && state.State == SocketWorkerState.Faulted,
            TimeSpan.FromSeconds(10));

        await WaitUntilAsync(
            () => Volatile.Read(ref calls) >= 5,
            TimeSpan.FromSeconds(10));

        transport.GetWorkerStates()[AccountA].State.Should().Be(SocketWorkerState.Faulted);

        allowSecondSuccess.SetResult();

        await WaitUntilAsync(
            () => transport.GetWorkerStates().TryGetValue(AccountA, out var state)
                && state.State == SocketWorkerState.Running
                && state.ConsecutiveErrors == 0,
            TimeSpan.FromSeconds(10));

        var workerState = transport.GetWorkerStates()[AccountA];
        await transport.StopAsync(CancellationToken.None);

        workerState.State.Should().Be(SocketWorkerState.Running);
        workerState.ConsecutiveErrors.Should().Be(0);
    }

    [Fact]
    public async Task Transport_DiscoveryFailure_PreservesWorkersAndDiscoveryFlag()
    {
        var discoverySucceeds = true;
        var signalClient = new ScriptedReceiveClient(async (_, ct) =>
        {
            await Task.Delay(TimeSpan.FromMilliseconds(100), ct);
            return null;
        });

        var transport = CreateTransport(
            signalClient,
            _ => discoverySucceeds
                ? JsonResponse(AccountsPayload(AccountA))
                : new HttpResponseMessage(HttpStatusCode.InternalServerError),
            new SignalSettings
            {
                AccountRefreshSeconds = 1,
                ReconnectDelaySeconds = 1,
                ReceiveClientDeadlineSeconds = 5
            });

        await transport.StartAsync(CancellationToken.None);

        await WaitUntilAsync(
            () => transport.GetWorkerStates().ContainsKey(AccountA),
            TimeSpan.FromSeconds(5));

        discoverySucceeds = false;
        await Task.Delay(TimeSpan.FromSeconds(3));

        var states = transport.GetWorkerStates();
        var discoveryCompleted = transport.IsInitialDiscoveryCompleted;
        var managerRunning = transport.IsManagerRunning;
        await transport.StopAsync(CancellationToken.None);

        states.Should().ContainKey(AccountA);
        discoveryCompleted.Should().BeTrue();
        managerRunning.Should().BeTrue();
    }

    [Fact]
    public async Task Transport_TransientEmptyDiscovery_PreservesExistingWorkers()
    {
        var callCount = 0;
        var fallbackPayload = AccountsPayload(AccountA);
        var signalClient = new ScriptedReceiveClient(async (_, ct) =>
        {
            await Task.Delay(TimeSpan.FromMilliseconds(100), ct);
            return null;
        });

        var transport = CreateTransport(
            signalClient,
            _ =>
            {
                var current = Interlocked.Increment(ref callCount);
                return current == 3
                    ? JsonResponse("[]")
                    : JsonResponse(fallbackPayload);
            },
            new SignalSettings
            {
                AccountRefreshSeconds = 1,
                ReconnectDelaySeconds = 1,
                ReceiveClientDeadlineSeconds = 5
            });

        await transport.StartAsync(CancellationToken.None);
        await WaitUntilAsync(
            () => transport.GetWorkerStates().ContainsKey(AccountA),
            TimeSpan.FromSeconds(5));

        await WaitUntilAsync(
            () => Volatile.Read(ref callCount) >= 3,
            TimeSpan.FromSeconds(5));

        var states = transport.GetWorkerStates();
        var managerRunning = transport.IsManagerRunning;
        await transport.StopAsync(CancellationToken.None);

        states.Should().ContainKey(AccountA);
        managerRunning.Should().BeTrue();
    }

    [Fact]
    public async Task Transport_ConsecutiveEmptyDiscovery_DeprovisionsWorkers()
    {
        var callCount = 0;
        var signalClient = new ScriptedReceiveClient(async (_, ct) =>
        {
            await Task.Delay(TimeSpan.FromMilliseconds(100), ct);
            return null;
        });

        var transport = CreateTransport(
            signalClient,
            _ =>
            {
                var current = Interlocked.Increment(ref callCount);
                if (current <= 2)
                {
                    return JsonResponse(AccountsPayload(AccountA));
                }

                return JsonResponse("[]");
            },
            new SignalSettings
            {
                AccountRefreshSeconds = 1,
                ReconnectDelaySeconds = 1,
                ReceiveClientDeadlineSeconds = 5
            });

        await transport.StartAsync(CancellationToken.None);
        await WaitUntilAsync(
            () => transport.GetWorkerStates().ContainsKey(AccountA),
            TimeSpan.FromSeconds(5));

        await WaitUntilAsync(
            () => !transport.GetWorkerStates().ContainsKey(AccountA),
            TimeSpan.FromSeconds(8));

        var states = transport.GetWorkerStates();
        var managerRunning = transport.IsManagerRunning;
        await transport.StopAsync(CancellationToken.None);

        states.Should().NotContainKey(AccountA);
        managerRunning.Should().BeTrue();
    }

    [Fact]
    public async Task Transport_FirstDiscoveryFailure_DoesNotCompleteInitialDiscovery()
    {
        var transport = CreateTransport(
            new ScriptedReceiveClient(async (_, _) => null),
            _ => new HttpResponseMessage(HttpStatusCode.InternalServerError),
            new SignalSettings
            {
                AccountRefreshSeconds = 1,
                ReconnectDelaySeconds = 1,
                ReceiveClientDeadlineSeconds = 5
            });

        await transport.StartAsync(CancellationToken.None);
        await Task.Delay(TimeSpan.FromSeconds(2));

        var discoveryCompleted = transport.IsInitialDiscoveryCompleted;
        var states = transport.GetWorkerStates();
        var managerRunning = transport.IsManagerRunning;
        await transport.StopAsync(CancellationToken.None);

        discoveryCompleted.Should().BeFalse();
        states.Should().BeEmpty();
        managerRunning.Should().BeTrue();
    }

    [Fact]
    public async Task Transport_RemovesDeprovisionedWorkers_FromTracking()
    {
        var discoveredAccounts = new[] { AccountA, AccountB };
        var signalClient = new ScriptedReceiveClient(async (_, ct) =>
        {
            await Task.Delay(TimeSpan.FromMilliseconds(100), ct);
            return null;
        });

        var transport = CreateTransport(
            signalClient,
            _ => JsonResponse(AccountsPayload(discoveredAccounts)),
            new SignalSettings
            {
                AccountRefreshSeconds = 1,
                ReconnectDelaySeconds = 1,
                ReceiveClientDeadlineSeconds = 5
            });

        await transport.StartAsync(CancellationToken.None);

        await WaitUntilAsync(
            () => transport.GetWorkerStates().ContainsKey(AccountB),
            TimeSpan.FromSeconds(5));

        discoveredAccounts = [AccountA];

        await WaitUntilAsync(
            () => !transport.GetWorkerStates().ContainsKey(AccountB),
            TimeSpan.FromSeconds(5));

        var states = transport.GetWorkerStates();
        await transport.StopAsync(CancellationToken.None);

        states.Should().ContainKey(AccountA);
        states.Should().NotContainKey(AccountB);
    }

    [Fact]
    public async Task Transport_ReportsManagerState_AfterStop()
    {
        var transport = CreateTransport(
            new ScriptedReceiveClient(async (_, _) => null),
            _ => JsonResponse("[]"),
            new SignalSettings());

        await transport.StartAsync(CancellationToken.None);
        await Task.Delay(TimeSpan.FromMilliseconds(300));

        var managerRunningBeforeStop = transport.IsManagerRunning;
        var startedBeforeStop = transport.IsTransportStarted;
        await transport.StopAsync(CancellationToken.None);
        var startedAfterStop = transport.IsTransportStarted;

        managerRunningBeforeStop.Should().BeTrue();
        startedBeforeStop.Should().BeTrue();
        startedAfterStop.Should().BeFalse();
    }

    [Fact]
    public async Task Transport_CountsMalformedJsonPayload_AsErrorWithoutFaulting()
    {
        var signalClient = new ScriptedReceiveClient(async (_, _) =>
        {
            await Task.Delay(TimeSpan.FromMilliseconds(100));
            return "not-json";
        });

        var transport = CreateTransport(
            signalClient,
            _ => JsonResponse(AccountsPayload(AccountA)),
            new SignalSettings
            {
                AccountRefreshSeconds = 30,
                ReconnectDelaySeconds = 1,
                ReceiveClientDeadlineSeconds = 5,
                WorkerConsecutiveErrorThreshold = 100,
                WorkerUnhealthyErrorThreshold = 200
            });

        await transport.StartAsync(CancellationToken.None);

        await WaitUntilAsync(
            () => transport.GetWorkerStates().TryGetValue(AccountA, out var state)
                && state.ConsecutiveErrors >= 1
                && state.State == SocketWorkerState.Running,
            TimeSpan.FromSeconds(5));

        var workerState = transport.GetWorkerStates()[AccountA];
        await transport.StopAsync(CancellationToken.None);

        workerState.State.Should().Be(SocketWorkerState.Running);
        workerState.ConsecutiveErrors.Should().BeGreaterThanOrEqualTo(1);
        workerState.LastErrorUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task Transport_CountsWebSocketFailure_AsErrorAndFaults()
    {
        var signalClient = new ScriptedReceiveClient(async (_, _) =>
            throw new WebSocketException("connection reset"));

        var transport = CreateTransport(
            signalClient,
            _ => JsonResponse(AccountsPayload(AccountA)),
            new SignalSettings
            {
                AccountRefreshSeconds = 30,
                ReconnectDelaySeconds = 1,
                ReceiveClientDeadlineSeconds = 2,
                WorkerConsecutiveErrorThreshold = 1,
                WorkerUnhealthyErrorThreshold = 2
            });

        await transport.StartAsync(CancellationToken.None);

        await WaitUntilAsync(
            () => transport.GetWorkerStates().TryGetValue(AccountA, out var state)
                && state.ConsecutiveErrors >= 2
                && state.State == SocketWorkerState.Faulted,
            TimeSpan.FromSeconds(10));

        var workerState = transport.GetWorkerStates()[AccountA];
        await transport.StopAsync(CancellationToken.None);

        workerState.ConsecutiveErrors.Should().BeGreaterThanOrEqualTo(2);
        workerState.State.Should().Be(SocketWorkerState.Faulted);
        workerState.LastErrorUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task Transport_DoesNotResetErrors_OnMalformedPayload()
    {
        var calls = 0;
        var signalClient = new ScriptedReceiveClient(async (_, _) =>
        {
            if (Interlocked.Increment(ref calls) % 2 == 1)
            {
                throw new WebSocketException("disconnect");
            }

            return "not-json";
        });

        var transport = CreateTransport(
            signalClient,
            _ => JsonResponse(AccountsPayload(AccountA)),
            new SignalSettings
            {
                AccountRefreshSeconds = 30,
                ReconnectDelaySeconds = 1,
                ReceiveClientDeadlineSeconds = 2,
                WorkerConsecutiveErrorThreshold = 1,
                WorkerUnhealthyErrorThreshold = 2
            });

        await transport.StartAsync(CancellationToken.None);

        await WaitUntilAsync(
            () => transport.GetWorkerStates().TryGetValue(AccountA, out var state)
                && state.State == SocketWorkerState.Faulted,
            TimeSpan.FromSeconds(10));

        var workerState = transport.GetWorkerStates()[AccountA];
        await transport.StopAsync(CancellationToken.None);

        workerState.State.Should().Be(SocketWorkerState.Faulted);
        workerState.ConsecutiveErrors.Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task Transport_DiscoveryAcceptsObjectShapedAccounts()
    {
        var signalClient = new ScriptedReceiveClient(async (_, ct) =>
        {
            await Task.Delay(TimeSpan.FromMilliseconds(100), ct);
            return null;
        });

        var transport = CreateTransport(
            signalClient,
            _ => JsonResponse("""[{"number":"+10000000001"}]"""),
            new SignalSettings
            {
                AccountRefreshSeconds = 1,
                ReconnectDelaySeconds = 1,
                ReceiveClientDeadlineSeconds = 5
            });

        await transport.StartAsync(CancellationToken.None);

        await WaitUntilAsync(
            () => transport.GetWorkerStates().ContainsKey(AccountA),
            TimeSpan.FromSeconds(5));

        var states = transport.GetWorkerStates();
        await transport.StopAsync(CancellationToken.None);

        states.Should().ContainKey(AccountA);
        transport.IsInitialDiscoveryCompleted.Should().BeTrue();
    }

    [Fact]
    public async Task Transport_DiscoveryFailure_WhenApiThrows()
    {
        var transport = CreateTransport(
            new ScriptedReceiveClient(async (_, _) => null),
            _ => throw new HttpRequestException("api down"),
            new SignalSettings
            {
                AccountRefreshSeconds = 1,
                ReconnectDelaySeconds = 1,
                ReceiveClientDeadlineSeconds = 5
            });

        await transport.StartAsync(CancellationToken.None);
        await Task.Delay(TimeSpan.FromSeconds(2));

        var discoveryCompleted = transport.IsInitialDiscoveryCompleted;
        var states = transport.GetWorkerStates();
        var managerRunning = transport.IsManagerRunning;
        await transport.StopAsync(CancellationToken.None);

        discoveryCompleted.Should().BeFalse();
        states.Should().BeEmpty();
        managerRunning.Should().BeTrue();
    }

    [Fact]
    public async Task Transport_DiscoveryFailure_WhenResponseIsNotArray()
    {
        var transport = CreateTransport(
            new ScriptedReceiveClient(async (_, _) => null),
            _ => JsonResponse("""{"error":"unexpected"}"""),
            new SignalSettings
            {
                AccountRefreshSeconds = 1,
                ReconnectDelaySeconds = 1,
                ReceiveClientDeadlineSeconds = 5
            });

        await transport.StartAsync(CancellationToken.None);
        await Task.Delay(TimeSpan.FromSeconds(2));

        var discoveryCompleted = transport.IsInitialDiscoveryCompleted;
        var states = transport.GetWorkerStates();
        await transport.StopAsync(CancellationToken.None);

        discoveryCompleted.Should().BeFalse();
        states.Should().BeEmpty();
    }

    [Fact]
    public async Task Transport_DiscoveryFailure_WhenResponseIsMalformedJson()
    {
        var discoveredAccounts = new[] { AccountA };
        var transport = CreateTransport(
            new ScriptedReceiveClient(async (_, ct) =>
            {
                await Task.Delay(TimeSpan.FromMilliseconds(100), ct);
                return null;
            }),
            _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("not-json", Encoding.UTF8, "application/json")
            },
            new SignalSettings
            {
                AccountRefreshSeconds = 1,
                ReconnectDelaySeconds = 1,
                ReceiveClientDeadlineSeconds = 5
            });

        await transport.StartAsync(CancellationToken.None);
        await Task.Delay(TimeSpan.FromSeconds(2));

        var states = transport.GetWorkerStates();
        var discoveryCompleted = transport.IsInitialDiscoveryCompleted;
        var managerRunning = transport.IsManagerRunning;
        await transport.StopAsync(CancellationToken.None);

        states.Should().BeEmpty();
        discoveryCompleted.Should().BeFalse();
        managerRunning.Should().BeTrue();
    }

    [Fact]
    public void SignalSettingsValidation_Passes_WithDefaults()
    {
        var options = BuildValidatedOptions(new Dictionary<string, string?>());

        var settings = options.Value;

        settings.EnableWorkerHealthCheck.Should().BeTrue();
        settings.WorkerDegradedThresholdSeconds.Should().Be(60);
        settings.WorkerUnhealthyThresholdSeconds.Should().Be(180);
        settings.WorkerConsecutiveErrorThreshold.Should().Be(3);
        settings.WorkerUnhealthyErrorThreshold.Should().Be(10);
    }

    [Fact]
    public void SignalSettingsValidation_Passes_WhenDeadlineUsesDefaultFallback()
    {
        var options = BuildValidatedOptions(new Dictionary<string, string?>
        {
            ["Signal:ReceiveClientDeadlineSeconds"] = "0",
            ["Signal:ReceiveTimeoutSeconds"] = "10",
            ["Signal:WorkerDegradedThresholdSeconds"] = "20"
        });

        var act = () => _ = options.Value;

        act.Should().NotThrow();
    }

    [Fact]
    public void SignalSettingsValidation_Rejects_UnhealthyNotGreaterThanDegraded()
    {
        var options = BuildValidatedOptions(new Dictionary<string, string?>
        {
            ["Signal:WorkerUnhealthyThresholdSeconds"] = "60",
            ["Signal:WorkerDegradedThresholdSeconds"] = "60"
        });

        var act = () => _ = options.Value;

        act.Should().Throw<OptionsValidationException>();
    }

    [Fact]
    public void SignalSettingsValidation_Rejects_DegradedNotGreaterThanClientDeadline()
    {
        var options = BuildValidatedOptions(new Dictionary<string, string?>
        {
            ["Signal:ReceiveClientDeadlineSeconds"] = "70"
        });

        var act = () => _ = options.Value;

        act.Should().Throw<OptionsValidationException>();
    }

    [Fact]
    public void SignalSettingsValidation_Rejects_UnhealthyErrorNotGreaterThanConsecutiveError()
    {
        var options = BuildValidatedOptions(new Dictionary<string, string?>
        {
            ["Signal:WorkerUnhealthyErrorThreshold"] = "2",
            ["Signal:WorkerConsecutiveErrorThreshold"] = "3"
        });

        var act = () => _ = options.Value;

        act.Should().Throw<OptionsValidationException>();
    }

    [Fact]
    public void SignalSettingsValidation_Rejects_ConsecutiveErrorBelowOne()
    {
        var options = BuildValidatedOptions(new Dictionary<string, string?>
        {
            ["Signal:WorkerConsecutiveErrorThreshold"] = "0"
        });

        var act = () => _ = options.Value;

        act.Should().Throw<OptionsValidationException>();
    }

    [Fact]
    public void SignalSettingsValidation_Rejects_DegradedWithoutReconnectDelayHeadroom()
    {
        var options = BuildValidatedOptions(new Dictionary<string, string?>
        {
            ["Signal:ReceiveClientDeadlineSeconds"] = "10",
            ["Signal:ReconnectDelaySeconds"] = "10",
            ["Signal:WorkerDegradedThresholdSeconds"] = "20"
        });

        var act = () => _ = options.Value;

        act.Should().Throw<OptionsValidationException>();
    }

    private static async Task<HealthCheckResult> RunCheckAsync(
        FakeSocketWorkerHealthProvider provider,
        SignalSettings? settings = null)
    {
        var check = new SocketWorkerHealthCheck(
            provider,
            Options.Create(settings ?? new SignalSettings()),
            NullLogger<SocketWorkerHealthCheck>.Instance);

        return await check.CheckHealthAsync(new HealthCheckContext());
    }

    private static SocketWorkerHealthState CreateState(
        string account = AccountA,
        SocketWorkerState state = SocketWorkerState.Running,
        DateTime? lastReceive = null,
        DateTime? lastTick = null,
        DateTime? startedUtc = null,
        int consecutiveErrors = 0,
        DateTime? lastError = null) =>
        new(account, state, lastReceive, lastTick, startedUtc ?? DateTime.UtcNow, consecutiveErrors, lastError);

    private static IOptions<SignalSettings> BuildValidatedOptions(Dictionary<string, string?> configuration)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(configuration)
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(config);
        services.AddSignalWorkerHealthCheck(config);

        var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IOptions<SignalSettings>>();
    }

    private static SocketTransportClient CreateTransport(
        ISignalReceiveClient receiveClient,
        Func<HttpRequestMessage, HttpResponseMessage> signalApiResponder,
        SignalSettings settings)
    {
        var httpClient = new HttpClient(new StubHttpMessageHandler(signalApiResponder))
        {
            BaseAddress = new Uri("http://localhost:8080")
        };

        return new SocketTransportClient(
            new StubHttpClientFactory(httpClient),
            Options.Create(settings),
            new StubCredentialProvider(),
            receiveClient,
            NullLogger<SocketTransportClient>.Instance);
    }

    private static HttpResponseMessage JsonResponse(string body) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

    private static string AccountsPayload(params string[] accounts) =>
        JsonSerializer.Serialize(accounts);

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(50);
        }

        throw new TimeoutException($"Condition not met within {timeout.TotalSeconds:F1}s.");
    }

    private sealed class FakeSocketWorkerHealthProvider : ISocketWorkerHealthProvider
    {
        public IReadOnlyDictionary<string, SocketWorkerHealthState> WorkerStates { get; set; } =
            new Dictionary<string, SocketWorkerHealthState>();

        public bool IsInitialDiscoveryCompleted { get; set; } = true;

        public bool IsTransportStarted { get; set; } = true;

        public bool IsManagerRunning { get; set; } = true;

        public DateTime StartedUtc { get; set; } = DateTime.UtcNow;

        public IReadOnlyDictionary<string, SocketWorkerHealthState> GetWorkerStates() => WorkerStates;

        public (bool Started, bool Running) GetManagerState() => (IsTransportStarted, IsManagerRunning);
    }

    private sealed class ScriptedReceiveClient(Func<string, CancellationToken, Task<string?>> handler) : ISignalReceiveClient
    {
        public Task<string?> ReceiveAsync(string account, Uri receiveUri, CancellationToken ct) => handler(account, ct);
    }

    private sealed class StubHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class StubCredentialProvider : IChannelCredentialProvider
    {
        public Task<string> ResolveBearerTokenAsync(string sender, CancellationToken ct) => Task.FromResult("token");
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(responder(request));
        }
    }
}
