using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace StartupMonitor.Shared.Models;

/// <summary>Запись автозапуска (программа, служба, задача, скрипт и т.д.).</summary>
public class StartupEntry : INotifyPropertyChanged
{
    /// <summary>Событие изменения свойства (для привязки в WPF).</summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Уведомляет UI об изменении свойства.</summary>
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    /// <summary>Сеттер с проверкой на изменение и уведомлением UI.</summary>
    /// <returns>true, если значение изменилось.</returns>
    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }

    /// <summary>Уникальный идентификатор записи.</summary>
    private string _id = Guid.NewGuid().ToString("N");
    /// <summary>Уникальный идентификатор записи.</summary>
    public string Id { get => _id; set => Set(ref _id, value); }

    /// <summary>Имя программы/скрипта.</summary>
    private string _name = string.Empty;
    /// <summary>Имя программы/скрипта.</summary>
    public string Name { get => _name; set => Set(ref _name, value); }

    /// <summary>Путь к исполняемому файлу.</summary>
    private string _filePath = string.Empty;
    /// <summary>Путь к исполняемому файлу.</summary>
    public string FilePath
    {
        get => _filePath;
        set { if (Set(ref _filePath, value)) OnPropertyChanged(nameof(DisplayPath)); }
    }

    /// <summary>Уведомляет UI об обновлении иконки программы (перепривязка FilePath).</summary>
    public void RefreshIcon()
    {
        OnPropertyChanged(nameof(FilePath));
    }

    /// <summary>Издатель (цифровая подпись).</summary>
    /// <summary>Издатель (цифровая подпись или PE CompanyName).</summary>
    private string _publisher = string.Empty;
    /// <summary>Издатель (цифровая подпись).</summary>
    /// <summary>Издатель (цифровая подпись или PE CompanyName).</summary>
    public string Publisher { get => _publisher; set => Set(ref _publisher, value); }

    /// <summary>Флаг наличия проверенной доверенной цифровой подписи Authenticode.</summary>
    private bool _isSigned;
    public bool IsSigned { get => _isSigned; set => Set(ref _isSigned, value); }

    /// <summary>Статус криптографической проверки цифровой подписи Authenticode.</summary>
    private DigitalSignatureStatus _signatureStatus = DigitalSignatureStatus.Unknown;
    public DigitalSignatureStatus SignatureStatus { get => _signatureStatus; set => Set(ref _signatureStatus, value); }

    /// <summary>Проверенное имя подписанта из сертификата Authenticode (CN).</summary>
    private string? _signerName;
    public string? SignerName { get => _signerName; set => Set(ref _signerName, value); }

    /// <summary>Удостоверяющий центр (CA), выдавший сертификат.</summary>
    private string? _signatureIssuer;
    public string? SignatureIssuer { get => _signatureIssuer; set => Set(ref _signatureIssuer, value); }

    /// <summary>Местоположение записи в системе.</summary>
    private EntryLocation _location;
    /// <summary>Местоположение записи в системе (реестр, папка, служба, планировщик...).</summary>
    public EntryLocation Location { get => _location; set => Set(ref _location, value); }

    /// <summary>Детали местоположения (путь в реестре, имя задачи, и т.д.).</summary>
    private string _locationDetail = string.Empty;
    /// <summary>Детали местоположения (путь в реестре, имя задачи, и т.д.).</summary>
    public string LocationDetail { get => _locationDetail; set => Set(ref _locationDetail, value); }

    /// <summary>Текущий статус записи.</summary>
    private EntryStatus _status = EntryStatus.Enabled;
    /// <summary>Статус записи (Enabled/Disabled/Blocked).</summary>
    public EntryStatus Status { get => _status; set => Set(ref _status, value); }

    /// <summary>Дата первого обнаружения записи (UTC).</summary>
    private DateTime _detectedAt = DateTime.UtcNow;
    /// <summary>Дата первого обнаружения записи (UTC).</summary>
    public DateTime DetectedAt { get => _detectedAt; set => Set(ref _detectedAt, value); }

    /// <summary>Локальная дата и время первого обнаружения записи для UI.</summary>
    [JsonIgnore]
    public DateTime LocalDetectedAt => DetectedAt.Kind == DateTimeKind.Utc
        ? DetectedAt.ToLocalTime()
        : (DetectedAt.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(DetectedAt, DateTimeKind.Utc).ToLocalTime()
            : DetectedAt);

    /// <summary>SHA256-хеш исполняемого файла.</summary>
    private string _sha256 = string.Empty;
    /// <summary>SHA256-хеш исполняемого файла.</summary>
    public string SHA256 { get => _sha256; set => Set(ref _sha256, value); }

    /// <summary>Командная строка (аргументы запуска).</summary>
    private string _commandLine = string.Empty;
    /// <summary>Командная строка (аргументы запуска).</summary>
    public string CommandLine
    {
        get => _commandLine;
        set { if (Set(ref _commandLine, value)) OnPropertyChanged(nameof(DisplayPath)); }
    }

    /// <summary>Флаг: запись из чёрного списка.</summary>
    private bool _fromBlacklist;
    /// <summary>Указывает, что запись добавлена из сработавшего правила чёрного списка.</summary>
    public bool FromBlacklist { get => _fromBlacklist; set => Set(ref _fromBlacklist, value); }

    /// <summary>JSON-строка с данными для восстановления отключённой записи.</summary>
    private string _disablePayload = string.Empty;
    /// <summary>JSON-строка с данными для восстановления отключённой записи.</summary>
    public string DisablePayload { get => _disablePayload; set => Set(ref _disablePayload, value); }

    /// <summary>Задержка отложенного запуска в секундах (если статус Delayed).</summary>
    private int _delaySeconds = 30;
    /// <summary>Задержка отложенного запуска в секундах.</summary>
    public int DelaySeconds { get => _delaySeconds; set => Set(ref _delaySeconds, value); }

    /// <summary>AI-рейтинг (1-10, от безопасного к опасному).</summary>
    private int _aiRating;
    /// <summary>AI-рейтинг (1 — безопасно, 10 — опасно).</summary>
    public int AiRating { get => _aiRating; set => Set(ref _aiRating, value); }

    /// <summary>AI-категория (legitimate, adware, trojan, ...).</summary>
    private string _aiCategory = "";
    /// <summary>Категория угрозы по версии AI.</summary>
    public string AiCategory { get => _aiCategory; set => Set(ref _aiCategory, value); }

    /// <summary>AI-рекомендация (описание угрозы).</summary>
    private string _aiRecommendation = "";
    /// <summary>Рекомендация AI по обработке записи.</summary>
    public string AiRecommendation { get => _aiRecommendation; set => Set(ref _aiRecommendation, value); }

    /// <summary>Уровень риска по версии AI (низкий, средний, высокий).</summary>
    private string _aiRisk = "";
    /// <summary>Уровень риска по версии AI.</summary>
    public string AiRisk { get => _aiRisk; set => Set(ref _aiRisk, value); }

    /// <summary>Зона репутации Kaspersky (Green/Red/Grey/NotFound).</summary>
    private string _kasperskyZone = "";
    /// <summary>Зона репутации Kaspersky (Green/Red/Grey/NotFound).</summary>
    public string KasperskyZone { get => _kasperskyZone; set => Set(ref _kasperskyZone, value); }

    /// <summary>Название обнаружения Kaspersky (если есть).</summary>
    private string _kasperskyDetection = "";
    /// <summary>Название обнаружения Kaspersky (например, "Trojan.Win32.Generic").</summary>
    public string KasperskyDetection { get => _kasperskyDetection; set => Set(ref _kasperskyDetection, value); }

    /// <summary>Статус файла по данным Kaspersky.</summary>
    private string _kasperskyFileStatus = "";
    /// <summary>Статус файла по данным Kaspersky.</summary>
    public string KasperskyFileStatus { get => _kasperskyFileStatus; set => Set(ref _kasperskyFileStatus, value); }

    /// <summary>Детектированный язык программы (ru, en, de, ...).</summary>
    private string _language = "";
    /// <summary>Язык программы (по PE-ресурсам).</summary>
    public string Language { get => _language; set => Set(ref _language, value); }

    /// <summary>Дополнительные данные (зависят от местоположения записи).</summary>
    public Dictionary<string, string> RawData { get; set; } = [];

    /// <summary>Информация об эвристической оценке командной строки и путей запуска.</summary>
    private CliThreatInfo? _threatInfo;
    /// <summary>Информация об эвристической оценке командной строки и путей запуска.</summary>
    public CliThreatInfo? ThreatInfo
    {
        get => _threatInfo;
        set
        {
            if (Set(ref _threatInfo, value))
                OnPropertyChanged(nameof(HasThreat));
        }
    }

    /// <summary>Признак обнаружения подозрительной команды или пути.</summary>
    [JsonIgnore]
    public bool HasThreat => ThreatInfo?.HasThreat == true;

    /// <summary>Текст бейджа различий со снимком системы (например "+ Новая" или "~ Изменена").</summary>
    private string? _snapshotDiffBadge;
    /// <summary>Текст бейджа различий со снимком системы.</summary>
    [JsonIgnore]
    public string? SnapshotDiffBadge
    {
        get => _snapshotDiffBadge;
        set => Set(ref _snapshotDiffBadge, value);
    }

    /// <summary>Цвет фона бейджа различий со снимком системы (#10B981 для новых, #3B82F6 для измененных).</summary>
    private string? _snapshotDiffColor;
    /// <summary>Цвет фона бейджа различий со снимком системы.</summary>
    [JsonIgnore]
    public string? SnapshotDiffColor
    {
        get => _snapshotDiffColor;
        set => Set(ref _snapshotDiffColor, value);
    }

    /// <summary>Всплывающая подсказка с подробностями отличия записи от снимка.</summary>
    private string? _snapshotDiffTooltip;
    /// <summary>Всплывающая подсказка с подробностями отличия записи от снимка.</summary>
    [JsonIgnore]
    public string? SnapshotDiffTooltip
    {
        get => _snapshotDiffTooltip;
        set => Set(ref _snapshotDiffTooltip, value);
    }

    /// <summary>Измеренное время замедления старта системы данным приложением в мс (из журнала Diagnostics-Performance).</summary>
    private long? _bootDelayImpactMs;
    /// <summary>Измеренное время замедления старта системы данным приложением в мс.</summary>
    [JsonIgnore]
    public long? BootDelayImpactMs
    {
        get => _bootDelayImpactMs;
        set => Set(ref _bootDelayImpactMs, value);
    }

    /// <summary>Текст бейджа замедления загрузки (например "⏳ +25.9с").</summary>
    private string? _bootDelayBadge;
    /// <summary>Текст бейджа замедления загрузки.</summary>
    [JsonIgnore]
    public string? BootDelayBadge
    {
        get => _bootDelayBadge;
        set => Set(ref _bootDelayBadge, value);
    }

    /// <summary>Всплывающая подсказка с описанием влияния на загрузку системы.</summary>
    private string? _bootDelayTooltip;
    /// <summary>Всплывающая подсказка с описанием влияния на загрузку системы.</summary>
    [JsonIgnore]
    public string? BootDelayTooltip
    {
        get => _bootDelayTooltip;
        set => Set(ref _bootDelayTooltip, value);
    }

    /// <summary>Строка для отображения: командная строка или путь к файлу.</summary>
    public string DisplayPath => string.IsNullOrEmpty(CommandLine) ? FilePath : CommandLine;
}
