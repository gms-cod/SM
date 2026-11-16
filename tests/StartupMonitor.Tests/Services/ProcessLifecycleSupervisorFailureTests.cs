using Microsoft.Extensions.Logging.Abstractions;
using StartupMonitor.Service.Services.ProcessLifecycle;

namespace StartupMonitor.Tests.Services;

/// <summary>Verifies test scenario for ProcessLifecycleSupervisorFailureTests.cs.</summary>
public sealed class ProcessLifecycleSupervisorFailureTests
{
    /// <summary>Verifies test scenario for ProcessLifecycleSupervisorFailureTests.cs.</summary>
    /// <summary>Verifies test scenario for ProcessLifecycleSupervisorFailureTests.cs.</summary>
    [Fact]
    public async Task LegacyMode_DoesNotInvokeSessionFactoryOrSnapshotSource()
    {
        var calls = 0;
        using var supervisor = new ProcessLifecycleSupervisor(new ProcessLifecycleRegistry(), new LifecycleIngress(),
            NullLogger<ProcessLifecycleSupervisor>.Instance, ProcessLifecycleMode.Legacy,
            snapshotSource: _ => throw new InvalidOperationException("Unexpected legacy baseline"),
            sessionFactory: () => { Interlocked.Increment(ref calls); throw new InvalidOperationException("Unexpected legacy ETW session"); });

        await supervisor.StartAsync(CancellationToken.None);
        await supervisor.StopAsync(CancellationToken.None);

        Assert.Equal(0, calls);
        Assert.Equal(ProcessLifecycleHealthState.Stopped, supervisor.HealthState);
        Assert.Empty(supervisor.Registry.GetLiveSnapshot());
    }

    /// <summary>
    /// <summary>Verifies test scenario for ProcessLifecycleSupervisorFailureTests.cs.</summary>
    /// <summary>Verifies test scenario for ProcessLifecycleSupervisorFailureTests.cs.</summary>
    /// <summary>Verifies test scenario for ProcessLifecycleSupervisorFailureTests.cs.</summary>
    /// <summary>Verifies test scenario for ProcessLifecycleSupervisorFailureTests.cs.</summary>
    /// </summary>
    /// <summary>Verifies test scenario for ProcessLifecycleSupervisorFailureTests.cs.</summary>
    [Fact]
    public async Task SessionFactoryFails_StartDoesNotBlockAndFailureRemainsDegradedDuringRetry()
    {
        var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        var calls = 0;
        var baselineCalls = 0;
        using var supervisor = new ProcessLifecycleSupervisor(new ProcessLifecycleRegistry(), new LifecycleIngress(),
            NullLogger<ProcessLifecycleSupervisor>.Instance, ProcessLifecycleMode.EtwPreferred,
            snapshotSource: _ => { Interlocked.Increment(ref baselineCalls); return Array.Empty<(int, int, DateTime, string)>(); },
            sessionFactory: () =>
            {
                Interlocked.Increment(ref calls);
                entered.TrySetResult(true);
                if (!release.Wait(TimeSpan.FromSeconds(30)))
                    throw new TimeoutException("Test did not release session factory");
                throw new IOException("Controlled session acquisition failure");
            });
        try
        {
            var start = supervisor.StartAsync(CancellationToken.None);
            Assert.True(start.IsCompletedSuccessfully, "Host.StartAsync должен вернуть завершённую задачу до разблокировки фабрики ETW.");
            await start;
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
                Assert.NotEqual(ProcessLifecycleHealthState.Healthy, supervisor.HealthState);
            }
            finally { release.Set(); }

            var deadline = DateTime.UtcNow.AddSeconds(10);
            while ((Volatile.Read(ref calls) < 2 || supervisor.HealthState != ProcessLifecycleHealthState.Degraded) && DateTime.UtcNow < deadline)
                await Task.Delay(10);
            Assert.True(Volatile.Read(ref calls) >= 2, "После отказа должна выполняться ограниченная повторная попытка.");
            Assert.Equal(ProcessLifecycleHealthState.Degraded, supervisor.HealthState);
            Assert.Equal(0, baselineCalls);
            Assert.Empty(supervisor.Registry.GetLiveSnapshot());
        }
        finally
        {
            // Verifies component behavior and assertions.
            release.Set();
            await supervisor.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
        }
        Assert.Equal(ProcessLifecycleHealthState.Stopped, supervisor.HealthState);
    }

    /// <summary>Verifies test scenario for ProcessLifecycleSupervisorFailureTests.cs.</summary>
    /// <summary>Verifies test scenario for ProcessLifecycleSupervisorFailureTests.cs.</summary>
    [Fact]
    public async Task Start_PreCanceled_DoesNotInvokeSessionFactory()
    {
        var calls = 0;
        using var supervisor = new ProcessLifecycleSupervisor(new ProcessLifecycleRegistry(), new LifecycleIngress(),
            NullLogger<ProcessLifecycleSupervisor>.Instance,
            sessionFactory: () => { Interlocked.Increment(ref calls); throw new InvalidOperationException("Unexpected session"); });
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => supervisor.StartAsync(cancellation.Token));

        Assert.Equal(0, calls);
    }
}
