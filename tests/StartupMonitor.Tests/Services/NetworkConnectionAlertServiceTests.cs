using StartupMonitor.Service.Services;
using StartupMonitor.Service.Services.Firewall;
using StartupMonitor.Shared.Models;
using StartupMonitor.Shared.Models.Firewall;

namespace StartupMonitor.Tests.Services;

/// <summary>
/// Модульные тесты детектора новых сетевых подключений
/// (<see cref="NetworkConnectionAlertService"/>): фильтрация частных адресов
/// и системных процессов, обработка решений AllowAlways / BlockAlways / AllowSession.
/// </summary>
public sealed class NetworkConnectionAlertServiceTests
{
    private const string EvilPath = @"C:\Apps\evil.exe";

    private static ProcessNetworkSocket Sock(
        int pid,
        string processName,
        string remoteAddress,
        int remotePort,
        string protocol = "TCP")
    {
        return new ProcessNetworkSocket
        {
            ProcessId = pid,
            ProcessName = processName,
            Protocol = protocol,
            LocalAddress = "192.168.1.10",
            LocalPort = 50000,
            RemoteAddress = remoteAddress,
            RemotePort = remotePort,
            State = "ESTABLISHED",
        };
    }

    private static NetworkConnectionAlertService CreateService(
        List<ProcessNetworkSocket>? sockets = null,
        Dictionary<int, string>? pathsByPid = null,
        Func<string, bool>? hasRule = null,
        List<FirewallRuleDto>? appliedRules = null,
        JsonDataStore? store = null)
    {
        var svc = new NetworkConnectionAlertService(
            new ProcessNetworkTracker(),
            new WindowsFirewallService(),
            store)
        {
            SocketsProvider = () => sockets ?? new List<ProcessNetworkSocket>(),
            ProcessPathResolver = pid =>
                pathsByPid != null && pathsByPid.TryGetValue(pid, out var p) ? p : null,
            HasFirewallRuleOverride = hasRule ?? (_ => false),
            ApplyFirewallRuleOverride = rule => appliedRules?.Add(rule),
        };
        return svc;
    }

    private static Dictionary<int, string> Paths(params (int Pid, string Path)[] pairs) =>
        pairs.ToDictionary(p => p.Pid, p => p.Path);

    // ── Фильтрация частных адресов ──

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("10.0.0.5")]
    [InlineData("192.168.1.5")]
    [InlineData("172.16.5.5")]
    [InlineData("169.254.10.20")]
    [InlineData("*")]
    [InlineData("0.0.0.0")]
    [InlineData("::")]
    [InlineData("::1")]
    public void ScanNewConnections_PrivateAddresses_AreFiltered(string remote)
    {
        var svc = CreateService(
            new List<ProcessNetworkSocket> { Sock(1234, "evil.exe", remote, 443) },
            Paths((1234, EvilPath)));

        var added = svc.ScanNewConnections();

        Assert.Equal(0, added);
        Assert.Equal(0, svc.PendingCount);
    }

    [Fact]
    public void ScanNewConnections_UdpListeningSocketWithoutRemote_IsFiltered()
    {
        var svc = CreateService(
            new List<ProcessNetworkSocket> { Sock(1234, "evil.exe", "*", 0, "UDP") },
            Paths((1234, EvilPath)));

        Assert.Equal(0, svc.ScanNewConnections());
    }

    // ── Фильтрация системных процессов ──

    [Theory]
    [InlineData("System")]
    [InlineData("svchost.exe")]
    [InlineData("SVCHOST.EXE")]
    [InlineData("lsass.exe")]
    [InlineData("services.exe")]
    [InlineData("StartupMonitor.Service.exe")]
    [InlineData("StartupMonitor.UI.exe")]
    public void ScanNewConnections_SystemProcesses_AreIgnored(string processName)
    {
        var svc = CreateService(
            new List<ProcessNetworkSocket> { Sock(4, processName, "8.8.8.8", 443) },
            Paths((4, @"C:\Windows\System32\" + processName.TrimEnd('m', 'M'))));

        var added = svc.ScanNewConnections();

        Assert.Equal(0, added);
        Assert.Empty(svc.GetPendingAlerts());
    }

    [Theory]
    [InlineData("svchost.exe", true)]
    [InlineData("SVCHOST.EXE", true)]
    [InlineData("System", true)]
    [InlineData("notepad.exe", false)]
    [InlineData("evil.exe", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsTrustedProcess_MatchesExpected(string? processName, bool expected)
    {
        Assert.Equal(expected, NetworkConnectionAlertService.IsTrustedProcess(processName, null));
    }

    [Fact]
    public void IsTrustedProcess_MatchesByProcessPathFileName()
    {
        Assert.True(NetworkConnectionAlertService.IsTrustedProcess(
            "unknown", @"C:\Windows\System32\svchost.exe"));
        Assert.False(NetworkConnectionAlertService.IsTrustedProcess(
            "unknown", @"C:\Apps\evil.exe"));
    }

    // ── Создание алертов ──

    [Fact]
    public void ScanNewConnections_PublicAddress_CreatesAlertWithFields()
    {
        var before = DateTime.UtcNow.AddMinutes(-1);
        var svc = CreateService(
            new List<ProcessNetworkSocket> { Sock(1234, "evil.exe", "93.184.216.34", 443) },
            Paths((1234, EvilPath)));

        var added = svc.ScanNewConnections();
        var pending = svc.GetPendingAlerts();

        Assert.Equal(1, added);
        var alert = Assert.Single(pending);
        Assert.False(string.IsNullOrWhiteSpace(alert.AlertId));
        Assert.Equal(1234, alert.ProcessId);
        Assert.Equal("evil.exe", alert.ProcessName);
        Assert.Equal(EvilPath, alert.ProcessPath);
        Assert.Equal("TCP", alert.Protocol);
        Assert.Equal("93.184.216.34", alert.RemoteAddress);
        Assert.Equal(443, alert.RemotePort);
        Assert.True(alert.DetectedAt >= before);
    }

    [Fact]
    public void ScanNewConnections_SameConnectionTwice_ProducesSingleAlert()
    {
        var sockets = new List<ProcessNetworkSocket>
        {
            Sock(1234, "evil.exe", "93.184.216.34", 443),
        };
        var svc = CreateService(sockets, Paths((1234, EvilPath)));

        Assert.Equal(1, svc.ScanNewConnections());
        Assert.Equal(0, svc.ScanNewConnections());
        Assert.Equal(1, svc.PendingCount);
    }

    [Fact]
    public void ScanNewConnections_DifferentRemotes_ProduceSeparateAlerts()
    {
        var svc = CreateService(
            new List<ProcessNetworkSocket>
            {
                Sock(1234, "evil.exe", "93.184.216.34", 443),
                Sock(1234, "evil.exe", "1.1.1.1", 80),
            },
            Paths((1234, EvilPath)));

        Assert.Equal(2, svc.ScanNewConnections());
        Assert.Equal(2, svc.GetPendingAlerts().Count);
    }

    [Fact]
    public void ScanNewConnections_ExistingFirewallRule_SkipsAlert()
    {
        var svc = CreateService(
            new List<ProcessNetworkSocket> { Sock(1234, "evil.exe", "93.184.216.34", 443) },
            Paths((1234, EvilPath)),
            hasRule: _ => true);

        Assert.Equal(0, svc.ScanNewConnections());
        Assert.Empty(svc.GetPendingAlerts());
    }

    // ── Резолв алертов ──

    private static NetworkConnectionAlertDto ScanSingle(NetworkConnectionAlertService svc)
    {
        Assert.Equal(1, svc.ScanNewConnections());
        return Assert.Single(svc.GetPendingAlerts());
    }

    [Fact]
    public void ResolveAlert_AllowAlways_CreatesAllowRuleAndRemovesAlert()
    {
        var applied = new List<FirewallRuleDto>();
        var svc = CreateService(
            new List<ProcessNetworkSocket> { Sock(1234, "evil.exe", "93.184.216.34", 443) },
            Paths((1234, EvilPath)),
            appliedRules: applied);
        var alert = ScanSingle(svc);

        var ok = svc.ResolveAlert(new ResolveNetworkAlertRequest
        {
            AlertId = alert.AlertId,
            ProcessPath = EvilPath,
            ProcessName = "evil.exe",
            RemoteAddress = "93.184.216.34",
            Resolution = NetworkAlertResolution.AllowAlways,
        });

        Assert.True(ok);
        Assert.Empty(svc.GetPendingAlerts());
        var rule = Assert.Single(applied);
        Assert.Equal(FirewallAction.Allow, rule.Action);
        Assert.Equal(EvilPath, rule.ApplicationPath);
        Assert.True(rule.Enabled);
        var key = NetworkConnectionAlertService.BuildSessionKey(EvilPath, "evil.exe");
        Assert.Equal(NetworkAlertResolution.AllowAlways, svc.SessionDecisions[key]);
    }

    [Fact]
    public void ResolveAlert_WhitelistProgram_CreatesAllowRuleAndWhitelistsSession()
    {
        var applied = new List<FirewallRuleDto>();
        var svc = CreateService(
            new List<ProcessNetworkSocket> { Sock(1234, "evil.exe", "93.184.216.34", 443) },
            Paths((1234, EvilPath)),
            appliedRules: applied);
        var alert = ScanSingle(svc);

        var ok = svc.ResolveAlert(new ResolveNetworkAlertRequest
        {
            AlertId = alert.AlertId,
            ProcessPath = EvilPath,
            ProcessName = "evil.exe",
            RemoteAddress = "93.184.216.34",
            Resolution = NetworkAlertResolution.WhitelistProgram,
        });

        Assert.True(ok);
        Assert.Empty(svc.GetPendingAlerts());
        var rule = Assert.Single(applied);
        Assert.Equal(FirewallAction.Allow, rule.Action);
        Assert.Equal(EvilPath, rule.ApplicationPath);
        Assert.True(rule.Enabled);
        var key = NetworkConnectionAlertService.BuildSessionKey(EvilPath, "evil.exe");
        Assert.Equal(NetworkAlertResolution.WhitelistProgram, svc.SessionDecisions[key]);
    }

    [Fact]
    public void ResolveAlert_BlockAlways_CreatesBlockRuleAndRemovesAlert()
    {
        var applied = new List<FirewallRuleDto>();
        var svc = CreateService(
            new List<ProcessNetworkSocket> { Sock(1234, "evil.exe", "93.184.216.34", 443) },
            Paths((1234, EvilPath)),
            appliedRules: applied);
        var alert = ScanSingle(svc);

        var ok = svc.ResolveAlert(new ResolveNetworkAlertRequest
        {
            AlertId = alert.AlertId,
            ProcessPath = EvilPath,
            ProcessName = "evil.exe",
            RemoteAddress = "93.184.216.34",
            Resolution = NetworkAlertResolution.BlockAlways,
        });

        Assert.True(ok);
        Assert.Empty(svc.GetPendingAlerts());
        var rule = Assert.Single(applied);
        Assert.Equal(FirewallAction.Block, rule.Action);
        Assert.Equal(EvilPath, rule.ApplicationPath);
        var key = NetworkConnectionAlertService.BuildSessionKey(EvilPath, "evil.exe");
        Assert.Equal(NetworkAlertResolution.BlockAlways, svc.SessionDecisions[key]);
    }

    [Fact]
    public void ResolveAlert_AllowSession_NoFirewallRule_SessionWhitelisted()
    {
        var applied = new List<FirewallRuleDto>();
        var sockets = new List<ProcessNetworkSocket>
        {
            Sock(1234, "evil.exe", "93.184.216.34", 443),
        };
        var svc = CreateService(sockets, Paths((1234, EvilPath)), appliedRules: applied);
        var alert = ScanSingle(svc);

        var ok = svc.ResolveAlert(new ResolveNetworkAlertRequest
        {
            AlertId = alert.AlertId,
            ProcessPath = EvilPath,
            ProcessName = "evil.exe",
            RemoteAddress = "93.184.216.34",
            Resolution = NetworkAlertResolution.AllowSession,
        });

        Assert.True(ok);
        Assert.Empty(applied);
        Assert.Empty(svc.GetPendingAlerts());
        var key = NetworkConnectionAlertService.BuildSessionKey(EvilPath, "evil.exe");
        Assert.Equal(NetworkAlertResolution.AllowSession, svc.SessionDecisions[key]);

        // Повторный скан (включая новый remote того же процесса) алертов не даёт.
        sockets.Add(Sock(1234, "evil.exe", "9.9.9.9", 53));
        Assert.Equal(0, svc.ScanNewConnections());
        Assert.Empty(svc.GetPendingAlerts());
    }

    [Fact]
    public void ResolveAlert_AllowAlways_PreventsFutureAlerts()
    {
        var applied = new List<FirewallRuleDto>();
        var svc = CreateService(
            new List<ProcessNetworkSocket> { Sock(1234, "evil.exe", "93.184.216.34", 443) },
            Paths((1234, EvilPath)),
            appliedRules: applied);
        var alert = ScanSingle(svc);

        svc.ResolveAlert(new ResolveNetworkAlertRequest
        {
            AlertId = alert.AlertId,
            ProcessPath = EvilPath,
            ProcessName = "evil.exe",
            Resolution = NetworkAlertResolution.AllowAlways,
        });

        Assert.Equal(0, svc.ScanNewConnections());
    }

    [Fact]
    public void ResolveAlert_UnknownId_ReturnsFalse()
    {
        var applied = new List<FirewallRuleDto>();
        var svc = CreateService(
            new List<ProcessNetworkSocket> { Sock(1234, "evil.exe", "93.184.216.34", 443) },
            Paths((1234, EvilPath)),
            appliedRules: applied);
        ScanSingle(svc);

        var ok = svc.ResolveAlert(new ResolveNetworkAlertRequest
        {
            AlertId = "nonexistent",
            ProcessPath = EvilPath,
            ProcessName = "evil.exe",
            Resolution = NetworkAlertResolution.AllowAlways,
        });

        Assert.False(ok);
        Assert.Empty(applied);
        Assert.Equal(1, svc.PendingCount);
    }

    [Fact]
    public void ResolveAlert_NullOrEmptyRequest_ReturnsFalse()
    {
        var svc = CreateService();

        Assert.False(svc.ResolveAlert(null!));
        Assert.False(svc.ResolveAlert(new ResolveNetworkAlertRequest()));
    }

    // ── Включение/выключение ──

    [Fact]
    public void SetAlertsEnabled_Disabled_SkipsScanAndClearsPending()
    {
        var svc = CreateService(
            new List<ProcessNetworkSocket> { Sock(1234, "evil.exe", "93.184.216.34", 443) },
            Paths((1234, EvilPath)));

        Assert.True(svc.AlertsEnabled);
        Assert.Equal(1, svc.ScanNewConnections());

        svc.SetAlertsEnabled(false);

        Assert.False(svc.AlertsEnabled);
        Assert.Empty(svc.GetPendingAlerts());
        Assert.Equal(0, svc.ScanNewConnections());

        svc.SetAlertsEnabled(true);
        Assert.True(svc.AlertsEnabled);
    }

    [Fact]
    public void GetPendingAlerts_ReturnsSnapshotOrderedByDetectionTime()
    {
        var svc = CreateService(
            new List<ProcessNetworkSocket>
            {
                Sock(1111, "a.exe", "93.184.216.34", 443),
                Sock(2222, "b.exe", "1.1.1.1", 80),
            },
            Paths((1111, @"C:\Apps\a.exe"), (2222, @"C:\Apps\b.exe")));

        Assert.Equal(2, svc.ScanNewConnections());
        var pending = svc.GetPendingAlerts();

        Assert.Equal(2, pending.Count);
        Assert.True(pending[0].DetectedAt <= pending[1].DetectedAt);
    }

    // ── Потокобезопасность и приоритет Blacklist > Whitelist ──

    [Fact]
    public void Blacklist_HasAbsolutePriorityOver_Whitelist()
    {
        var svc = CreateService(
            new List<ProcessNetworkSocket> { Sock(1234, "conflict.exe", "93.184.216.34", 443) },
            Paths((1234, @"C:\Apps\conflict.exe")));

        // Сначала добавляем алерт и применяем Whitelist
        Assert.Equal(1, svc.ScanNewConnections());
        var alert = Assert.Single(svc.GetPendingAlerts());
        svc.ResolveAlert(new ResolveNetworkAlertRequest
        {
            AlertId = alert.AlertId,
            Resolution = NetworkAlertResolution.WhitelistProgram,
            ProcessPath = @"C:\Apps\conflict.exe",
            ProcessName = "conflict.exe"
        });

        Assert.True(svc.IsWhitelistedApp(@"C:\Apps\conflict.exe", "conflict.exe"));
        Assert.False(svc.IsBlacklistedApp(@"C:\Apps\conflict.exe", "conflict.exe"));

        // Теперь применяем Blacklist к той же программе
        // Генерируем новый алерт для резолва
        var svc2 = CreateService(
            new List<ProcessNetworkSocket> { Sock(5678, "conflict.exe", "93.184.216.35", 80) },
            Paths((5678, @"C:\Apps\conflict.exe")));
        Assert.Equal(1, svc2.ScanNewConnections());
        var alert2 = Assert.Single(svc2.GetPendingAlerts());
        svc2.ResolveAlert(new ResolveNetworkAlertRequest
        {
            AlertId = alert2.AlertId,
            Resolution = NetworkAlertResolution.BlacklistProgram,
            ProcessPath = @"C:\Apps\conflict.exe",
            ProcessName = "conflict.exe"
        });

        // Проверяем: в svc2 при добавлении в черный список программа строго заблокирована
        Assert.True(svc2.IsBlacklistedApp(@"C:\Apps\conflict.exe", "conflict.exe"));
        Assert.False(svc2.IsWhitelistedApp(@"C:\Apps\conflict.exe", "conflict.exe"));
    }

    [Fact]
    public void ConcurrentAccess_ToWhitelistedAndBlacklistedApps_DoesNotThrow()
    {
        var svc = CreateService();
        var exceptions = new System.Collections.Concurrent.ConcurrentBag<Exception>();

        Parallel.For(0, 1000, i =>
        {
            try
            {
                var appPath = $@"C:\Apps\app_{i % 50}.exe";
                var appName = $"app_{i % 50}.exe";

                if (i % 3 == 0)
                {
                    svc.IsWhitelistedApp(appPath, appName);
                }
                else if (i % 3 == 1)
                {
                    svc.IsBlacklistedApp(appPath, appName);
                }
                else
                {
                    svc.IsPortAllowed(1000 + (i % 20));
                }
            }
            catch (Exception ex)
            {
                exceptions.Add(ex);
            }
        });

        Assert.Empty(exceptions);
    }

    /// <summary>
    /// Проверяет ограничение размера истории сетевых подключений (MaxHistoryItems = 2500)
    /// и вытеснение самых старых записей.
    /// </summary>
    [Fact]
    public void RecordHistory_LimitsMaxItems_AndPrunesOldest()
    {
        var svc = CreateService();
        svc.ClearConnectionHistory();

        for (int i = 0; i < 2600; i++)
        {
            svc.RecordHistory(new NetworkConnectionHistoryItem
            {
                Id = $"item-{i}",
                Timestamp = DateTime.UtcNow.AddSeconds(i),
                ProcessName = $"pp_{i}.exe",
                RemoteAddress = $"1.1.1.{i % 250}",
                RemotePort = 80,
                Verdict = NetworkConnectionVerdict.Allowed
            });
        }

        var history = svc.GetConnectionHistory();
        Assert.Equal(2500, history.Count);
        Assert.Equal("item-2599", history[0].Id);
        Assert.Equal("item-100", history[^1].Id);
    }

    /// <summary>
    /// Проверяет полную очистку истории сетевых подключений.
    /// </summary>
    [Fact]
    public void ClearConnectionHistory_PurgesAllEntries()
    {
        var svc = CreateService();
        svc.RecordHistory(new NetworkConnectionHistoryItem
        {
            Id = "test-1",
            ProcessName = "test.exe",
            Verdict = NetworkConnectionVerdict.PendingAlert
        });

        Assert.NotEmpty(svc.GetConnectionHistory());

        svc.ClearConnectionHistory();
        Assert.Empty(svc.GetConnectionHistory());
    }

    /// <summary>
    /// Проверяет обновление статуса в истории при вызове ResolveAlert.
    /// </summary>
    [Fact]
    public void ResolveAlert_UpdatesHistoryItemVerdict()
    {
        var svc = CreateService(
            sockets: new List<ProcessNetworkSocket> { Sock(1234, "test.exe", "93.184.216.34", 443) },
            pathsByPid: Paths((1234, @"C:\Apps\test.exe")));

        svc.ScanNewConnections();
        var alert = Assert.Single(svc.GetPendingAlerts());

        var historyBefore = svc.GetConnectionHistory();
        var itemBefore = Assert.Single(historyBefore, h => h.Id == alert.AlertId);
        Assert.Equal(NetworkConnectionVerdict.PendingAlert, itemBefore.Verdict);

        svc.ResolveAlert(new ResolveNetworkAlertRequest
        {
            AlertId = alert.AlertId,
            Resolution = NetworkAlertResolution.AllowAlways,
            ProcessPath = @"C:\Apps\test.exe",
            ProcessName = "test.exe"
        });

        var historyAfter = svc.GetConnectionHistory();
        var itemAfter = Assert.Single(historyAfter, h => h.Id == alert.AlertId);
        Assert.Equal(NetworkConnectionVerdict.RuleCreated, itemAfter.Verdict);
    }

    /// <summary>
    /// Проверяет, что при включённых автономных решениях ИИ с режимом AllowSession
    /// алерт НЕ добавляется в очередь всплывающего окна, а решение AllowSession
    /// мгновенно фиксируется в сессии и журнале истории с метаданными ИИ.
    /// </summary>
    [Fact]
    public async Task ScanNewConnections_AutonomousAllowSession_AppliesDecisionWithoutAlert()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"smon_test_{Guid.NewGuid():N}.json");
        try
        {
            var store = new JsonDataStore(tmp);
            var cfg = store.Load();
            cfg.Settings.AiAutonomousNetworkDecisionsEnabled = true;
            cfg.Settings.AiAutonomousDecisionMode = "AllowSession";
            store.Save(cfg);

            var svc = CreateService(
                sockets: new List<ProcessNetworkSocket> { Sock(5555, "browser.exe", "93.184.216.34", 443) },
                pathsByPid: Paths((5555, @"C:\Appsrowser.exe")),
                store: store);

            svc.AiEvaluatorOverride = (name, path, ip, port) => Task.FromResult<(string, string, string, string)?>((
                "Веб-браузер для доступа в интернет",
                "Риск блокировки сетевых сайтов и загрузок",
                "Разрешить подключение на сеанс",
                "AllowSession"));

            var added = svc.ScanNewConnections();

            // Всплывающий алерт НЕ должен создаваться
            Assert.Equal(0, added);
            Assert.Equal(0, svc.PendingCount);
            Assert.Empty(svc.GetPendingAlerts());

            // Ожидаем завершения асинхронной фиксации автономного решения
            var deadline = DateTime.UtcNow.AddSeconds(2);
            while (DateTime.UtcNow < deadline && svc.GetConnectionHistory().Count == 0)
            {
                await Task.Delay(20);
            }

            var history = svc.GetConnectionHistory();
            Assert.NotEmpty(history);
            var item = history[0];
            Assert.Equal(NetworkConnectionVerdict.AllowSession, item.Verdict);
            Assert.Contains("Веб-браузер", item.Details);
        }
        finally
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
        }
    }

    /// <summary>
    /// Проверяет, что при включённых автономных решениях ИИ с режимом BlockSession
    /// применяется сессионная блокировка без всплывающих окон.
    /// </summary>
    [Fact]
    public async Task ScanNewConnections_AutonomousBlockSession_AppliesDecisionWithoutAlert()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"smon_test_{Guid.NewGuid():N}.json");
        try
        {
            var store = new JsonDataStore(tmp);
            var cfg = store.Load();
            cfg.Settings.AiAutonomousNetworkDecisionsEnabled = true;
            cfg.Settings.AiAutonomousDecisionMode = "BlockSession";
            store.Save(cfg);

            var svc = CreateService(
                sockets: new List<ProcessNetworkSocket> { Sock(6666, "miner.exe", "185.190.140.10", 3333) },
                pathsByPid: Paths((6666, @"C:\Temp\miner.exe")),
                store: store);

            svc.AiEvaluatorOverride = (name, path, ip, port) => Task.FromResult<(string, string, string, string)?>((
                "Подозрительный криптомайнер",
                "Блокировка безопасна для операционной системы",
                "Заблокировать сетевой трафик",
                "BlockSession"));

            var added = svc.ScanNewConnections();

            Assert.Equal(0, added);
            Assert.Equal(0, svc.PendingCount);

            var deadline = DateTime.UtcNow.AddSeconds(2);
            while (DateTime.UtcNow < deadline && svc.GetConnectionHistory().Count == 0)
            {
                await Task.Delay(20);
            }

            var history = svc.GetConnectionHistory();
            Assert.NotEmpty(history);
            Assert.Equal(NetworkConnectionVerdict.BlockSession, history[0].Verdict);
        }
        finally
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
        }
    }

    /// <summary>
    /// Проверяет, что при отключённых автономных решениях ИИ алерт создаётся штатно
    /// и обогащается полями AI (AiPurpose, AiBlockingRisk, AiRecommendation).
    /// </summary>
    [Fact]
    public async Task ScanNewConnections_StandardMode_EnrichesPendingAlertWithAi()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"smon_test_{Guid.NewGuid():N}.json");
        try
        {
            var store = new JsonDataStore(tmp);
            var cfg = store.Load();
            cfg.Settings.AiAutonomousNetworkDecisionsEnabled = false;
            store.Save(cfg);

            var svc = CreateService(
                sockets: new List<ProcessNetworkSocket> { Sock(7777, "telegram.exe", "149.154.167.50", 443) },
                pathsByPid: Paths((7777, @"C:\Apps	elegram.exe")),
                store: store);

            svc.AiEvaluatorOverride = (name, path, ip, port) => Task.FromResult<(string, string, string, string)?>((
                "Мессенджер Telegram",
                "Нарушится доставка личных сообщений и звонков",
                "Разрешить соединение",
                "AllowSession"));

            var added = svc.ScanNewConnections();

            Assert.Equal(1, added);
            Assert.Equal(1, svc.PendingCount);

            var deadline = DateTime.UtcNow.AddSeconds(2);
            var alerts = svc.GetPendingAlerts();
            Assert.Single(alerts);

            while (DateTime.UtcNow < deadline && string.IsNullOrEmpty(alerts[0].AiPurpose))
            {
                await Task.Delay(20);
            }

            Assert.Equal("Мессенджер Telegram", alerts[0].AiPurpose);
            Assert.Equal("Нарушится доставка личных сообщений и звонков", alerts[0].AiBlockingRisk);
            Assert.Equal("Разрешить соединение", alerts[0].AiRecommendation);
            Assert.Equal("AllowSession", alerts[0].AiRecommendedAction);
        }
        finally
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
        }
    }

    /// <summary>
    /// Проверяет, что AiRatingService.EvaluateNetworkProcessAsync мгновенно
    /// распознаёт компоненты StartupMonitor и системные доверенные службы Windows.
    /// </summary>
    [Fact]
    public async Task AiRatingService_EvaluateNetworkProcessAsync_ReturnsImmediateTrustedVerdict()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"smon_test_{Guid.NewGuid():N}.json");
        try
        {
            var store = new JsonDataStore(tmp);
            var cache = new StartupMonitor.Service.Services.Ai.RatingsCache(store);
            var logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<StartupMonitor.Service.Services.Ai.AiRatingService>.Instance;

            using var aiService = new StartupMonitor.Service.Services.Ai.AiRatingService(cache, logger, "test-api-key");

            // Проверка для StartupMonitor
            var smonVerdict = await aiService.EvaluateNetworkProcessAsync(
                "StartupMonitor.Service.exe",
                @"C:\Program Files\StartupMonitor\StartupMonitor.Service.exe",
                "1.1.1.1",
                443);

            Assert.NotNull(smonVerdict);
            Assert.Equal("AllowAlways", smonVerdict.Value.RecommendedAction);
            Assert.Contains("Startup Monitor", smonVerdict.Value.Purpose);

            // Проверка для доверенного процесса Windows
            var sysVerdict = await aiService.EvaluateNetworkProcessAsync(
                "svchost.exe",
                @"C:\Windows\System32\svchost.exe",
                "8.8.8.8",
                53);

            Assert.NotNull(sysVerdict);
            Assert.Equal("AllowAlways", sysVerdict.Value.RecommendedAction);
            Assert.Contains("Windows", sysVerdict.Value.Purpose);
        }
        finally
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
        }
    }
}