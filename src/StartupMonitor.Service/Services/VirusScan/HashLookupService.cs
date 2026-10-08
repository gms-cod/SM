using System.Text.Json;
using Microsoft.Extensions.Logging;
using StartupMonitor.Shared.Models;
using StartupMonitor.Shared.Diagnostics;

namespace StartupMonitor.Service.Services.VirusScan;

/// <summary>
/// Проверяет SHA256 хеши записей через CIRCL hashlookup (free, no auth).
/// https://hashlookup.circl.lu/
/// </summary>
public class HashLookupService
{
    /// <summary>HTTP-клиент с таймаутом 10с.</summary>
    private readonly HttpClient _http;
    /// <summary>Хранилище конфига для обновления Kaspersky-полей записи.</summary>
    private readonly JsonDataStore _store;
    /// <summary>Логгер.</summary>
    private readonly ILogger<HashLookupService> _logger;
    /// <summary>Семафор для исключения параллельных сканирований.</summary>
    private readonly SemaphoreSlim _scanLock = new(1, 1);
    /// <summary>Множество ID записей, уже проверенных в текущей сессии.</summary>
    private readonly HashSet<string> _sessionScanned = [];

    /// <summary>Базовый URL CIRCL lookup API.</summary>
    private const string LookupUrl = "https://hashlookup.circl.lu/api/lookup/sha256/";

    /// <summary>Принимает хранилище конфига и логгер.</summary>
    public HashLookupService(JsonDataStore store, ILogger<HashLookupService> logger)
    {
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        _store = store;
        _logger = logger;
    }

    /// <summary>Проверяет одну запись через CIRCL hashlookup.
    /// Если SHA256 нет — пытается вычислить через ResolveAndEnrichEntry.</summary>
    /// <param name="entry">Проверяемая запись.</param>
    /// <param name="ct">Токен отмены.</param>
    /// <returns>true — запрос к CIRCL выполнен; false — нет хеша (помечен Grey/NoHash).</returns>
    public async Task<bool> ScanEntryAsync(StartupEntry entry, CancellationToken ct = default)
    {
        using var _prof = ExecutionProfiler.Measure("Service.HashLookup.ScanEntry");
        // Если SHA256 нет — пытаемся вычислить из файла
        if (string.IsNullOrEmpty(entry.SHA256))
        {
            EntryOperations.ResolveAndEnrichEntry(entry);
            if (string.IsNullOrEmpty(entry.SHA256))
            {
                // Нет файла на диске — нечего проверять: серая зона
                entry.KasperskyZone = "Grey";
                entry.KasperskyFileStatus = "NoHash";
                _store.UpdateEntry(entry);
                return false;
            }
        }

        try
        {
            var (zone, detection, status) = await QueryHashAsync(entry.SHA256, ct);

            // Сохраняем результат в поля записи
            entry.KasperskyZone = zone;
            entry.KasperskyDetection = detection;
            entry.KasperskyFileStatus = status;
            _store.UpdateEntry(entry);
            return true;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning("HashLookup: таймаут {Hash}", entry.SHA256);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "HashLookup: ошибка {Hash}", entry.SHA256);
        }
        return false;
    }

    /// <summary>Проверяет все записи без дублей в сессии (через _sessionScanned).</summary>
    public async Task ScanAllAsync(List<StartupEntry> entries, CancellationToken ct = default)
    {
        // Только один одновременный проход
        if (!await _scanLock.WaitAsync(0, ct))
        {
            _logger.LogDebug("HashLookup: сканирование уже выполняется, пропускаю");
            return;
        }

        try
        {
            var toScan = entries
                .Where(e => !_sessionScanned.Contains(e.Id))
                .ToList();

            if (toScan.Count == 0) return;

            _logger.LogInformation("HashLookup: начинаю проверку {Count} записей через CIRCL", toScan.Count);

            var ok = 0;
            var noHash = 0;
            foreach (var entry in toScan)
            {
                if (ct.IsCancellationRequested) break;

                // Наш собственный сервис — всегда Green
                if (EntryOperations.IsOwnService(entry))
                {
                    _logger.LogInformation("HashLookup: свой сервис {Name} — Green", entry.Name);
                    entry.KasperskyZone = "Green";
                    entry.KasperskyFileStatus = "Trusted";
                    _sessionScanned.Add(entry.Id);
                    _store.UpdateEntry(entry);
                    ok++;
                    continue;
                }

                var scanned = await ScanEntryAsync(entry, ct);
                if (scanned && !string.IsNullOrEmpty(entry.KasperskyZone))
                    ok++;
                else
                    noHash++;

                _sessionScanned.Add(entry.Id);
            }

            _logger.LogInformation("HashLookup: проверка завершена — проверено {Ok}, без хеша/недоступно {NoHash}", ok, noHash);
        }
        finally
        {
            _scanLock.Release();
        }
    }

    /// <summary>Отправляет GET-запрос к CIRCL lookup по SHA256.</summary>
    /// <returns>Кортеж (zone, detection, status).</returns>
    private async Task<(string zone, string detection, string status)> QueryHashAsync(string sha256, CancellationToken ct)
    {
        var url = $"{LookupUrl}{sha256}";
        try
        {
            using var response = await _http.GetAsync(url, ct);

            // 404 — хеш не найден в базе, считаем чистым
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                // В базе нет — считаем чистым (зелёный)
                return ("Green", "", "NotFound");
            }

            // Другая ошибка HTTP — серая зона
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("HashLookup: {Code} для {Hash}", response.StatusCode, sha256);
                return ("Grey", "", $"HTTP {(int)response.StatusCode}");
            }

            var json = await response.Content.ReadAsStringAsync(ct);
            return ParseResponse(json);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning("HashLookup: запрос не удался для {Hash}: {Msg}", sha256, ex.Message);
            return ("Grey", "", "NetworkError");
        }
    }

    /// <summary>Парсит ответ CIRCL: извлекает Source, db, FileName.</summary>
    private static (string zone, string detection, string status) ParseResponse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // Источник данных о хеше
            var source = root.TryGetProperty("Source", out var src)
                ? src.GetString() ?? ""
                : "";

            var db = root.TryGetProperty("db", out var dbEl)
                ? dbEl.GetString() ?? ""
                : "";

            var fileName = root.TryGetProperty("FileName", out var fn)
                ? fn.GetString() ?? ""
                : "";

            // Хеш найден в базе CIRCL — красный (подробность — источник базы)
            var info = !string.IsNullOrEmpty(source) ? source
                : !string.IsNullOrEmpty(db) ? db
                : fileName;
            return ("Red", info, "Found");
        }
        catch (JsonException)
        {
            return ("Grey", "", "ParseError");
        }
    }
}