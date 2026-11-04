// Модели экспорта/импорта правил брандмауэра и готовых пресетов безопасности.
// Используются службой (SYSTEM), Pipe IPC и UI. Чистые POCO — без зависимости от COM.
using System.Text.Json.Serialization;

namespace StartupMonitor.Shared.Models.Firewall;

/// <summary>Готовый пресет безопасности брандмауэра.</summary>
public enum SecurityPresetType
{
    /// <summary>Максимальная приватность: блокировка телеметрии MS, Cortana, диагностических серверов.</summary>
    MaxPrivacy = 0,
    /// <summary>Игровой режим: открытие портов Steam, Battle.net, Epic Games.</summary>
    Gaming = 1,
    /// <summary>Публичный Wi-Fi: блокировка входящих NetBIOS, SMB, RDP, RPC.</summary>
    PublicWifi = 2,
    /// <summary>Сброс к стандартным параметрам (удаление управляемых правил, режим Normal).</summary>
    ResetToDefaults = 3
}

/// <summary>DTO бэкапа правил брандмауэра (экспорт/импорт через Pipe IPC и файлы).</summary>
public sealed class FirewallBackupDto
{
    /// <summary>Версия формата бэкапа.</summary>
    [JsonPropertyName("version")]
    public string Version { get; set; } = "1.6.0";

    /// <summary>Время экспорта (UTC).</summary>
    [JsonPropertyName("exportedAt")]
    public DateTime ExportedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Человекочитаемое описание бэкапа.</summary>
    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    /// <summary>Правила брандмауэра.</summary>
    [JsonPropertyName("rules")]
    public List<FirewallRuleDto> Rules { get; set; } = new();
}
