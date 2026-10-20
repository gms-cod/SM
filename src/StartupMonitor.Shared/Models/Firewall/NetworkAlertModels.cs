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

    /// <summary>Протокол соединения ("TCP", "TCPv6", "UDP", "UDPv6").</summary>
    [JsonPropertyName("protocol")]
    public string Protocol { get; set; } = "TCP";

    /// <summary>Удалённый IP-адрес.</summary>
    [JsonPropertyName("remoteAddress")]
    public string RemoteAddress { get; set; } = string.Empty;

    /// <summary>Удалённый порт.</summary>
    [JsonPropertyName("remotePort")]
    public int RemotePort { get; set; }

    /// <summary>Момент обнаружения (UTC).</summary>
    [JsonPropertyName("detectedAt")]
    public DateTime DetectedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Решение пользователя по сетевому алерту.</summary>
public enum NetworkAlertResolution
{
    /// <summary>Всегда разрешать: создать разрешающее правило в Windows Firewall.</summary>
    AllowAlways = 0,

    /// <summary>Разрешить на текущую сессию (без создания правила в Firewall).</summary>
    AllowSession = 1,

    /// <summary>Всегда блокировать: создать блокирующее правило в Windows Firewall.</summary>
    BlockAlways = 2,

    /// <summary>Добавить программу в белый список: создать глобальное разрешающее правило в Windows Firewall для программы и перманентно доверять.</summary>
    WhitelistProgram = 3,

    /// <summary>Разрешить порт: прекратить всплывающие алерты по данному удаленному порту.</summary>
    AllowPort = 4
}

/// <summary>Запрос резолва сетевого алерта (Pipe: ResolveNetworkAlert).</summary>
public class ResolveNetworkAlertRequest
{
    /// <summary>Идентификатор алерта из <see cref="NetworkConnectionAlertDto.AlertId"/>.</summary>
    [JsonPropertyName("alertId")]
    public string AlertId { get; set; } = string.Empty;

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

    /// <summary>Выбранное решение.</summary>
    [JsonPropertyName("resolution")]
    public NetworkAlertResolution Resolution { get; set; }
}
