using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace StartupMonitor.Shared.Models;

/// <summary>Информация о запущенном процессе системы и его метриках производительности.</summary>
public class ProcessInfo : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }

    private int _processId;
    /// <summary>Идентификатор процесса (PID).</summary>
    public int ProcessId { get => _processId; set => Set(ref _processId, value); }

    private int _parentProcessId;
    /// <summary>PID родительского процесса.</summary>
    public int ParentProcessId { get => _parentProcessId; set => Set(ref _parentProcessId, value); }

    private string _processName = string.Empty;
    /// <summary>Имя процесса (например, chrome.exe).</summary>
    public string ProcessName { get => _processName; set => Set(ref _processName, value); }

    private string _filePath = string.Empty;
    /// <summary>Полный путь к исполняемому файлу на диске.</summary>
    public string FilePath { get => _filePath; set => Set(ref _filePath, value); }

    private string _commandLine = string.Empty;
    /// <summary>Командная строка запуска процесса с аргументами.</summary>
    public string CommandLine { get => _commandLine; set => Set(ref _commandLine, value); }

    private string _publisher = string.Empty;
    /// <summary>Издатель (цифровая подпись Authenticode).</summary>
    /// <summary>Издатель (цифровая подпись Authenticode или PE CompanyName).</summary>
    public string Publisher { get => _publisher; set => Set(ref _publisher, value); }

    private bool _isSigned;
    /// <summary>Флаг наличия проверенной доверенной цифровой подписи Authenticode.</summary>
    public bool IsSigned { get => _isSigned; set => Set(ref _isSigned, value); }

    private DigitalSignatureStatus _signatureStatus = DigitalSignatureStatus.Unknown;
    /// <summary>Статус криптографической проверки цифровой подписи Authenticode.</summary>
    public DigitalSignatureStatus SignatureStatus { get => _signatureStatus; set => Set(ref _signatureStatus, value); }

    private string? _signerName;
    /// <summary>Проверенное имя подписанта из сертификата Authenticode (CN).</summary>
    public string? SignerName { get => _signerName; set => Set(ref _signerName, value); }

    private string? _signatureIssuer;
    /// <summary>Удостоверяющий центр (CA), выдавший сертификат.</summary>
    public string? SignatureIssuer { get => _signatureIssuer; set => Set(ref _signatureIssuer, value); }

    private double _cpuUsagePercent;
    /// <summary>Загрузка процессора в процентах (0.0% - 100.0%).</summary>
    public double CpuUsagePercent
    {
        get => _cpuUsagePercent;
        set
        {
            if (Set(ref _cpuUsagePercent, value))
                OnPropertyChanged(nameof(DisplayCpu));
        }
    }

    private long _workingSetBytes;
    /// <summary>Использование оперативной памяти (Working Set в байтах).</summary>
    public long WorkingSetBytes
    {
        get => _workingSetBytes;
        set
        {
            if (Set(ref _workingSetBytes, value))
                OnPropertyChanged(nameof(DisplayRam));
        }
    }

    private long _privateBytes;
    /// <summary>Выделенная память (Private Bytes в байтах).</summary>
    public long PrivateBytes { get => _privateBytes; set => Set(ref _privateBytes, value); }

    private long _diskReadBytesPerSec;
    /// <summary>Скорость чтения с диска (Байт/с).</summary>
    public long DiskReadBytesPerSec
    {
        get => _diskReadBytesPerSec;
        set
        {
            if (Set(ref _diskReadBytesPerSec, value))
                OnPropertyChanged(nameof(DisplayDisk));
        }
    }

    private long _diskWriteBytesPerSec;
    /// <summary>Скорость записи на диск (Байт/с).</summary>
    public long DiskWriteBytesPerSec
    {
        get => _diskWriteBytesPerSec;
        set
        {
            if (Set(ref _diskWriteBytesPerSec, value))
                OnPropertyChanged(nameof(DisplayDisk));
        }
    }

    private long _networkBytesPerSec;
    /// <summary>Суммарная сетевая активность (Байт/с).</summary>
    public long NetworkBytesPerSec
    {
        get => _networkBytesPerSec;
        set
        {
            var hadActivity = HasNetworkActivity;
            if (Set(ref _networkBytesPerSec, value))
            {
                OnPropertyChanged(nameof(DisplayNetwork));
                if (HasNetworkActivity != hadActivity)
                {
                    OnPropertyChanged(nameof(HasNetworkActivity));
                }
            }
        }
    }

    private int _activeSocketsCount;
    /// <summary>Количество активных сетевых сокетов (TCP/UDP).</summary>
    public int ActiveSocketsCount
    {
        get => _activeSocketsCount;
        set
        {
            if (Set(ref _activeSocketsCount, value))
            {
                OnPropertyChanged(nameof(HasNetworkActivity));
            }
        }
    }

    private bool _hasNetworkActivity;
    /// <summary>Признак сетевой активности (наличие открытых сокетов или передача данных).</summary>
    public bool HasNetworkActivity
    {
        get => _hasNetworkActivity || _activeSocketsCount > 0 || _networkBytesPerSec > 0;
        set => Set(ref _hasNetworkActivity, value);
    }

    private DateTime _startTime;
    /// <summary>Время старта процесса.</summary>
    public DateTime StartTime { get => _startTime; set => Set(ref _startTime, value); }

    private string? _startupEntryId;
    /// <summary>ID связанной записи автозапуска (если процесс запущен из автозапуска).</summary>
    public string? StartupEntryId
    {
        get => _startupEntryId;
        set
        {
            if (Set(ref _startupEntryId, value))
                OnPropertyChanged(nameof(IsFromStartup));
        }
    }

    private EntryLocation? _startupLocation;
    /// <summary>Тип расположения автозапуска.</summary>
    public EntryLocation? StartupLocation
    {
        get => _startupLocation;
        set
        {
            if (Set(ref _startupLocation, value))
                OnPropertyChanged(nameof(DisplayStartupStatus));
        }
    }

    private string? _startupLocationDetail;
    /// <summary>Детали расположения автозапуска (ключ реестра, имя службы или задачи).</summary>
    public string? StartupLocationDetail { get => _startupLocationDetail; set => Set(ref _startupLocationDetail, value); }

    private string? _parentProcessName;
    /// <summary>Имя родительского процесса (если запущен вручную).</summary>
    public string? ParentProcessName
    {
        get => _parentProcessName;
        set
        {
            if (Set(ref _parentProcessName, value))
                OnPropertyChanged(nameof(DisplayStartupStatus));
        }
    }

    private bool _isSystemProcess;
    /// <summary>Является ли системным критическим процессом Windows (защита от закрытия).</summary>
    public bool IsSystemProcess { get => _isSystemProcess; set => Set(ref _isSystemProcess, value); }

    private bool _isBlocked;
    /// <summary>Заблокирован ли процесс правилом Чёрного списка.</summary>
    public bool IsBlocked { get => _isBlocked; set => Set(ref _isBlocked, value); }

    private bool _isService;
    /// <summary>Является ли процесс службой Windows.</summary>
    public bool IsService { get => _isService; set => Set(ref _isService, value); }

    private bool _hasWindow;
    /// <summary>Имеет ли процесс видимое главное окно (интерактивное приложение).</summary>
    public bool HasWindow { get => _hasWindow; set => Set(ref _hasWindow, value); }

    private string _description = string.Empty;
    /// <summary>Описание процесса или отображаемое имя службы.</summary>
    public string Description { get => _description; set => Set(ref _description, value); }

    private int _sessionId;
    /// <summary>Идентификатор сессии Windows (0 для служб/ядра, >0 для пользовательских сессий).</summary>
    public int SessionId { get => _sessionId; set => Set(ref _sessionId, value); }

    private bool _isSuspended;
    /// <summary>Приостановлен ли процесс (заморожен потоками через NtSuspendProcess).</summary>
    public bool IsSuspended { get => _isSuspended; set => Set(ref _isSuspended, value); }

    private CliThreatInfo? _threatInfo;
    /// <summary>Информация об эвристической оценке командной строки и пути процесса.</summary>
    public CliThreatInfo? ThreatInfo
    {
        get => _threatInfo;
        set
        {
            if (Set(ref _threatInfo, value))
                OnPropertyChanged(nameof(HasThreat));
        }
    }

    // Удобные форматированные свойства для UI
    [JsonIgnore]
    public bool HasThreat => ThreatInfo?.HasThreat == true;

    [JsonIgnore]
    public bool IsFromStartup => !string.IsNullOrEmpty(StartupEntryId);

    [JsonIgnore]
    public string DisplayCpu => CpuUsagePercent switch
    {
        < 0.05 => "0 %",
        < 10.0 => $"{CpuUsagePercent:F1} %",
        _ => $"{CpuUsagePercent:F0} %"
    };

    [JsonIgnore]
    public string DisplayRam => WorkingSetBytes switch
    {
        < 1024 * 1024 => $"{WorkingSetBytes / 1024.0:F0} КБ",
        < 1024L * 1024 * 1024 => $"{WorkingSetBytes / (1024.0 * 1024):F1} МБ",
        _ => $"{WorkingSetBytes / (1024.0 * 1024 * 1024):F2} ГБ"
    };

    [JsonIgnore]
    public string DisplayDisk
    {
        get
        {
            var total = DiskReadBytesPerSec + DiskWriteBytesPerSec;
            return total switch
            {
                < 1024 => "0 КБ/с",
                < 1024 * 1024 => $"{total / 1024.0:F0} КБ/с",
                _ => $"{total / (1024.0 * 1024):F1} МБ/с"
            };
        }
    }

    [JsonIgnore]
    public string DisplayNetwork => NetworkBytesPerSec switch
    {
        <= 0 => "0 КБ/с",
        < 1024 => $"{NetworkBytesPerSec / 1024.0:F1} КБ/с",
        < 1024 * 1024 => $"{NetworkBytesPerSec / 1024.0:F0} КБ/с",
        _ => $"{NetworkBytesPerSec / (1024.0 * 1024):F1} МБ/с"
    };

    [JsonIgnore]
    public string DisplayStartupStatus
    {
        get
        {
            if (StartupLocation.HasValue)
            {
                return StartupLocation.Value switch
                {
                    EntryLocation.RegistryRun => "Реестр (Run)",
                    EntryLocation.RegistryRunOnce => "Реестр (RunOnce)",
                    EntryLocation.RegistryRunServices => "Реестр (RunServices)",
                    EntryLocation.RegistryPolicies => "Групповая политика",
                    EntryLocation.StartupFolder => "Папка Автозагрузка",
                    EntryLocation.Service => "Служба Windows",
                    EntryLocation.Driver => "Драйвер Windows",
                    EntryLocation.TaskScheduler => "Планировщик",
                    EntryLocation.Winlogon => "Winlogon",
                    EntryLocation.ActiveSetup => "Active Setup",
                    EntryLocation.AppxStartupTask => "UWP / AppX",
                    EntryLocation.WmiEventSubscription => "WMI-подписка",
                    _ => "Автозапуск"
                };
            }
            if (!string.IsNullOrEmpty(ParentProcessName))
            {
                return ParentProcessName.Equals("explorer.exe", StringComparison.OrdinalIgnoreCase)
                    ? "Вручную (Проводник)"
                    : $"Родитель: {ParentProcessName}";
            }
            return "Фоновый процесс";
        }
    }

    [JsonIgnore]
    public int Pid { get => ProcessId; set => ProcessId = value; }

    [JsonIgnore]
    public string Name { get => ProcessName; set => ProcessName = value; }

    [JsonIgnore]
    public string ExecutablePath { get => FilePath; set => FilePath = value; }

    [JsonIgnore]
    public double CpuPercent { get => CpuUsagePercent; set => CpuUsagePercent = value; }

    [JsonIgnore]
    public long DiskIoBytesPerSec
    {
        get => DiskReadBytesPerSec + DiskWriteBytesPerSec;
        set => DiskReadBytesPerSec = value;
    }

    [JsonIgnore]
    public bool IsStartupEntry
    {
        get => IsFromStartup;
        set { }
    }

    private int _treeLevel;
    /// <summary>Уровень вложенности в иерархическом дереве процессов (0 для корня).</summary>
    [JsonIgnore]
    public int TreeLevel
    {
        get => _treeLevel;
        set
        {
            if (Set(ref _treeLevel, value))
            {
                OnPropertyChanged(nameof(TreeIndentLeft));
                OnPropertyChanged(nameof(IsChildProcess));
            }
        }
    }

    /// <summary>Величина левого отступа в пикселях для отображения уровня в дереве процессов.</summary>
    [JsonIgnore]
    public double TreeIndentLeft => TreeLevel * 18.0;

    /// <summary>Флаг дочернего процесса (TreeLevel > 0).</summary>
    [JsonIgnore]
    public bool IsChildProcess => TreeLevel > 0;

    private bool _hasChildren;
    /// <summary>Имеет ли процесс запущенные дочерние процессы.</summary>
    [JsonIgnore]
    public bool HasChildren { get => _hasChildren; set => Set(ref _hasChildren, value); }

    private bool _isExpanded = true;
    /// <summary>Развернута ли ветка дерева данного процесса.</summary>
    [JsonIgnore]
    public bool IsExpanded { get => _isExpanded; set => Set(ref _isExpanded, value); }

    private int _childrenCount;
    /// <summary>Количество прямых дочерних процессов.</summary>
    [JsonIgnore]
    public int ChildrenCount { get => _childrenCount; set => Set(ref _childrenCount, value); }
}

