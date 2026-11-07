using StartupMonitor.Service.Services.ProcessLifecycle;
using StartupMonitor.Shared.Models;

namespace StartupMonitor.Tests.Services;

/// <summary>
/// Проверяет восстановление реестра после потери событий, строгое различение экземпляров PID
/// и изоляцию опубликованных снимков правил от последующих изменений конфигурации.
/// Все события синтетические: нативные сессии ETW и процессы ОС не создаются.
/// </summary>
public sealed class ProcessLifecycleReconciliationRegressionTests
{
    private static readonly DateTime StartUtc = new(2026, 11, 5, 10, 0, 0, DateTimeKind.Utc);

    /// <summary>Создаёт событие с фиксированным PID и управляемыми временем, ключом и порядковым номером.</summary>
    /// <param name="epoch">Эпоха источника ETW.</param>
    /// <param name="kind">Вид события.</param>
    /// <param name="time">Время события в UTC.</param>
    /// <param name="key">Непрозрачный ключ экземпляра процесса.</param>
    /// <param name="sequence">Порядковый номер принятого события.</param>
    /// <returns>Неизменяемое событие для чистого редуктора.</returns>
    private static ProcessLifecycleEvent Event(Guid epoch, LifecycleEventKind kind, DateTime time, ulong key = 42, long sequence = 1) =>
        new(epoch, sequence, kind, 1234, 10, key, sequence * 1000, time, "client.exe", string.Empty);

    /// <summary>Проверяет восстановление пропущенного Stop по полному контрольному снимку.</summary>
    [Fact]
    public void ApplyBaseline_AbsentPreviouslyKnownProcess_IsRetired()
    {
        var epoch = Guid.NewGuid();
        var registry = new ProcessLifecycleRegistry(epoch);
        var original = registry.ApplyEvent(Event(epoch, LifecycleEventKind.Started, StartUtc));
        Assert.NotNull(original);

        registry.ApplyBaseline(Array.Empty<(int, int, DateTime, string)>(), StartUtc.AddSeconds(10));

        Assert.Empty(registry.GetLiveSnapshot());
        Assert.Equal(original.InstanceId, Assert.Single(registry.GetHistorySnapshot()).InstanceId);
    }

    /// <summary>Проверяет, что снимок, сделанный раньше поступившего Start, не удаляет новый процесс.</summary>
    [Fact]
    public void ApplyBaseline_ProcessStartedAfterSnapshot_RemainsAlive()
    {
        var epoch = Guid.NewGuid();
        var registry = new ProcessLifecycleRegistry(epoch);
        var started = registry.ApplyEvent(Event(epoch, LifecycleEventKind.Started, StartUtc.AddSeconds(1)));
        Assert.NotNull(started);

        registry.ApplyBaseline(Array.Empty<(int, int, DateTime, string)>(), StartUtc);

        Assert.Equal(started.InstanceId, Assert.Single(registry.GetLiveSnapshot()).InstanceId);
        Assert.Empty(registry.GetHistorySnapshot());
    }

    /// <summary>Проверяет, что старый снимок того же PID не прикрепляет FILETIME прежнего процесса к более позднему Start.</summary>
    [Fact]
    public void ApplyBaseline_OlderSnapshotContainsReusedPid_DoesNotOverwriteNewStart()
    {
        var epoch = Guid.NewGuid();
        var registry = new ProcessLifecycleRegistry(epoch);
        var current = registry.ApplyEvent(Event(epoch, LifecycleEventKind.Started, StartUtc.AddSeconds(1), 52));
        Assert.NotNull(current);

        registry.ApplyBaseline([(1234, 99, StartUtc.AddHours(-1), "old.exe")], StartUtc);

        var remaining = Assert.Single(registry.GetLiveSnapshot());
        Assert.Equal(current.InstanceId, remaining.InstanceId);
        Assert.Equal(current.CreationTimeUtc, remaining.CreationTimeUtc);
        Assert.Equal(current.ImageName, remaining.ImageName);
        Assert.Empty(registry.GetHistorySnapshot());
    }

    /// <summary>Проверяет быстрое переиспользование PID внутри бывшего двухсекундного допуска.</summary>
    [Fact]
    public void ApplyBaseline_CreationTimeChangesByOneMillisecond_CreatesNewGeneration()
    {
        var epoch = Guid.NewGuid();
        var registry = new ProcessLifecycleRegistry(epoch);
        var old = registry.ApplyEvent(Event(epoch, LifecycleEventKind.Started, StartUtc));
        Assert.NotNull(old);

        registry.ApplyBaseline([(1234, 10, StartUtc.AddMilliseconds(1), "client.exe")], StartUtc.AddSeconds(1));

        var current = Assert.Single(registry.GetLiveSnapshot());
        Assert.NotEqual(old.InstanceId, current.InstanceId);
        Assert.Equal(StartUtc.AddMilliseconds(1), current.CreationTimeUtc);
        Assert.Equal(old.InstanceId, Assert.Single(registry.GetHistorySnapshot()).InstanceId);
    }

    /// <summary>Проверяет, что переиспользованный непрозрачный KernelKey не заменяет проверку времени нового Start.</summary>
    [Fact]
    public void ApplyEvent_NewStartReusesSameKernelKey_AssignsNewGeneration()
    {
        var epoch = Guid.NewGuid();
        var registry = new ProcessLifecycleRegistry(epoch);
        var old = registry.ApplyEvent(Event(epoch, LifecycleEventKind.Started, StartUtc, 42, 1));

        var current = registry.ApplyEvent(Event(epoch, LifecycleEventKind.Started, StartUtc.AddMilliseconds(1), 42, 2));

        Assert.NotNull(old);
        Assert.NotNull(current);
        Assert.NotEqual(old.InstanceId, current.InstanceId);
        Assert.Equal(current, Assert.Single(registry.GetLiveSnapshot()));
        Assert.Equal(old.InstanceId, Assert.Single(registry.GetHistorySnapshot()).InstanceId);
    }

    /// <summary>Проверяет, что старый бесключевой Stop не завершает новый экземпляр PID.</summary>
    [Fact]
    public void ApplyEvent_StopBeforeCurrentBirthWithoutKernelKey_DoesNotRetireCurrent()
    {
        var epoch = Guid.NewGuid();
        var registry = new ProcessLifecycleRegistry(epoch);
        var current = registry.ApplyEvent(Event(epoch, LifecycleEventKind.Started, StartUtc.AddSeconds(1), 52));
        Assert.NotNull(current);

        registry.ApplyEvent(Event(epoch, LifecycleEventKind.Stopped, StartUtc, 0, 2));

        Assert.Equal(current.InstanceId, Assert.Single(registry.GetLiveSnapshot()).InstanceId);
        Assert.Empty(registry.GetHistorySnapshot());
    }

    /// <summary>Проверяет, что запоздалый Start старого PID не заменяет новый активный экземпляр.</summary>
    [Fact]
    public void ApplyEvent_StaleStartBeforeCurrentBirth_DoesNotReplaceCurrent()
    {
        var epoch = Guid.NewGuid();
        var registry = new ProcessLifecycleRegistry(epoch);
        var current = registry.ApplyEvent(Event(epoch, LifecycleEventKind.Started, StartUtc.AddSeconds(1), 52));
        Assert.NotNull(current);

        registry.ApplyEvent(Event(epoch, LifecycleEventKind.Started, StartUtc, 42, 2));

        Assert.Equal(current.InstanceId, Assert.Single(registry.GetLiveSnapshot()).InstanceId);
        Assert.Empty(registry.GetHistorySnapshot());
    }

    /// <summary>Проверяет, что rundown не выдаёт время наблюдения существующего процесса за время его создания.</summary>
    [Fact]
    public void ApplyEvent_Rundown_DoesNotInventCreationTimestamp()
    {
        var epoch = Guid.NewGuid();
        var registry = new ProcessLifecycleRegistry(epoch);

        var observed = registry.ApplyEvent(Event(epoch, LifecycleEventKind.Rundown, StartUtc));

        Assert.NotNull(observed);
        Assert.True(observed.IsFromBaseline);
        Assert.Equal(DateTime.MinValue, observed.CreationTimeUtc);
    }

    /// <summary>Проверяет дополнение неизвестного rundown времени создания точным системным снимком без смены поколения.</summary>
    [Fact]
    public void ApplyBaseline_RundownThenAuthoritativeCreation_UpgradesExistingGeneration()
    {
        var epoch = Guid.NewGuid();
        var registry = new ProcessLifecycleRegistry(epoch);
        var observed = registry.ApplyEvent(Event(epoch, LifecycleEventKind.Rundown, StartUtc));
        Assert.NotNull(observed);

        registry.ApplyBaseline([(1234, 10, StartUtc.AddHours(-1), "client.exe")], StartUtc.AddSeconds(1));

        var confirmed = Assert.Single(registry.GetLiveSnapshot());
        Assert.Equal(observed.InstanceId, confirmed.InstanceId);
        Assert.Equal(StartUtc.AddHours(-1), confirmed.CreationTimeUtc);
        Assert.Empty(registry.GetHistorySnapshot());
    }

    /// <summary>Проверяет слияние уже захваченного FILETIME с ожидающим Start, timestamp которого позднее рождения на один такт.</summary>
    [Fact]
    public void ApplyEvent_QueuedStartAfterAuthoritativeBaseline_PreservesGenerationAndBindsKernelKey()
    {
        var epoch = Guid.NewGuid();
        var registry = new ProcessLifecycleRegistry(epoch);
        registry.ApplyBaseline([(1234, 10, StartUtc, "client.exe")], StartUtc.AddSeconds(1));
        var baseline = Assert.Single(registry.GetLiveSnapshot());

        registry.ApplyEvent(Event(epoch, LifecycleEventKind.Started, StartUtc.AddTicks(1), 42));

        var merged = Assert.Single(registry.GetLiveSnapshot());
        Assert.Equal(baseline.InstanceId, merged.InstanceId);
        Assert.Equal(StartUtc, merged.CreationTimeUtc);
        Assert.Equal(42UL, merged.KernelKey);
        Assert.Empty(registry.GetHistorySnapshot());
        registry.ApplyEvent(Event(epoch, LifecycleEventKind.Stopped, StartUtc.AddSeconds(2), 42, 2));
        Assert.Empty(registry.GetLiveSnapshot());
        Assert.Equal(baseline.InstanceId, Assert.Single(registry.GetHistorySnapshot()).InstanceId);
    }

    /// <summary>Проверяет, что запоздалое перечисление уже завершённого экземпляра не возвращает его в live-набор.</summary>
    [Fact]
    public void ApplyEvent_RundownBeforeKnownStop_DoesNotResurrectExitedInstance()
    {
        var epoch = Guid.NewGuid();
        var registry = new ProcessLifecycleRegistry(epoch);
        registry.ApplyEvent(Event(epoch, LifecycleEventKind.Started, StartUtc));
        registry.ApplyEvent(Event(epoch, LifecycleEventKind.Stopped, StartUtc.AddSeconds(2), 42, 2));

        var result = registry.ApplyEvent(Event(epoch, LifecycleEventKind.Rundown, StartUtc.AddSeconds(1), 42, 3));

        Assert.Null(result);
        Assert.Empty(registry.GetLiveSnapshot());
        Assert.Single(registry.GetHistorySnapshot());
    }

    /// <summary>Проверяет приоритет Stop над снимком, захват которого начался раньше завершения процесса.</summary>
    [Fact]
    public void ApplyBaseline_SnapshotPredatesKnownStop_DoesNotResurrectExitedInstance()
    {
        var epoch = Guid.NewGuid();
        var registry = new ProcessLifecycleRegistry(epoch);
        registry.ApplyEvent(Event(epoch, LifecycleEventKind.Started, StartUtc));
        registry.ApplyEvent(Event(epoch, LifecycleEventKind.Stopped, StartUtc.AddSeconds(2), 42, 2));

        registry.ApplyBaseline([(1234, 10, StartUtc, "client.exe")], StartUtc.AddSeconds(1));

        Assert.Empty(registry.GetLiveSnapshot());
        Assert.Single(registry.GetHistorySnapshot());
        // Настоящий новый экземпляр после Stop всё же учитывается.
        registry.ApplyBaseline([(1234, 10, StartUtc.AddSeconds(3), "client.exe")], StartUtc.AddSeconds(4));
        Assert.Equal(StartUtc.AddSeconds(3), Assert.Single(registry.GetLiveSnapshot()).CreationTimeUtc);
    }

    /// <summary>Проверяет атомарность относительно исключения перечислителя источника снимка.</summary>
    [Fact]
    public void ApplyBaseline_SourceEnumerationFails_DoesNotPartiallyMutateLiveSet()
    {
        var epoch = Guid.NewGuid();
        var registry = new ProcessLifecycleRegistry(epoch);
        var original = registry.ApplyEvent(Event(epoch, LifecycleEventKind.Started, StartUtc));

        Assert.Throws<InvalidOperationException>(() => registry.ApplyBaseline(BrokenSnapshot(), StartUtc.AddSeconds(5)));

        Assert.Equal(original, Assert.Single(registry.GetLiveSnapshot()));
        Assert.Empty(registry.GetHistorySnapshot());
    }

    /// <summary>Выдаёт одну новую запись и затем воспроизводит контролируемый отказ источника.</summary>
    /// <returns>Последовательность, которая не может считаться полным авторитетным снимком.</returns>
    private static IEnumerable<(int Pid, int ParentPid, DateTime CreationTimeUtc, string ImageName)> BrokenSnapshot()
    {
        yield return (5678, 10, StartUtc, "partial.exe");
        throw new InvalidOperationException("Controlled snapshot enumeration failure");
    }

    /// <summary>Проверяет явную деградацию при превышении лимита, его жёсткость и восстановление последующим полным снимком.</summary>
    [Fact]
    public void ApplyBaseline_LiveCapacityExceeded_RemainsBoundedAndClearsFlagOnlyAfterSuccessfulSnapshot()
    {
        var registry = new ProcessLifecycleRegistry();
        var large = Enumerable.Range(1, ProcessLifecycleRegistry.MaxLiveCapacity + 1)
            .Select(pid => (pid, 0, StartUtc, "client.exe"));

        registry.ApplyBaseline(large, StartUtc.AddSeconds(1));

        Assert.True(registry.IsCapacityExceeded);
        Assert.Equal(ProcessLifecycleRegistry.MaxLiveCapacity, registry.ActiveProcessCount);
        registry.ApplyBaseline([(1, 0, StartUtc, "client.exe")], StartUtc.AddSeconds(2));
        Assert.False(registry.IsCapacityExceeded);
        Assert.Single(registry.GetLiveSnapshot());
    }

    /// <summary>Проверяет, что асинхронные метаданные старого поколения не прикрепляются к новому после сверки.</summary>
    [Fact]
    public void TryUpdateMetadata_PidReusedDuringReconciliation_RejectsOldWork()
    {
        var epoch = Guid.NewGuid();
        var registry = new ProcessLifecycleRegistry(epoch);
        var old = registry.ApplyEvent(Event(epoch, LifecycleEventKind.Started, StartUtc));
        Assert.NotNull(old);
        registry.ApplyBaseline([(1234, 10, StartUtc.AddMilliseconds(1), "client.exe")], StartUtc.AddSeconds(1));

        Assert.False(registry.TryUpdateMetadata(old.InstanceId, ProcessMetadataStatus.SignatureChecked, @"C:\Old\client.exe", "Old signer"));

        var current = Assert.Single(registry.GetLiveSnapshot());
        Assert.Equal(ProcessMetadataStatus.Unknown, current.MetadataStatus);
        Assert.Null(current.ResolvedExecutablePath);
        Assert.Null(current.Signer);
    }

    /// <summary>Проверяет приём ленивой подписи только для точного FILETIME и отсутствие повторного сигнала на cache-hit.</summary>
    [Fact]
    public void TryUpdateVerifiedMetadata_CurrentCreationTime_AppliesOnceWithoutSignalLoop()
    {
        var registry = new ProcessLifecycleRegistry();
        registry.ApplyBaseline([(1234, 10, StartUtc, "client.exe")], StartUtc.AddSeconds(1));
        var update = new StartupMonitor.Service.Services.ProcessMetadataUpdate(1234, StartUtc,
            @"C:\Apps\client.exe", "Verified signer", ProcessMetadataStatus.SignatureChecked);

        Assert.True(registry.TryUpdateVerifiedMetadata(update));
        var revision = registry.Revision;
        Assert.True(registry.TryUpdateVerifiedMetadata(update));

        Assert.Equal(revision, registry.Revision);
        var current = Assert.Single(registry.GetLiveSnapshot());
        Assert.Equal(ProcessMetadataStatus.SignatureChecked, current.MetadataStatus);
        Assert.Equal(update.ExecutablePath, current.ResolvedExecutablePath);
        Assert.Equal(update.Signer, current.Signer);
    }

    /// <summary>Проверяет отказ поздней подписи после PID reuse и отказ неизвестной identity rundown.</summary>
    [Fact]
    public void TryUpdateVerifiedMetadata_LateOldCreationOrUnknownIdentity_IsRejected()
    {
        var epoch = Guid.NewGuid();
        var registry = new ProcessLifecycleRegistry(epoch);
        var oldUpdate = new StartupMonitor.Service.Services.ProcessMetadataUpdate(1234, StartUtc,
            @"C:\Old\client.exe", "Old signer", ProcessMetadataStatus.SignatureChecked);
        registry.ApplyBaseline([(1234, 10, StartUtc.AddTicks(1), "new.exe")], StartUtc.AddSeconds(1));

        Assert.False(registry.TryUpdateVerifiedMetadata(oldUpdate));
        Assert.Equal(ProcessMetadataStatus.Unknown, Assert.Single(registry.GetLiveSnapshot()).MetadataStatus);
        var unknown = new ProcessLifecycleRegistry(epoch);
        unknown.ApplyEvent(Event(epoch, LifecycleEventKind.Rundown, StartUtc.AddSeconds(1)));
        Assert.False(unknown.TryUpdateVerifiedMetadata(oldUpdate));
        Assert.Null(Assert.Single(unknown.GetLiveSnapshot()).Signer);
    }

    /// <summary>Проверяет, что изменения исходного mutable правила не изменяют уже опубликованный снимок.</summary>
    [Fact]
    public void BlacklistRuleSnapshot_SourceRuleMutated_DoesNotChangePublishedPolicy()
    {
        var rule = new BlacklistRule
        {
            MatchType = BlacklistMatchType.Path,
            Action = BlacklistAction.NotifyAndRemove,
            Value = @"C:\Apps\client.exe"
        };
        var source = new List<BlacklistRule> { rule };
        var snapshot = new BlacklistRuleSnapshot(7, source);

        rule.Value = @"D:\Other\client.exe";
        rule.Action = BlacklistAction.NotifyOnly;
        source.Clear();

        var published = Assert.Single(snapshot.ActiveRules);
        Assert.Equal(@"C:\Apps\client.exe", published.Value);
        Assert.Equal(BlacklistAction.NotifyAndRemove, published.Action);
        Assert.True(ProcessRuleEvaluator.Evaluate("client.exe", @"C:\Apps\client.exe", snapshot.ActiveRules).IsMatched);
        Assert.False(ProcessRuleEvaluator.Evaluate("client.exe", @"D:\Other\client.exe", snapshot.ActiveRules).IsMatched);
    }
}
