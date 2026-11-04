using System.Text;
using System.Text.Json;
using System.Runtime.CompilerServices;
using StartupMonitor.Shared.Models.Firewall;
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
    /// <summary>Исходные неизменяемые состояния снимков; слабые ключи не удерживают завершившихся читателей.</summary>
    private readonly ConditionalWeakTable<AppConfig, AppConfig> _snapshotOrigins = new();
    /// <summary>Исходные состояния отдельных записей для сохранения независимых параллельных полей.</summary>
    private readonly ConditionalWeakTable<StartupEntry, StartupEntry> _entryOrigins = new();
    /// <summary>Приёмник диагностических сообщений без зависимости Shared от конкретного логгера.</summary>
    private readonly Action<string, Exception?> _logWarning;
    /// <summary>Время последней физической компактации истории.</summary>
    private DateTime _lastHistoryCompactionUtc = DateTime.MinValue;
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
    /// <param name="logWarning">Приёмник сообщений восстановления; без него используется Trace.</param>
    public JsonDataStore(string filePath, Action<string, Exception?>? logWarning = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        _logWarning = logWarning ?? ((message, exception) => System.Diagnostics.Trace.TraceWarning("{0}: {1}", message, exception));
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
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            PropertyNameCaseInsensitive = true
        };
        _historyOptions = new JsonSerializerOptions
        {
            WriteIndented = false,
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            PropertyNameCaseInsensitive = true
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
            var source = _cache ?? throw new InvalidOperationException("Конфигурация не загружена.");
            var snapshot = CloneSnapshot(source);
            _snapshotOrigins.Add(snapshot, source);
            for (var i = 0; i < snapshot.Entries.Count; i++)
                _entryOrigins.Add(snapshot.Entries[i], source.Entries[i]);
            return snapshot;
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

    /// <summary>Создаёт новую оболочку кэша; вызывающий метод копирует только изменяемую коллекцию.</summary>
    private void ForkCache()
    {
        var source = _cache ?? throw new InvalidOperationException("Конфигурация не загружена.");
        _cache = new AppConfig
        {
            Language = source.Language, Theme = source.Theme, Settings = source.Settings,
            Entries = source.Entries, Blacklist = source.Blacklist,
            RemovedEntries = source.RemovedEntries, History = source.History
        };
    }

    /// <summary>Проверяет обязательные объекты и диапазоны до публикации десериализованной секции.</summary>
    /// <typeparam name="T">Тип секции.</typeparam>
    /// <param name="value">Десериализованный объект.</param>
    /// <returns>Проверенный объект.</returns>
    private static T ValidateSection<T>(T? value) where T : class
    {
        if (value is null) throw new JsonException("Обязательная секция содержит null.");
        switch (value)
        {
            case SettingsSection section:
                ValidateSettings(section.Settings);
                break;
            case AppConfig config:
                ValidateSettings(config.Settings);
                ValidateSection(config.Entries);
                ValidateSection(config.Blacklist);
                ValidateSection(config.RemovedEntries);
                ValidateSection(config.History);
                break;
            case IEnumerable<StartupEntry> entries:
                foreach (var entry in entries)
                    if (entry is null || entry.RawData is null || entry.ThreatInfo is { Indicators: null } ||
                        !Enum.IsDefined(entry.Status) || !Enum.IsDefined(entry.Location))
                        throw new JsonException("Запись автозапуска содержит недопустимый объект или enum.");
                break;
            case System.Collections.IEnumerable items when value is not string:
                foreach (var item in items)
                {
                    if (item is null) throw new JsonException("Коллекция содержит обязательный элемент null.");
                    if (item is BaselineSnapshot snapshot) ValidateSection(snapshot.Entries);
                }
                break;
        }
        return value;
    }

    /// <summary>Проверяет границы настроек, необходимые для безопасного клонирования и расчёта времени.</summary>
    /// <param name="settings">Проверяемые настройки.</param>
    private static void ValidateSettings(AppSettings? settings)
    {
        if (settings is null || settings.PortDefender is null || settings.ActiveBans is null ||
            settings.FirewallAllowedPorts is null || settings.FirewallWhitelistedApps is null ||
            settings.FirewallBlacklistedApps is null)
            throw new JsonException("Настройки содержат обязательный объект null.");
        if (settings.HistoryDays is < 1 or > 36500 || settings.CpuThrottleThreshold is < 0 or > 100)
            throw new JsonException("Настройки содержат недопустимый срок истории или порог CPU.");
        var defender = settings.PortDefender;
        if (defender.Rules is null || defender.ProtectedPorts is null || defender.WhitelistIps is null ||
            defender.Rules.Any(rule => rule is null || rule.WhitelistIps is null || !Enum.IsDefined(rule.WindowUnit)) ||
            settings.ActiveBans.Any(ban => ban is null))
            throw new JsonException("Настройки защиты портов содержат недопустимую коллекцию или enum.");
    }

    /// <summary>Раскладывает проверенный legacy-файл по секциям; отказ архивирования диагностируется без потери новых секций.</summary>
    private void MigrateLegacyIfNeeded()
    {
        if (!File.Exists(_legacyPath)) return;
        // Новые файлы уже есть — миграция выполнялась ранее, старый просто убираем
        if (File.Exists(EntriesPath) || File.Exists(SettingsPath))
        {
            try { File.Move(_legacyPath, _legacyPath + ".migrated", overwrite: true); }
            catch (Exception ex) { _logWarning("Не удалось архивировать уже перенесённую конфигурацию.", ex); }
            return;
        }

        AppConfig? legacy = null;
        try
        {
            legacy = ValidateSection(JsonSerializer.Deserialize<AppConfig>(ReadUtf8Text(_legacyPath), _options));
        }
        catch (Exception ex) { _logWarning("Не удалось прочитать исходную конфигурацию для миграции.", ex); }
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

        try { File.Move(_legacyPath, _legacyPath + ".migrated", overwrite: true); }
        catch (Exception ex) { _logWarning("Секции перенесены, но исходную конфигурацию не удалось архивировать.", ex); }
    }

    // ───────────────────────────── Section IO ─────────────────────────────

    /// <summary>
    /// Читает текстовый файл в кодировке UTF-8, бесшовно поддерживая наличие или отсутствие BOM-маркера (P0-03).
    /// </summary>
    /// <param name="filePath">Путь к считываемому файлу.</param>
    /// <returns>Строка содержимого файла без BOM.</returns>
    private static string ReadUtf8Text(string filePath)
    {
        var bytes = File.ReadAllBytes(filePath);
        if (bytes.Length == 0) return string.Empty;

        var offset = 0;
        // Проверяем 3-байтовый преамбулу UTF-8 BOM (0xEF, 0xBB, 0xBF)
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            offset = 3;
        }

        var text = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetString(bytes, offset, bytes.Length - offset);
        return text.TrimStart('\uFEFF');
    }

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
                    var backupJson = ReadUtf8Text(backup);
                    var restored = ValidateSection(JsonSerializer.Deserialize<T>(backupJson, _options));
                    if (restored != null)
                    {
                        File.Copy(backup, path, overwrite: true);
                        _writtenHashes[path] = ContentHash(backupJson);
                        return restored;
                    }
                }
                catch (Exception ex) { _logWarning($"Не удалось восстановить секцию '{path}' из резервной копии.", ex); }
            }
            return null;
        }

        try
        {
            var json = ReadUtf8Text(path);
            var section = ValidateSection(JsonSerializer.Deserialize<T>(json, _options));
            _writtenHashes[path] = ContentHash(json);
            return section;
        }
        catch (Exception ex)
        {
            _logWarning($"Повреждена секция '{path}', выполняется восстановление.", ex);
            try { File.Move(path, path + ".corrupt", overwrite: true); }
            catch (Exception moveError) { _logWarning($"Не удалось сохранить повреждённую секцию '{path}'.", moveError); }
            if (File.Exists(backup))
            {
                try
                {
                    var backupJson = ReadUtf8Text(backup);
                    var restored = ValidateSection(JsonSerializer.Deserialize<T>(backupJson, _options));
                    if (restored != null)
                    {
                        File.Copy(backup, path, overwrite: true);
                        _writtenHashes[path] = ContentHash(backupJson);
                        return restored;
                    }
                }
                catch (Exception restoreError) { _logWarning($"Не удалось восстановить секцию '{path}' из резервной копии.", restoreError); }
            }
            return null;
        }
    }

    /// <summary>Читает JSONL и legacy-объекты; неполные фрагменты диагностирует один раз, сохраняя доступные события.</summary>
    /// <returns>Восстановленные события без повреждённых или незавершённых записей.</returns>
    public List<HistoryEntry> ReadHistory()
    {
        var result = new List<HistoryEntry>();
        if (!File.Exists(HistoryPath)) return result;
        try
        {
            var content = ReadUtf8Text(HistoryPath);
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
                        if (multiLineBuffer.Length > 0)
                            _logWarning("Перед корректным событием истории пропущен повреждённый фрагмент JSON.", null);
                        result.Add(single);
                        multiLineBuffer.Clear();
                        continue;
                    }
                }
                catch (JsonException)
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
                    catch (JsonException)
                    {
                        // Ещё не закрытый объект или синтаксическая неполнота
                    }
                }
            }

            if (multiLineBuffer.Length > 0)
                _logWarning("Файл истории заканчивается повреждённой или незавершённой записью JSON.", null);

            // Если были записи в legacy многострочном формате, мигрируем файл в чистый однострочный JSONL
            if (hadLegacyMultiline && result.Count > 0)
            {
                try
                {
                    var lines = result.Select(h => JsonSerializer.Serialize(h, _historyOptions));
                    File.WriteAllText(HistoryPath, string.Join(Environment.NewLine, lines) + Environment.NewLine);
                }
                catch (Exception ex) { _logWarning("Не удалось преобразовать файл истории в однострочный JSONL.", ex); }
            }
        }
        catch (Exception ex) { _logWarning("Не удалось полностью прочитать файл истории; возвращены восстановленные события.", ex); }
        return result;
    }

    /// <summary>Вычисляет SHA256-хеш строки содержимого для сравнения изменений.</summary>
    private static string ContentHash(string content) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(content)));

    /// <summary>Атомарно записывает секцию с fsync; отказ обновления резервной копии диагностируется до замены основного файла.</summary>
    /// <param name="path">Путь сохраняемой секции.</param>
    /// <param name="json">Полное проверенное содержимое секции.</param>
    private void WriteSectionAtomic(string path, string json)
    {
        var hash = ContentHash(json);
        if (_writtenHashes.TryGetValue(path, out var old) && old == hash)
            return;

        var tmp = path + ".tmp";
        using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
        // Гарантированная запись в чистом UTF-8 БЕЗ BOM (RFC 8259) для исключения битых символов
        using (var writer = new StreamWriter(fs, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
        {
            writer.Write(json);
            writer.Flush();
            fs.Flush(flushToDisk: true);
        }

        var backup = path + ".backup";
        if (File.Exists(path))
        {
            try { File.Copy(path, backup, overwrite: true); }
            catch (Exception ex) { _logWarning($"Не удалось обновить резервную копию секции {Path.GetFileName(path)}.", ex); }
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

    /// <summary>Сохраняет изменения снимка, объединяя их с актуальными секциями; повторный вызов сравнивается с последним намерением этого writer.</summary>
    /// <param name="config">Независимый снимок вызывающего кода; неизвестные ему конкурентные поля не откатываются.</param>
    public void Save(AppConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        ValidateSection(config);
        using var _profSave = ExecutionProfiler.Profile("Service.JsonDataStoreSave");
        using var _prof = ExecutionProfiler.Measure("Shared.Store.Save");
        lock (_lock)
        {
            EnsureLoadedLocked();
            // Базовые состояния неизменяемы: независимые изменения объединяются только при устаревшем снимке.
            var current = _cache ?? throw new InvalidOperationException("Конфигурация не загружена.");
            _cache = _snapshotOrigins.TryGetValue(config, out var original) && !ReferenceEquals(original, current)
                ? ConfigurationMerge.Merge(GetSnapshotMergeBaseline(original, config), config, current, _options)
                : CloneSnapshot(config);
            SaveEntries();
            SaveBlacklist();
            SaveRemoved();
            SaveSettings();
            // База повторного сохранения содержит только известные writer данные, а не весь результат merge:
            // это допускает возврат поля к прежнему значению и не превращает неизвестные добавления в удаление.
            var writerBaseline = CloneSnapshot(config);
            _snapshotOrigins.Remove(config);
            _snapshotOrigins.Add(config, writerBaseline);
            for (var index = 0; index < config.Entries.Count; index++)
            {
                _entryOrigins.Remove(config.Entries[index]);
                _entryOrigins.Add(config.Entries[index], writerBaseline.Entries[index]);
            }
        }
    }

    /// <summary>Учитывает успешные UpdateEntry того же writer перед общим Save, не меняя исходное членство коллекции.</summary>
    /// <param name="original">Последнее известное writer состояние полного снимка.</param>
    /// <param name="requested">Снимок с отдельными DTO, которые могли сохраняться через UpdateEntry.</param>
    /// <returns>Оболочка исходной базы с актуальной для writer базой только первоначально присутствовавших записей.</returns>
    private AppConfig GetSnapshotMergeBaseline(AppConfig original, AppConfig requested)
    {
        var entryOrigins = new Dictionary<string, StartupEntry>(StringComparer.Ordinal);
        foreach (var entry in requested.Entries)
        {
            if (!string.IsNullOrEmpty(entry.Id) && _entryOrigins.TryGetValue(entry, out var origin) && origin.Id == entry.Id)
                entryOrigins.TryAdd(entry.Id, origin);
        }
        return new AppConfig
        {
            Language = original.Language,
            Theme = original.Theme,
            Settings = original.Settings,
            Blacklist = original.Blacklist,
            RemovedEntries = original.RemovedEntries,
            History = original.History,
            Entries = original.Entries.Select(entry => !string.IsNullOrEmpty(entry.Id) && entryOrigins.TryGetValue(entry.Id, out var origin)
                ? origin : entry).ToList()
        };
    }

    // ───────────────────────────── Entries ─────────────────────────────

    /// <summary>Очищает все записи автозапуска и историю.</summary>
    public void ClearEntries()
    {
        lock (_lock)
        {
            EnsureLoadedLocked();
            ForkCache();
            _cache!.Entries = [];
            _cache.History = [];
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
        ArgumentNullException.ThrowIfNull(entry);
        lock (_lock)
        {
            EnsureLoadedLocked();
            if (_cache == null) return false;
            if (checkDuplicate && _cache.Entries.Any(e => e.Id == entry.Id))
                return false;
            ForkCache();
            _cache!.Entries = [.. _cache.Entries, entry.Clone()];
            SaveEntries();
            return true;
        }
    }

    /// <summary>
    /// Находит запись по предикату напрямую из закэшированного снимка без тяжелого клонирования всего хранилища и истории (P0-01).
    /// </summary>
    public StartupEntry? FindEntry(Func<StartupEntry, bool> predicate)
    {
        lock (_lock)
        {
            EnsureLoadedLocked();
            var entry = _cache?.Entries.FirstOrDefault(predicate);
            if (entry is null) return null;
            var snapshot = entry.Clone();
            _entryOrigins.Add(snapshot, entry);
            return snapshot;
        }
    }

    /// <summary>
    /// Проверяет наличие записи по Id напрямую в кэше без выделения памяти в куче (P0-02).
    /// </summary>
    public bool EntryExists(string id)
    {
        lock (_lock)
        {
            EnsureLoadedLocked();
            return _cache?.Entries.Any(e => e.Id == id) ?? false;
        }
    }

    /// <summary>
    /// Проверяет наличие записи по произвольному предикату без выделения памяти (P0-02).
    /// </summary>
    public bool EntryExists(Func<StartupEntry, bool> predicate)
    {
        lock (_lock)
        {
            EnsureLoadedLocked();
            return _cache?.Entries.Any(predicate) ?? false;
        }
    }

    /// <summary>
    /// Возвращает изолированную копию настроек без дорогостоящего клонирования коллекций записей и истории (P0-03).
    /// </summary>
    public AppSettings GetSettings()
    {
        lock (_lock)
        {
            EnsureLoadedLocked();
            return _cache?.Settings.Clone() ?? new AppSettings();
        }
    }

    /// <summary>
    /// Находит первое подходящее правило черного списка без клонирования всего снимка конфигурации (P0-04).
    /// </summary>
    public BlacklistRule? FindBlacklistRule(Func<BlacklistRule, bool> predicate)
    {
        lock (_lock)
        {
            EnsureLoadedLocked();
            var rule = _cache?.Blacklist.FirstOrDefault(predicate);
            return rule?.Clone();
        }
    }

    /// <summary>
    /// Возвращает изолированную копию записей истории для отображения в интерфейсе без клонирования всех остальных сущностей.
    /// </summary>
    public List<HistoryEntry> GetHistoryEntries()
    {
        lock (_lock)
        {
            EnsureLoadedLocked();
            return _cache?.History.Select(h => h.Clone()).ToList() ?? [];
        }
    }

    /// <summary>Добавляет пачку записей (опционально с предварительной очисткой).</summary>
    /// <param name="entries">Список добавляемых записей.</param>
    /// <param name="clearFirst">Очистить существующие записи и историю перед добавлением.</param>
    public void AddEntryBatch(List<StartupEntry> entries, bool clearFirst = false)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ValidateSection(entries);
        lock (_lock)
        {
            EnsureLoadedLocked();
            if (_cache == null) return;
            ForkCache();
            if (clearFirst)
            {
                _cache!.Entries = [];
                _cache.History = [];
                WriteSectionAtomic(HistoryPath, "");
            }
            _cache!.Entries = [.. _cache.Entries, .. entries.Select(e => e.Clone())];
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
            EnsureLoadedLocked();
            if (_cache == null) return;
            ForkCache();
            _cache!.Entries = _cache.Entries.Select(entry => entry.Clone()).ToList();
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
            var retained = _cache.Entries.Where(e => e.Id != entryId).ToList();
            var removed = _cache.Entries.Count - retained.Count;
            if (removed > 0)
            {
                ForkCache();
                _cache!.Entries = retained;
                SaveEntries();
            }
        }
    }

    /// <summary>Обновляет принадлежащие снимку изменения записи по Id; после успеха запоминает его новое известное состояние.</summary>
    /// <param name="entry">Изменённый независимый снимок записи.</param>
    public void UpdateEntry(StartupEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        lock (_lock)
        {
            EnsureLoadedLocked();
            if (_cache == null) return;
            var idx = _cache.Entries.FindIndex(e => e.Id == entry.Id);
            if (idx >= 0)
            {
                var current = _cache.Entries[idx];
                var replacement = _entryOrigins.TryGetValue(entry, out var original) && !ReferenceEquals(original, current)
                    ? ConfigurationMerge.Merge(original, entry, current, _options)
                    : entry.Clone();
                ForkCache();
                _cache!.Entries = [.. _cache.Entries];
                _cache.Entries[idx] = replacement;
                SaveEntries();
                _entryOrigins.Remove(entry);
                _entryOrigins.Add(entry, entry.Clone());
            }
        }
    }

    /// <summary>Сохраняет репутационные поля пачки записей одной записью файла, не меняя их актуальные статусы.</summary>
    /// <param name="entries">Завершённые результаты репутационного сканирования.</param>
    public void UpdateEntryReputations(IEnumerable<StartupEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var updates = entries.ToDictionary(entry => entry.Id, StringComparer.Ordinal);
        if (updates.Count == 0) return;
        lock (_lock)
        {
            EnsureLoadedLocked();
            ForkCache();
            var current = _cache ?? throw new InvalidOperationException("Конфигурация не загружена.");
            current.Entries = current.Entries.Select(existing =>
            {
                if (!updates.TryGetValue(existing.Id, out var update)) return existing;
                var result = existing.Clone();
                result.KasperskyZone = update.KasperskyZone;
                result.KasperskyFileStatus = update.KasperskyFileStatus;
                result.KasperskyDetection = update.KasperskyDetection;
                if (!string.IsNullOrEmpty(update.SHA256)) result.SHA256 = update.SHA256;
                return result;
            }).ToList();
            SaveEntries();
        }
    }

    // ───────────────────────────── History ─────────────────────────────

    /// <summary>Добавляет запись в историю, обрезая элементы старше HistoryDays.</summary>
    public void AddHistory(HistoryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        lock (_lock)
        {
            EnsureLoadedLocked();
            if (_cache == null) return;
            var now = DateTime.UtcNow;
            var cutoff = now.AddDays(-_cache.Settings.HistoryDays);
            if (entry.Timestamp < cutoff) return;
            ForkCache();
            _cache!.History = _cache.History.Where(item => item.Timestamp >= cutoff).ToList();
            _cache.History.Add(entry.Clone());
            // Новая запись уже входит в компактацию; повторный append в этой ветке исключён.
            if (now - _lastHistoryCompactionUtc >= TimeSpan.FromHours(1))
            {
                var lines = _cache.History.Select(item => JsonSerializer.Serialize(item, _historyOptions));
                WriteSectionAtomic(HistoryPath, string.Join(Environment.NewLine, lines) + Environment.NewLine);
                _lastHistoryCompactionUtc = now;
            }
            else
            {
                AppendHistoryLine(entry);
            }
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
        ArgumentNullException.ThrowIfNull(entry);
        lock (_lock)
        {
            EnsureLoadedLocked();
            if (_cache == null) return;
            // Один бэкап на запись: повторное срабатывание правила не плодит дубликаты
            ForkCache();
            _cache!.RemovedEntries = _cache.RemovedEntries.Where(item => item.OriginalEntryId != entry.OriginalEntryId).ToList();
            _cache.RemovedEntries.Add(entry.Clone());
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
            EnsureLoadedLocked();
            if (_cache == null) return 0;
            ForkCache();
            _cache!.RemovedEntries = [.. _cache.RemovedEntries];
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
            EnsureLoadedLocked();
            if (_cache == null) return;
            ForkCache();
            _cache!.RemovedEntries = [.. _cache.RemovedEntries];
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
            EnsureLoadedLocked();
            if (_cache == null) return;
            ForkCache();
            _cache!.RemovedEntries = [.. _cache.RemovedEntries];
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
