using StartupMonitor.Service.Services;
using StartupMonitor.Service.Services.Firewall;
using StartupMonitor.Shared.Models;
using StartupMonitor.Shared.Models.Firewall;

namespace StartupMonitor.Tests.Services;

/// <summary>
/// Проверяет подтверждение пользовательского решения, сохранение ожидающего алерта
/// при отказе брандмауэра и изоляцию решений для разных исполняемых файлов.
/// Реальные правила Windows Firewall не изменяются: используется явный шов применения.
/// </summary>
public sealed class NetworkAlertResolutionRegressionTests
{
    private const string AppPath = @"C:\Apps\client.exe";
    private const string OtherPath = @"D:\Other\client.exe";

    /// <summary>Создаёт исходящее соединение с управляемыми PID, портом и удалённым адресом.</summary>
    /// <param name="pid">Идентификатор тестового процесса.</param>
    /// <param name="port">Удалённый порт.</param>
    /// <param name="address">Публичный удалённый адрес.</param>
    /// <returns>Независимый снимок сокета.</returns>
    private static ProcessNetworkSocket Socket(int pid = 1001, int port = 443, string address = "8.8.8.8") => new()
    {
        ProcessId = pid,
        ProcessName = "client.exe",
        LocalAddress = "192.168.1.10",
        LocalPort = 50000 + pid,
        RemoteAddress = address,
        RemotePort = port,
        Protocol = "TCP",
        State = "ESTABLISHED"
    };

    /// <summary>Создаёт детектор без обращения к реальному брандмауэру или процессам.</summary>
    /// <param name="sockets">Управляемый список соединений.</param>
    /// <param name="apply">Действие применения правила либо имитация отказа.</param>
    /// <param name="sessionPolicy">Управляемый адаптер временных правил, если требуется проверка вызовов.</param>
    /// <returns>Детектор, подлежащий освобождению вызывающей стороной.</returns>
    private static NetworkConnectionAlertService CreateService(List<ProcessNetworkSocket> sockets, Action<FirewallRuleDto> apply, TestSessionFirewallPolicy? sessionPolicy = null) =>
        new(new ProcessNetworkTracker(), new WindowsFirewallService(), sessionFirewall: sessionPolicy ?? new TestSessionFirewallPolicy())
        {
            SocketsProvider = () => sockets,
            ProcessPathResolver = pid => pid == 1001 ? AppPath : OtherPath,
            HasFirewallRuleOverride = _ => false,
            ApplyFirewallRuleOverride = apply
        };

    /// <summary>Формирует решение для конкретного алерта без доверия к клиентским метаданным.</summary>
    /// <param name="alert">Авторитетный алерт службы.</param>
    /// <param name="resolution">Выбранное пользователем действие.</param>
    /// <returns>Запрос применения решения.</returns>
    private static ResolveNetworkAlertRequest Request(NetworkConnectionAlertDto alert, NetworkAlertResolution resolution) => new()
    {
        AlertId = alert.AlertId,
        ProcessPath = alert.ProcessPath,
        ProcessName = alert.ProcessName,
        RemotePort = alert.RemotePort,
        RemoteAddress = alert.RemoteAddress,
        Resolution = resolution
    };

    /// <summary>Проверяет отсутствие ложного подтверждения и изменения истории при отказе применения правила.</summary>
    /// <param name="resolution">Постоянное действие, требующее реального правила брандмауэра.</param>
    [Theory]
    [InlineData(NetworkAlertResolution.AllowAlways)]
    [InlineData(NetworkAlertResolution.BlockAlways)]
    [InlineData(NetworkAlertResolution.WhitelistProgram)]
    [InlineData(NetworkAlertResolution.BlacklistProgram)]
    [InlineData(NetworkAlertResolution.AllowPort)]
    public void ResolveAlert_ApplyFails_KeepsPendingHistoryAndDecisionUnchanged(NetworkAlertResolution resolution)
    {
        using var service = CreateService([Socket()], _ => throw new InvalidOperationException("Controlled firewall failure"));
        Assert.Equal(1, service.ScanNewConnections());
        var alert = Assert.Single(service.GetPendingAlerts());

        Assert.False(service.ResolveAlert(Request(alert, resolution)));

        Assert.Equal(alert.AlertId, Assert.Single(service.GetPendingAlerts()).AlertId);
        Assert.Empty(service.SessionDecisions);
        Assert.Equal(NetworkConnectionVerdict.PendingAlert, Assert.Single(service.GetConnectionHistory()).Verdict);
        Assert.False(service.IsWhitelistedApp(AppPath, "client.exe"));
        Assert.False(service.IsBlacklistedApp(AppPath, "client.exe"));
    }

    /// <summary>Проверяет отклонение неизвестного действия без потери ожидающего запроса.</summary>
    [Fact]
    public void ResolveAlert_UnknownResolution_DoesNotAcknowledgeOrRemovePending()
    {
        var applied = new List<FirewallRuleDto>();
        using var service = CreateService([Socket()], applied.Add);
        service.ScanNewConnections();
        var alert = Assert.Single(service.GetPendingAlerts());

        Assert.False(service.ResolveAlert(Request(alert, (NetworkAlertResolution)int.MaxValue)));

        Assert.Equal(alert.AlertId, Assert.Single(service.GetPendingAlerts()).AlertId);
        Assert.Empty(applied);
        Assert.Empty(service.SessionDecisions);
    }

    /// <summary>Проверяет, что клиент не может перенаправить действие на другой исполняемый файл.</summary>
    [Fact]
    public void ResolveAlert_ClientSuppliesDifferentPath_DoesNotApplyRuleToThatPath()
    {
        var applied = new List<FirewallRuleDto>();
        using var service = CreateService([Socket()], applied.Add);
        service.ScanNewConnections();
        var alert = Assert.Single(service.GetPendingAlerts());
        var request = Request(alert, NetworkAlertResolution.AllowAlways);
        request.ProcessPath = OtherPath;

        var accepted = service.ResolveAlert(request);

        Assert.DoesNotContain(applied, rule => string.Equals(rule.ApplicationPath, OtherPath, StringComparison.OrdinalIgnoreCase));
        if (accepted)
            Assert.Equal(AppPath, Assert.Single(applied).ApplicationPath);
        else
            Assert.Equal(alert.AlertId, Assert.Single(service.GetPendingAlerts()).AlertId);
    }

    /// <summary>Проверяет, что одинаковое имя файла в другом каталоге не наследует разрешение.</summary>
    [Fact]
    public void ResolveAlert_SameFilenameAtDifferentPaths_PreservesUnrelatedPending()
    {
        using var service = CreateService([Socket(), Socket(1002, 443, "9.9.9.9")], _ => { });
        Assert.Equal(2, service.ScanNewConnections());
        var alert = Assert.Single(service.GetPendingAlerts(), item => item.ProcessPath == AppPath);

        Assert.True(service.ResolveAlert(Request(alert, NetworkAlertResolution.AllowAlways)));

        Assert.Equal(OtherPath, Assert.Single(service.GetPendingAlerts()).ProcessPath);
        Assert.Equal(0, service.ScanNewConnections());
        Assert.Equal(OtherPath, Assert.Single(service.GetPendingAlerts()).ProcessPath);
    }

    /// <summary>Проверяет ограничение разрешения порта выбранным портом, включая другие подключения приложения.</summary>
    [Fact]
    public void ResolveAlert_AllowPort_DoesNotResolveOtherPortsOfSameApplication()
    {
        using var service = CreateService([Socket(), Socket(1001, 8443, "9.9.9.9"), Socket(1002)], _ => { });
        Assert.Equal(2, service.ScanNewConnections());
        var alert = Assert.Single(service.GetPendingAlerts(), item => item.ProcessPath == AppPath && item.RemotePort == 443);

        Assert.True(service.ResolveAlert(Request(alert, NetworkAlertResolution.AllowPort)));

        var remaining = Assert.Single(service.GetPendingAlerts());
        Assert.Equal(8443, remaining.RemotePort);
        Assert.True(service.IsPortAllowed(443));
        Assert.False(service.IsPortAllowed(8443));
    }

    /// <summary>Проверяет, что разрешение удалённого порта создаёт реальную TCP-политику до подтверждения действия.</summary>
    [Fact]
    public void ResolveAlert_AllowPort_CreatesOutboundTcpRuleForAuthoritativePort()
    {
        var applied = new List<FirewallRuleDto>();
        using var service = CreateService([Socket()], applied.Add);
        service.ScanNewConnections();
        var request = Request(Assert.Single(service.GetPendingAlerts()), NetworkAlertResolution.AllowPort);
        request.RemotePort = 8443;

        Assert.True(service.ResolveAlert(request));

        var rule = Assert.Single(applied);
        Assert.Equal(FirewallDirection.Outbound, rule.Direction);
        Assert.Equal(FirewallAction.Allow, rule.Action);
        Assert.Equal(FirewallProtocol.Tcp, rule.Protocol);
        Assert.Equal("443", rule.RemotePorts);
        Assert.True(rule.Enabled);
        Assert.True(service.IsPortAllowed(443));
        Assert.False(service.IsPortAllowed(8443));
    }

    /// <summary>Проверяет отказ временного разрешения, которое текущий режим WFP не может обеспечить.</summary>
    [Fact]
    public void ResolveAlert_UnsupportedSessionAllow_DoesNotAcknowledgeOrInvokePolicy()
    {
        var temporary = new TestSessionFirewallPolicy { CanAllowSession = false };
        using var service = CreateService([Socket()], _ => throw new InvalidOperationException("Unexpected permanent rule"), temporary);
        service.ScanNewConnections();
        var alert = Assert.Single(service.GetPendingAlerts());
        Assert.False(alert.CanAllowSession);
        Assert.True(alert.CanBlockSession);

        Assert.False(service.ResolveAlert(Request(alert, NetworkAlertResolution.AllowSession)));

        Assert.Equal(0, temporary.ApplyCalls);
        Assert.Equal(alert.AlertId, Assert.Single(service.GetPendingAlerts()).AlertId);
        Assert.Empty(service.SessionDecisions);
    }

    /// <summary>Проверяет повторную доставку подтверждённого решения после потери ответа IPC без повторного правила.</summary>
    [Fact]
    public void ResolveAlert_RetriedAfterLostAcknowledgement_IsIdempotent()
    {
        var applied = new List<FirewallRuleDto>();
        using var service = CreateService([Socket()], applied.Add);
        service.ScanNewConnections();
        var request = Request(Assert.Single(service.GetPendingAlerts()), NetworkAlertResolution.AllowAlways);

        Assert.True(service.ResolveAlert(request));
        Assert.True(service.ResolveAlert(request));

        Assert.Single(applied);
        Assert.Empty(service.GetPendingAlerts());
    }

    /// <summary>Проверяет, что повторная доставка другого решения не считается подтверждением исходного.</summary>
    [Fact]
    public void ResolveAlert_RetryChangesAction_DoesNotAcknowledgeDifferentDecision()
    {
        var applied = new List<FirewallRuleDto>();
        using var service = CreateService([Socket()], applied.Add);
        service.ScanNewConnections();
        var request = Request(Assert.Single(service.GetPendingAlerts()), NetworkAlertResolution.AllowAlways);
        Assert.True(service.ResolveAlert(request));
        request.Resolution = NetworkAlertResolution.BlockAlways;

        Assert.False(service.ResolveAlert(request));

        Assert.Equal(FirewallAction.Allow, Assert.Single(applied).Action);
    }

    /// <summary>Проверяет, что сессионные решения подтверждаются после обращения к временному адаптеру, без постоянного правила.</summary>
    /// <param name="resolution">Выбранное сессионное действие.</param>
    /// <param name="action">Соответствующее направление политики адаптера.</param>
    [Theory]
    [InlineData(NetworkAlertResolution.AllowSession, FirewallAction.Allow)]
    [InlineData(NetworkAlertResolution.BlockSession, FirewallAction.Block)]
    public void ResolveAlert_SessionAction_AppliesTemporaryPolicyBeforeAcknowledgement(NetworkAlertResolution resolution, FirewallAction action)
    {
        var temporary = new TestSessionFirewallPolicy();
        var permanent = new List<FirewallRuleDto>();
        using var service = CreateService([Socket()], permanent.Add, temporary);
        service.ScanNewConnections();
        var alert = Assert.Single(service.GetPendingAlerts());

        Assert.True(service.ResolveAlert(Request(alert, resolution)));

        Assert.Equal(1, temporary.ApplyCalls);
        Assert.Equal(AppPath, temporary.LastPath);
        Assert.Equal(action, temporary.LastAction);
        Assert.Equal(TimeSpan.FromHours(8), temporary.LastLifetime);
        Assert.True(temporary.IsActive(AppPath));
        Assert.Empty(permanent);
        Assert.Empty(service.GetPendingAlerts());
    }

    /// <summary>Проверяет, что отказ временной политики не подменяется подавлением следующих уведомлений.</summary>
    /// <param name="resolution">Сессионное действие пользователя.</param>
    [Theory]
    [InlineData(NetworkAlertResolution.AllowSession)]
    [InlineData(NetworkAlertResolution.BlockSession)]
    public void ResolveAlert_SessionPolicyFails_KeepsPendingAndHistory(NetworkAlertResolution resolution)
    {
        var temporary = new TestSessionFirewallPolicy { ApplySucceeds = false };
        using var service = CreateService([Socket()], _ => throw new InvalidOperationException("Unexpected permanent rule"), temporary);
        service.ScanNewConnections();
        var alert = Assert.Single(service.GetPendingAlerts());

        Assert.False(service.ResolveAlert(Request(alert, resolution)));

        Assert.Equal(1, temporary.ApplyCalls);
        Assert.Equal(alert.AlertId, Assert.Single(service.GetPendingAlerts()).AlertId);
        Assert.Empty(service.SessionDecisions);
        Assert.Equal(NetworkConnectionVerdict.PendingAlert, Assert.Single(service.GetConnectionHistory()).Verdict);
    }

    /// <summary>Проверяет, что истечение временного правила вновь требует решения для нового соединения приложения.</summary>
    [Fact]
    public void ScanNewConnections_SessionPolicyExpired_DoesNotSilentlySuppressNewConnection()
    {
        var temporary = new TestSessionFirewallPolicy();
        var sockets = new List<ProcessNetworkSocket> { Socket() };
        using var service = CreateService(sockets, _ => throw new InvalidOperationException("Unexpected permanent rule"), temporary);
        service.ScanNewConnections();
        Assert.True(service.ResolveAlert(Request(Assert.Single(service.GetPendingAlerts()), NetworkAlertResolution.BlockSession)));
        temporary.Remove(AppPath);
        sockets.Add(Socket(1001, 8443, "9.9.9.9"));

        Assert.Equal(1, service.ScanNewConnections());

        Assert.Equal(8443, Assert.Single(service.GetPendingAlerts()).RemotePort);
    }

    /// <summary>Проверяет передачу ограниченного TTL общему сессионному адаптеру и возврат к запросам после явного снятия.</summary>
    [Fact]
    public void ApplySessionAccess_TimedDecisionAndClear_DoNotCreatePermanentRule()
    {
        var temporary = new TestSessionFirewallPolicy();
        var permanent = new List<FirewallRuleDto>();
        using var service = CreateService([Socket()], permanent.Add, temporary);
        service.ScanNewConnections();

        var result = service.ApplySessionAccess(AppPath, "client.exe", NetworkAlertResolution.BlockSession, TimeSpan.FromMinutes(15));

        Assert.True(result.Success);
        Assert.Equal(TimeSpan.FromMinutes(15), temporary.LastLifetime);
        Assert.Equal(FirewallAction.Block, temporary.LastAction);
        Assert.Empty(service.GetPendingAlerts());
        Assert.Empty(permanent);
        service.ClearSessionAccess(AppPath, "client.exe");
        Assert.False(temporary.IsActive(AppPath));
        Assert.Empty(service.SessionDecisions);
        Assert.Equal(1, service.ScanNewConnections());
    }

    /// <summary>Проверяет отклонение недопустимого TTL до вызова адаптера и изменения очереди.</summary>
    /// <param name="minutes">Недопустимая длительность правила в минутах.</param>
    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(481)]
    public void ApplySessionAccess_InvalidLifetime_DoesNotApplyOrResolveAlert(int minutes)
    {
        var temporary = new TestSessionFirewallPolicy();
        using var service = CreateService([Socket()], _ => throw new InvalidOperationException("Unexpected permanent rule"), temporary);
        service.ScanNewConnections();

        var result = service.ApplySessionAccess(AppPath, "client.exe", NetworkAlertResolution.BlockSession, TimeSpan.FromMinutes(minutes));

        Assert.False(result.Success);
        Assert.Equal(0, temporary.ApplyCalls);
        Assert.Single(service.GetPendingAlerts());
        Assert.Empty(service.SessionDecisions);
    }

    /// <summary>Проверяет приоритет временной блокировки над постоянным белым списком в журнале новых соединений.</summary>
    [Fact]
    public void ScanNewConnections_SessionBlockOverridesWhitelistVerdict()
    {
        var temporary = new TestSessionFirewallPolicy();
        var sockets = new List<ProcessNetworkSocket> { Socket() };
        using var service = CreateService(sockets, _ => { }, temporary);
        service.ScanNewConnections();
        Assert.True(service.ResolveAlert(Request(Assert.Single(service.GetPendingAlerts()), NetworkAlertResolution.WhitelistProgram)));
        Assert.True(service.ApplySessionAccess(AppPath, "client.exe", NetworkAlertResolution.BlockSession).Success);
        sockets.Add(Socket(1001, 8443, "9.9.9.9"));

        service.ScanNewConnections();

        var newConnection = Assert.Single(service.GetConnectionHistory(), history => history.RemoteAddress == "9.9.9.9");
        Assert.Equal(NetworkConnectionVerdict.BlockSession, newConnection.Verdict);
        Assert.Empty(service.GetPendingAlerts());
    }

    /// <summary>
    /// Проверяет повторную доставку одного решения во время применения правила:
    /// ожидающее уведомление сохраняется до успеха, а конкурентные запросы применяют правило один раз.
    /// </summary>
    /// <returns>Задача, завершающаяся после обоих подтверждений без реального IPC или брандмауэра.</returns>
    [Fact]
    public async Task ResolveAlert_ConcurrentDuplicateRequest_CommitsOnceAfterRuleApplication()
    {
        var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        var calls = 0;
        using var service = CreateService([Socket()], _ =>
        {
            Interlocked.Increment(ref calls);
            entered.TrySetResult(true);
            if (!release.Wait(TimeSpan.FromSeconds(5)))
                throw new TimeoutException("Тест не освободил управляемое применение правила.");
        });
        service.ScanNewConnections();
        var request = Request(Assert.Single(service.GetPendingAlerts()), NetworkAlertResolution.AllowAlways);
        var first = Task.Run(() => service.ResolveAlert(request));
        Task<bool>? second = null;
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            second = Task.Run(() => service.ResolveAlert(request));
            Assert.Equal(1, service.PendingCount);
            Assert.Equal(NetworkConnectionVerdict.PendingAlert, Assert.Single(service.GetConnectionHistory()).Verdict);
        }
        finally { release.Set(); }

        var results = await Task.WhenAll(first, second ?? throw new InvalidOperationException("Не создан конкурентный запрос."))
            .WaitAsync(TimeSpan.FromSeconds(5));
        Assert.All(results, result => Assert.True(result));
        Assert.Equal(1, calls);
        Assert.Empty(service.GetPendingAlerts());
    }

    /// <summary>Проверяет точную область очистки конфликтующих правил, сохраняя чужие правила и одноимённые приложения.</summary>
    /// <param name="rulePath">Путь приложения в правиле брандмауэра.</param>
    /// <param name="managed">Принадлежит ли правило StartupMonitor.</param>
    /// <param name="expected">Должно ли правило считаться принадлежащим целевому приложению.</param>
    [Theory]
    [InlineData(@"C:\Apps\client.exe", true, true)]
    [InlineData("\"c:/apps/client.exe\"", true, true)]
    [InlineData(@"C:\Apps\client.exe", false, false)]
    [InlineData(@"D:\Other\client.exe", true, false)]
    [InlineData("", true, false)]
    public void IsManagedApplicationRule_UsesOwnershipAndExactNormalizedPath(string rulePath, bool managed, bool expected)
    {
        var rule = new FirewallRuleDto { ApplicationPath = rulePath, IsManaged = managed };

        Assert.Equal(expected, NetworkConnectionAlertService.IsManagedApplicationRule(rule, AppPath));
        Assert.False(NetworkConnectionAlertService.IsManagedApplicationRule(null, AppPath));
    }
}
