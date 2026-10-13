// DTO модели сетевого экрана Windows (Windows Advanced Firewall).
// Используются службой (SYSTEM), Pipe IPC и UI. Не зависят от COM — чистые POCO/record.
using System.Net;
using System.Text.Json.Serialization;
using StartupMonitor.Shared.Models;

namespace StartupMonitor.Shared.Models.Firewall;

/// <summary>Направление правила брандмауэра.</summary>
public enum FirewallDirection
{
    /// <summary>Входящий трафик.</summary>
    Inbound = 1,
    /// <summary>Исходящий трафик.</summary>
    Outbound = 2,
    /// <summary>Оба направления (создаются два правила In+Out).</summary>
    Both = 3
}

/// <summary>Протокол для правил портов / IP.</summary>
public enum FirewallProtocol
{
    /// <summary>Любой протокол (для IP-блоков).</summary>
    Any = 256,
    /// <summary>TCP (6).</summary>
    Tcp = 6,
    /// <summary>UDP (17).</summary>
    Udp = 17
}

/// <summary>Действие правила.</summary>
public enum FirewallAction
{
    /// <summary>Блокировать.</summary>
    Block = 0,
    /// <summary>Разрешить.</summary>
    Allow = 1
}

/// <summary>Профили сети Windows (битовая маска, как в INetFwRule.Profiles).</summary>
[Flags]
public enum FirewallProfile
{
    /// <summary>Доменная сеть (1).</summary>
    Domain = 1,
    /// <summary>Частная сеть (2).</summary>
    Private = 2,
    /// <summary>Публичная сеть (4).</summary>
    Public = 4,
    /// <summary>Все профили (0x7FFFFFFF в COM API).</summary>
    All = Domain | Private | Public
}

/// <summary>Статус удалённого IP в брандмауэре.</summary>
public enum FirewallBlockStatus
{
    /// <summary>Не заблокирован.</summary>
    Allowed = 0,
    /// <summary>Заблокирован входящий трафик.</summary>
    BlockedInbound = 1,
    /// <summary>Заблокирован исходящий трафик.</summary>
    BlockedOutbound = 2,
    /// <summary>Заблокирован в обоих направлениях.</summary>
    BlockedBoth = 3
}

/// <summary>Результат операции с брандмауэром (Zero-Crash: исключения не уходят наружу без упаковки).</summary>
public sealed class FirewallOperationResult
{
    /// <summary>Успех операции.</summary>
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    /// <summary>Человекочитаемое сообщение (RU).</summary>
    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    /// <summary>Имя затронутого правила (если есть).</summary>
    [JsonPropertyName("ruleName")]
    public string? RuleName { get; set; }

    /// <summary>Создано ли правило заново (false — уже существовало / обновлено).</summary>
    [JsonPropertyName("created")]
    public bool Created { get; set; }

    /// <summary>FirewallOperationResult: успешный результат.</summary>
    public static FirewallOperationResult Ok(string message, string? ruleName = null, bool created = false) =>
        new() { Success = true, Message = message, RuleName = ruleName, Created = created };

    /// <summary>FirewallOperationResult: неуспешный результат.</summary>
    public static FirewallOperationResult Fail(string message, string? ruleName = null) =>
        new() { Success = false, Message = message, RuleName = ruleName };
}

/// <summary>DTO одного правила брандмауэра (для UI-таблиц и Pipe IPC).</summary>
public sealed class FirewallRuleDto
{
    /// <summary>Имя правила (уникальный ключ в INetFwRules).</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>Описание правила.</summary>
    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    /// <summary>Направление.</summary>
    [JsonPropertyName("direction")]
    public FirewallDirection Direction { get; set; }

    /// <summary>Действие.</summary>
    [JsonPropertyName("action")]
    public FirewallAction Action { get; set; }

    /// <summary>Протокол.</summary>
    [JsonPropertyName("protocol")]
    public FirewallProtocol Protocol { get; set; }

    /// <summary>Локальные порты ("80", "80,443", "5000-5010", "RPC", "*").</summary>
    [JsonPropertyName("localPorts")]
    public string LocalPorts { get; set; } = string.Empty;

    /// <summary>Удалённые адреса (CSV: "1.2.3.4,5.6.0.0/16").</summary>
    [JsonPropertyName("remoteAddresses")]
    public string RemoteAddresses { get; set; } = string.Empty;

    /// <summary>Приложение (ApplicationName / FullPath), пусто = вся система.</summary>
    [JsonPropertyName("applicationPath")]
    public string? ApplicationPath { get; set; }

    /// <summary>Включено ли правило.</summary>
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }

    /// <summary>Профили, к которым применено.</summary>
    [JsonPropertyName("profiles")]
    public FirewallProfile Profiles { get; set; } = FirewallProfile.All;

    /// <summary>Признак правила StartupMonitor (префикс имени).</summary>
    [JsonPropertyName("isManaged")]
    public bool IsManaged { get; set; }
}

/// <summary>Запрос блокировки IP.</summary>
public sealed class BlockIpRequest
{
    /// <summary>Удалённый IP (v4/v6) или подсеть ("1.2.3.4", "10.0.0.0/24").</summary>
    [JsonPropertyName("ip")]
    public string Ip { get; set; } = string.Empty;

    /// <summary>Направление блокировки.</summary>
    [JsonPropertyName("direction")]
    public FirewallDirection Direction { get; set; } = FirewallDirection.Both;

    /// <summary>Использовать агрегированное правило (рекомендуется). False = отдельное правило на IP.</summary>
    [JsonPropertyName("aggregated")]
    public bool Aggregated { get; set; } = true;

    /// <summary>Комментарий для описания правила.</summary>
    [JsonPropertyName("reason")]
    public string? Reason { get; set; }

    /// <summary>Длительность блокировки в минутах (null = перманентно / по умолчанию).</summary>
    [JsonPropertyName("banDurationMinutes")]
    public int? BanDurationMinutes { get; set; }
}

/// <summary>Запрос разблокировки IP.</summary>
public sealed class UnblockIpRequest
{
    /// <summary>Удалённый IP / подсеть.</summary>
    [JsonPropertyName("ip")]
    public string Ip { get; set; } = string.Empty;

    /// <summary>Направление (Both снимает оба).</summary>
    [JsonPropertyName("direction")]
    public FirewallDirection Direction { get; set; } = FirewallDirection.Both;
}

/// <summary>Запрос блокировки порта.</summary>
public sealed class BlockPortRequest
{
    /// <summary>Номер порта 1–65535.</summary>
    [JsonPropertyName("port")]
    public int Port { get; set; }

    /// <summary>Протокол.</summary>
    [JsonPropertyName("protocol")]
    public FirewallProtocol Protocol { get; set; } = FirewallProtocol.Tcp;

    /// <summary>Направление.</summary>
    [JsonPropertyName("direction")]
    public FirewallDirection Direction { get; set; } = FirewallDirection.Inbound;

    /// <summary>Привязка к исполняемому файлу; null/пусто = вся система.</summary>
    [JsonPropertyName("applicationPath")]
    public string? ApplicationPath { get; set; }

    /// <summary>Комментарий.</summary>
    [JsonPropertyName("reason")]
    public string? Reason { get; set; }
}

/// <summary>Запрос разблокировки порта.</summary>
public sealed class UnblockPortRequest
{
    /// <summary>Номер порта.</summary>
    [JsonPropertyName("port")]
    public int Port { get; set; }

    /// <summary>Протокол.</summary>
    [JsonPropertyName("protocol")]
    public FirewallProtocol Protocol { get; set; } = FirewallProtocol.Tcp;

    /// <summary>Направление (Both снимает In+Out).</summary>
    [JsonPropertyName("direction")]
    public FirewallDirection Direction { get; set; } = FirewallDirection.Inbound;

    /// <summary>Привязка к исполняемому файлу (должна совпадать с блокировкой).</summary>
    [JsonPropertyName("applicationPath")]
    public string? ApplicationPath { get; set; }
}

/// <summary>Сводный статус брандмауэра Windows для UI-бейджей.</summary>
public sealed class FirewallStatusDto
{
    /// <summary>Доступен ли COM API (служба MpsSvc запущена).</summary>
    [JsonPropertyName("available")]
    public bool Available { get; set; }

    /// <summary>Включён ли файрвол для текущего профиля.</summary>
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }

    /// <summary>Битовая маска текущих профилей (Domain=1, Private=2, Public=4).</summary>
    [JsonPropertyName("currentProfiles")]
    public int CurrentProfiles { get; set; }

    /// <summary>Включённость по профилям.</summary>
    [JsonPropertyName("domainEnabled")]
    public bool DomainEnabled { get; set; }
    /// <summary>Включённость для частного профиля.</summary>
    [JsonPropertyName("privateEnabled")]
    public bool PrivateEnabled { get; set; }
    /// <summary>Включённость для публичного профиля.</summary>
    [JsonPropertyName("publicEnabled")]
    public bool PublicEnabled { get; set; }

    /// <summary>Количество управляемых правил StartupMonitor.</summary>
    [JsonPropertyName("managedRulesCount")]
    public int ManagedRulesCount { get; set; }

    /// <summary>Количество IP в агрегированных правилах.</summary>
    [JsonPropertyName("blockedIpsCount")]
    public int BlockedIpsCount { get; set; }
}

/// <summary>Вспомогательные валидаторы для firewall-DTO (защита от самоблокировки).</summary>
public static class FirewallGuard
{
    /// <summary>
    /// Проверяет, допустимо ли блокировать адрес.
    /// Запрещены: loopback (127.x, ::1), unspecified (0.0.0.0, ::).
    /// Мультикаст/широковещательные — тоже отказ (бессмысленно и опасно для сети).
    /// RFC 1918 разрешены, но вызывающий код должен логировать предупреждение.
    /// </summary>
    public static bool IsBlockableAddress(string ipOrSubnet, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(ipOrSubnet))
        {
            error = "Пустой IP-адрес.";
            return false;
        }

        // Отделяем маску подсети: "10.0.0.0/24" -> "10.0.0.0"
        var addrPart = ipOrSubnet.Split('/')[0].Trim().Trim('[', ']');
        if (!IPAddress.TryParse(addrPart, out var ip))
        {
            error = $"Некорректный IP-адрес: {ipOrSubnet}.";
            return false;
        }

        if (IPAddress.IsLoopback(ip))
        {
            error = $"Блокировка loopback {addrPart} запрещена (самоблокировка).";
            return false;
        }

        var bytes = ip.GetAddressBytes();
        bool allZero = true;
        foreach (var b in bytes)
        {
            if (b != 0) { allZero = false; break; }
        }

        if (allZero)
        {
            error = $"Блокировка unspecified-адреса {addrPart} запрещена.";
            return false;
        }

        if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
        {
            // 255.255.255.255, 224.0.0.0/4 multicast
            if (bytes[0] >= 224)
            {
                error = $"Блокировка multicast/broadcast {addrPart} запрещена.";
                return false;
            }
        }
        else if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
        {
            if (ip.IsIPv6Multicast)
            {
                error = $"Блокировка multicast {addrPart} запрещена.";
                return false;
            }
        }

        return true;
    }

    /// <summary>Проверяет номер порта.</summary>
    public static bool IsValidPort(int port) => port >= 1 && port <= 65535;
}

/// <summary>Единица измерения скользящего окна времени (в секундах).</summary>
public enum TimeWindowUnit
{
    /// <summary>1 секунда.</summary>
    Second = 1,
    /// <summary>1 минута (60 секунд).</summary>
    Minute = 60,
    /// <summary>1 час (3600 секунд).</summary>
    Hour = 3600
}

/// <summary>Конфигурация правила защиты портов (Port Defender).</summary>
public sealed class PortDefenderRuleConfig
{
    /// <summary>Локальный защищаемый порт (0 = любой порт).</summary>
    [JsonPropertyName("localPort")]
    public int LocalPort { get; set; }

    /// <summary>Путь к процессу (null = любой процесс).</summary>
    [JsonPropertyName("processPath")]
    public string? ProcessPath { get; set; }

    /// <summary>Включено ли правило.</summary>
    [JsonPropertyName("isEnabled")]
    public bool IsEnabled { get; set; } = true;

    /// <summary>Порог срабатывания (N подключений).</summary>
    [JsonPropertyName("thresholdCount")]
    public int ThresholdCount { get; set; } = 5;

    /// <summary>Единица скользящего окна.</summary>
    [JsonPropertyName("windowUnit")]
    public TimeWindowUnit WindowUnit { get; set; } = TimeWindowUnit.Minute;

    /// <summary>Базовый срок бана в минутах (0 = постоянный).</summary>
    [JsonPropertyName("banDurationMinutes")]
    public int BanDurationMinutes { get; set; } = 15;

    /// <summary>Автоматическая эскалация срока при рецидиве.</summary>
    [JsonPropertyName("autoEscalate")]
    public bool AutoEscalate { get; set; } = true;

    /// <summary>Список доверенных IP/подсетей (белый список).</summary>
    [JsonPropertyName("whitelistIps")]
    public List<string> WhitelistIps { get; set; } = new();

    /// <summary>Конструктор по умолчанию.</summary>
    public PortDefenderRuleConfig() { }

    /// <summary>Параметризованный конструктор.</summary>
    public PortDefenderRuleConfig(int localPort, string? processPath, bool isEnabled, int thresholdCount, TimeWindowUnit windowUnit, int banDurationMinutes, bool autoEscalate, List<string> whitelistIps)
    {
        LocalPort = localPort;
        ProcessPath = processPath;
        IsEnabled = isEnabled;
        ThresholdCount = thresholdCount;
        WindowUnit = windowUnit;
        BanDurationMinutes = banDurationMinutes;
        AutoEscalate = autoEscalate;
        WhitelistIps = whitelistIps;
    }
}

/// <summary>Конфигурация службы защиты портов (Port Defender).</summary>
public sealed class PortDefenderConfigDto
{
    /// <summary>Включена ли служба Port Defender.</summary>
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    /// <summary>Список отслеживаемых портов по умолчанию.</summary>
    [JsonPropertyName("protectedPorts")]
    public List<int> ProtectedPorts { get; set; } = new() { 3389 };

    /// <summary>Порог срабатывания: максимальное количество подключений.</summary>
    [JsonPropertyName("maxAttempts")]
    public int MaxAttempts { get; set; } = 10;

    /// <summary>Алиас порога подключений.</summary>
    [JsonPropertyName("thresholdCount")]
    public int ThresholdCount
    {
        get => MaxAttempts;
        set => MaxAttempts = value;
    }

    /// <summary>Единица временного окна (секунда / минута / час).</summary>
    [JsonPropertyName("windowUnit")]
    public TimeWindowUnit WindowUnit { get; set; } = TimeWindowUnit.Minute;

    /// <summary>Размер окна в секундах.</summary>
    [JsonPropertyName("windowSeconds")]
    public int WindowSeconds { get; set; } = 60;

    /// <summary>Базовая длительность бана в минутах (15, 60, 1440, 0 = навсегда).</summary>
    [JsonPropertyName("banDurationMinutes")]
    public int BanDurationMinutes { get; set; } = 15;

    /// <summary>Адаптивная эскалация (удвоение времени бана при повторных атаках).</summary>
    [JsonPropertyName("autoEscalate")]
    public bool AutoEscalate { get; set; } = true;

    /// <summary>Доверять частным сетям RFC 1918 (не банить локальные адреса).</summary>
    [JsonPropertyName("trustPrivateNetworks")]
    public bool TrustPrivateNetworks { get; set; } = true;

    /// <summary>Белый список доверенных IP-адресов / подсетей.</summary>
    [JsonPropertyName("whitelistIps")]
    public List<string> WhitelistIps { get; set; } = new();

    /// <summary>Алиас глобального включения службы защиты.</summary>
    [JsonPropertyName("isGloballyEnabled")]
    public bool IsGloballyEnabled
    {
        get => Enabled;
        set => Enabled = value;
    }

    /// <summary>Алиас базовой длительности бана по умолчанию.</summary>
    [JsonPropertyName("defaultBanDurationMinutes")]
    public int DefaultBanDurationMinutes
    {
        get => BanDurationMinutes;
        set => BanDurationMinutes = value;
    }

    /// <summary>Индивидуальные правила по портам / процессам.</summary>
    [JsonPropertyName("rules")]
    public List<PortDefenderRuleConfig> Rules { get; set; } = new();
}

/// <summary>
/// Снимок сокета TCP для анализа скорости подключений в Port Defender.
/// </summary>
public class TcpSocketInfo : ProcessNetworkSocket
{
    /// <summary>Полный путь к исполняемому файлу процесса (если доступен).</summary>
    [JsonPropertyName("processPath")]
    public string? ProcessPath { get; set; }

    /// <summary>Создаёт пустой экземпляр TcpSocketInfo.</summary>
    public TcpSocketInfo() { }

    /// <summary>Создаёт экземпляр TcpSocketInfo из существующего сокета ProcessNetworkSocket.</summary>
    public TcpSocketInfo(ProcessNetworkSocket socket, string? processPath = null)
    {
        ProcessId = socket.ProcessId;
        ProcessName = socket.ProcessName;
        Protocol = socket.Protocol;
        LocalAddress = socket.LocalAddress;
        LocalPort = socket.LocalPort;
        RemoteAddress = socket.RemoteAddress;
        RemotePort = socket.RemotePort;
        State = socket.State;
        IsRemotePrivate = socket.IsRemotePrivate;
        ProcessPath = processPath;
    }
}

/// <summary>DTO активного бана (Port Defender).</summary>
public sealed class ActiveBanDto
{
    /// <summary>Заблокированный IP-адрес.</summary>
    [JsonPropertyName("ip")]
    public string Ip { get; set; } = string.Empty;

    /// <summary>Атакованный локальный порт.</summary>
    [JsonPropertyName("targetPort")]
    public int TargetPort { get; set; }

    /// <summary>Алиас порта для совместимости.</summary>
    [JsonPropertyName("port")]
    public int Port
    {
        get => TargetPort;
        set => TargetPort = value;
    }

    /// <summary>Имя или путь атакованного процесса.</summary>
    [JsonPropertyName("targetProcess")]
    public string TargetProcess { get; set; } = string.Empty;

    /// <summary>Протокол (TCP/UDP).</summary>
    [JsonPropertyName("protocol")]
    public FirewallProtocol Protocol { get; set; } = FirewallProtocol.Tcp;

    /// <summary>Время начала бана (UTC).</summary>
    [JsonPropertyName("banStartedUtc")]
    public DateTime BanStartedUtc { get; set; } = DateTime.UtcNow;

    /// <summary>Алиас времени бана для совместимости.</summary>
    [JsonPropertyName("bannedAtUtc")]
    public DateTime BannedAtUtc
    {
        get => BanStartedUtc;
        set => BanStartedUtc = value;
    }

    /// <summary>Время истечения бана (UTC, null = навсегда).</summary>
    [JsonPropertyName("expiresAtUtc")]
    public DateTime? ExpiresAtUtc { get; set; }

    /// <summary>Зафиксированная скорость атаки (подключений в окне).</summary>
    [JsonPropertyName("attackRate")]
    public int AttackRate { get; set; }

    /// <summary>Алиас количества попыток для совместимости.</summary>
    [JsonPropertyName("attemptCount")]
    public int AttemptCount
    {
        get => AttackRate;
        set => AttackRate = value;
    }

    /// <summary>Причина бана.</summary>
    [JsonPropertyName("reason")]
    public string Reason { get; set; } = string.Empty;

    /// <summary>Количество повторных нарушений (1-based).</summary>
    [JsonPropertyName("repeatCount")]
    public int RepeatCount { get; set; }

    /// <summary>Имя затронутого правила Windows Firewall.</summary>
    [JsonPropertyName("ruleName")]
    public string? RuleName { get; set; }

    /// <summary>Постоянный ли бан.</summary>
    [JsonPropertyName("isPermanent")]
    public bool IsPermanent => ExpiresAtUtc == null;

    /// <summary>Конструктор по умолчанию.</summary>
    public ActiveBanDto() { }

    /// <summary>Параметризованный конструктор.</summary>
    public ActiveBanDto(string ip, int targetPort, string targetProcess, DateTime banStartedUtc, DateTime? expiresAtUtc, int attackRate, string reason, int repeatCount)
    {
        Ip = ip;
        TargetPort = targetPort;
        TargetProcess = targetProcess;
        BanStartedUtc = banStartedUtc;
        ExpiresAtUtc = expiresAtUtc;
        AttackRate = attackRate;
        Reason = reason;
        RepeatCount = repeatCount;
    }
}
