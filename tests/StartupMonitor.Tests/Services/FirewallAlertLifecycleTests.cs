using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using StartupMonitor.Service.Services;
using StartupMonitor.Service.Services.Ai;
using StartupMonitor.Service.Services.Firewall;
using StartupMonitor.Shared.Models;
using StartupMonitor.Shared.Models.Firewall;

namespace StartupMonitor.Tests.Services;

/// <summary>Проверяет жизненный цикл групп и ИИ с настоящей логикой службы, исключая COM, WFP и внешнюю сеть.</summary>
public sealed class FirewallAlertLifecycleTests
{
    private const string AppPath = @"C:\Apps\lifecycle.exe";

    /// <summary>Массовые TCP-сокеты образуют одну группу и не увеличивают счётчик при повторном снимке.</summary>
    [Fact]
    public void ManyLocalPorts_AggregateAndRemainUniqueBeyondSeenTtl()
    {
        using var rig = new Rig();
        rig.Sockets.AddRange(Enumerable.Range(0, 100).Select(index => Socket(50000 + index)));
        Assert.Equal(1, rig.Service.ScanNewConnections());
        var alert = Assert.Single(rig.Service.GetPendingAlerts());
        Assert.Equal(100, alert.ConnectionCount);
        rig.Now = rig.Now.AddMinutes(11);
        Assert.Equal(0, rig.Service.ScanNewConnections());
        var repeated = Assert.Single(rig.Service.GetPendingAlerts());
        Assert.Equal(alert.AlertId, repeated.AlertId);
        Assert.Equal(100, repeated.ConnectionCount);
        Assert.Equal(rig.Now, repeated.LastSeenAt);
    }

    /// <summary>Восстановление пути завершает неизвестную карточку, не оставляя блокирующий имя-псевдоним.</summary>
    [Fact]
    public void RecoveredPath_SupersedesUnknownIdentity()
    {
        using var rig = new Rig();
        rig.Path = null;
        rig.Sockets.Add(Socket());
        rig.Service.ScanNewConnections();
        var unknown = Assert.Single(rig.Service.GetPendingAlerts());
        Assert.False(unknown.CanBlockSession);
        rig.Path = AppPath;
        rig.Service.ScanNewConnections();
        var known = Assert.Single(rig.Service.GetPendingAlerts());
        Assert.NotEqual(unknown.AlertId, known.AlertId);
        Assert.True(rig.Service.ResolveAlert(Request(known, NetworkAlertResolution.AllowSession)));
        Assert.Empty(rig.Service.GetPendingAlerts());
        Assert.Equal(NetworkAlertResolutionStatus.Superseded, rig.Service.ResolveAlertDetailed(Request(unknown, NetworkAlertResolution.BlockSession)).Status);
    }

    /// <summary>Отложенная карточка остаётся группой, но не показывается до срока и не создаёт сетевую политику.</summary>
    [Fact]
    public void Defer_HidesSameGroupForCooldownWithoutAllowingTraffic()
    {
        using var rig = new Rig();
        rig.Sockets.Add(Socket());
        rig.Service.ScanNewConnections();
        var alert = Assert.Single(rig.Service.GetPendingAlerts());
        var result = rig.Service.ResolveAlertDetailed(Request(alert, NetworkAlertResolution.Defer));
        Assert.Equal(NetworkAlertResolutionStatus.Deferred, result.Status);
        Assert.Empty(rig.Service.GetPendingAlerts());
        rig.Sockets.Add(Socket(50001));
        Assert.Equal(0, rig.Service.ScanNewConnections());
        Assert.Equal(1, rig.Service.PendingCount);
        Assert.Equal(0, rig.Policy.ApplyCalls);
        Assert.Empty(rig.Rules);
        rig.Now = rig.Now.AddMinutes(6);
        Assert.Equal(alert.AlertId, Assert.Single(rig.Service.GetPendingAlerts()).AlertId);
    }

    /// <summary>Отсутствие заключения ИИ возвращает одну ручную группу без автоматического Allow и повторного запуска для новых сокетов.</summary>
    [Fact]
    public void MissingAiVerdict_QueuesManualGroupAndDoesNotRetryBehindItsWindow()
    {
        using var rig = new Rig(autonomous: true);
        var calls = 0;
        rig.Service.AiEvaluatorOverride = (_, _, _, _) =>
        {
            calls++;
            return Task.FromResult<(string, string, string, string)?>(null);
        };
        rig.Sockets.Add(Socket());
        rig.Service.ScanNewConnections();
        Assert.Single(rig.Service.GetPendingAlerts());
        rig.Sockets.Add(Socket(50001));
        rig.Service.ScanNewConnections();
        Assert.Equal(1, calls);
        Assert.Equal(0, rig.Policy.ApplyCalls);
        Assert.Equal(2, Assert.Single(rig.Service.GetPendingAlerts()).ConnectionCount);
    }

    /// <summary>Поздний ответ ИИ не записывает политику после подтверждённого отключения детектора.</summary>
    [Fact]
    public async Task Disable_InvalidatesLateAiCommit()
    {
        using var rig = new Rig(autonomous: true);
        var answer = new TaskCompletionSource<(string, string, string, string)?>(TaskCreationOptions.RunContinuationsAsynchronously);
        rig.Service.AiEvaluatorOverride = (_, _, _, _) => answer.Task;
        rig.Sockets.Add(Socket());
        rig.Service.ScanNewConnections();
        rig.Service.SetAlertsEnabled(false);
        answer.SetResult(("Purpose", "Risk", "Recommendation", "AllowSession"));
        await Task.Delay(50);
        Assert.Equal(0, rig.Policy.ApplyCalls);
        Assert.Empty(rig.Service.GetPendingAlerts());
        Assert.Empty(rig.Service.SessionDecisions);
    }

    /// <summary>Смена настройки автономности инвалидирует старую оценку и возвращает существующий сокет к ручному запросу.</summary>
    [Fact]
    public async Task AutonomousModeChanged_DoesNotApplyOldChoiceAndRescansSameSocket()
    {
        using var rig = new Rig(autonomous: true);
        var answer = new TaskCompletionSource<(string, string, string, string)?>(TaskCreationOptions.RunContinuationsAsynchronously);
        rig.Service.AiEvaluatorOverride = (_, _, _, _) => answer.Task;
        rig.Sockets.Add(Socket());
        rig.Service.ScanNewConnections();
        var cfg = rig.Store.Load();
        cfg.Settings.AiAutonomousNetworkDecisionsEnabled = false;
        rig.Store.Save(cfg);
        answer.SetResult(("Purpose", "Risk", "Recommendation", "AllowSession"));
        await Task.Delay(50);
        rig.Service.ScanNewConnections();
        Assert.Equal(0, rig.Policy.ApplyCalls);
        Assert.Single(rig.Service.GetPendingAlerts());
    }

    /// <summary>Один operationId не допускает противоположное повторное действие после утраты подтверждения.</summary>
    [Fact]
    public void OperationIdentity_IsIdempotentAndRejectsDifferentChoice()
    {
        using var rig = new Rig();
        rig.Sockets.Add(Socket());
        rig.Service.ScanNewConnections();
        var request = Request(Assert.Single(rig.Service.GetPendingAlerts()), NetworkAlertResolution.BlockSession);
        Assert.Equal(NetworkAlertResolutionStatus.Applied, rig.Service.ResolveAlertDetailed(request).Status);
        Assert.Equal(NetworkAlertResolutionStatus.AlreadyApplied, rig.Service.ResolveAlertDetailed(request).Status);
        request.Resolution = NetworkAlertResolution.AllowSession;
        Assert.Equal(NetworkAlertResolutionStatus.Conflict, rig.Service.ResolveAlertDetailed(request).Status);
        Assert.Equal(1, rig.Policy.ApplyCalls);
    }

    /// <summary>Обновлённый чёрный список проверяется перед разрешением из ранее выданного окна.</summary>
    [Fact]
    public void StalePopup_CannotAcknowledgeSessionAllowAgainstBlacklist()
    {
        using var rig = new Rig();
        rig.Sockets.Add(Socket());
        rig.Service.ScanNewConnections();
        var alert = Assert.Single(rig.Service.GetPendingAlerts());
        var cfg = rig.Store.Load();
        cfg.Settings.FirewallBlacklistedApps.Add(AppPath);
        rig.Store.Save(cfg);
        var result = rig.Service.ResolveAlertDetailed(Request(alert, NetworkAlertResolution.AllowSession));
        Assert.False(result.Success);
        Assert.Equal(NetworkAlertResolutionStatus.Conflict, result.Status);
        Assert.Equal(0, rig.Policy.ApplyCalls);
    }

    /// <summary>Одно имя системного или собственного компонента вне ожидаемого каталога не является доверием.</summary>
    [Theory]
    [InlineData("svchost.exe", @"C:\Temp\svchost.exe")]
    [InlineData("StartupMonitor.Service.exe", @"C:\Temp\StartupMonitor.Service.exe")]
    [InlineData("tool.exe", @"C:\Temp\StartupMonitorCopy\tool.exe")]
    public void TrustedIdentity_RejectsFilenameAndSubstringImpersonation(string name, string path) =>
        Assert.False(NetworkConnectionAlertService.IsTrustedProcess(name, path));

    /// <summary>HTTP-адаптер возвращает только контролируемый ответ без обращения к внешней сети.</summary>
    private sealed class Handler(Func<CancellationToken, Task<HttpResponseMessage>> action) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => action(cancellationToken);
    }

    /// <summary>Неполный либо неизвестный action не превращается в разрешающее заключение модели.</summary>
    [Theory]
    [InlineData("{}")]
    [InlineData("{\"purpose\":\"p\",\"blocking_risk\":\"r\",\"recommendation\":\"x\",\"recommended_action\":\"Maybe\"}")]
    public async Task InvalidAiSchema_ReturnsUnknown(string content)
    {
        using var rig = new Rig();
        using var ai = CreateAi(rig, new Handler(_ => Task.FromResult(Response(content))));
        Assert.Null(await ai.EvaluateNetworkProcessAsync("lifecycle.exe", AppPath, "8.8.8.8", 443));
    }

    /// <summary>Одновременные потребители одной оценки используют один HTTP-запрос; отмена одного не обрывает другого.</summary>
    [Fact]
    public async Task ConcurrentAiConsumers_ShareRequestAndIsolateCancellation()
    {
        using var rig = new Rig();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        using var ai = CreateAi(rig, new Handler(async ct =>
        {
            Interlocked.Increment(ref calls);
            started.TrySetResult();
            await release.Task.WaitAsync(ct);
            return Response("{\"purpose\":\"p\",\"blocking_risk\":\"r\",\"recommendation\":\"x\",\"recommended_action\":\"BlockSession\"}");
        }));
        using var cancelled = new CancellationTokenSource();
        var first = ai.EvaluateNetworkProcessAsync("lifecycle.exe", AppPath, "8.8.8.8", 443, cancelled.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var second = ai.EvaluateNetworkProcessAsync("lifecycle.exe", AppPath, "8.8.8.8", 443);
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        release.SetResult();
        Assert.NotNull(await second.WaitAsync(TimeSpan.FromSeconds(3)));
        Assert.NotNull(await ai.EvaluateNetworkProcessAsync("lifecycle.exe", AppPath, "8.8.8.8", 443));
        Assert.Equal(1, calls);
    }

    /// <summary>Создаёт OpenAI-совместимый конверт ответа для изолированного обработчика.</summary>
    private static HttpResponseMessage Response(string content) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(JsonSerializer.Serialize(new { choices = new[] { new { message = new { content } } } }))
    };

    /// <summary>Создаёт оценщик с внедрённым HTTP-обработчиком, который никогда не использует сокеты.</summary>
    private static AiRatingService CreateAi(Rig rig, HttpMessageHandler handler) => new(new RatingsCache(rig.Store), NullLogger<AiRatingService>.Instance,
        "unit-test", apiUrl: "https://unit.test/chat/completions", httpHandler: handler);

    /// <summary>Формирует управляемый сокет с неизвестным направлением наблюдения.</summary>
    private static ProcessNetworkSocket Socket(int localPort = 50000) => new()
    {
        ProcessId = 1001, ProcessName = "lifecycle.exe", LocalAddress = "192.168.1.10", LocalPort = localPort,
        RemoteAddress = "8.8.8.8", RemotePort = 443, Protocol = "TCP", State = "ESTABLISHED", Direction = "Unknown"
    };

    /// <summary>Сохраняет одну идентичность операции для повторов подтверждения.</summary>
    private static ResolveNetworkAlertRequest Request(NetworkConnectionAlertDto alert, NetworkAlertResolution action) => new()
    {
        AlertId = alert.AlertId, Resolution = action, OperationId = Guid.NewGuid().ToString("N")
    };

    /// <summary>Владеет полностью изолированными источниками конфигурации, сокетов и сетевых правил.</summary>
    private sealed class Rig : IDisposable
    {
        private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("FirewallLifecycle_");
        private readonly ProcessNetworkTracker _tracker = new();
        public readonly List<ProcessNetworkSocket> Sockets = [];
        public readonly List<FirewallRuleDto> Rules = [];
        public readonly TestSessionFirewallPolicy Policy = new();
        public readonly JsonDataStore Store;
        public readonly NetworkConnectionAlertService Service;
        public string? Path = AppPath;
        public DateTime Now = DateTime.UtcNow;

        /// <summary>Подключает подмены, исключающие COM/WFP, системные процессы и внешнюю сеть.</summary>
        public Rig(bool autonomous = false)
        {
            Store = new JsonDataStore(_directory.FullName);
            var cfg = Store.Load();
            cfg.Settings.AiAutonomousNetworkDecisionsEnabled = autonomous;
            Store.Save(cfg);
            Service = new(_tracker, new WindowsFirewallService(), Store, sessionFirewall: Policy)
            {
                SocketsProvider = () => Sockets,
                ProcessPathResolver = _ => Path,
                ProcessInstanceResolver = _ => 1234,
                UtcNowProvider = () => Now,
                HasFirewallRuleOverride = path => Rules.Any(rule => rule.Action == FirewallAction.Allow && string.Equals(rule.ApplicationPath, path, StringComparison.OrdinalIgnoreCase)),
                HasApplicationPolicyOverride = (path, action) => Rules.Any(rule => rule.Action == action && string.Equals(rule.ApplicationPath, path, StringComparison.OrdinalIgnoreCase)),
                ApplyFirewallRuleOverride = Rules.Add
            };
        }

        /// <summary>Освобождает детектор и только принадлежащие этому тесту временные файлы.</summary>
        public void Dispose()
        {
            Service.Dispose();
            _tracker.Dispose();
            _directory.Delete(recursive: true);
        }
    }
}
