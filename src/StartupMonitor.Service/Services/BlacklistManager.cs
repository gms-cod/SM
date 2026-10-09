using System.Security.Cryptography;
using System.Text.RegularExpressions;
using StartupMonitor.Shared.Models;
using StartupMonitor.Shared.Helpers;

namespace StartupMonitor.Service.Services;

/// <summary>Менеджер правил чёрного списка (ЧС): сопоставление записей с правилами и применение действий.</summary>
public class BlacklistManager
{
    /// <summary>Ссылка на хранилище конфигурации (содержит список правил).</summary>
    private readonly JsonDataStore _store;

    /// <summary>BlacklistManager: Выполняет операцию BlacklistManager.</summary>
    /// <param name="store">Входные данные: значение параметра store (JsonDataStore).</param>
    public BlacklistManager(JsonDataStore store)
    {
        _store = store;
    }

    /// <summary>Ищет первое подходящее правило ЧС для указанной записи.</summary>
    public BlacklistRule? MatchEntry(StartupEntry entry)
    {
        var config = _store.Load();

        foreach (var rule in config.Blacklist)
        {
            if (IsMatchStatic(rule, entry))
                return rule;
        }

        return null;
    }

    /// <summary>Применяет действие правила ЧС к записи: блокировка + удаление или только уведомление.</summary>
    public void ApplyBlacklistAction(StartupEntry entry, BlacklistRule? rule, JsonDataStore store, NotificationService notifications)
    {
        if (rule == null) return;
        // Защита ядра Windows: системные службы и компоненты ОС запрещено удалять или глушить через ЧС
        if (SystemProtectionHelper.IsProtectedSystemEntry(entry)) return;

        entry.Status = EntryStatus.Blocked;
        entry.FromBlacklist = true;

        // NotifyAndRemove: убить процесс + удалить из системы + сохранить бэкап для восстановления
        if (rule.Action == BlacklistAction.NotifyAndRemove)
        {
            EntryOperations.KillProcess(entry);
            var (success, _, payload) = EntryOperations.RemoveFromSystem(entry);
            if (success)
            {
                // Сохраняем в RemovedEntry для возможности восстановления
                store.AddRemovedEntry(new RemovedEntry
                {
                    OriginalEntryId = entry.Id,
                    Name = entry.Name,
                    FilePath = entry.FilePath,
                    Publisher = entry.Publisher,
                    Location = entry.Location,
                    LocationDetail = entry.LocationDetail,
                    DetectedAt = entry.DetectedAt,
                    RestorePayload = payload ?? "",
                });
                // Запись ОСТАЁТСЯ в списке как Blocked: из системы удалена,
                // но пользователь видит блокировку и может разблокировать (= восстановить)
                store.UpdateEntry(entry);
                // Уведомляем UI об авто-блокировке (покажет предупреждение)
                notifications.Enqueue(new PipeMessage
                {
                    Type = PipeMessageTypes.BlacklistMatch,
                    Payload = System.Text.Json.JsonSerializer.Serialize(entry)
                });
            }
        }
        else
        {
            // NotifyOnly: только блокировка и уведомление, без удаления из системы
            notifications.Enqueue(new PipeMessage
            {
                Type = PipeMessageTypes.BlacklistMatch,
                Payload = System.Text.Json.JsonSerializer.Serialize(entry)
            });
        }
        // Статус/флаг записи изменён (Blocked/FromBlacklist) — двигаем ревизию Entries,
        // чтобы дельта-опрос UI (GetEntriesDelta) забрал актуальный снимок.
        Pipe.PipeServer.NotifyExternalEntriesChanged();
    }

    /// <summary>Проверяет, подходит ли запись под правило ЧС по типу совпадения (путь/маска/издатель/SHA256).</summary>
    public static bool IsMatchStatic(BlacklistRule rule, StartupEntry entry)
    {
        if (rule == null || entry == null) return false;
        if (string.IsNullOrWhiteSpace(rule.Value)) return false;

        // Защита системных компонентов ОС от ложного сопоставления с правилами ЧС
        if (SystemProtectionHelper.IsProtectedSystemEntry(entry)) return false;

        // Для служб Windows: если правило совпадает с именем службы (LocationDetail)
        if (entry.Location is EntryLocation.Service or EntryLocation.Driver)
        {
            if (!string.IsNullOrWhiteSpace(entry.LocationDetail) &&
                string.Equals(entry.LocationDetail.Trim(), rule.Value?.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // Службы на базе хост-процессов (svchost.exe и др.) никогда не сопоставляются по чистому пути к хост-файлу
            if (SystemProtectionHelper.IsHostProcess(entry.FilePath))
            {
                return false;
            }
        }

        // Если файл записи — хост-процесс (svchost, rundll32, cmd, powershell), а правило указывает на хост без аргументов: не сопоставлять!
        if (SystemProtectionHelper.IsHostProcess(entry.FilePath) &&
            !string.IsNullOrWhiteSpace(rule.Value) &&
            SystemProtectionHelper.IsHostProcess(rule.Value))
        {
            return false;
        }

        return rule.MatchType switch
        {
            BlacklistMatchType.Path => MatchesFilePath(rule, entry.FilePath),
            BlacklistMatchType.PathWildcard => MatchesFilePath(rule, entry.FilePath),
            BlacklistMatchType.Publisher =>
                !string.IsNullOrWhiteSpace(entry.Publisher) && string.Equals(entry.Publisher, rule.Value, StringComparison.OrdinalIgnoreCase),

            BlacklistMatchType.SHA256 =>
                !string.IsNullOrWhiteSpace(entry.SHA256) && string.Equals(entry.SHA256, rule.Value, StringComparison.OrdinalIgnoreCase),

            _ => false
        };
    }

    /// <summary>Проверяет, подходит ли путь файла под правило ЧС (точный путь/маска).
    /// Отдельно от IsMatchStatic — для сопоставления процессов с ЧС вне списка записей.</summary>
    public static bool MatchesFilePath(BlacklistRule rule, string filePath)
    {
        if (string.IsNullOrEmpty(filePath) || string.IsNullOrEmpty(rule.Value)) return false;
        return rule.MatchType switch
        {
            BlacklistMatchType.Path =>
                string.Equals(filePath, rule.Value, StringComparison.OrdinalIgnoreCase),

            BlacklistMatchType.PathWildcard =>
                TryWildcardMatch(filePath, rule.Value),

            _ => false
        };
    }

    /// <summary>Безопасное сопоставление с wildcard-шаблоном с защитой от ReDoS и некорректных масок.</summary>
    public static bool TryWildcardMatch(string input, string pattern)
    {
        try
        {
            return Regex.IsMatch(input, WildcardToRegex(pattern), RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(250));
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Преобразует wildcard-шаблон (*, ?) в регулярное выражение.</summary>
    private static string WildcardToRegex(string pattern)
    {
        var escaped = Regex.Escape(pattern)
            .Replace("\\*", ".*")
            .Replace("\\?", ".");
        return $"^{escaped}$";
    }

    /// <summary>Вычисляет SHA256 хеш файла (для правил типа SHA256).</summary>
    public static string ComputeSha256(string filePath)
    {
        if (!File.Exists(filePath)) return string.Empty;

        try
        {
            using var stream = File.OpenRead(filePath);
            var hash = SHA256.HashData(stream);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }
        catch
        {
            return string.Empty;
        }
    }
}
