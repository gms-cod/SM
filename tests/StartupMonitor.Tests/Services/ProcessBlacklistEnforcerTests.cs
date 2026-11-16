using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using StartupMonitor.Service.Services;
using StartupMonitor.Service.Services.ProcessLifecycle;
using StartupMonitor.Shared.Models;

namespace StartupMonitor.Tests.Services;

/// <summary>Verifies test scenario for ProcessBlacklistEnforcerTests.cs.</summary>
/// <summary>Verifies test scenario for ProcessBlacklistEnforcerTests.cs.</summary>
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

    /// <summary>Verifies test scenario for ProcessBlacklistEnforcerTests.cs.</summary>
    public ProcessBlacklistEnforcerTests()
    {
        _store = new JsonDataStore(_directory);
        var config = _store.Load();
        config.Blacklist.Add(new BlacklistRule { Id = "kill-rule", Value = @"C:\Apps\blocked.exe" });
        _store.Save(config);
        _coordinator = new MonitoringStateCoordinator(_store);
    }

    /// <summary>Verifies test scenario for ProcessBlacklistEnforcerTests.cs.</summary>
    /// <summary>Verifies test scenario for ProcessBlacklistEnforcerTests.cs.</summary>
    /// <summary>Verifies test scenario for ProcessBlacklistEnforcerTests.cs.</summary>
    /// <summary>Verifies test scenario for ProcessBlacklistEnforcerTests.cs.</summary>
    /// <summary>Verifies test scenario for ProcessBlacklistEnforcerTests.cs.</summary>
    private TargetState AddProcess(int pid = 1234, string name = "blocked.exe", string? path = @"C:\Apps\blocked.exe")
    {
        var created = DateTime.UtcNow.AddMinutes(-1).AddTicks(pid);
        _snapshot.Add((pid, 10, created, name));
        _registry.ApplyBaseline(_snapshot, DateTime.UtcNow);
        var state = new TargetState { CreationTimeUtc = created, ProcessName = name, ExecutablePath = path };
        _targets[pid] = state;
        return state;
    }

    /// <summary>Verifies test scenario for ProcessBlacklistEnforcerTests.cs.</summary>
    /// <summary>Verifies test scenario for ProcessBlacklistEnforcerTests.cs.</summary>
    /// <summary>Verifies test scenario for ProcessBlacklistEnforcerTests.cs.</summary>
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

    /// <summary>Verifies test scenario for ProcessBlacklistEnforcerTests.cs.</summary>
    /// <summary>Verifies test scenario for ProcessBlacklistEnforcerTests.cs.</summary>
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

    /// <summary>Verifies test scenario for ProcessBlacklistEnforcerTests.cs.</summary>
    /// <summary>Verifies test scenario for ProcessBlacklistEnforcerTests.cs.</summary>
    [Fact]
    public async Task Shadow_DoesNotKillOrSuppressLegacy()
    {
        var process = AddProcess();
        var enforcer = CreateEnforcer(ProcessLifecycleMode.Shadow);

        await enforcer.ProcessPendingAsync(CancellationToken.None);

        Assert.Equal(0, process.KillAttempts);
        Assert.False(enforcer.CanSuppressLegacySweep);
    }

    /// <summary>Verifies test scenario for ProcessBlacklistEnforcerTests.cs.</summary>
    /// <summary>Verifies test scenario for ProcessBlacklistEnforcerTests.cs.</summary>
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

    /// <summary>Verifies test scenario for ProcessBlacklistEnforcerTests.cs.</summary>
    /// <summary>Verifies test scenario for ProcessBlacklistEnforcerTests.cs.</summary>
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

    /// <summary>Verifies test scenario for ProcessBlacklistEnforcerTests.cs.</summary>
    /// <summary>Verifies test scenario for ProcessBlacklistEnforcerTests.cs.</summary>
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

    /// <summary>Verifies test scenario for ProcessBlacklistEnforcerTests.cs.</summary>
    /// <summary>Verifies test scenario for ProcessBlacklistEnforcerTests.cs.</summary>
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

    /// <summary>Verifies test scenario for ProcessBlacklistEnforcerTests.cs.</summary>
    /// <summary>Verifies test scenario for ProcessBlacklistEnforcerTests.cs.</summary>
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

    /// <summary>Verifies test scenario for ProcessBlacklistEnforcerTests.cs.</summary>
    /// <summary>Verifies test scenario for ProcessBlacklistEnforcerTests.cs.</summary>
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

    /// <summary>Verifies test scenario for ProcessBlacklistEnforcerTests.cs.</summary>
    /// <summary>Verifies test scenario for ProcessBlacklistEnforcerTests.cs.</summary>
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

    /// <summary>Verifies test scenario for ProcessBlacklistEnforcerTests.cs.</summary>
    /// <summary>Verifies test scenario for ProcessBlacklistEnforcerTests.cs.</summary>
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

    /// <summary>Verifies test scenario for ProcessBlacklistEnforcerTests.cs.</summary>
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
    /// <summary>Verifies test scenario for ProcessBlacklistEnforcerTests.cs.</summary>
    /// <summary>Verifies test scenario for ProcessBlacklistEnforcerTests.cs.</summary>
    /// </summary>
    [Fact]
    public void NativeTarget_VirtualSecureSystemWhenPresent_UnavailableImageRemainsUnknown()
    {
        var processes = Process.GetProcesses();
        try
        {
            var secure = processes.FirstOrDefault(p => string.Equals(p.ProcessName.Replace(" ", string.Empty),
                "SecureSystem", StringComparison.OrdinalIgnoreCase));
            if (secure == null) return; // VBS is an optional OS capability rather than a standard CI requirement.
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

    /// <summary>Verifies test scenario for ProcessBlacklistEnforcerTests.cs.</summary>
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

    /// <summary>Verifies test scenario for ProcessBlacklistEnforcerTests.cs.</summary>
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

    /// <summary>Verifies test scenario for ProcessBlacklistEnforcerTests.cs.</summary>
    /// <summary>Verifies test scenario for ProcessBlacklistEnforcerTests.cs.</summary>
    private static Process StartOwnedSleeper() => Process.Start(new ProcessStartInfo
    {
        FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe"),
        Arguments = "-NoProfile -NonInteractive -Command Start-Sleep -Seconds 30",
        UseShellExecute = false,
        CreateNoWindow = true
    }) ?? throw new InvalidOperationException("Не удалось создать принадлежащий тесту процесс.");

    /// <summary>Verifies test scenario for ProcessBlacklistEnforcerTests.cs.</summary>
    /// <summary>Verifies test scenario for ProcessBlacklistEnforcerTests.cs.</summary>
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
            // Verifies component behavior and assertions.
            // Verifies component behavior and assertions.
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(60));
            Assert.Equal(259, process.ExitCode);
            Assert.True(target.HasExited);
        }
        finally { if (!process.HasExited) process.Kill(entireProcessTree: true); }
    }

    /// <summary>Verifies test scenario for ProcessBlacklistEnforcerTests.cs.</summary>
    /// <summary>Verifies test scenario for ProcessBlacklistEnforcerTests.cs.</summary>
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
            // Verifies component behavior and assertions.
            // Verifies component behavior and assertions.
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

    /// <summary>Verifies test scenario for ProcessBlacklistEnforcerTests.cs.</summary>
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

    /// <summary>Verifies test scenario for ProcessBlacklistEnforcerTests.cs.</summary>
    /// <summary>Verifies test scenario for ProcessBlacklistEnforcerTests.cs.</summary>
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

    /// <summary>Verifies test scenario for ProcessBlacklistEnforcerTests.cs.</summary>
    /// <summary>Verifies test scenario for ProcessBlacklistEnforcerTests.cs.</summary>
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

    /// <summary>Verifies test scenario for ProcessBlacklistEnforcerTests.cs.</summary>
    /// <summary>Verifies test scenario for ProcessBlacklistEnforcerTests.cs.</summary>
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

    /// <summary>Verifies test scenario for ProcessBlacklistEnforcerTests.cs.</summary>
    /// <summary>Verifies test scenario for ProcessBlacklistEnforcerTests.cs.</summary>
    /// <summary>Verifies test scenario for ProcessBlacklistEnforcerTests.cs.</summary>
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

    /// <summary>Verifies test scenario for ProcessBlacklistEnforcerTests.cs.</summary>
    /// <summary>Verifies test scenario for ProcessBlacklistEnforcerTests.cs.</summary>
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

    /// <summary>Verifies test scenario for ProcessBlacklistEnforcerTests.cs.</summary>
    /// <summary>Verifies test scenario for ProcessBlacklistEnforcerTests.cs.</summary>
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

    /// <summary>Verifies test scenario for ProcessBlacklistEnforcerTests.cs.</summary>
    private sealed class TargetState
    {
        public DateTime CreationTimeUtc { get; set; }
        /// <summary>
        /// Provides operations for this member.
        /// </summary>
        public string ProcessName { get; init; } = string.Empty;
        public string? ExecutablePath { get; set; } = string.Empty;
        /// <summary>
        /// Provides operations for this member.
        /// </summary>
        public int KillAttempts { get; set; }
        /// <summary>
        /// Provides operations for this member.
        /// </summary>
        public int SuccessfulKills { get; set; }
        /// <summary>
        /// Provides operations for this member.
        /// </summary>
        public int FailuresRemaining { get; set; }
    }

    /// <summary>Verifies test scenario for ProcessBlacklistEnforcerTests.cs.</summary>
    /// <summary>Verifies test scenario for ProcessBlacklistEnforcerTests.cs.</summary>
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

        /// <summary>Verifies test scenario for ProcessBlacklistEnforcerTests.cs.</summary>
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

        /// <summary>Verifies test scenario for ProcessBlacklistEnforcerTests.cs.</summary>
        public void Dispose() => _disposed = true;
    }

    /// <summary>Verifies test scenario for ProcessBlacklistEnforcerTests.cs.</summary>
    private sealed class TestTimeProvider : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UtcNow;
        /// <summary>Verifies test scenario for ProcessBlacklistEnforcerTests.cs.</summary>
        public override DateTimeOffset GetUtcNow() => _now;
        /// <summary>Verifies test scenario for ProcessBlacklistEnforcerTests.cs.</summary>
        /// <summary>Verifies test scenario for ProcessBlacklistEnforcerTests.cs.</summary>
        public void Advance(TimeSpan duration) => _now += duration;
    }

    /// <summary>Verifies test scenario for ProcessBlacklistEnforcerTests.cs.</summary>
    private sealed class TestLogger : ILogger<ProcessBlacklistEnforcer>
    {
        /// <summary>
        /// Provides operations for this member.
        /// </summary>
        public int Warnings { get; private set; }
        /// <summary>Verifies test scenario for ProcessBlacklistEnforcerTests.cs.</summary>
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        /// <summary>Verifies test scenario for ProcessBlacklistEnforcerTests.cs.</summary>
        public bool IsEnabled(LogLevel logLevel) => true;
        /// <summary>Verifies test scenario for ProcessBlacklistEnforcerTests.cs.</summary>
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning) Warnings++;
        }
    }

    /// <summary>Verifies test scenario for ProcessBlacklistEnforcerTests.cs.</summary>
    public void Dispose()
    {
        _enforcer?.Dispose();
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
