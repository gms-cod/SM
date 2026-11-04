using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using StartupMonitor.UI.Services;

namespace StartupMonitor.UI.Models;

/// <summary>
/// Уровень риска / критичности открытого сетевого порта.
/// </summary>
public enum PortRiskLevel
{
    /// <summary>Низкий риск (информационные сервисы, стандартные исходящие протоколы).</summary>
    Low,
    /// <summary>Средний риск (веб-трафик, базы данных в изолированной сети).</summary>
    Medium,
    /// <summary>Высокий риск (СУБД, удалённые оболочки, критические сетевые службы).</summary>
    High,
    /// <summary>Критический риск (RDP, SMB, RPC — частые мишени вымогателей и эксплойтов).</summary>
    Critical,
    /// <summary>Пользовательский порт.</summary>
    Custom
}

/// <summary>
/// Вспомогательный справочник известных портов и протоколов для Port Defender.
/// Предоставляет названия служб, описания и уровни риска для портов TCP.
/// </summary>
public static class PortDefenderPortHelper
{
    private static readonly Dictionary<int, (string Name, string Description, PortRiskLevel Risk)> KnownPorts = new()
    {
        [21] = ("FTP", "DefenderPortDescription21", PortRiskLevel.High),
        [22] = ("SSH", "DefenderPortDescription22", PortRiskLevel.Medium),
        [23] = ("Telnet", "DefenderPortDescription23", PortRiskLevel.Critical),
        [25] = ("SMTP", "DefenderPortDescription25", PortRiskLevel.Low),
        [53] = ("DNS", "DefenderPortDescription53", PortRiskLevel.Low),
        [80] = ("HTTP", "DefenderPortDescription80", PortRiskLevel.Medium),
        [110] = ("POP3", "DefenderPortDescription110", PortRiskLevel.Low),
        [135] = ("RPC", "DefenderPortDescription135", PortRiskLevel.Critical),
        [139] = ("NetBIOS", "DefenderPortDescription139", PortRiskLevel.Critical),
        [143] = ("IMAP", "DefenderPortDescription143", PortRiskLevel.Low),
        [443] = ("HTTPS", "DefenderPortDescription443", PortRiskLevel.Medium),
        [445] = ("SMB", "DefenderPortDescription445", PortRiskLevel.Critical),
        [993] = ("IMAPS", "DefenderPortDescription993", PortRiskLevel.Low),
        [995] = ("POP3S", "DefenderPortDescription995", PortRiskLevel.Low),
        [1433] = ("MSSQL", "DefenderPortDescription1433", PortRiskLevel.High),
        [1521] = ("Oracle", "DefenderPortDescription1521", PortRiskLevel.High),
        [3306] = ("MySQL", "DefenderPortDescription3306", PortRiskLevel.High),
        [3389] = ("RDP", "DefenderPortDescription3389", PortRiskLevel.Critical),
        [5432] = ("PostgreSQL", "DefenderPortDescription5432", PortRiskLevel.High),
        [5900] = ("VNC", "DefenderPortDescription5900", PortRiskLevel.High),
        [6379] = ("Redis", "DefenderPortDescription6379", PortRiskLevel.High),
        [8080] = ("HTTP-Alt", "DefenderPortDescription8080", PortRiskLevel.Medium),
        [8443] = ("HTTPS-Alt", "DefenderPortDescription8443", PortRiskLevel.Medium),
        [27017] = ("MongoDB", "DefenderPortDescription27017", PortRiskLevel.High)
    };

    /// <summary>
    /// Возвращает метаданные известного порта (название, описание, уровень риска).
    /// </summary>
    /// <param name="port">Номер TCP порта (1..65535).</param>
    /// <returns>Кортеж с именем службы, описанием и категорией риска.</returns>
    public static (string Name, string Description, PortRiskLevel Risk) GetPortInfo(int port)
    {
        if (port is < 1 or > 65535) throw new ArgumentOutOfRangeException(nameof(port));
        var loc = LocalizationService.Instance;
        if (KnownPorts.TryGetValue(port, out var info))
            return (info.Name, loc[info.Description], info.Risk);

        if (port is >= 1 and <= 1023)
            return (loc["DefenderSystemPort"], loc.Format("DefenderSystemPortFormat", port), PortRiskLevel.Medium);

        return (loc["DefenderCustomPort"], loc.Format("DefenderCustomPortFormat", port), PortRiskLevel.Custom);
    }

    /// <summary>
    /// Возвращает текстовый бейдж уровня риска на активном языке интерфейса.
    /// </summary>
    /// <param name="risk">Языконезависимая категория риска.</param>
    /// <returns>Локализованная подпись бейджа.</returns>
    public static string GetRiskBadgeText(PortRiskLevel risk) => LocalizationService.Instance[risk switch
    {
        PortRiskLevel.Critical => "DefenderRiskCritical",
        PortRiskLevel.High => "DefenderRiskHigh",
        PortRiskLevel.Medium => "DefenderRiskMedium",
        PortRiskLevel.Low => "DefenderRiskLow",
        _ => "DefenderRiskCustom"
    }];

    /// <summary>
    /// Возвращает цвет фона бейджа риска (HEX) для WPF UI.
    /// </summary>
    public static string GetRiskBadgeColor(PortRiskLevel risk) => risk switch
    {
        PortRiskLevel.Critical => "#D32F2F", // Красный
        PortRiskLevel.High => "#F57C00",     // Оранжевый
        PortRiskLevel.Medium => "#FBC02D",   // Янтарный/Жёлтый
        PortRiskLevel.Low => "#1976D2",      // Синий
        _ => "#00897B"                       // Бирюзовый
    };

    /// <summary>
    /// Возвращает цвет текста для бейджа риска (HEX).
    /// </summary>
    public static string GetRiskTextColor(PortRiskLevel risk) => risk switch
    {
        PortRiskLevel.Medium => "#212121",   // Тёмный текст на жёлтом фоне
        _ => "#FFFFFF"                       // Белый текст
    };

    /// <summary>
    /// Возвращает список популярных предустановленных портов для быстрого выбора.
    /// </summary>
    /// <returns>Названия шаблонов с описаниями на текущем языке.</returns>
    public static IReadOnlyList<(int Port, string Title)> GetPresets() =>
        new[] { 3389, 445, 22, 1433, 3306, 5432, 6379, 27017, 80, 443, 21, 5900, 8080 }
            .Select(port =>
            {
                var info = GetPortInfo(port);
                return (port, $"{info.Name} — {info.Description} ({port})");
            }).ToList();
}

/// <summary>
/// Представление защищаемого порта в интерфейсе Port Defender.
/// Реализует INotifyPropertyChanged для реактивного обновления элементов интерфейса.
/// </summary>
public sealed class PortDefenderPortItem : INotifyPropertyChanged
{
    private int _port;
    private string _serviceName = string.Empty;
    private string _description = string.Empty;
    private PortRiskLevel _riskLevel = PortRiskLevel.Custom;
    private string _riskBadge = string.Empty;
    private string _badgeColor = "#00897B";
    private string _badgeTextColor = "#FFFFFF";

    /// <summary>Событие изменения свойств модели.</summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Инициализирует защищаемый порт и вычисляет его метаданные по номеру порта.
    /// </summary>
    /// <param name="port">Номер порта TCP (1..65535).</param>
    public PortDefenderPortItem(int port)
    {
        UpdatePort(port);
        // Слабая подписка не удерживает строки таблицы после обновления коллекции или закрытия диалога.
        PropertyChangedEventManager.AddHandler(LocalizationService.Instance, OnLanguageChanged, string.Empty);
    }

    /// <summary>Перечитывает описания и бейдж без смены номера порта при переключении языка.</summary>
    /// <param name="sender">Сервис ресурсов.</param>
    /// <param name="e">Уведомление об изменении языка.</param>
    private void OnLanguageChanged(object? sender, PropertyChangedEventArgs e) => UpdatePort(_port);

    /// <summary>
    /// Номер защищаемого TCP порта.
    /// </summary>
    public int Port
    {
        get => _port;
        set
        {
            if (_port != value)
            {
                UpdatePort(value);
            }
        }
    }

    /// <summary>
    /// Название сетевой службы или протокола.
    /// </summary>
    public string ServiceName
    {
        get => _serviceName;
        private set => SetField(ref _serviceName, value);
    }

    /// <summary>
    /// Краткое описание роли порта и угроз безопасности.
    /// </summary>
    public string Description
    {
        get => _description;
        private set => SetField(ref _description, value);
    }

    /// <summary>
    /// Уровень риска открытого порта.
    /// </summary>
    public PortRiskLevel RiskLevel
    {
        get => _riskLevel;
        private set => SetField(ref _riskLevel, value);
    }

    /// <summary>
    /// Текстовый бейдж уровня риска ("Критический", "Высокий", "Средний", "Инфо", "Пользовательский").
    /// </summary>
    public string RiskBadge
    {
        get => _riskBadge;
        private set => SetField(ref _riskBadge, value);
    }

    /// <summary>
    /// Цвет фона бейджа риска (HEX).
    /// </summary>
    public string BadgeColor
    {
        get => _badgeColor;
        private set => SetField(ref _badgeColor, value);
    }

    /// <summary>
    /// Цвет текста бейджа риска (HEX).
    /// </summary>
    public string BadgeTextColor
    {
        get => _badgeTextColor;
        private set => SetField(ref _badgeTextColor, value);
    }

    /// <summary>
    /// Форматированная строка номера порта для отображения (например "#3389").
    /// </summary>
    public string FormattedPort => $"#{_port}";

    /// <summary>
    /// Обновляет параметры модели на основе нового номера порта.
    /// </summary>
    /// <param name="newPort">Новый номер порта.</param>
    public void UpdatePort(int newPort)
    {
        _port = newPort;
        var (name, desc, risk) = PortDefenderPortHelper.GetPortInfo(newPort);
        ServiceName = name;
        Description = desc;
        RiskLevel = risk;
        RiskBadge = PortDefenderPortHelper.GetRiskBadgeText(risk);
        BadgeColor = PortDefenderPortHelper.GetRiskBadgeColor(risk);
        BadgeTextColor = PortDefenderPortHelper.GetRiskTextColor(risk);

        OnPropertyChanged(nameof(Port));
        OnPropertyChanged(nameof(FormattedPort));
    }

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        OnPropertyChanged(propertyName);
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
