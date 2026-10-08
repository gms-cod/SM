using System.Security.Principal;
using System.Text;
using Microsoft.Win32;
using StartupMonitor.Shared.Models;

namespace StartupMonitor.Service.Services;

/// <summary>Вспомогательные методы для watcher'ов: добавление/обновление записей,
/// парсинг путей из командной строки, вычисление стабильного ID и т.п.</summary>
public static class WatcherHelper
{
    /// <summary>Возвращает список (SID, имя) загруженных пользовательских кустов HKEY_USERS
    /// (без SYSTEM/служб и *_Classes). Нужен, т.к. служба работает как LocalSystem
    /// и её HKCU — это профиль SYSTEM, а не реальных пользователей.
    /// Профиль текущей сессии исключается — он уже сканируется через HKCU
    /// (иначе каждая запись автозапуска попадает в список дважды: HKCU\... и HKCU\Имя\...).</summary>
    public static List<(string Sid, string Name)> GetUserProfiles()
    {
        // SID текущей сессии: при запуске службы под обычным пользователем его профиль
        // присутствует и в HKCU, и в HKEY_USERS — сканировать его надо только один раз.
        string? currentSid = null;
        try { currentSid = System.Security.Principal.WindowsIdentity.GetCurrent().User?.Value; }
        catch { }

        var result = new List<(string Sid, string Name)>();
        try
        {
            using var users = Registry.Users;
            foreach (var sid in users.GetSubKeyNames())
            {
                if (!sid.StartsWith("S-1-5-21", StringComparison.OrdinalIgnoreCase) ||
                    sid.Contains("_Classes", StringComparison.OrdinalIgnoreCase)) continue;

                // Пропускаем профиль текущей сессии — его сканирует HKCU-ветка
                if (currentSid != null && sid.Equals(currentSid, StringComparison.OrdinalIgnoreCase)) continue;

                var name = sid;
                try
                {
                    name = new SecurityIdentifier(sid).Translate(typeof(NTAccount)).Value;
                    name = name.Split('\\')[^1];
                }
                catch { }
                result.Add((sid, name));
            }
        }
        catch { }
        return result;
    }


    /// <summary>Добавляет запись в хранилище; если запись с таким Id уже есть —
    /// обновляет её при изменении командной строки (детект EntryModified).
    /// Если существующая запись заблокирована, а программа прописалась заново —
    /// правило ЧС применяется повторно (авто-удаление).
    /// Возвращает true, если запись новая (нужно уведомление EntryAdded).</summary>
    public static bool AddOrUpdate(
        JsonDataStore store,
        NotificationService notifications,
        StartupEntry entry,
        BlacklistRule? matchedRule = null,
        BlacklistManager? blacklist = null)
    {
        // Если мониторинг приостановлен, не обрабатываем фоновые обновления
        if (store.Load().Settings.IsMonitoringPaused) return false;

        // 0. Защита программы: собственные компоненты StartupMonitor всегда разрешены и никогда не блокируются
        if (EntryOperations.IsSelfEntry(entry))
        {
            if (store.AddEntry(entry))
            {
                Pipe.PipeServer.NotifyExternalEntriesChanged();
                return true;
            }
            return false;
        }

        // Запись ранее была удалена: проверяем причину удаления
        var existingRemoved = store.GetRemovedEntryByOriginalId(entry.Id);
        if (existingRemoved != null)
        {
            // Если запись была удалена режимом Lockdown (StrictMode), но сейчас строгий режим выключен:
            // НЕ блокируем её повторно, а удаляем из бэкапа и разрешаем штатное добавление
            if (string.Equals(existingRemoved.RemovalReason, "LockdownBlocked", StringComparison.OrdinalIgnoreCase)
                && !store.Load().Settings.StrictMode)
            {
                store.RemoveRemovedEntryByOriginalId(entry.Id);
            }
            else
            {
                // Запись удалена пользователем осознанно (UserRemoved) либо строгий режим всё ещё активен:
                // повторно стираем запись из системы (remove) с балуном.
                // ВАЖНО: Если программы нет в Чёрном списке, её НЕЛЬЗЯ закрывать (KillProcess запрещён)!
                // Мы только удаляем её из автозагрузки в системе, чтобы она не стартовала при загрузке Windows,
                // но не мешаем работе запущенного приложения. Завершение процесса допустимо ТОЛЬКО при наличии
                // активного правила в Чёрном списке или в строгом режиме Lockdown.
                if (matchedRule != null && matchedRule.Action == BlacklistAction.NotifyAndRemove)
                {
                    EntryOperations.KillProcess(entry);
                }
                else if (string.Equals(existingRemoved.RemovalReason, "LockdownBlocked", StringComparison.OrdinalIgnoreCase)
                    && store.Load().Settings.StrictMode)
                {
                    EntryOperations.KillProcess(entry);
                }

                var (ok, _, payload) = EntryOperations.RemoveFromSystem(entry);
                if (ok)
                {
                    existingRemoved.RestorePayload = payload ?? "";
                    existingRemoved.FilePath = entry.FilePath;
                    store.AddRemovedEntry(existingRemoved);
                    notifications.Enqueue(new PipeMessage
                    {
                        Type = PipeMessageTypes.ActionNotice,
                        Payload = System.Text.Json.JsonSerializer.Serialize(
                            new ActionNoticePayload(entry.Name ?? "", "removedAgain"))
                    });
                }
                store.RemoveEntry(entry.Id);
                Pipe.PipeServer.NotifyExternalEntriesChanged();
                return false;
            }
        }

        // Если включён строгий режим (блокировка любых новых записей автозапуска):
        // новая запись не добавляется, а сразу стирается из системы с завершением процесса и балуном.
        // Исключение: собственные компоненты программы и системные службы/задачи ОС Windows.
        if (store.FindEntry(e => e.Id == entry.Id) == null && store.Load().Settings.StrictMode)
        {
            if (EntryOperations.IsProtectedSystemEntry(entry))
            {
                // Системные компоненты ОС Windows не трогаем — добавляем в хранилище
                if (store.AddEntry(entry))
                {
                    Pipe.PipeServer.NotifyExternalEntriesChanged();
                    return true;
                }
                return false;
            }

            EntryOperations.KillProcess(entry);
            var (ok, _, payload) = EntryOperations.RemoveFromSystem(entry);
            var removed = new RemovedEntry
            {
                Id = Guid.NewGuid().ToString("N")[..8],
                OriginalEntryId = entry.Id,
                Name = entry.Name,
                Location = entry.Location,
                LocationDetail = entry.LocationDetail,
                FilePath = entry.FilePath,
                RemovedAt = DateTime.UtcNow,
                DetectedAt = entry.DetectedAt,
                RestorePayload = payload ?? "",
                RemovalReason = "LockdownBlocked"
            };
            store.AddRemovedEntry(removed);
            store.AddHistory(new HistoryEntry
            {
                ChangeType = "BlockedByLockdown",
                EntryName = entry.Name,
                EntryPath = entry.FilePath,
                Location = entry.Location.ToString()
            });

            notifications.Enqueue(new PipeMessage
            {
                Type = PipeMessageTypes.ActionNotice,
                Payload = System.Text.Json.JsonSerializer.Serialize(
                    new ActionNoticePayload(entry.Name ?? "", "lockdownBlocked"))
            });

            return false;
        }

        // Проверка автоматических политик защиты Auto-Guards для новых записей
        if (store.FindEntry(e => e.Id == entry.Id) == null)
        {
            var policyMatch = Guards.AutoGuardEngine.Evaluate(entry, store.Load().Settings);
            if (policyMatch != null)
            {
                var isEnforce = string.Equals(store.Load().Settings.AutoGuardMode, "EnforceBlock", StringComparison.OrdinalIgnoreCase);
                if (isEnforce)
                {
                    // Режим активной блокировки: завершаем процесс, нейтрализуем точку старта и изолируем в бэкап
                    EntryOperations.KillProcess(entry);
                    var (ok, _, payload) = EntryOperations.RemoveFromSystem(entry);
                    var removed = new RemovedEntry
                    {
                        Id = Guid.NewGuid().ToString("N")[..8],
                        OriginalEntryId = entry.Id,
                        Name = entry.Name,
                        Location = entry.Location,
                        LocationDetail = entry.LocationDetail,
                        FilePath = entry.FilePath,
                        RemovedAt = DateTime.UtcNow,
                        DetectedAt = entry.DetectedAt,
                        RestorePayload = payload ?? "",
                        RemovalReason = $"AutoGuardBlocked:{policyMatch.PolicyKey}"
                    };
                    store.AddRemovedEntry(removed);
                    store.AddHistory(new HistoryEntry
                    {
                        ChangeType = "BlockedByPolicy",
                        EntryName = entry.Name,
                        EntryPath = entry.FilePath,
                        Location = entry.Location.ToString(),
                        Action = policyMatch.RuleTitle
                    });

                    notifications.Enqueue(new PipeMessage
                    {
                        Type = PipeMessageTypes.ActionNotice,
                        Payload = System.Text.Json.JsonSerializer.Serialize(
                            new ActionNoticePayload(entry.Name ?? "", $"policyBlocked:{policyMatch.PolicyKey}"))
                    });

                    return false;
                }
                else
                {
                    // Режим аудита (AuditOnly): регистрируем предупреждение в журнале истории
                    store.AddHistory(new HistoryEntry
                    {
                        ChangeType = "PolicyWarning",
                        EntryName = entry.Name,
                        EntryPath = entry.FilePath,
                        Location = entry.Location.ToString(),
                        Action = $"{policyMatch.RuleTitle}: {policyMatch.Description}"
                    });
                }
            }
        }

        // Если запись новая (добавлена) — возвращаем true
        if (store.AddEntry(entry))
        {
            Pipe.PipeServer.NotifyExternalEntriesChanged();
            store.AddHistory(new HistoryEntry
            {
                ChangeType = "Added",
                EntryName = entry.Name,
                EntryPath = entry.FilePath,
                Location = entry.Location.ToString(),
                Action = "Detected"
            });
            return true;
        }

        // Если запись уже есть — проверяем, изменилась ли CommandLine
        var existing = store.FindEntry(e => e.Id == entry.Id);
        if (existing != null && existing.CommandLine != entry.CommandLine)
        {
            existing.CommandLine = entry.CommandLine;
            existing.FilePath = entry.FilePath;
            existing.ThreatInfo = entry.ThreatInfo ?? StartupMonitor.Shared.Helpers.CliHeuristicAnalyzer.Analyze(existing.CommandLine, existing.FilePath);
            store.UpdateEntry(existing);
            Pipe.PipeServer.NotifyExternalEntriesChanged();
            store.AddHistory(new HistoryEntry
            {
                ChangeType = "Modified",
                EntryName = existing.Name,
                EntryPath = existing.FilePath,
                Location = existing.Location.ToString(),
                Action = "ParametersChanged"
            });
            notifications.Enqueue(new PipeMessage
            {
                Type = PipeMessageTypes.EntryModified,
                Payload = System.Text.Json.JsonSerializer.Serialize(existing)
            });
        }

        // Заблокированная запись снова появилась в системе — применяем правило повторно
        if (existing?.FromBlacklist == true
            && matchedRule?.Action == BlacklistAction.NotifyAndRemove
            && blacklist != null)
        {
            blacklist.ApplyBlacklistAction(existing, matchedRule, store, notifications);
        }
        // Отключённая пользователем запись снова появилась в системе — отключаем повторно
        // (программа восстановила автозапуск сама, например uTorrent при обновлении или браузер при старте).
        // ВАЖНО: в фоновом режиме ни в коем случае не завершаем процесс работающего приложения (пользователь
        // может прямо сейчас работать в нем, например в браузере или редакторе), а только гарантируем
        // снятие записи с автозапуска в системе. Завершение процесса допустимо только при явном добавлении в ЧС
        // или при ручном отключении пользователем через UI.
        else if (existing?.Status == EntryStatus.Disabled)
        {
            EntryOperations.DisableFromSystem(existing);
            notifications.Enqueue(new PipeMessage
            {
                Type = PipeMessageTypes.ActionNotice,
                Payload = System.Text.Json.JsonSerializer.Serialize(
                    new ActionNoticePayload(existing.Name ?? "", "autoDisabled"))
            });
            notifications.Enqueue(new PipeMessage
            {
                Type = PipeMessageTypes.EntryModified,
                Payload = System.Text.Json.JsonSerializer.Serialize(existing)
            });
        }

        return false;
    }

    /// <summary>Извлекает путь к исполняемому файлу из командной строки.
    /// Поддерживает кавычки, абсолютные пути с диском, %variables%,
    /// а также сложные случаи типа "rundll32.exe C:\path\to.dll,EntryPoint".</summary>
    public static string ParsePath(string commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine)) return string.Empty;
        var trimmed = commandLine.Trim();

        // В кавычках — извлекаем содержимое
        if (trimmed.StartsWith('"'))
        {
            var end = trimmed.IndexOf('"', 1);
            return end > 1 ? trimmed[1..end] : trimmed;
        }

        var spaceIdx = trimmed.IndexOf(' ');
        if (spaceIdx <= 0) return trimmed;

        var tokens = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        // Pass 1: ищем первый абсолютный путь (с буквой диска или %env%) с исполняемым расширением
        // Это нужно для "rundll32.exe C:\Windows\System32\my.dll,EntryPoint" → C:\Windows\...\my.dll
        foreach (var token in tokens)
        {
            var clean = token;
            var commaIdx = token.IndexOf(',');
            if (commaIdx > 0) clean = token[..commaIdx];

            if (!clean.Contains(':') && !clean.Contains('%')) continue;

            var ext = Path.GetExtension(clean.AsSpan()).ToString();
            if (IsExecutableExtension(ext))
                return clean;
        }

        // Pass 2: реконструкция путей вида "C:\Program Files\..."
        // Склеиваем токены с начала до первого флага аргумента (- или /)
        var result = new StringBuilder();
        for (var i = 0; i < tokens.Length; i++)
        {
            if (i > 0 && (tokens[i][0] == '-' || tokens[i][0] == '/'))
                break;

            if (result.Length > 0) result.Append(' ');
            result.Append(tokens[i]);

            // Проверяем, заканчивается ли накопленный путь на исполняемое расширение
            var curExt = Path.GetExtension(result.ToString().AsSpan()).ToString();
            if (!IsExecutableExtension(curExt)) continue;

            // Токен — plausible executable. Если следующий не похож на путь — стоп
            if (i + 1 < tokens.Length)
            {
                var next = tokens[i + 1];
                if (next[0] == '-' || next[0] == '/')
                    break;
                // Если следующий токен не содержит разделителей пути — это аргумент
                if (!next.Contains('\\') && !next.Contains('/') && !next.Contains(':') && !next.Contains('%'))
                    break;
            }
        }

        return result.ToString();
    }

    /// <summary>Определяет, похоже ли значение реестра на команду автозапуска.
    /// Командой считается: первый токен — абсолютный путь (диск/UNC/%env%) либо
    /// исполняемый файл (explorer.exe). Значения-метаданные (CLSID, версии, "yes",
    /// числа, списки через запятую, имена DLL без пути — кодеки, мониторы, LSA)
    /// командой не являются.</summary>
    public static bool LooksLikeCommand(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        // '>' — префикс деактивации (Active Setup): такая команда не выполняется
        if (value.TrimStart().StartsWith('>')) return false;
        // Отбрасываем ведущие декораторы иконок/ссылок: "@C:\...dll,-128", "&Menu"
        var trimmed = value.Trim().TrimStart('@', '&', '~', '+');
        if (trimmed.Length == 0) return false;

        // Первый токен: в кавычках — до закрывающей кавычки, иначе до пробела
        string first;
        if (trimmed.StartsWith('"'))
        {
            var end = trimmed.IndexOf('"', 1);
            first = end > 1 ? trimmed[1..end] : trimmed;
        }
        else
        {
            var space = trimmed.IndexOf(' ');
            first = space > 0 ? trimmed[..space] : trimmed;
        }

        // Абсолютный путь (буква диска, UNC, %переменная%) — команда,
        // но только если ведёт на исполняемый файл (или файл без расширения);
        // пути к библиотекам (.dll/.acm/.drv) не запускаются как программы
        var commaIdx = first.IndexOf(',');
        var token = commaIdx > 0 ? first[..commaIdx] : first;
        var ext = Path.GetExtension(token);
        if (first.Contains(':') || first.StartsWith('\\') || first.Contains('%'))
            return ext.Length == 0 || IsExecutableExtension(ext);

        // Исполняемый файл без пути (shell=explorer.exe) — команда;
        // файлы-библиотеки (.dll/.drv/.acm) без пути не запускаются — мусор
        return IsExecutableExtension(ext)
            && !token.EndsWith(".dll", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Возвращает StubPath компонента Active Setup, если компонент активен и его
    /// StubPath похож на команду. Деактивированные компоненты (имя ключа или StubPath с
    /// префиксом '>') и ключи без StubPath не выполняются при входе — возвращает null.</summary>
    public static string? GetActiveSetupStubPath(RegistryKey componentKey)
    {
        var stub = componentKey.GetValue("StubPath") as string;
        if (string.IsNullOrWhiteSpace(stub) || stub.StartsWith('>')) return null;
        if (!LooksLikeCommand(stub)) return null;
        return stub;
    }

    /// <summary>Вычисляет стабильный (детерминированный) ID записи на основе Location, LocationDetail и Name.
    /// Использует SHA256 первых 12 символов hex.</summary>
    public static string ComputeStableId(StartupEntry entry)
    {
        var key = $"{(int)entry.Location}|{entry.LocationDetail ?? ""}|{entry.Name ?? ""}";
        using var sha = System.Security.Cryptography.SHA256.Create();
        var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(key));
        return Convert.ToHexString(hash).ToLowerInvariant()[..12];
    }

    /// <summary>Проверяет, является ли расширение файла исполняемым (.exe, .dll, .com, .scr, .bat, .cmd).</summary>
    public static bool IsExecutableExtension(string ext)
    {
        return ext.Equals(".exe", StringComparison.OrdinalIgnoreCase) ||
               ext.Equals(".dll", StringComparison.OrdinalIgnoreCase) ||
               ext.Equals(".com", StringComparison.OrdinalIgnoreCase) ||
               ext.Equals(".scr", StringComparison.OrdinalIgnoreCase) ||
               ext.Equals(".bat", StringComparison.OrdinalIgnoreCase) ||
               ext.Equals(".cmd", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Парсит путь к исполняемому файлу из строки команды службы (PathName).
    /// Учитывает кавычки.</summary>
    public static string ParseServicePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;
        var trimmed = path.Trim();
        if (trimmed.StartsWith('"'))
        {
            var end = trimmed.IndexOf('"', 1);
            return end > 1 ? trimmed[1..end] : trimmed;
        }
        var spaceIdx = trimmed.IndexOf(' ');
        return spaceIdx > 0 ? trimmed[..spaceIdx] : trimmed;
    }

    /// <summary>Определяет тип расположения (EntryLocation) по пути ключа реестра.</summary>
    public static EntryLocation GetLocationFromKeyPath(string keyPath)
    {
        if (keyPath.Contains("RunOnce", StringComparison.OrdinalIgnoreCase))
            return EntryLocation.RegistryRunOnce;
        if (keyPath.Contains("Policies", StringComparison.OrdinalIgnoreCase))
            return EntryLocation.RegistryPolicies;
        if (keyPath.Contains("RunServices", StringComparison.OrdinalIgnoreCase))
            return EntryLocation.RegistryRunServices;
        return EntryLocation.RegistryRun;
    }
}
