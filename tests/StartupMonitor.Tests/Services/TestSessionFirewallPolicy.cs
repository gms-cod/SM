using StartupMonitor.Service.Services.Firewall;
using StartupMonitor.Shared.Models.Firewall;

namespace StartupMonitor.Tests.Services;

/// <summary>Управляемая временная политика unit-тестов без вызовов WFP, COM или изменения сетевых правил ОС.</summary>
internal sealed class TestSessionFirewallPolicy : ISessionFirewallPolicy
{
    private readonly Dictionary<string, (FirewallAction Action, TimeSpan Lifetime)> _active = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Управляет успешностью следующего применения без исключений из нативного кода.</summary>
    internal bool ApplySucceeds { get; set; } = true;

    /// <summary>Определяет доступность временного разрешения в управляемом режиме фильтрации.</summary>
    public bool CanAllowSession { get; set; } = true;

    /// <summary>Число вызовов применения, включая контролируемые отказы.</summary>
    internal int ApplyCalls { get; private set; }

    /// <summary>Последний полный путь, переданный в применение политики.</summary>
    internal string? LastPath { get; private set; }

    /// <summary>Последнее действие, переданное в применение политики.</summary>
    internal FirewallAction? LastAction { get; private set; }

    /// <summary>Последний запрошенный срок действия временной политики.</summary>
    internal TimeSpan? LastLifetime { get; private set; }

    /// <summary>Фиксирует временное решение в памяти, позволяя проверять реальный вызов адаптера.</summary>
    /// <param name="path">Путь целевого исполняемого файла.</param>
    /// <param name="action">Ожидаемое разрешение либо блокировка.</param>
    /// <param name="lifetime">Срок временного решения.</param>
    /// <returns>Явный успех либо заданный тестом отказ.</returns>
    public FirewallOperationResult Apply(string path, FirewallAction action, TimeSpan lifetime)
    {
        ApplyCalls++;
        LastPath = path;
        LastAction = action;
        LastLifetime = lifetime;
        if (!ApplySucceeds) return FirewallOperationResult.Fail("Controlled session policy refusal");
        _active[path] = (action, lifetime);
        return FirewallOperationResult.Ok("Controlled session policy applied");
    }

    /// <summary>Удаляет конкретную тестовую политику, имитируя её отзыв или истечение срока.</summary>
    /// <param name="path">Путь приложения.</param>
    public void Remove(string path) => _active.Remove(path);

    /// <summary>Возвращает наличие временной политики в памяти теста.</summary>
    /// <param name="path">Путь приложения.</param>
    /// <returns>Истина для действующей тестовой политики.</returns>
    public bool IsActive(string path) => _active.ContainsKey(path);

    /// <summary>Имитирует удаление временных правил при остановке владельца.</summary>
    public void Dispose() => _active.Clear();
}
