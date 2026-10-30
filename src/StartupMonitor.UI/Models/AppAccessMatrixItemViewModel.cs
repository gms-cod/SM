using System;
using System.ComponentModel;
using System.Windows.Media;
using StartupMonitor.Shared.Models.Firewall;

namespace StartupMonitor.UI.Models;

/// <summary>
/// Представление элемента матрицы доступа приложения в DataGrid.
/// Реализует INotifyPropertyChanged для реактивного отклика пользовательского интерфейса.
/// </summary>
public sealed class AppAccessMatrixItemViewModel : INotifyPropertyChanged
{
    private readonly AppAccessMatrixItemDto _dto;

    /// <summary>Событие изменения свойств модели.</summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Инициализирует модель представления на основе DTO элемента матрицы доступа.
    /// </summary>
    /// <param name="dto">Транспортный объект данных элемента матрицы.</param>
    public AppAccessMatrixItemViewModel(AppAccessMatrixItemDto dto)
    {
        _dto = dto ?? throw new ArgumentNullException(nameof(dto));
        UniqueGroupKey = $"Grp_{Guid.NewGuid():N}";
    }

    /// <summary>Базовый DTO объект данных.</summary>
    public AppAccessMatrixItemDto Dto => _dto;

    /// <summary>Уникальный ключ группы для радиокнопок строки DataGrid.</summary>
    public string UniqueGroupKey { get; }

    /// <summary>Имя процесса.</summary>
    public string ProcessName => _dto.ProcessName;

    /// <summary>Полный путь к исполняемому файлу.</summary>
    public string FullPath => _dto.FullPath;

    /// <summary>Число активных сетевых сокетов в реальном времени.</summary>
    public int ActiveSocketsCount => _dto.ActiveSocketsCount;

    /// <summary>Форматированное отображение числа активных сокетов.</summary>
    public string ActiveSocketsDisplay => _dto.ActiveSocketsCount > 0 ? $"⚡ {_dto.ActiveSocketsCount}" : "—";

    /// <summary>Флаг критического ядра Windows (Уровень 3 защиты).</summary>
    public bool IsCriticalSystem => _dto.ProtectionLevel == ProcessProtectionLevel.Critical;

    /// <summary>Флаг защищенной системной подсистемы Windows (Уровень 2 защиты).</summary>
    public bool IsProtectedSubsystem => _dto.ProtectionLevel == ProcessProtectionLevel.HighRisk;

    /// <summary>Уровень защиты процесса.</summary>
    public ProcessProtectionLevel ProtectionLevel => _dto.ProtectionLevel;

    /// <summary>
    /// Возможность блокировки: для критических компонентов ядра Windows аппаратно заблокирована.
    /// </summary>
    public bool CanBlock => _dto.ProtectionLevel != ProcessProtectionLevel.Critical;

    /// <summary>
    /// Состояние переключателя «Разрешено».
    /// </summary>
    public bool IsAllowedChecked
    {
        get => _dto.State == AccessControlState.Allowed;
        set
        {
            if (value)
                SetState(AccessControlState.Allowed);
        }
    }

    /// <summary>
    /// Состояние переключателя «По запросу».
    /// </summary>
    public bool IsPromptChecked
    {
        get => _dto.State == AccessControlState.Prompt;
        set
        {
            if (value)
                SetState(AccessControlState.Prompt);
        }
    }

    /// <summary>
    /// Состояние переключателя «Заблокировано».
    /// </summary>
    public bool IsBlockedChecked
    {
        get => _dto.State == AccessControlState.Blocked;
        set
        {
            if (value)
                SetState(AccessControlState.Blocked);
        }
    }

    /// <summary>
    /// Текущее состояние контроля доступа.
    /// </summary>
    public AccessControlState State => _dto.State;

    /// <summary>
    /// Область действия правила (Постоянно, До перезагрузки, По таймеру).
    /// </summary>
    public AccessRuleScope Scope => _dto.Scope;

    /// <summary>
    /// Обновляет текущее состояние доступа и уведомляет UI об изменениях.
    /// </summary>
    /// <param name="newState">Новое состояние доступа.</param>
    public void SetState(AccessControlState newState)
    {
        if (_dto.State == newState) return;
        _dto.State = newState;
        OnPropertyChanged(nameof(IsAllowedChecked));
        OnPropertyChanged(nameof(IsPromptChecked));
        OnPropertyChanged(nameof(IsBlockedChecked));
        OnPropertyChanged(nameof(State));
        OnPropertyChanged(nameof(SourceBadgeText));
        OnPropertyChanged(nameof(SourceBadgeBg));
        OnPropertyChanged(nameof(SourceBadgeBorder));
    }

    /// <summary>
    /// Текстовое описание области действия правила.
    /// </summary>
    public string ScopeDisplay => _dto.Scope switch
    {
        AccessRuleScope.Permanent => "📌 Постоянно",
        AccessRuleScope.UntilRestart => "⏳ До перезагрузки",
        AccessRuleScope.TimedTtl => _dto.ExpiresAtUtc.HasValue
            ? $"⏱️ {Math.Max(1, (int)(_dto.ExpiresAtUtc.Value - DateTime.UtcNow).TotalMinutes)}м"
            : "⏱️ Временно",
        _ => "—"
    };

    /// <summary>
    /// Кисть цвета текста области действия правила.
    /// </summary>
    public Brush ScopeColorBrush => _dto.Scope switch
    {
        AccessRuleScope.Permanent => Brushes.WhiteSmoke,
        AccessRuleScope.UntilRestart => new SolidColorBrush(Color.FromRgb(0x00, 0xD2, 0xD3)),
        AccessRuleScope.TimedTtl => new SolidColorBrush(Color.FromRgb(0xF5, 0x9E, 0x0B)),
        _ => Brushes.Gray
    };

    /// <summary>
    /// Текстовая метка источника правила или блокировки.
    /// </summary>
    public string SourceBadgeText => _dto.BlockSource switch
    {
        BlockSourceType.StartupMonitorBlacklist => "🚫 Чёрный список",
        BlockSourceType.WindowsFirewall => "🛡️ Windows Firewall",
        BlockSourceType.PortDefenderBan => "⚡ Бан Defender",
        BlockSourceType.KillswitchActive => "🛑 Killswitch",
        BlockSourceType.SessionBlock => "⏱️ Сессионный блок",
        _ => (_dto.State == AccessControlState.Allowed ? "🛡️ Доверенное" : "🔍 Мониторинг")
    };

    /// <summary>
    /// Фоновая кисть бейджа источника правила.
    /// </summary>
    public Brush SourceBadgeBg => _dto.BlockSource switch
    {
        BlockSourceType.StartupMonitorBlacklist => new SolidColorBrush(Color.FromArgb(0x40, 0xEF, 0x44, 0x44)),
        BlockSourceType.WindowsFirewall => new SolidColorBrush(Color.FromArgb(0x40, 0x3B, 0x82, 0xF6)),
        BlockSourceType.PortDefenderBan => new SolidColorBrush(Color.FromArgb(0x40, 0xF5, 0x9E, 0x0B)),
        _ => new SolidColorBrush(Color.FromArgb(0x20, 0xFF, 0xFF, 0xFF))
    };

    /// <summary>
    /// Кисть рамки бейджа источника правила.
    /// </summary>
    public Brush SourceBadgeBorder => _dto.BlockSource switch
    {
        BlockSourceType.StartupMonitorBlacklist => new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44)),
        BlockSourceType.WindowsFirewall => new SolidColorBrush(Color.FromRgb(0x3B, 0x82, 0xF6)),
        BlockSourceType.PortDefenderBan => new SolidColorBrush(Color.FromRgb(0xF5, 0x9E, 0x0B)),
        _ => new SolidColorBrush(Color.FromRgb(0x47, 0x55, 0x69))
    };

    /// <summary>
    /// Кисть цвета шрифта бейджа источника правила.
    /// </summary>
    public Brush SourceBadgeFg => Brushes.White;

    private void OnPropertyChanged(string propName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));
}

