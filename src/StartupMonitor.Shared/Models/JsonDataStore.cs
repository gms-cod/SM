using System.Text.Json;
using StartupMonitor.Shared.Diagnostics;
using StartupMonitor.Shared.Models.Snapshot;

namespace StartupMonitor.Shared.Models;

/// <summary>
/// Хранилище конфигурации, разделённое по файлам:
/// settings.json, blacklist.json, startup-entries.json, removed-entries.json, history.jsonl, snapshots.json.
/// Запись атомарная (tmp + move), секция пишется только если содержимое изменилось.
/// Старый единый startup-monitor.json мигрирует при первом запуске.
/// </summary>
public class JsonDataStore
{
    /// <summary>Путь к старому единому файлу конфигурации (для миграции).</summary>
    private readonly string _legacyPath;
    /// <summary>Директория, в которой лежат файлы конфигурации.</summary>
    private readonly string _dir;
    /// <summary>Настройки сериализации JSON (snake_case, отступы).</summary>
    private readonly JsonSerializerOptions _options;
    /// <summary>Настройки сериализации для history.jsonl (строго в одну строку).</summary>
    private readonly JsonSerializerOptions _historyOptions;
    /// <summary>Кэш загруженного конфига (чтобы не перечитывать секции каждый раз).</summary>
    /// <summary>Кэш загруженного конфига (чтобы не перечитывать секции каждый раз).</summary>
    private AppConfig? _cache;
    /// <summary>Монотонно возрастающая ревизия данных для оптимизации передачи по IPC (P1-04).</summary>
    private long _revision;
    /// <summary>Текущая ревизия данных хранилища.</summary>
    public long Revision => Interlocked.Read(ref _revision);
    /// <summary>Принудительное увеличение номера ревизии.</summary>
    public void IncrementRevision() => Interlocked.Increment(ref _revision);
    /// <summary>Базовая директория файлов конфигурации и данных.</summary>
    public string BaseDirectory => _dir;

    /// <summary>Объект блокировки для потокобезопасного доступа.</summary>
    private readonly object _lock = new();
    /// <summary>Хеши уже записанного содержимого — чтобы не писать неизменённые секции</summary>
    private readonly Dictionary<string, string> _writtenHashes = new();

    /// <summary>Путь к файлу startup-entries.json.</summary>
    private string EntriesPath => Path.Combine(_dir, "startup-entries.json");
    /// <summary>Путь к файлу blacklist.json.</summary>
    private string BlacklistPath => Path.Combine(_dir, "blacklist.json");
    /// <summary>Путь к файлу removed-entries.json.</summary>
    private string RemovedPath => Path.Combine(_dir, "removed-entries.json");
    /// <summary>Путь к файлу settings.json.</summary>
    private string SettingsPath => Path.Combine(_dir, "settings.json");
    /// <summary>Путь к файлу history.jsonl (append-only).</summary>
    private string HistoryPath => Path.Combine(_dir, "history.jsonl");
    /// <summary>Путь к файлу snapshots.json.</summary>
    private string SnapshotsPath => Path.Combine(_dir, "snapshots.json");

    /// <summary>Секция настроек: language/theme — поля верхнего уровня старого AppConfig</summary>
    private class SettingsSection
    {
        /// <summary>Язык интерфейса.</summary>
        public string Language { get; set; } = "ru";
        /// <summary>Тема оформления.</summary>
        public string Theme { get; set; } = "dark";
        /// <summary>Поведенческие настройки.</summary>
        public AppSettings Settings { get; set; } = new();
    }

    /// <summary>Инициализирует хранилище с путём к старому единому файлу конфигурации.</summary>
    /// <param name="filePath">Путь к legacy-файлу (startup-monitor.json).</param>
    public JsonDataStore(string filePath)
    {
        if (Directory.Exists(filePath) || string.IsNullOrEmpty(Path.GetExtension(filePath)))
        {
            _dir = filePath;
            _legacyPath = Path.Combine(filePath, "startup-monitor.json");
        }
        else
        {
            _legacyPath = filePath;
            _dir = Path.GetDirectoryName(filePath) ?? ".";
        }
        Directory.CreateDirectory(_dir);
        _options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
        };
        _historyOptions = new JsonSerializerOptions
        {
            WriteIndented = false,
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
        };
    }

    // ───────────────────────────── Load / Migrate ─────────────────────────────

    /// <summary>Загружает и возвращает полный конфиг из секционных файлов. Мигрирует legacy при необходимости.</summary>
    /// <summary>Гарантирует загрузку данных в _cache под объектом блокировки _lock.</summary>
    private void EnsureLoadedLocked()
    {
        if (_cache != null) return;

        MigrateLegacyIfNeeded();

        _cache = new AppConfig
        {
            Entries = ReadSection<List<StartupEntry>>(EntriesPath) ?? [],
            Blacklist = ReadSection<List<BlacklistRule>>(BlacklistPath) ?? [],
            RemovedEntries = ReadSection<List<RemovedEntry>>(RemovedPath) ?? [],
            History = ReadHistory(),
        };
        var settings = ReadSection<SettingsSection>(SettingsPath);
        if (settings != null)
        {
            _cache.Language = settings.Language;
            _cache.Theme = settings.Theme;
            _cache.Settings = settings.Settings;
        }

        TrimHistoryFile();
    }

    /// <summary>Загружает и возвращает изолированный глубокий снимок конфигурации (P0-02).</summary>
    public AppConfig Load()
    {
        using var _profLoad = ExecutionProfiler.Profile("Service.JsonDataStoreLoad");
        using var _prof = ExecutionProfiler.Measure("Shared.Store.Load");
        lock (_lock)
        {
            EnsureLoadedLocked();
            return CloneSnapshot(_cache!);
        }
    }

    /// <summary>
    /// Создает изолированную глубокую копию всей конфигурации для безопасной параллельной работы.
    /// </summary>
    private static AppConfig CloneSnapshot(AppConfig source)
    {
        return new AppConfig
        {
            Language = source.Language,
            Theme = source.Theme,
            Entries = source.Entries.Select(e => e.Clone()).ToList(),
            Blacklist = source.Blacklist.Select(b => b.Clone()).ToList(),
            RemovedEntries = source.RemovedEntries.Select(r => r.Clone()).ToList(),
            History = source.History.Select(h => h.Clone()).ToList(),
            Settings = source.Settings.Clone()
        };
    }

    /// <summary>Старый единый файл раскладывается по секционным и переименовывается в .migrated.</summary>
    private void MigrateLegacyIfNeeded()
    {
        if (!File.Exists(_legacyPath)) return;
        // Новые файлы уже есть — миграция выполнялась ранее, старый просто убираем
        if (File.Exists(EntriesPath) || File.Exists(SettingsPath))
        {
            try { File.Move(_legacyPath, _legacyPath + ".migrated", overwrite: true); } catch { }
            return;
        }

        AppConfig? legacy = null;
        try
        {
            legacy = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(_legacyPath), _options);
        }
        catch { }
        legacy ??= new AppConfig();

        WriteSectionAtomic(EntriesPath, JsonSerializer.Serialize(legacy.Entries, _options));
        WriteSectionAtomic(BlacklistPath, JsonSerializer.Serialize(legacy.Blacklist, _options));
        WriteSectionAtomic(RemovedPath, JsonSerializer.Serialize(legacy.RemovedEntries, _options));
        WriteSectionAtomic(SettingsPath, JsonSerializer.Serialize(new SettingsSection
        {
            Language = legacy.Language,
            Theme = legacy.Theme,
            Settings = legacy.Settings
        }, _options));
        foreach (var h in legacy.History)
            AppendHistoryLine(h);

        try { File.Move(_legacyPath, _legacyPath + ".migrated", overwrite: true); } catch { }
    }

    // ───────────────────────────── Section IO ─────────────────────────────

    /// <summary>Читает секцию с защитой от сбоев: при повреждении восстанавливает из .backup (P0-03).</summary>
    private T? ReadSection<T>(string path) where T : class
    {
        var backup = path + ".backup";
        if (!File.Exists(path))
        {
            if (File.Exists(backup))
            {
                try
                {
                    var backupJson = File.ReadAllText(backup);
                    var restored = JsonSerializer.Deserialize<T>(backupJson, _options);
                    if (restored != null)
                    {
                        File.Copy(backup, path, overwrite: true);
                        _writtenHashes[path] = ContentHash(backupJson);
                        return restored;
                    }
                }
                catch { }
            }
            return null;
        }

        try
        {
            var json = File.ReadAllText(path);
            _writtenHashes[path] = ContentHash(json);
            return JsonSerializer.Deserialize<T>(json, _options);
        }
        catch (Exception)
        {
            try { File.Move(path, path + ".corrupt", overwrite: true); } catch { }
            if (File.Exists(backup))
            {
                try
                {
                    var backupJson = File.ReadAllText(backup);
                    var restored = JsonSerializer.Deserialize<T>(backupJson, _options);
                    if (restored != null)
                    {
                        File.Copy(backup, path, overwrite: true);
                        _writtenHashes[path] = ContentHash(backupJson);
                        return restored;
                    }
                }
                catch { }
            }
            return null;
        }
    }

    /// <summary>Читает history.jsonl (поддерживая как однострочный JSONL, так и многострочный legacy формат).</summary>
    public List<HistoryEntry> ReadHistory()
    {
        var result = new List<HistoryEntry>();
        if (!File.Exists(HistoryPath)) return result;
        try
        {
            var content = File.ReadAllText(HistoryPath);
            if (string.IsNullOrWhiteSpace(content)) return result;

            using var reader = new StringReader(content);
            string? line;
            var multiLineBuffer = new System.Text.StringBuilder();
            bool hadLegacyMultiline = false;

            while ((line = reader.ReadLine()) != null)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;

                // 1. Пробуем десериализовать одну строку как полноценный JSONL
                try
                {
                    var single = JsonSerializer.Deserialize<HistoryEntry>(line, _historyOptions);
                    if (single != null && !string.IsNullOrEmpty(single.Id))
                    {
                        result.Add(single);
                        multiLineBuffer.Clear();
                        continue;
                    }
                }
                catch
                {
                    // Не отдельная строка — возможно, блок многострочного JSON
                }

                // 2. Накопление буфера для восстановления записей из многострочного формата
                multiLineBuffer.AppendLine(line);
                var bufferStr = multiLineBuffer.ToString().Trim();
                if (bufferStr.StartsWith('{') && bufferStr.EndsWith('}'))
                {
                    try
                    {
                        var entry = JsonSerializer.Deserialize<HistoryEntry>(bufferStr, _options);
                        if (entry != null && !string.IsNullOrEmpty(entry.Id))
                        {
                            result.Add(entry);
                            hadLegacyMultiline = true;
                            multiLineBuffer.Clear();
                        }
                    }
                    catch
                    {
                        // Ещё не закрытый объект или синтаксическая неполнота
                    }
                }
            }

            // Если были записи в legacy многострочном формате, мигрируем файл в чистый однострочный JSONL
            if (hadLegacyMultiline && result.Count > 0)
            {
                try
                {
                    var lines = result.Select(h => JsonSerializer.Serialize(h, _historyOptions));
                    File.WriteAllText(HistoryPath, string.Join(Environment.NewLine, lines) + Environment.NewLine);
                }
                catch { }
            }
        }
        catch { }
        return result;
    }

    /// <summary>Вычисляет SHA256-хеш строки содержимого для сравнения изменений.</summary>
    private static string ContentHash(string content) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(content)));

    /// <summary>Атомарная запись секции с принудительным сбросом кэша диска (fsync) и ротируемым бэкапом (P0-03).</summary>
    private void WriteSectionAtomic(string path, string json)
    {
        var hash = ContentHash(json);
        if (_writtenHashes.TryGetValue(path, out var old) && old == hash)
            return;

        var tmp = path + ".tmp";
        using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
        using (var writer = new StreamWriter(fs, System.Text.Encoding.UTF8))
        {
            writer.Write(json);
            writer.Flush();
            fs.Flush(flushToDisk: true);
        }

        var backup = path + ".backup";
        if (File.Exists(path))
        {
            try { File.Copy(path, backup, overwrite: true); } catch { }
        }

        File.Move(tmp, path, overwrite: true);
        _writtenHashes[path] = hash;
        Interlocked.Increment(ref _revision);
    }

    /// <summary>Дописывает одну запись истории в конец history.jsonl в одну строку.</summary>
    private void AppendHistoryLine(HistoryEntry entry)
    {
        var line = JsonSerializer.Serialize(entry, _historyOptions);
        File.AppendAllText(HistoryPath, line + Environment.NewLine);
    }

    /// <summary>Вычищает из history.jsonl события старше HistoryDays (перезаписью при старте).</summary>
    private void TrimHistoryFile()
    {
        if (_cache == null || _cache.History.Count == 0) return;
        var cutoff = DateTime.UtcNow.AddDays(-_cache.Settings.HistoryDays);
        var before = _cache.History.Count;
        _cache.History.RemoveAll(h => h.Timestamp < cutoff);
        if (_cache.History.Count != before)
        {
            var lines = _cache.History.Select(h => JsonSerializer.Serialize(h, _historyOptions));
            WriteSectionAtomic(HistoryPath, string.Join(Environment.NewLine, lines) + Environment.NewLine);
        }
    }

    /// <summary>Сохраняет startup-entries.json.</summary>
    private void SaveEntries() => WriteSectionAtomic(EntriesPath, JsonSerializer.Serialize(_cache!.Entries, _options));
    /// <summary>Сохраняет blacklist.json.</summary>
    private void SaveBlacklist() => WriteSectionAtomic(BlacklistPath, JsonSerializer.Serialize(_cache!.Blacklist, _options));
    /// <summary>Сохраняет removed-entries.json.</summary>
    private void SaveRemoved() => WriteSectionAtomic(RemovedPath, JsonSerializer.Serialize(_cache!.RemovedEntries, _options));

    /// <summary>Сохраняет settings.json (language, theme, AppSettings).</summary>
    private void SaveSettings() => WriteSectionAtomic(SettingsPath, JsonSerializer.Serialize(new SettingsSection
    {
        Language = _cache!.Language,
        Theme = _cache.Theme,
        Settings = _cache.Settings
    }, _options));

    /// <summary>Сохраняет весь конфиг — все секции целиком.</summary>
    public void Save(AppConfig config)
    {
        using var _profSave = ExecutionProfiler.Profile("Service.JsonDataStoreSave");
        using var _prof = ExecutionProfiler.Measure("Shared.Store.Save");
        lock (_lock)
        {
            _cache = config;
            SaveEntries();
            SaveBlacklist();
            SaveRemoved();
            SaveSettings();
        }
    }

    // ───────────────────────────── Entries ─────────────────────────────

    /// <summary>Очищает все записи автозапуска и историю.</summary>
    public void ClearEntries()
    {
        lock (_lock)
        {
            EnsureLoadedLocked();
            _cache!.Entries.Clear();
            _cache.History.Clear();
            SaveEntries();
            WriteSectionAtomic(HistoryPath, "");
        }
    }

    /// <summary>Добавляет запись автозапуска с проверкой на дубликат по Id.</summary>
    /// <param name="entry">Добавляемая запись.</param>
    /// <param name="checkDuplicate">Проверять ли дубликат по Id.</param>
    /// <returns>true, если запись добавлена; false, если дубликат найден.</returns>
    public bool AddEntry(StartupEntry entry, bool checkDuplicate = true)
    {
        lock (_lock)
        {
            EnsureLoadedLocked();
            if (_cache == null) return false;
            if (checkDuplicate && _cache.Entries.Any(e => e.Id == entry.Id))
                return false;
            _cache.Entries.Add(entry.Clone());
            SaveEntries();
            return true;
        }
    }

    /// <summary>Находит запись по предикату.</summary>
    public StartupEntry? FindEntry(Func<StartupEntry, bool> predicate)
    {
        lock (_lock)
        {
            var cfg = Load();
            return cfg.Entries.FirstOrDefault(predicate);
        }
    }

    /// <summary>Добавляет пачку записей (опционально с предварительной очисткой).</summary>
    /// <param name="entries">Список добавляемых записей.</param>
    /// <param name="clearFirst">Очистить существующие записи и историю перед добавлением.</param>
    public void AddEntryBatch(List<StartupEntry> entries, bool clearFirst = false)
    {
        lock (_lock)
        {
            EnsureLoadedLocked();
            if (_cache == null) return;
            if (clearFirst)
            {
                _cache.Entries.Clear();
                _cache.History.Clear();
                WriteSectionAtomic(HistoryPath, "");
            }
            _cache.Entries.AddRange(entries.Select(e => e.Clone()));
            SaveEntries();
        }
    }

    /// <summary>
    /// Нормализует строку местоположения для дедупликации (устраняет расхождение HKCU\user\ vs HKCU\).
    /// </summary>
    public static string NormalizeLocationDetail(string? detail)
    {
        if (string.IsNullOrWhiteSpace(detail)) return string.Empty;
        var normalized = detail.Replace('/', '\\');
        // Если путь вида HKCU\<username>\Software\... -> приводим к HKCU\Software\...
        if (normalized.StartsWith(@"HKCU\", StringComparison.OrdinalIgnoreCase))
        {
            var parts = normalized.Split('\\', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 3 && !parts[1].Equals("Software", StringComparison.OrdinalIgnoreCase))
            {
                if (parts[2].Equals("Software", StringComparison.OrdinalIgnoreCase))
                {
                    return @"HKCU\" + string.Join('\\', parts.Skip(2));
                }
            }
        }
        return normalized;
    }

    /// <summary>Удаляет дубликаты записей с одинаковыми Name|Location|NormalizedLocationDetail (регистронезависимо).</summary>
    public void DeduplicateEntries()
    {
        using var _prof = ExecutionProfiler.Measure("Shared.Store.Deduplicate");
        lock (_lock)
        {
            Load();
            if (_cache == null) return;
            var map = new Dictionary<string, StartupEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in _cache.Entries)
            {
                var normDetail = NormalizeLocationDetail(e.LocationDetail);
                var key = $"{e.Name}|{e.Location}|{normDetail}";
                if (!map.TryGetValue(key, out var existing))
                {
                    map[key] = e;
                }
                else
                {
                    // Предпочитаем запись с конкретным именем профиля (HKCU\<user>\...),
                    // так как служба под LocalSystem работает именно с ними.
                    var currentHasProfile = e.LocationDetail.StartsWith(@"HKCU\", StringComparison.OrdinalIgnoreCase)
                        && !e.LocationDetail.StartsWith(@"HKCU\Software", StringComparison.OrdinalIgnoreCase);
                    var existingHasProfile = existing.LocationDetail.StartsWith(@"HKCU\", StringComparison.OrdinalIgnoreCase)
                        && !existing.LocationDetail.StartsWith(@"HKCU\Software", StringComparison.OrdinalIgnoreCase);

                    if (currentHasProfile && !existingHasProfile)
                    {
                        if (existing.AiRating > 0 && e.AiRating == 0)
                        {
                            e.AiRating = existing.AiRating;
                            e.AiCategory = existing.AiCategory;
                            e.AiRisk = existing.AiRisk;
                            e.AiRecommendation = existing.AiRecommendation;
                        }
                        map[key] = e;
                    }
                    else if (!currentHasProfile && existingHasProfile)
                    {
                        if (e.AiRating > 0 && existing.AiRating == 0)
                        {
                            existing.AiRating = e.AiRating;
                            existing.AiCategory = e.AiCategory;
                            existing.AiRisk = e.AiRisk;
                            existing.AiRecommendation = e.AiRecommendation;
                        }
                    }
                }
            }
            if (map.Count != _cache.Entries.Count)
            {
                _cache.Entries = map.Values.ToList();
                SaveEntries();
            }
        }
    }

    /// <summary>Удаляет запись по её Id.</summary>
    public void RemoveEntry(string entryId)
    {
        lock (_lock)
        {
            EnsureLoadedLocked();
            if (_cache == null) return;
            var removed = _cache.Entries.RemoveAll(e => e.Id == entryId);
            if (removed > 0)
            {
                SaveEntries();
            }
        }
    }

    /// <summary>Обновляет запись (заменяет по Id).</summary>
    public void UpdateEntry(StartupEntry entry)
    {
        lock (_lock)
        {
            EnsureLoadedLocked();
            if (_cache == null) return;
            var idx = _cache.Entries.FindIndex(e => e.Id == entry.Id);
            if (idx >= 0)
            {
                _cache.Entries[idx] = entry.Clone();
                SaveEntries();
            }
        }
    }

    // ───────────────────────────── History ─────────────────────────────

    /// <summary>Добавляет запись в историю, обрезая элементы старше HistoryDays.</summary>
    public void AddHistory(HistoryEntry entry)
    {
        lock (_lock)
        {
            EnsureLoadedLocked();
            if (_cache == null) return;
            _cache.History.Add(entry.Clone());

            var cutoff = DateTime.UtcNow.AddDays(-_cache.Settings.HistoryDays);
            _cache.History.RemoveAll(h => h.Timestamp < cutoff);

            AppendHistoryLine(entry);
            Interlocked.Increment(ref _revision);
        }
    }

    // ───────────────────────────── Removed ─────────────────────────────

    /// <summary>Возвращает копию списка удалённых записей.</summary>
    public List<RemovedEntry> GetRemovedEntries()
    {
        lock (_lock)
        {
            var config = Load();
            return [.. config.RemovedEntries];
        }
    }

    /// <summary>Проверяет, есть ли запись с указанным стабильным Id в списке удалённых.</summary>
    public bool IsRemoved(string entryId)
    {
        lock (_lock)
        {
            var config = Load();
            return config.RemovedEntries.Any(r => r.OriginalEntryId == entryId);
        }
    }

    /// <summary>Добавляет запись в бэкап удалённых (или обновляет существующую по OriginalEntryId).</summary>
    public void AddRemovedEntry(RemovedEntry entry)
    {
        lock (_lock)
        {
            Load();
            if (_cache == null) return;
            // Один бэкап на запись: повторное срабатывание правила не плодит дубликаты
            _cache.RemovedEntries.RemoveAll(r => r.OriginalEntryId == entry.OriginalEntryId);
            _cache.RemovedEntries.Add(entry);
            SaveRemoved();
        }
    }

    /// <summary>Возвращает бэкап удаленной записи по её OriginalEntryId или null.</summary>
    public RemovedEntry? GetRemovedEntryByOriginalId(string entryId)
    {
        lock (_lock)
        {
            var config = Load();
            return config.RemovedEntries.FirstOrDefault(r => r.OriginalEntryId == entryId);
        }
    }

    /// <summary>Удаляет все записи из бэкапа с указанной причиной удаления (например LockdownBlocked).</summary>
    public int RemoveRemovedEntriesByReason(string reason)
    {
        lock (_lock)
        {
            Load();
            if (_cache == null) return 0;
            var count = _cache.RemovedEntries.RemoveAll(r => string.Equals(r.RemovalReason, reason, StringComparison.OrdinalIgnoreCase));
            if (count > 0)
            {
                SaveRemoved();
            }
            return count;
        }
    }

    /// <summary>Удаляет запись из бэкапа по OriginalEntryId.</summary>
    public void RemoveRemovedEntryByOriginalId(string entryId)
    {
        lock (_lock)
        {
            Load();
            if (_cache == null) return;
            var count = _cache.RemovedEntries.RemoveAll(r => r.OriginalEntryId == entryId);
            if (count > 0)
            {
                SaveRemoved();
            }
        }
    }

    /// <summary>Удаляет запись из бэкапа удалённых по Id.</summary>
    public void RemoveRemovedEntry(string removedId)
    {
        lock (_lock)
        {
            Load();
            if (_cache == null) return;
            _cache.RemovedEntries.RemoveAll(r => r.Id == removedId);
            SaveRemoved();
        }
    }

    // ───────────────────────────── Snapshots ─────────────────────────────

    /// <summary>Загружает и возвращает список всех сохранённых снимков автозапуска (сортировка от новых к старым).</summary>
    public List<BaselineSnapshot> GetSnapshots()
    {
        lock (_lock)
        {
            var list = ReadSection<List<BaselineSnapshot>>(SnapshotsPath) ?? [];
            return list.OrderByDescending(s => s.CreatedAt).ToList();
        }
    }

    /// <summary>Находит и возвращает снимок по его уникальному Id.</summary>
    public BaselineSnapshot? GetSnapshot(string snapshotId)
    {
        lock (_lock)
        {
            var list = ReadSection<List<BaselineSnapshot>>(SnapshotsPath) ?? [];
            return list.FirstOrDefault(s => string.Equals(s.Id, snapshotId, StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>Сохраняет (добавляет или обновляет) снимок автозапуска в snapshots.json.</summary>
    public void SaveSnapshot(BaselineSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        lock (_lock)
        {
            var list = ReadSection<List<BaselineSnapshot>>(SnapshotsPath) ?? [];
            list.RemoveAll(s => string.Equals(s.Id, snapshot.Id, StringComparison.OrdinalIgnoreCase));
            list.Add(snapshot);
            WriteSectionAtomic(SnapshotsPath, JsonSerializer.Serialize(list, _options));
        }
    }

    /// <summary>Удаляет снимок по его Id.</summary>
    public bool DeleteSnapshot(string snapshotId)
    {
        lock (_lock)
        {
            var list = ReadSection<List<BaselineSnapshot>>(SnapshotsPath) ?? [];
            var removed = list.RemoveAll(s => string.Equals(s.Id, snapshotId, StringComparison.OrdinalIgnoreCase));
            if (removed > 0)
            {
                WriteSectionAtomic(SnapshotsPath, JsonSerializer.Serialize(list, _options));
                return true;
            }
            return false;
        }
    }
}
