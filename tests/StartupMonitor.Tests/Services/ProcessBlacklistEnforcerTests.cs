using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using StartupMonitor.Service.Services;
using StartupMonitor.Service.Services.ProcessLifecycle;
using StartupMonitor.Shared.Models;

namespace StartupMonitor.Tests.Services;

/// <summary>
/// Проверяет событийное применение правил ЧС к управляемым целям: catch-up, пауза,
/// версия правил, точный FILETIME и повтор отказавшего Kill. Процессы ОС не завершаются.
/// </summary>
public sealed class ProcessBlacklistEnforcerTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "StartupMonitorEventBlacklist_" + Guid.NewGuid().ToString("N"));
    private readonly JsonDataStore _store;
    private readonly MonitoringStateCoordinator _coordinator;
    private readonly ProcessLifecycleRegistry _registry = new();
    private readonly LifecycleIngress _ingress = new();
    private readonly List<(int Pid, int ParentPid, DateTime CreationTimeUtc, string ImageName)> _snapshot = [];
    private readonly Dictionary<int, TargetState> _targets = [];
    private ProcessBlacklistEnforcer? _enforcer;
    private int _snapshotCalls;
    private int _targetOpens;
    private Action? _onOpen;
    private ProcessLifecycleHealthState _health = ProcessLifecycleHealthState.Healthy;
    private readonly TestTimeProvider _time = new();
    private readonly TestLogger _logger = new();

    /// <summary>Создаёт изолированный store и координатор без службы уведомлений или реального IPC.</summary>
    public ProcessBlacklistEnforcerTests()
    {
        _store = new JsonDataStore(_directory);
        var config = _store.Load();
        config.Blacklist.Add(new BlacklistRule { Id = "kill-rule", Value = @"C:\Apps\blocked.exe" });
        _store.Save(config);
        _coordinator = new MonitoringStateCoordinator(_store);
    }

    /// <summary>Добавляет процесс в управляемый системный снимок и реестр с точным временем создания.</summary>
    /// <param name="pid">Тестовый PID.</param>
    /// <param name="name">Имя процесса.</param>
    /// <param name="path">Полный путь, возвращаемый удерживаемой целью.</param>
    /// <returns>Состояние, позволяющее проверить вызовы Kill и задать контролируемый отказ.</returns>
    private TargetState AddProcess(int pid = 1234, string name = "blocked.exe", string? path = @"C:\Apps\blocked.exe")
    {
        var created = DateTime.UtcNow.AddMinutes(-1).AddTicks(pid);
        _snapshot.Add((pid, 10, created, name));
        _registry.ApplyBaseline(_snapshot, DateTime.UtcNow);
        var state = new TargetState { CreationTimeUtc = created, ProcessName = name, ExecutablePath = path };
        _targets[pid] = state;
        return state;
    }

    /// <summary>Создаёт обработчик со снимком и удерживаемыми целями, полностью принадлежащими тесту.</summary>
    /// <param name="mode">Режим обработки ETW.</param>
    /// <returns>Обработчик, освобождаемый вместе с тестовым контекстом.</returns>
    private ProcessBlacklistEnforcer CreateEnforcer(ProcessLifecycleMode mode = ProcessLifecycleMode.EtwPreferred)
    {
        _enforcer = new ProcessBlacklistEnforcer(_store, _coordinator, _registry, _ingress, mode,
            () => _health,
            cancellation =>
            {
                cancellation.ThrowIfCancellationRequested();
                _snapshotCalls++;
                return _snapshot.ToArray();
            }, _logger,
            pid => { _targetOpens++; _onOpen?.Invoke(); return new Target(_targets[pid]); }, _time);
        return _enforcer;
    }

    /// <summary>Проверяет, что Legacy не запускает событийную оценку или дополнительный снимок.</summary>
    /// <returns>Задача обработки одного управляемого пакета.</returns>
    [Fact]
    public async Task Legacy_DoesNotOpenTargetsOrCaptureSnapshot()
    {
        var process = AddProcess();
        var enforcer = CreateEnforcer(ProcessLifecycleMode.Legacy);

        await enforcer.ProcessPendingAsync(CancellationToken.None);

        Assert.Equal(0, _snapshotCalls);
        Assert.Equal(0, _targetOpens);
        Assert.Equal(0, process.KillAttempts);
        Assert.False(enforcer.CanSuppressLegacySweep);
    }

    /// <summary>Проверяет сохранение legacy-защиты и отсутствие Kill в теневом режиме.</summary>
    /// <returns>Задача обработки одного управляемого пакета.</returns>
    [Fact]
    public async Task Shadow_DoesNotKillOrSuppressLegacy()
    {
        var process = AddProcess();
        var enforcer = CreateEnforcer(ProcessLifecycleMode.Shadow);

        await enforcer.ProcessPendingAsync(CancellationToken.None);

        Assert.Equal(0, process.KillAttempts);
        Assert.False(enforcer.CanSuppressLegacySweep);
    }

    /// <summary>Проверяет первоначальный catch-up уже работающего запрещённого процесса и идемпотентность повторного пакета.</summary>
    /// <returns>Задача обработки двух последовательных пакетов.</returns>
    [Fact]
    public async Task Preferred_ExistingMatchingProcess_IsKilledOnceAfterCatchUp()
    {
        var process = AddProcess();
        var enforcer = CreateEnforcer();
        Assert.False(enforcer.CanSuppressLegacySweep);

        await enforcer.ProcessPendingAsync(CancellationToken.None);
        await enforcer.ProcessPendingAsync(CancellationToken.None);

        Assert.Equal(1, process.KillAttempts);
        Assert.Equal(1, process.SuccessfulKills);
        Assert.True(enforcer.CanSuppressLegacySweep);
    }

    /// <summary>Проверяет немедленное возвращение legacy-защиты при переполнении реестра после успешного catch-up.</summary>
    /// <returns>Задача двух пакетов без создания или завершения процессов ОС.</returns>
    [Fact]
    public async Task Preferred_RegistryCapacityExceeded_DoesNotSuppressFallbackOrOpenPartialTargets()
    {
        AddProcess();
        var enforcer = CreateEnforcer();
        await enforcer.ProcessPendingAsync(CancellationToken.None);
        Assert.True(enforcer.CanSuppressLegacySweep);
        var opens = _targetOpens;
        var created = DateTime.UtcNow.AddMinutes(-2);
        _registry.ApplyBaseline(Enumerable.Range(1, ProcessLifecycleRegistry.MaxLiveCapacity + 1)
            .Select(pid => (pid, 0, created, "unrelated.exe")), DateTime.UtcNow);

        Assert.False(enforcer.CanSuppressLegacySweep);
        await enforcer.ProcessPendingAsync(CancellationToken.None);

        Assert.Equal(opens, _targetOpens);
        Assert.False(enforcer.CanSuppressLegacySweep);
    }

    /// <summary>Проверяет отказ от Kill, если удерживаемый дескриптор принадлежит другому экземпляру PID.</summary>
    /// <returns>Задача оценки устаревшего решения.</returns>
    [Fact]
    public async Task Preferred_CreationFileTimeMismatch_DoesNotKillReusedPid()
    {
        var process = AddProcess();
        process.CreationTimeUtc = process.CreationTimeUtc.AddTicks(1);
        var enforcer = CreateEnforcer();

        await enforcer.ProcessPendingAsync(CancellationToken.None);

        Assert.Equal(0, process.KillAttempts);
        Assert.False(enforcer.CanSuppressLegacySweep);
    }

    /// <summary>Проверяет, что отказ Kill не попадает в кэш успешных действий и сохраняет fallback до повторного успеха.</summary>
    /// <returns>Задача двух попыток применения правила.</returns>
    [Fact]
    public async Task Preferred_KillFails_RetriesAndKeepsFallbackUntilSuccess()
    {
        var process = AddProcess();
        process.FailuresRemaining = 1;
        var enforcer = CreateEnforcer();

        await enforcer.ProcessPendingAsync(CancellationToken.None);
        Assert.Equal(1, process.KillAttempts);
        Assert.Equal(0, process.SuccessfulKills);
        Assert.False(enforcer.CanSuppressLegacySweep);
        await enforcer.ProcessPendingAsync(CancellationToken.None);
        Assert.Equal(1, process.KillAttempts);
        _time.Advance(TimeSpan.FromSeconds(1));
        await enforcer.ProcessPendingAsync(CancellationToken.None);

        Assert.Equal(2, process.KillAttempts);
        Assert.Equal(1, process.SuccessfulKills);
    }

    /// <summary>Отказ query для PPL при широкой маске не вызывает Kill, повторное открытие или шторм предупреждений.</summary>
    /// <returns>Два прохода доступной unknown-path политики без взаимодействия с процессами ОС.</returns>
    [Fact]
    public async Task Preferred_QueryDeniedUnmatchedBroadWildcard_CompletesAvailablePolicyWithoutWarningStorm()
    {
        var process = AddProcess(name: "MpDefenderCoreService.exe");
        var config = _store.Load();
        config.Blacklist[0].Value = "*teams*";
        config.Blacklist[0].MatchType = BlacklistMatchType.PathWildcard;
        _store.Save(config);
        _onOpen = () => throw new Win32Exception(5);
        var enforcer = CreateEnforcer();

        await enforcer.ProcessPendingAsync(CancellationToken.None);
        await enforcer.ProcessPendingAsync(CancellationToken.None);

        Assert.Equal(1, _targetOpens);
        Assert.Equal(0, process.KillAttempts);
        Assert.Equal(ProcessMetadataStatus.Unknown, _registry.GetLiveProcess(1234)?.MetadataStatus);
        Assert.Equal(0, _logger.Warnings);
        Assert.True(enforcer.CanSuppressLegacySweep);
    }

    /// <summary>Совпавшее имя при запрете query сохраняет fallback и повторяется с backoff до восстановления доступа.</summary>
    /// <returns>Проверка двух отложенных отказов и последующего успешного применения.</returns>
    [Fact]
    public async Task Preferred_QueryDeniedMatchedName_RetriesWithBoundedBackoffAndLogsOnlyFirstFailure()
    {
        var process = AddProcess();
        var config = _store.Load();
        config.Blacklist[0].Value = "*blocked.exe";
        config.Blacklist[0].MatchType = BlacklistMatchType.PathWildcard;
        _store.Save(config);
        _onOpen = () => throw new Win32Exception(5);
        var enforcer = CreateEnforcer();

        await enforcer.ProcessPendingAsync(CancellationToken.None);
        await enforcer.ProcessPendingAsync(CancellationToken.None);
        Assert.Equal(1, _targetOpens);
        Assert.False(enforcer.CanSuppressLegacySweep);
        _time.Advance(TimeSpan.FromSeconds(1));
        await enforcer.ProcessPendingAsync(CancellationToken.None);
        Assert.Equal(2, _targetOpens);
        Assert.Equal(1, _logger.Warnings);
        _onOpen = null;
        _time.Advance(TimeSpan.FromSeconds(2));
        await enforcer.ProcessPendingAsync(CancellationToken.None);

        Assert.Equal(1, process.SuccessfulKills);
        Assert.True(enforcer.CanSuppressLegacySweep);
    }

    /// <summary>Изменение версии правил немедленно отменяет backoff и позволяет обработать восстановившуюся цель.</summary>
    /// <returns>Оценка сразу после изменения политики без продвижения часов.</returns>
    [Fact]
    public async Task Preferred_RulesChangedDuringBackoff_ReevaluatesImmediately()
    {
        var process = AddProcess();
        process.FailuresRemaining = 1;
        var enforcer = CreateEnforcer();
        await enforcer.ProcessPendingAsync(CancellationToken.None);
        var config = _store.Load();
        config.Blacklist[0].Value = "*blocked.exe";
        config.Blacklist[0].MatchType = BlacklistMatchType.PathWildcard;
        _store.Save(config);

        await enforcer.ProcessPendingAsync(CancellationToken.None);

        Assert.Equal(2, process.KillAttempts);
        Assert.Equal(1, process.SuccessfulKills);
    }

    /// <summary>Недоступный путь сохраняет Unknown; успешное последующее обогащение повторно проверяет экземпляр.</summary>
    /// <returns>Проверка catch-up доступных данных и инвалидации stamp при Basic metadata.</returns>
    [Fact]
    public async Task Preferred_QuerySucceedsButPathUnknown_CompletesWithoutFalselyClaimingBasicMetadata()
    {
        var process = AddProcess(path: null);
        var enforcer = CreateEnforcer();
        await enforcer.ProcessPendingAsync(CancellationToken.None);
        Assert.True(enforcer.CanSuppressLegacySweep);
        Assert.Equal(ProcessMetadataStatus.Unknown, _registry.GetLiveProcess(1234)?.MetadataStatus);
        Assert.Equal(0, process.KillAttempts);
        process.ExecutablePath = @"C:\Apps\blocked.exe";
        var record = _registry.GetLiveProcess(1234);
        Assert.NotNull(record);
        Assert.True(_registry.TryUpdateMetadata(record.InstanceId, ProcessMetadataStatus.Basic, process.ExecutablePath));

        await enforcer.ProcessPendingAsync(CancellationToken.None);

        Assert.Equal(1, process.SuccessfulKills);
    }

    /// <summary>Реальный query-only target читает собственный процесс без запроса прав завершения или системного снимка.</summary>
    [Fact]
    public void NativeTarget_ReadsCurrentProcessWithoutCapturingSnapshot()
    {
        using var process = Process.GetCurrentProcess();
        using var target = new ProcessBlacklistEnforcer.WindowsProcessBlacklistTarget(process.Id,
            _ => throw new InvalidOperationException("Read-only path must not enumerate system processes"));

        Assert.Equal(process.StartTime.ToUniversalTime(), target.CreationTimeUtc);
        Assert.False(target.HasExited);
        Assert.False(string.IsNullOrEmpty(target.ExecutablePath));
        Assert.False(string.IsNullOrEmpty(target.ProcessName));
    }

    /// <summary>
    /// На хостах с VBS проверяет реальный Secure System, у которого Windows не предоставляет Win32 image.
    /// Отсутствие такого дополнительного процесса в CI не отменяет детерминированные unknown-path проверки выше.
    /// </summary>
    [Fact]
    public void NativeTarget_VirtualSecureSystemWhenPresent_UnavailableImageRemainsUnknown()
    {
        var processes = Process.GetProcesses();
        try
        {
            var secure = processes.FirstOrDefault(p => string.Equals(p.ProcessName.Replace(" ", string.Empty),
                "SecureSystem", StringComparison.OrdinalIgnoreCase));
            if (secure == null) return; // VBS является дополнительной возможностью ОС, а не требованием обычного CI.
            using var target = new ProcessBlacklistEnforcer.WindowsProcessBlacklistTarget(secure.Id,
                _ => throw new InvalidOperationException("Read-only image inspection must not capture a tree snapshot"), secure.ProcessName);
            Assert.False(target.HasExited);
            Assert.True(target.CreationTimeUtc > DateTime.MinValue);
            Assert.Null(target.ExecutablePath);
            Assert.Equal(secure.ProcessName, target.ProcessName);
        }
        finally
        {
            foreach (var process in processes) process.Dispose();
        }
    }

    /// <summary>Native Kill использует проверенный action handle и завершает только созданный этим тестом процесс.</summary>
    [Fact]
    public void NativeTarget_KillTerminatesOwnedProcessWithVerifiedHandle()
    {
        using var process = StartOwnedSleeper();
        try
        {
            using var target = new ProcessBlacklistEnforcer.WindowsProcessBlacklistTarget(process.Id,
                _ => Array.Empty<(int, int, DateTime, string)>());
            Assert.Equal(process.StartTime.ToUniversalTime(), target.CreationTimeUtc);
            target.Kill();
            Assert.True(process.WaitForExit(5000));
            Assert.True(target.HasExited);
        }
        finally { if (!process.HasExited) process.Kill(entireProcessTree: true); }
    }

    /// <summary>Отказ native-снимка не завершает родителя до проверки дерева и не выдаёт частичное действие за успех.</summary>
    [Fact]
    public void NativeTarget_SnapshotFailure_DoesNotTerminateOwnedProcess()
    {
        using var process = StartOwnedSleeper();
        try
        {
            using var target = new ProcessBlacklistEnforcer.WindowsProcessBlacklistTarget(process.Id,
                _ => throw new IOException("Controlled snapshot failure"));
            Assert.Throws<IOException>(() => target.Kill());
            Assert.False(process.HasExited);
        }
        finally { if (!process.HasExited) process.Kill(entireProcessTree: true); }
    }

    /// <summary>Создаёт только принадлежащий тесту процесс без окон, автозагрузки, сетевой активности и изменения файлов.</summary>
    /// <returns>Владелец процесса, обязательно завершаемого в finally каждого native-теста.</returns>
    private static Process StartOwnedSleeper() => Process.Start(new ProcessStartInfo
    {
        FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe"),
        Arguments = "-NoProfile -NonInteractive -Command Start-Sleep -Seconds 30",
        UseShellExecute = false,
        CreateNoWindow = true
    }) ?? throw new InvalidOperationException("Не удалось создать принадлежащий тесту процесс.");

    /// <summary>Signal удерживаемого query handle распознаёт реальный выход 259 и не путает его с STILL_ACTIVE.</summary>
    /// <returns>Задача собственного короткоживущего процесса, завершаемого в finally при сбое теста.</returns>
    [Fact]
    public async Task NativeTarget_ExitCode259_IsReportedExited()
    {
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe"),
            Arguments = "-NoProfile -NonInteractive -Command Start-Sleep -Milliseconds 500; exit 259",
            UseShellExecute = false,
            CreateNoWindow = true
        }) ?? throw new InvalidOperationException("Не удалось создать собственный процесс выхода 259.");
        try
        {
            using var target = new ProcessBlacklistEnforcer.WindowsProcessBlacklistTarget(process.Id,
                _ => Array.Empty<(int, int, DateTime, string)>());
            // Холодный запуск PowerShell на CI может занять больше пяти секунд;
            // этот бюджет относится к подготовке процесса, а не к скорости native Kill.
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(60));
            Assert.Equal(259, process.ExitCode);
            Assert.True(target.HasExited);
        }
        finally { if (!process.HasExited) process.Kill(entireProcessTree: true); }
    }

    /// <summary>Native tree Kill завершает собственного родителя и ребёнка, пропуская исчезнувший PID из того же снимка.</summary>
    /// <returns>Задача двух принадлежащих тесту процессов; оба освобождаются даже при ошибке проверки.</returns>
    [Fact]
    public async Task NativeTarget_TreeKillTerminatesOwnedDescendantAndSkipsDisappearedChild()
    {
        using var parent = Process.Start(new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe"),
            Arguments = "-NoProfile -NonInteractive -Command \"$child = Start-Process powershell.exe -ArgumentList '-NoProfile -NonInteractive -Command Start-Sleep -Seconds 120' -WindowStyle Hidden -PassThru; [Console]::WriteLine($child.Id); Start-Sleep -Seconds 120\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true
        }) ?? throw new InvalidOperationException("Не удалось создать собственное дерево процессов.");
        Process? child = null;
        try
        {
            // Ожидание готовности учитывает холодный запуск двух PowerShell на runner.
            // Жизнь тестового дерева превышает этот бюджет; Kill по-прежнему проверяется за пять секунд.
            var childIdText = await parent.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(60));
            Assert.True(int.TryParse(childIdText, out var childId));
            child = Process.GetProcessById(childId);
            var parentTime = parent.StartTime.ToUniversalTime();
            var childTime = child.StartTime.ToUniversalTime();
            using var target = new ProcessBlacklistEnforcer.WindowsProcessBlacklistTarget(parent.Id,
                _ => [(parent.Id, Environment.ProcessId, parentTime, "powershell.exe"),
                      (child.Id, parent.Id, childTime, "powershell.exe"),
                      (int.MaxValue, parent.Id, DateTime.UtcNow, "already-gone.exe")]);

            target.Kill();

            Assert.True(parent.WaitForExit(5000));
            Assert.True(child.WaitForExit(5000));
        }
        finally
        {
            if (!parent.HasExited) parent.Kill(entireProcessTree: true);
            if (child != null)
            {
                if (!child.HasExited) child.Kill(entireProcessTree: true);
                child.Dispose();
            }
        }
    }

    /// <summary>Потомок с заменённым FILETIME исключается из дерева; чужой экземпляр того же PID остаётся работать.</summary>
    [Fact]
    public void NativeTarget_DescendantIdentityMismatch_SkipsUnrelatedInstance()
    {
        using var parent = StartOwnedSleeper();
        using var unrelated = StartOwnedSleeper();
        try
        {
            using var target = new ProcessBlacklistEnforcer.WindowsProcessBlacklistTarget(parent.Id,
                _ => [(unrelated.Id, parent.Id, unrelated.StartTime.ToUniversalTime().AddTicks(1), "reused-pid.exe")]);

            target.Kill();

            Assert.True(parent.WaitForExit(5000));
            Assert.False(unrelated.HasExited);
        }
        finally
        {
            if (!parent.HasExited) parent.Kill(entireProcessTree: true);
            if (!unrelated.HasExited) unrelated.Kill(entireProcessTree: true);
        }
    }

    /// <summary>Проверяет повторную оценку уже существующих процессов после изменения версии правил.</summary>
    /// <returns>Задача оценки до и после изменения политики.</returns>
    [Fact]
    public async Task Preferred_RulesChanged_ReevaluatesAlreadyRunningProcess()
    {
        var process = AddProcess(path: @"D:\Other\blocked.exe");
        var enforcer = CreateEnforcer();
        await enforcer.ProcessPendingAsync(CancellationToken.None);
        Assert.Equal(0, process.KillAttempts);
        var config = _store.Load();
        config.Blacklist[0].Value = @"D:\Other\blocked.exe";
        _store.Save(config);
        Assert.False(enforcer.CanSuppressLegacySweep);

        await enforcer.ProcessPendingAsync(CancellationToken.None);

        Assert.Equal(1, process.SuccessfulKills);
    }

    /// <summary>Проверяет catch-up после паузы: процесс, появившийся во время паузы, оценивается без нового ETW Start.</summary>
    /// <returns>Задача оценки переходов паузы.</returns>
    [Fact]
    public async Task Preferred_Resume_CatchesUpProcessThatStartedWhilePaused()
    {
        var enforcer = CreateEnforcer();
        await enforcer.ProcessPendingAsync(CancellationToken.None);
        _coordinator.SetPaused(true);
        var process = AddProcess();
        await enforcer.ProcessPendingAsync(CancellationToken.None);
        Assert.Equal(0, process.KillAttempts);
        Assert.False(enforcer.CanSuppressLegacySweep);
        _coordinator.SetPaused(false);

        await enforcer.ProcessPendingAsync(CancellationToken.None);

        Assert.Equal(1, process.SuccessfulKills);
    }

    /// <summary>Проверяет сохранение исключений для защищённого script host при совпадающем правиле пути.</summary>
    /// <returns>Задача оценки защищённого процесса.</returns>
    [Fact]
    public async Task Preferred_ProtectedHost_DoesNotKillEvenWhenExactPathMatches()
    {
        var process = AddProcess(name: "powershell.exe", path: @"C:\Apps\powershell.exe");
        var config = _store.Load();
        config.Blacklist[0].Value = @"C:\Apps\powershell.exe";
        _store.Save(config);
        var enforcer = CreateEnforcer();

        await enforcer.ProcessPendingAsync(CancellationToken.None);

        Assert.Equal(0, process.KillAttempts);
    }

    /// <summary>Проверяет повторную проверку полномочий непосредственно перед Kill после открытия удерживаемого дескриптора.</summary>
    /// <param name="change">Изменение, отменяющее ранее найденное совпадение.</param>
    /// <returns>Задача оценки решения, устаревшего во время открытия цели.</returns>
    [Theory]
    [InlineData("Pause")]
    [InlineData("RemoveRule")]
    [InlineData("NewGeneration")]
    public async Task Preferred_AuthorityChangesWhileOpeningTarget_DoesNotExecuteStaleKill(string change)
    {
        var process = AddProcess();
        _onOpen = () =>
        {
            switch (change)
            {
                case "Pause":
                    _coordinator.SetPaused(true);
                    break;
                case "RemoveRule":
                    var config = _store.Load();
                    config.Blacklist.Clear();
                    _store.Save(config);
                    break;
                case "NewGeneration":
                    _registry.ApplyBaseline([(1234, 10, process.CreationTimeUtc.AddTicks(1), "blocked.exe")], DateTime.UtcNow);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(change));
            }
        };
        var enforcer = CreateEnforcer();

        await enforcer.ProcessPendingAsync(CancellationToken.None);

        Assert.Equal(0, process.KillAttempts);
        Assert.False(enforcer.CanSuppressLegacySweep);
    }

    /// <summary>Проверяет сохранение fallback при потере согласованности до подтверждения сверки.</summary>
    /// <returns>Задача оценки до и после подтверждения reconciliation.</returns>
    [Fact]
    public async Task Preferred_ReconciliationPending_DefersActionsUntilAcknowledged()
    {
        var process = AddProcess();
        var enforcer = CreateEnforcer();
        var generation = _ingress.RequestReconciliation();

        await enforcer.ProcessPendingAsync(CancellationToken.None);
        Assert.Equal(0, process.KillAttempts);
        Assert.Equal(0, _targetOpens);
        Assert.False(enforcer.CanSuppressLegacySweep);
        _ingress.AcknowledgeReconciliation(generation);
        await enforcer.ProcessPendingAsync(CancellationToken.None);

        Assert.Equal(1, process.SuccessfulKills);
    }

    /// <summary>Проверяет сохранение legacy-прохода до catch-up, при деградации и запрет прохода на паузе.</summary>
    /// <returns>Задача первоначального catch-up.</returns>
    [Fact]
    public async Task RunFallbackSweep_RespectsCatchUpHealthAndPause()
    {
        AddProcess();
        var enforcer = CreateEnforcer();
        var sweeps = 0;
        enforcer.RunFallbackSweep(() => sweeps++);
        Assert.Equal(1, sweeps);
        await enforcer.ProcessPendingAsync(CancellationToken.None);
        enforcer.RunFallbackSweep(() => sweeps++);
        Assert.Equal(1, sweeps);
        _health = ProcessLifecycleHealthState.Degraded;
        enforcer.RunFallbackSweep(() => sweeps++);
        Assert.Equal(2, sweeps);
        _coordinator.SetPaused(true);
        enforcer.RunFallbackSweep(() => sweeps++);
        Assert.Equal(2, sweeps);
    }

    /// <summary>Хранит независимые от lifetime дескриптора наблюдения тестового процесса.</summary>
    private sealed class TargetState
    {
        public DateTime CreationTimeUtc { get; set; }
        public string ProcessName { get; init; } = string.Empty;
        public string? ExecutablePath { get; set; } = string.Empty;
        public int KillAttempts { get; set; }
        public int SuccessfulKills { get; set; }
        public int FailuresRemaining { get; set; }
    }

    /// <summary>Управляемый удерживаемый дескриптор: Kill только меняет счётчики и не вызывает API процессов ОС.</summary>
    /// <param name="state">Общее наблюдаемое состояние цели.</param>
    private sealed class Target(TargetState state) : IProcessBlacklistTarget
    {
        private bool _disposed;
        /// <inheritdoc />
        public DateTime CreationTimeUtc => state.CreationTimeUtc;
        /// <inheritdoc />
        public string ProcessName => state.ProcessName;
        /// <inheritdoc />
        public string? ExecutablePath => state.ExecutablePath;
        /// <inheritdoc />
        public bool HasExited => state.SuccessfulKills > 0;

        /// <summary>Регистрирует попытку либо выбрасывает заранее заданный контролируемый отказ.</summary>
        public void Kill()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            state.KillAttempts++;
            if (state.FailuresRemaining > 0)
            {
                state.FailuresRemaining--;
                throw new InvalidOperationException("Controlled process termination failure");
            }
            state.SuccessfulKills++;
        }

        /// <summary>Управляемая цель не владеет ресурсами ОС; состояние наблюдения сохраняется для утверждений теста.</summary>
        public void Dispose() => _disposed = true;
    }

    /// <summary>Управляемые монотонно продвигаемые часы исключают sleep в проверках bounded retry.</summary>
    private sealed class TestTimeProvider : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UtcNow;
        /// <summary>Возвращает текущее управляемое время UTC.</summary>
        public override DateTimeOffset GetUtcNow() => _now;
        /// <summary>Продвигает время без блокировки тестового worker.</summary>
        /// <param name="duration">Положительная длительность до следующей попытки.</param>
        public void Advance(TimeSpan duration) => _now += duration;
    }

    /// <summary>Считает предупреждения, чтобы тест обнаруживал возврат ежесекундного потока одинаковых отказов.</summary>
    private sealed class TestLogger : ILogger<ProcessBlacklistEnforcer>
    {
        public int Warnings { get; private set; }
        /// <summary>Scope не владеет ресурсами в изолированном тестовом журнале.</summary>
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        /// <summary>Все уровни включены, чтобы проверка не скрывала повторные предупреждения фильтром.</summary>
        public bool IsEnabled(LogLevel logLevel) => true;
        /// <summary>Сохраняет число предупреждений, не записывая тестовые исключения в пользовательский журнал.</summary>
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning) Warnings++;
        }
    }

    /// <summary>Снимает подписки обработчика до удаления только уникального каталога текущего теста.</summary>
    public void Dispose()
    {
        _enforcer?.Dispose();
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
