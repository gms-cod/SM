using System.Collections;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using StartupMonitor.Service.Services;
using StartupMonitor.Service.Services.Firewall;
using StartupMonitor.Shared.Models;
using StartupMonitor.Shared.Models.Firewall;

namespace StartupMonitor.Tests.Services;

/// <summary>Проверяет подтверждение COM-подобных операций, порядок событий и сохранение конфигурации без изменения firewall машины.</summary>
public sealed class FirewallStateRegressionTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "FirewallState_" + Guid.NewGuid().ToString("N"));

    /// <summary>Подтверждённое отсутствие правила допускает идемпотентный разбан.</summary>
    /// <param name="hresult">Документированный код отсутствия объекта.</param>
    [Theory]
    [InlineData(unchecked((int)0x80070002))]
    [InlineData(unchecked((int)0x80070490))]
    public void RemoveRule_ConfirmedAbsenceReturnsFalse(int hresult)
        => Assert.False(WindowsFirewallService.RemoveRuleByName(new RemovalRules(new COMException("Missing", hresult)), "Rule"));

    /// <summary>Отказ удаления существующего правила не превращается в успешное отсутствие.</summary>
    [Fact]
    public void RemoveRule_AccessDeniedPropagates()
    {
        var rules = new RemovalRules(new COMException("Denied", unchecked((int)0x80070005)));
        var error = Assert.Throws<COMException>(() => WindowsFirewallService.RemoveRuleByName(rules, "Rule"));
        Assert.Equal(unchecked((int)0x80070005), error.ErrorCode);
        Assert.Equal(1, rules.Attempts);
    }

    /// <summary>Неожиданный программный отказ также передаётся вызывающему коду.</summary>
    [Fact]
    public void RemoveRule_UnexpectedFailurePropagates()
        => Assert.Throws<InvalidOperationException>(() =>
            WindowsFirewallService.RemoveRuleByName(new RemovalRules(new InvalidOperationException("Injected")), "Rule"));

    /// <summary>Отказ прямого поиска нельзя маскировать резервным обходом и ложным отсутствием.</summary>
    [Fact]
    public void FindRule_AccessDeniedDoesNotFallBackToEmptyEnumeration()
    {
        var rules = new DeniedLookupRules();
        var method = typeof(WindowsFirewallService).GetMethod("FindRule", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Метод поиска правила отсутствует.");
        var error = Assert.Throws<TargetInvocationException>(() => method.Invoke(null, [rules, "Rule"]));
        Assert.IsType<COMException>(error.InnerException);
        Assert.False(rules.Enumerated);
    }

    /// <summary>Повторный IP восстанавливает отключённое или изменённое управляемое правило, сохраняя остальные адреса.</summary>
    [Fact]
    public void ExistingAggregateIp_RestoresRequiredRuleProperties()
    {
        var rule = new AggregateRule { RemoteAddresses = "198.51.100.25,198.51.100.26", Enabled = false, Action = 1, Protocol = 6 };
        WindowsFirewallService.UpdateAggregatedIpRule(rule, "198.51.100.25", 1);
        Assert.True(rule.Enabled);
        Assert.Equal(0, rule.Action);
        Assert.Equal(1, rule.Direction);
        Assert.Equal(256, rule.Protocol);
        Assert.Equal(int.MaxValue, rule.Profiles);
        Assert.Equal("198.51.100.25,198.51.100.26", rule.RemoteAddresses);
    }

    /// <summary>API, не подтвердивший включение правила, не позволяет объявить IP заблокированным.</summary>
    [Fact]
    public void AggregateRule_RefusedEnableIsNotSuccessful()
    {
        var rule = new AggregateRule { RemoteAddresses = "198.51.100.25", RefuseEnable = true };
        Assert.Throws<InvalidOperationException>(() => WindowsFirewallService.UpdateAggregatedIpRule(rule, "198.51.100.25", 1));
    }

    /// <summary>Отказ замены файла не меняет работающие правила и не объявляет сохранение успешным.</summary>
    [Fact]
    public void DefenderConfiguration_FailedWritePreservesMemoryAndDisk()
    {
        var store = new JsonDataStore(_directory);
        using var network = new ProcessNetworkTracker();
        using var notifications = new NotificationService(new InitialScanTracker(0));
        using var defender = new ProcessPortDefenderService(new TestPortDefenderFirewall(), network, notifications,
            store, NullLogger<ProcessPortDefenderService>.Instance, startMonitoring: false);
        defender.SaveConfig(new PortDefenderConfigDto { IsGloballyEnabled = false, ProtectedPorts = [8080], MaxAttempts = 17 });
        var path = Path.Combine(_directory, "firewall-defender.json");
        var previousContent = File.ReadAllText(path);
        using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var failure = Record.Exception(() => defender.SaveConfig(new PortDefenderConfigDto { ProtectedPorts = [9090] }));
            Assert.True(failure is IOException or UnauthorizedAccessException,
                $"Ожидался отказ файловой системы при замене занятого файла, получено: {failure?.GetType().Name ?? "успех"}.");
            Assert.False(defender.GetConfig().IsGloballyEnabled);
            Assert.Equal(new[] { 8080 }, defender.GetConfig().ProtectedPorts);
        }
        Assert.Equal(previousContent, File.ReadAllText(path));
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
    }

    /// <summary>Параллельный переход не обгоняет уже начатую доставку предыдущего состояния ни в событии, ни в IPC.</summary>
    [Fact]
    public async Task PauseTransitions_ConcurrentCallsDeliverInCommitOrder()
    {
        var store = new JsonDataStore(_directory);
        using var notifications = CreateManualNotifications();
        var coordinator = new MonitoringStateCoordinator(store, notifications);
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var observed = new List<bool>();
        coordinator.StateChanged += (_, paused) =>
        {
            if (paused)
            {
                entered.TrySetResult();
                if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("Обработчик не освобождён.");
            }
            observed.Add(paused);
        };
        var first = Task.Run(() => coordinator.SetPaused(true));
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            coordinator.SetPaused(false);
        }
        finally { release.Set(); }
        await first.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(new[] { true, false }, observed);
        Assert.False(coordinator.IsPaused);
        Assert.False(store.GetSettings().IsMonitoringPaused);
        Assert.Equal(observed, FlushPauseMessages(notifications));
    }

    /// <summary>Повторный SetPaused из подписчика ставится в очередь, а ошибка соседнего подписчика не прерывает доставку.</summary>
    [Fact]
    public void PauseTransitions_ReentrancyAndSubscriberFailurePreserveOrder()
    {
        var store = new JsonDataStore(_directory);
        using var notifications = CreateManualNotifications();
        var coordinator = new MonitoringStateCoordinator(store, notifications);
        var observed = new List<bool>();
        coordinator.StateChanged += (_, _) => throw new InvalidOperationException("Injected subscriber failure.");
        coordinator.StateChanged += (_, paused) => { if (paused) coordinator.SetPaused(false); };
        coordinator.StateChanged += (_, paused) => observed.Add(paused);
        coordinator.SetPaused(true);
        Assert.Equal(new[] { true, false }, observed);
        Assert.False(coordinator.IsPaused);
        Assert.Equal(observed, FlushPauseMessages(notifications));
    }

    /// <summary>Отключает таймер доставки, чтобы проверить очередь без ожиданий по настенным часам.</summary>
    /// <returns>Сервис с завершённым начальным сканированием.</returns>
    private static NotificationService CreateManualNotifications()
    {
        var notifications = new NotificationService(new InitialScanTracker(0));
        var field = typeof(NotificationService).GetField("_timer", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("Таймер уведомлений отсутствует.");
        var timer = (Timer)(field.GetValue(notifications) ?? throw new InvalidOperationException("Таймер не создан."));
        timer.Change(Timeout.Infinite, Timeout.Infinite);
        return notifications;
    }

    /// <summary>Принудительно доставляет настоящий батч и читает порядок IPC-полезной нагрузки.</summary>
    /// <param name="notifications">Проверяемая очередь уведомлений.</param>
    /// <returns>Последовательность состояний, которая будет передана клиенту.</returns>
    private static List<bool> FlushPauseMessages(NotificationService notifications)
    {
        var states = new List<bool>();
        notifications.MessagesBatchReady += (_, batch) =>
        {
            foreach (var message in batch.Where(message => message.Type == PipeMessageTypes.MonitoringStateChanged))
                states.Add((JsonSerializer.Deserialize<MonitoringStatePayload>(message.Payload)
                    ?? throw new InvalidOperationException("Пустой IPC-переход.")).IsPaused);
        };
        var flush = typeof(NotificationService).GetMethod("FlushBatch", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("Метод доставки отсутствует.");
        flush.Invoke(notifications, [null]);
        return states;
    }

    /// <summary>Удаляет только принадлежащую тесту уникальную директорию после освобождения сервисов.</summary>
    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    /// <summary>COM-подобная коллекция с управляемым отказом удаления для проверки настоящего алгоритма адаптера.</summary>
    public sealed class RemovalRules(Exception failure)
    {
        public int Attempts { get; private set; }
        /// <summary>Возвращает заданный отказ вместо изменения правил операционной системы.</summary>
        /// <param name="name">Имя удаляемого правила.</param>
        public void Remove(string name) { Attempts++; throw failure; }
    }

    /// <summary>Коллекция, запрещающая поиск правила, с наблюдаемым резервным перечислением.</summary>
    public sealed class DeniedLookupRules : IEnumerable
    {
        public bool Enumerated { get; private set; }
        /// <summary>Воспроизводит отказ доступа при прямом поиске.</summary>
        /// <param name="name">Искомое имя.</param>
        /// <returns>Правило не возвращается из-за отказа.</returns>
        public object Item(string name) => throw new COMException("Denied", unchecked((int)0x80070005));
        /// <summary>Фиксирует ошибочное обращение к резервному обходу.</summary>
        /// <returns>Пустой перечислитель.</returns>
        public IEnumerator GetEnumerator() { Enumerated = true; return Array.Empty<object>().GetEnumerator(); }
    }

    /// <summary>Изменяемое COM-подобное правило для проверки восстановления обязательных свойств.</summary>
    public sealed class AggregateRule
    {
        private bool _enabled;
        public string RemoteAddresses { get; set; } = string.Empty;
        public bool RefuseEnable { get; set; }
        public bool Enabled { get => _enabled; set { if (!RefuseEnable) _enabled = value; } }
        public int Direction { get; set; }
        public int Action { get; set; }
        public int Profiles { get; set; }
        public int Protocol { get; set; }
    }
}
