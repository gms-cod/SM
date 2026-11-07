using System.Collections.Concurrent;
using System.Reflection;
using StartupMonitor.Service.Services;
using StartupMonitor.Shared.Models;
using StartupMonitor.Service.Services.ProcessLifecycle;

namespace StartupMonitor.Tests.Services;

/// <summary>Проверяет завершение проверки подписи после попадания базовых данных в кэш без изменения опубликованного DTO.</summary>
public sealed class ProcessMetadataCompletionTests
{
    /// <summary>Возвращает кэш конкретного worker для контролируемого воспроизведения состояния частичного обогащения.</summary>
    /// <param name="enricher">Изолированный worker текущего теста.</param>
    /// <returns>Потокобезопасный кэш с точным ключом экземпляра процесса.</returns>
    private static ConcurrentDictionary<(int Pid, long StartTimeTicks), CachedProcessMetadata> Cache(ProcessMetadataEnricher enricher)
    {
        var field = typeof(ProcessMetadataEnricher).GetField("_cache", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new MissingFieldException("Не найден кэш метаданных процесса.");
        return Assert.IsType<ConcurrentDictionary<(int, long), CachedProcessMetadata>>(field.GetValue(enricher));
    }

    /// <summary>
    /// Воспроизводит cache-hit на базовых данных до проверки подписи. Проверка должна завершиться,
    /// а подписчики уже опубликованного снимка не должны получать изменения с фонового потока.
    /// </summary>
    /// <returns>Задача завершения локальной проверки подписи тестового файла.</returns>
    [Fact]
    public async Task EnrichOrQueue_BasicCacheHit_CompletesSignatureWithoutMutatingPublishedSnapshot()
    {
        var file = Path.Combine(Path.GetTempPath(), "StartupMonitorMetadata_" + Guid.NewGuid().ToString("N") + ".exe");
        File.Copy(typeof(ProcessMetadataCompletionTests).Assembly.Location, file);
        try
        {
            await using var enricher = new ProcessMetadataEnricher();
            var created = DateTime.UtcNow.AddMinutes(-1);
            var key = (Pid: 654321, StartTimeTicks: created.Ticks);
            var cache = Cache(enricher);
            cache[key] = new CachedProcessMetadata(file, "client.exe", string.Empty, 10, false, "Fixture");
            var snapshot = new ProcessInfo
            {
                ProcessId = key.Pid,
                StartTime = created,
                ProcessName = "client.exe",
                FilePath = file,
                CommandLine = "client.exe",
                ParentProcessId = 10,
                Description = "Fixture"
            };
            var changes = new ConcurrentQueue<string?>();
            snapshot.PropertyChanged += (_, args) => changes.Enqueue(args.PropertyName);

            enricher.EnrichOrQueue(snapshot, IntPtr.Zero);
            changes.Clear();
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while ((!cache.TryGetValue(key, out var current) || !current.SignatureChecked) && DateTime.UtcNow < deadline)
                await Task.Delay(10);

            Assert.True(cache.TryGetValue(key, out var completed));
            Assert.True(completed.SignatureChecked, "Попадание в базовый кэш не должно пропускать Authenticode.");
            Assert.Equal(DigitalSignatureStatus.NotSigned, completed.SignatureStatus);
            Assert.Equal(DigitalSignatureStatus.Unknown, snapshot.SignatureStatus);
            Assert.Empty(changes);
            var nextSnapshot = new ProcessInfo { ProcessId = key.Pid, StartTime = created, ProcessName = "client.exe" };
            enricher.EnrichOrQueue(nextSnapshot, IntPtr.Zero);
            Assert.Equal(DigitalSignatureStatus.NotSigned, nextSnapshot.SignatureStatus);
        }
        finally { File.Delete(file); }
    }

    /// <summary>Проверяет запрет устойчивого кэширования PID с неизвестным временем создания.</summary>
    /// <returns>Задача завершения worker, чтобы проверить отсутствие поздней записи в кэш.</returns>
    [Fact]
    public async Task EnrichOrQueue_UnknownCreationTime_DoesNotCreatePidZeroIdentityCache()
    {
        await using var enricher = new ProcessMetadataEnricher();
        var snapshot = new ProcessInfo
        {
            ProcessId = 654321,
            ProcessName = "client.exe",
            FilePath = @"C:\DoesNotExist\client.exe",
            StartTime = DateTime.MinValue
        };

        enricher.EnrichOrQueue(snapshot, IntPtr.Zero);
        await enricher.DisposeAsync();

        Assert.Empty(Cache(enricher));
    }

    /// <summary>Проверяет вытеснение старого поколения кэша, хотя тот же PID продолжает существовать.</summary>
    /// <returns>Задача детерминированного освобождения неактивного worker.</returns>
    [Fact]
    public async Task PruneDeadProcesses_ExactInstances_RemovesOldGenerationOfStillActivePid()
    {
        await using var enricher = new ProcessMetadataEnricher();
        var first = (Pid: 1234, StartTimeTicks: DateTime.UtcNow.Ticks);
        var next = (Pid: 1234, StartTimeTicks: first.StartTimeTicks + 1);
        var cache = Cache(enricher);
        cache[first] = new CachedProcessMetadata("old.exe", "", "", 0, false, "");
        cache[next] = new CachedProcessMetadata("new.exe", "", "", 0, false, "");

        enricher.PruneDeadProcesses(new HashSet<(int Pid, long StartTimeTicks)> { next });

        Assert.Equal("new.exe", Assert.Single(cache).Value.FilePath);
        Assert.False(cache.ContainsKey(first));
    }

    /// <summary>Проверяет повторную доставку готовой подписи после baseline без повторной работы Authenticode.</summary>
    /// <returns>Задача завершения единственной проверки подписи локального изолированного файла.</returns>
    [Fact]
    public async Task MetadataReady_SignatureBeforeBaseline_IsDeliveredOnNextCacheHit()
    {
        var file = Path.Combine(Path.GetTempPath(), "StartupMonitorMetadataFlow_" + Guid.NewGuid().ToString("N") + ".exe");
        File.Copy(typeof(ProcessMetadataCompletionTests).Assembly.Location, file);
        try
        {
            await using var enricher = new ProcessMetadataEnricher();
            var registry = new ProcessLifecycleRegistry();
            var created = DateTime.UtcNow.AddMinutes(-1);
            const int pid = 654321;
            var received = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            enricher.MetadataReady += update =>
            {
                registry.TryUpdateVerifiedMetadata(update);
                received.TrySetResult(true);
            };
            Cache(enricher)[(pid, created.Ticks)] = new CachedProcessMetadata(file, "client.exe", "", 10, false, "");
            enricher.EnrichOrQueue(new ProcessInfo { ProcessId = pid, StartTime = created, ProcessName = "client.exe" }, IntPtr.Zero);
            await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Empty(registry.GetLiveSnapshot());

            registry.ApplyBaseline([(pid, 10, created, "client.exe")], DateTime.UtcNow);
            enricher.EnrichOrQueue(new ProcessInfo { ProcessId = pid, StartTime = created, ProcessName = "client.exe" }, IntPtr.Zero);

            var current = Assert.Single(registry.GetLiveSnapshot());
            Assert.Equal(ProcessMetadataStatus.SignatureChecked, current.MetadataStatus);
            Assert.Equal(file, current.ResolvedExecutablePath);
        }
        finally { File.Delete(file); }
    }
}
