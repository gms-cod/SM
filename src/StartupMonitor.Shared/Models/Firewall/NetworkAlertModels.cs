// DTO модели алертов о новых сетевых подключениях программ.
// Используются службой (SYSTEM), Pipe IPC и UI. Не зависят от COM — чистые POCO.
using System.Text.Json.Serialization;

namespace StartupMonitor.Shared.Models.Firewall;

/// <summary>Алерт о новом исходящем сетевом подключении программы во внешнюю сеть.</summary>
public class NetworkConnectionAlertDto
{
    /// <summary>Уникальный идентификатор алерта (для резолва через Pipe).</summary>
    [JsonPropertyName("alertId")]
    public string AlertId { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>PID процесса, установившего соединение.</summary>
    [JsonPropertyName("processId")]
    public int ProcessId { get; set; }

    /// <summary>Имя процесса (например "notepad.exe").</summary>
    [JsonPropertyName("processName")]
    public string ProcessName { get; set; } = string.Empty;

    /// <summary>Полный путь к исполняемому файлу процесса.</summary>
    [JsonPropertyName("processPath")]
    public string ProcessPath { get; set; } = string.Empty;

    /// <summary>Временное разрешение поддерживается текущим режимом фильтрации и доступной идентичностью.</summary>
    [JsonPropertyName("canAllowSession")]
    public bool CanAllowSession { get; set; } = true;

    /// <summary>Временная блокировка доступна для достоверного полного пути приложения.</summary>
    [JsonPropertyName("canBlockSession")]
    public bool CanBlockSession { get; set; } = true;

    /// <summary>Протокол соединения ("TCP", "TCPv6", "UDP", "UDPv6").</summary>
    [JsonPropertyName("protocol")]
    public string Protocol { get; set; } = "TCP";

    /// <summary>Направление наблюдения либо Unknown; установленный сокет сам по себе не доказывает исходящее соединение.</summary>
    [JsonPropertyName("direction")]
    public string Direction { get; set; } = "Unknown";

    /// <summary>Удалённый IP-адрес.</summary>
    [JsonPropertyName("remoteAddress")]
    public string RemoteAddress { get; set; } = string.Empty;

    /// <summary>Удалённый порт.</summary>
    [JsonPropertyName("remotePort")]
    public int RemotePort { get; set; }

    /// <summary>Момент обнаружения (UTC).</summary>
    [JsonPropertyName("detectedAt")]
    public DateTime DetectedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Число соединений, объединённых в один запрос решения для приложения.</summary>
    [JsonPropertyName("connectionCount")]
    public int ConnectionCount { get; set; } = 1;

    /// <summary>Последнее наблюдение соединения группы в системном снимке, UTC.</summary>
    [JsonPropertyName("lastSeenAt")]
    public DateTime LastSeenAt { get; set; } = DateTime.UtcNow;

    /// <summary>Наблюдалось ли соединение группы в последнем завершённом снимке.</summary>
    [JsonPropertyName("isActive")]
    public bool IsActive { get; set; } = true;

    /// <summary>Назначение программы по оценке нейросети.</summary>
    [JsonPropertyName("aiPurpose")]
    public string? AiPurpose { get; set; }

    /// <summary>Оценка рисков блокировки процесса (для ОС, печати, сети, работы приложений) по мнению нейросети.</summary>
    [JsonPropertyName("aiBlockingRisk")]
    public string? AiBlockingRisk { get; set; }

    /// <summary>Рекомендация нейросети пользователю.</summary>
    [JsonPropertyName("aiRecommendation")]
    public string? AiRecommendation { get; set; }

    /// <summary>Рекомендуемое нейросетью действие ("AllowSession", "AllowAlways", "BlockSession", "BlockAlways").</summary>
    [JsonPropertyName("aiRecommendedAction")]
    public string? AiRecommendedAction { get; set; }
}

/// <summary>Решение пользователя по сетевому алерту с поддержкой ненавязчивых и постоянных политик.</summary>
public enum NetworkAlertResolution
{
    /// <summary>Всегда разрешать: создать разрешающее правило в Windows Firewall.</summary>
    AllowAlways = 0,

    /// <summary>Разрешить на текущую сессию (без создания постоянного правила в Firewall).</summary>
    AllowSession = 1,

    /// <summary>Всегда блокировать: создать блокирующее правило в Windows Firewall.</summary>
    BlockAlways = 2,

    /// <summary>Добавить программу в белый список: создать глобальное разрешающее правило в Windows Firewall для программы и перманентно доверять.</summary>
    WhitelistProgram = 3,

    /// <summary>Разрешить порт: прекратить всплывающие алерты по данному удаленному порту.</summary>
    AllowPort = 4,

    /// <summary>Добавить программу в чёрный список: создать глобальное блокирующее правило в Windows Firewall и перманентно запретить любые сетевые соединения.</summary>
    BlacklistProgram = 5,

    /// <summary>Запретить на текущую сессию (временная блокировка соединения без создания постоянного правила в реестре).</summary>
    BlockSession = 6,

    /// <summary>Создать детальное правило для приложения с ручным выбором направления, портов и протоколов.</summary>
    CreateRule = 7,

    /// <summary>Отложить показ запроса без изменения сетевой политики приложения.</summary>
    Defer = 8
}

/// <summary>Проверенный исход применения выбора; неизвестный транспортный исход не является отказом политики.</summary>
public enum NetworkAlertResolutionStatus
{
    /// <summary>Политика применена и подтверждена.</summary>
    Applied,
    /// <summary>Эта же операция уже была подтверждена ранее.</summary>
    AlreadyApplied,
    /// <summary>Запрос заменён более новым состоянием политики либо завершён другим клиентом.</summary>
    Superseded,
    /// <summary>Показ отложен без изменения правил.</summary>
    Deferred,
    /// <summary>Выбранная операция недоступна для текущей идентичности или режима.</summary>
    Unsupported,
    /// <summary>Выбор конфликтует с актуальной политикой или правом владения запросом.</summary>
    Conflict,
    /// <summary>Операция отклонена; сохранение прежнего состояния подтверждено.</summary>
    Failed,
    /// <summary>Не удалось подтвердить полное применение или восстановление прежнего состояния.</summary>
    PartialFailure,
    /// <summary>Ответ потерян либо состояние службы недоступно; требуется сверка или идемпотентный повтор.</summary>
    UnknownOutcome
}

/// <summary>Запрос резолва сетевого алерта (Pipe: ResolveNetworkAlert).</summary>
public class ResolveNetworkAlertRequest
{
    /// <summary>Идентификатор алерта из <see cref="NetworkConnectionAlertDto.AlertId"/>.</summary>
    [JsonPropertyName("alertId")]
    public string AlertId { get; set; } = string.Empty;

    /// <summary>Устойчивый идентификатор попытки, сохраняемый при повторе после потери подтверждения.</summary>
    [JsonPropertyName("operationId")]
    public string OperationId { get; set; } = string.Empty;

    /// <summary>Полный путь к исполняемому файлу процесса.</summary>
    [JsonPropertyName("processPath")]
    public string ProcessPath { get; set; } = string.Empty;

    /// <summary>Имя процесса.</summary>
    [JsonPropertyName("processName")]
    public string ProcessName { get; set; } = string.Empty;

    /// <summary>Удалённый IP-адрес из алерта.</summary>
    [JsonPropertyName("remoteAddress")]
    public string RemoteAddress { get; set; } = string.Empty;

    /// <summary>Удалённый порт из алерта.</summary>
    [JsonPropertyName("remotePort")]
    public int RemotePort { get; set; }

    /// <summary>Выбранное пользователем решение.</summary>
    [JsonPropertyName("resolution")]
    public NetworkAlertResolution Resolution { get; set; }
}
