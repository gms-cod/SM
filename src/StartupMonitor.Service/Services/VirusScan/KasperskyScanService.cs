using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using StartupMonitor.Shared.Models;
using StartupMonitor.Shared.Diagnostics;

namespace StartupMonitor.Service.Services.VirusScan;

/// <summary>
/// Сервис проверки репутации хешей исполняемых файлов через официальный API Kaspersky OpenTIP.
/// https://opentip.kaspersky.com/
/// </summary>
public class KasperskyScanService
{
    private readonly HttpClient _http;
    private readonly JsonDataStore _store;
    private readonly ILogger<KasperskyScanService> _logger;
    private readonly SemaphoreSlim _scanLock = new(1, 1);
    private readonly HashSet<string> _sessionScanned = [];

    /// <summary>Тестовый SHA256 хэш (системный Windows cmd.exe) для проверки API-токена.</summary>
    private const string TestVerificationHash = "308caeee4c0706a8ab67c5e0aa0ac21926239711c5017561be97e0c9dfbb01f3";

    public KasperskyScanService(JsonDataStore store, ILogger<KasperskyScanService> logger, HttpClient? httpClient = null)
    {
        _store = store;
        _logger = logger;
        _http = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
    }

    /// <summary>
    /// Проверяет подключение к API Kaspersky OpenTIP с переданным токеном.
    /// </summary>
    public async Task<KasperskyTestResult> TestConnectionAsync(string apiKey, string? apiUrl = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return new KasperskyTestResult
            {
                Success = false,
                Message = "API-токен Kaspersky OpenTIP не указан."
            };
        }

        var baseUrl = string.IsNullOrWhiteSpace(apiUrl)
            ? "https://opentip.kaspersky.com/api/v1/search/hash"
            : apiUrl.TrimEnd('/');

        var requestUrl = $"{baseUrl}?request={TestVerificationHash}";

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, requestUrl);
            req.Headers.TryAddWithoutValidation("x-api-key", apiKey.Trim());
            req.Headers.TryAddWithoutValidation("User-Agent", "StartupMonitor/1.0");

            using var resp = await _http.SendAsync(req, ct);

            if (resp.StatusCode == HttpStatusCode.Unauthorized || resp.StatusCode == HttpStatusCode.Forbidden)
            {
                return new KasperskyTestResult
                {
                    Success = false,
                    Message = $"Ошибка авторизации (HTTP {(int)resp.StatusCode}). Проверьте правильность API-токена."
                };
            }

            if (!resp.IsSuccessStatusCode)
            {
                return new KasperskyTestResult
                {
                    Success = false,
                    Message = $"Сервер вернул ошибку: HTTP {(int)resp.StatusCode} {resp.ReasonPhrase}"
                };
            }

            var json = await resp.Content.ReadAsStringAsync(ct);
            var parsed = ParseKasperskyResponse(json);

            return new KasperskyTestResult
            {
                Success = true,
                Message = "Подключение успешно установлено. API-токен действителен.",
                Zone = parsed.Zone,
                Status = parsed.FileStatus
            };
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new KasperskyTestResult
            {
                Success = false,
                Message = "Таймаут ожидания ответа от сервера Kaspersky OpenTIP (10с)."
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "KasperskyScanService: ошибка при тестовом запросе");
            return new KasperskyTestResult
            {
                Success = false,
                Message = $"Ошибка сетевого подключения: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Проверяет отдельную запись автозапуска через Kaspersky OpenTIP.
    /// </summary>
    public async Task<bool> ScanEntryAsync(StartupEntry entry, CancellationToken ct = default)
    {
        using var _prof = ExecutionProfiler.Measure("Service.Kaspersky.ScanEntry");
        // Наш собственный сервис и UI всегда считаем доверенными
        if (EntryOperations.IsOwnService(entry))
        {
            entry.KasperskyZone = "Green";
            entry.KasperskyFileStatus = "Trusted";
            entry.KasperskyDetection = "";
            _store.UpdateEntry(entry);
            return true;
        }

        // Если SHA256 нет — пытаемся вычислить из файла
        if (string.IsNullOrEmpty(entry.SHA256))
        {
            EntryOperations.ResolveAndEnrichEntry(entry);
            if (string.IsNullOrEmpty(entry.SHA256))
            {
                entry.KasperskyZone = "Grey";
                entry.KasperskyFileStatus = "NoHash";
                entry.KasperskyDetection = "";
                _store.UpdateEntry(entry);
                return false;
            }
        }

        var cfg = _store.Load();
        if (!cfg.Settings.KasperskyEnabled)
        {
            _logger.LogDebug("KasperskyScanService: проверка отключена в настройках");
            return false;
        }

        var apiKey = cfg.Settings.KasperskyApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogWarning("KasperskyScanService: API-токен не настроен");
            return false;
        }

        var apiUrl = string.IsNullOrWhiteSpace(cfg.Settings.KasperskyApiUrl)
            ? "https://opentip.kaspersky.com/api/v1/search/hash"
            : cfg.Settings.KasperskyApiUrl.TrimEnd('/');

        try
        {
            var result = await QueryKasperskyHashAsync(entry.SHA256, apiKey, apiUrl, ct);
            if (result != null)
            {
                entry.KasperskyZone = result.Zone;
                entry.KasperskyDetection = result.DetectionName;
                entry.KasperskyFileStatus = result.FileStatus;
                _store.UpdateEntry(entry);
                return true;
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning("KasperskyScanService: таймаут запроса для {Name} ({Hash})", entry.Name, entry.SHA256);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "KasperskyScanService: сбой запроса для {Name} ({Hash})", entry.Name, entry.SHA256);
        }

        return false;
    }

    /// <summary>
    /// Проверяет список записей автозапуска с соблюдением интервалов и предотвращением повторных запросов.
    /// </summary>
    public async Task ScanAllAsync(List<StartupEntry> entries, CancellationToken ct = default)
    {
        using var _profKaspersky = ExecutionProfiler.Profile("Service.KasperskyScan");
        using var _prof = ExecutionProfiler.Measure("Service.Kaspersky.ScanAll");
        if (!await _scanLock.WaitAsync(0, ct))
        {
            _logger.LogDebug("KasperskyScanService: сканирование уже выполняется, пропускаю");
            return;
        }

        try
        {
            var cfg = _store.Load();
            if (!cfg.Settings.KasperskyEnabled || string.IsNullOrWhiteSpace(cfg.Settings.KasperskyApiKey))
            {
                return;
            }

            var toScan = entries
                .Where(e => !_sessionScanned.Contains(e.Id))
                .ToList();

            if (toScan.Count == 0) return;

            _logger.LogInformation("KasperskyScanService: начинаю проверку {Count} записей", toScan.Count);

            var ok = 0;
            var noHash = 0;

            foreach (var entry in toScan)
            {
                if (ct.IsCancellationRequested) break;

                if (EntryOperations.IsOwnService(entry))
                {
                    entry.KasperskyZone = "Green";
                    entry.KasperskyFileStatus = "Trusted";
                    entry.KasperskyDetection = "";
                    _sessionScanned.Add(entry.Id);
                    _store.UpdateEntry(entry);
                    ok++;
                    continue;
                }

                // Если запись уже имеет подлинный статус Kaspersky (Clean, Malware, Adware, Grey/NotFound), не тратим квоту повторно.
                // Записи со связкой Green + NotFound — это старый артефакт CIRCL, их перепроверяем.
                var isOldCirclArtifact = entry.KasperskyZone == "Green" && entry.KasperskyFileStatus == "NotFound";
                if (!string.IsNullOrEmpty(entry.KasperskyZone) && !string.IsNullOrEmpty(entry.SHA256) && entry.KasperskyFileStatus != "NoHash" && !isOldCirclArtifact)
                {
                    _sessionScanned.Add(entry.Id);
                    ok++;
                    continue;
                }

                var scanned = await ScanEntryAsync(entry, ct);
                if (scanned && !string.IsNullOrEmpty(entry.KasperskyZone))
                    ok++;
                else
                    noHash++;

                _sessionScanned.Add(entry.Id);

                // Пауза 350 мс между сетевыми запросами для соблюдения квот API
                await Task.Delay(350, ct);
            }

            _logger.LogInformation("KasperskyScanService: проверка завершена — проверено {Ok}, без хеша/ошибок {NoHash}", ok, noHash);
        }
        finally
        {
            _scanLock.Release();
        }
    }

    /// <summary>
    /// Выполняет GET-запрос к Kaspersky OpenTIP API по SHA256.
    /// </summary>
    private async Task<KasperskyResult?> QueryKasperskyHashAsync(string sha256, string apiKey, string apiUrl, CancellationToken ct)
    {
        var requestUrl = $"{apiUrl}?request={sha256}";

        using var req = new HttpRequestMessage(HttpMethod.Get, requestUrl);
        req.Headers.TryAddWithoutValidation("x-api-key", apiKey.Trim());
        req.Headers.TryAddWithoutValidation("User-Agent", "StartupMonitor/1.0");

        using var resp = await _http.SendAsync(req, ct);

        // 404 — хэш неизвестен Касперскому (файл не встречался / не категоризирован)
        if (resp.StatusCode == HttpStatusCode.NotFound)
        {
            return new KasperskyResult
            {
                Zone = "Grey",
                FileStatus = "NotFound",
                DetectionName = "",
                IsFound = false
            };
        }

        if (resp.StatusCode == HttpStatusCode.Unauthorized || resp.StatusCode == HttpStatusCode.Forbidden)
        {
            _logger.LogWarning("KasperskyScanService: ошибка авторизации API (HTTP {Code})", (int)resp.StatusCode);
            return null;
        }

        if (!resp.IsSuccessStatusCode)
        {
            _logger.LogWarning("KasperskyScanService: HTTP {Code} для хеша {Hash}", (int)resp.StatusCode, sha256);
            return null;
        }

        var json = await resp.Content.ReadAsStringAsync(ct);
        return ParseKasperskyResponse(json);
    }

    /// <summary>
    /// Парсит JSON-ответ Kaspersky OpenTIP.
    /// </summary>
    public static KasperskyResult ParseKasperskyResponse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var zone = root.TryGetProperty("Zone", out var zoneEl)
                ? zoneEl.GetString() ?? "Grey"
                : "Grey";

            var fileStatus = "";
            int? hitsCount = null;
            string? signer = null;

            if (root.TryGetProperty("FileGeneralInfo", out var genInfo) && genInfo.ValueKind == JsonValueKind.Object)
            {
                if (genInfo.TryGetProperty("FileStatus", out var fsEl))
                    fileStatus = fsEl.GetString() ?? "";

                if (genInfo.TryGetProperty("HitsCount", out var hitsEl) && hitsEl.TryGetInt32(out var h))
                    hitsCount = h;

                if (genInfo.TryGetProperty("Signer", out var signerEl))
                    signer = signerEl.GetString();
            }

            var detectionName = "";
            string? descUrl = null;

            if (root.TryGetProperty("DetectionsInfo", out var dets) && dets.ValueKind == JsonValueKind.Array)
            {
                // Ищем первое обнаружение из Red зоны или первое любое
                JsonElement? chosenDet = null;
                foreach (var det in dets.EnumerateArray())
                {
                    if (det.TryGetProperty("Zone", out var dZone) && dZone.GetString() == "Red")
                    {
                        chosenDet = det;
                        break;
                    }
                    chosenDet ??= det;
                }

                if (chosenDet.HasValue)
                {
                    if (chosenDet.Value.TryGetProperty("DetectionName", out var detNameEl))
                        detectionName = detNameEl.GetString() ?? "";

                    if (chosenDet.Value.TryGetProperty("DescriptionUrl", out var urlEl))
                        descUrl = urlEl.GetString();
                }
            }

            // Если статус пуст, но зона есть
            if (string.IsNullOrWhiteSpace(fileStatus))
            {
                fileStatus = zone switch
                {
                    "Green" => "Clean",
                    "Red" => "Malware",
                    "Yellow" => "Adware",
                    _ => "Not categorized"
                };
            }

            return new KasperskyResult
            {
                Zone = zone,
                DetectionName = detectionName,
                FileStatus = fileStatus,
                HitsCount = hitsCount,
                Signer = signer,
                DescriptionUrl = descUrl,
                IsFound = true
            };
        }
        catch (JsonException)
        {
            return new KasperskyResult
            {
                Zone = "Grey",
                FileStatus = "ParseError",
                IsFound = false
            };
        }
    }
}
