using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

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
        [21] = ("FTP", "Передача файлов (незащищённый протокол)", PortRiskLevel.High),
        [22] = ("SSH", "Защищённая удалённая оболочка Secure Shell", PortRiskLevel.Medium),
        [23] = ("Telnet", "Незащищённая консоль удалённого доступа", PortRiskLevel.Critical),
        [25] = ("SMTP", "Простой протокол передачи почты", PortRiskLevel.Low),
        [53] = ("DNS", "Служба доменных имён", PortRiskLevel.Low),
        [80] = ("HTTP", "Веб-сервер без шифрования", PortRiskLevel.Medium),
        [110] = ("POP3", "Получение почтовых сообщений", PortRiskLevel.Low),
        [135] = ("RPC", "Удалённый вызов процедур Microsoft Windows", PortRiskLevel.Critical),
        [139] = ("NetBIOS", "Служба сеансов NetBIOS Windows", PortRiskLevel.Critical),
        [143] = ("IMAP", "Протокол доступа к электронной почте", PortRiskLevel.Low),
        [443] = ("HTTPS", "Защищённый веб-трафик (TLS/SSL)", PortRiskLevel.Medium),
        [445] = ("SMB", "Общий доступ к файлам и принтерам Windows", PortRiskLevel.Critical),
        [993] = ("IMAPS", "Защищённый доступ к почте IMAP over TLS", PortRiskLevel.Low),
        [995] = ("POP3S", "Защищённый доступ к почте POP3 over TLS", PortRiskLevel.Low),
        [1433] = ("MSSQL", "Microsoft SQL Server Database Engine", PortRiskLevel.High),
        [1521] = ("Oracle", "Oracle Database Listener", PortRiskLevel.High),
        [3306] = ("MySQL", "Сервер баз данных MySQL / MariaDB", PortRiskLevel.High),
        [3389] = ("RDP", "Удалённый рабочий стол Microsoft Windows", PortRiskLevel.Critical),
        [5432] = ("PostgreSQL", "Сервер реляционной СУБД PostgreSQL", PortRiskLevel.High),
        [5900] = ("VNC", "Удалённое графическое управление экраном", PortRiskLevel.High),
        [6379] = ("Redis", "Сетевое хранилище данных в памяти Redis", PortRiskLevel.High),
        [8080] = ("HTTP-Alt", "Альтернативный веб-порт / Прокси-сервер", PortRiskLevel.Medium),
        [8443] = ("HTTPS-Alt", "Альтернативный защищённый веб-порт", PortRiskLevel.Medium),
        [27017] = ("MongoDB", "Документоориентированная СУБД MongoDB", PortRiskLevel.High)
    };

    /// <summary>
    /// Возвращает метаданные известного порта (название, описание, уровень риска).
    /// </summary>
    /// <param name="port">Номер TCP порта (1..65535).</param>
    /// <returns>Кортеж с именем службы, описанием и категорией риска.</returns>
    public static (string Name, string Description, PortRiskLevel Risk) GetPortInfo(int port)
    {
        if (KnownPorts.TryGetValue(port, out var info))
            return info;

        if (port is >= 1 and <= 1023)
            return ("System Port", $"Системный привилегированный порт TCP {port}", PortRiskLevel.Medium);

        return ("Custom Port", $"Пользовательский отслеживаемый TCP порт {port}", PortRiskLevel.Custom);
    }

    /// <summary>
    /// Возвращает текстовый бейдж уровня риска на русском языке.
    /// </summary>
    public static string GetRiskBadgeText(PortRiskLevel risk) => risk switch
    {
        PortRiskLevel.Critical => "Критический",
        PortRiskLevel.High => "Высокий",
        PortRiskLevel.Medium => "Средний",
        PortRiskLevel.Low => "Инфо",
        _ => "Пользовательский"
    };

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
    public static IReadOnlyList<(int Port, string Title)> GetPresets() => new List<(int, string)>
    {
        (3389, "RDP — Удалённый рабочий стол (3389)"),
        (445, "SMB — Файловые шары Windows (445)"),
        (22, "SSH — Защищённая консоль (22)"),
        (1433, "MSSQL — Microsoft SQL Server (1433)"),
        (3306, "MySQL — Сервер MySQL (3306)"),
        (5432, "PostgreSQL — База данных (5432)"),
        (6379, "Redis — Key-Value хранилище (6379)"),
        (27017, "MongoDB — NoSQL СУБД (27017)"),
        (80, "HTTP — Веб-трафик (80)"),
        (443, "HTTPS — Защищённый веб-трафик (443)"),
        (21, "FTP — Передача файлов (21)"),
        (5900, "VNC — Удалённый доступ к экрану (5900)"),
        (8080, "HTTP-Alt — Альтернативный веб-сервер (8080)")
    };
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
    }

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
