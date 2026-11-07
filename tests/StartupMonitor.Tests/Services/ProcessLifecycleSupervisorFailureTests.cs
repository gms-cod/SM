using Microsoft.Extensions.Logging.Abstractions;
using StartupMonitor.Service.Services.ProcessLifecycle;

namespace StartupMonitor.Tests.Services;

/// <summary>Проверяет отказ запуска и отмену супервизора через фабрику, которая никогда не создаёт нативную ETW-сессию.</summary>
public sealed class ProcessLifecycleSupervisorFailureTests
{
    /// <summary>Проверяет отсутствие даже попытки запуска источника в режиме Legacy.</summary>
    /// <returns>Задача безопасного завершения супервизора.</returns>
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
    /// Проверяет немедленный возврат Host.StartAsync при медленном контроллере, переход в Degraded
    /// и повтор попытки после отказа без ложного Healthy или заполнения реестра фиктивным baseline.
    /// Сам контракт неблокирующего запуска проверяется завершённой задачей, независимо от задержек ThreadPool
    /// полного параллельного набора; bounded ожидания фабрики не подменяют эту проверку таймингом планировщика.
    /// </summary>
    /// <returns>Задача ожидания управляемого отказа и последующего shutdown.</returns>
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
            // Даже провал первого assertion освобождает заблокированную фабрику и завершает все задачи супервизора.
            release.Set();
            await supervisor.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
        }
        Assert.Equal(ProcessLifecycleHealthState.Stopped, supervisor.HealthState);
    }

    /// <summary>Проверяет отмену до запуска без вызова фабрики и без активации фоновых задач.</summary>
    /// <returns>Задача проверки отменённого запуска.</returns>
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
