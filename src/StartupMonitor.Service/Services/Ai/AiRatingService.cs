using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using StartupMonitor.Shared.Models;
using StartupMonitor.Shared.Diagnostics;

namespace StartupMonitor.Service.Services.Ai;

public class AiRatingService : IDisposable
{
    private readonly HttpClient _http;
    private readonly RatingsCache _cache;
    private readonly ILogger<AiRatingService> _logger;
    // Язык интерфейса (settings.json → language): на нём строятся промпт и ответ Groq
    private string _uiLang;
    private string _apiKey;
    // Окно остывания при повторных 429: не спамим API, ждём восстановления лимита
    private DateTime _cooldownUntil = DateTime.MinValue;

    private static readonly SemaphoreSlim _prefetchLock = new(1, 1);
    private string _apiUrl = "https://api.groq.com/openai/v1/chat/completions";
    private string _model = "openai/gpt-oss-120b";
    private const int CooldownMinutes = 5;
    private static readonly Random _jitter = new();
    private static readonly string WindowsVersion = RuntimeInformation.OSDescription;

    public AiRatingService(RatingsCache cache, ILogger<AiRatingService> logger, string apiKey, string uiLang = "ru",
        string? apiUrl = null, string? model = null)
    {
        _http = new HttpClient();
        // Каноничный User-Agent для исключения блокировок Cloudflare/Groq WAF (403 Forbidden)
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("StartupMonitor/2.0");
        _cache = cache;
        _logger = logger;
        _apiKey = apiKey;
        _uiLang = string.IsNullOrWhiteSpace(uiLang) ? "ru" : uiLang;
        if (!string.IsNullOrWhiteSpace(apiUrl)) _apiUrl = apiUrl.Trim();
        if (!string.IsNullOrWhiteSpace(model)) _model = model.Trim();
    }

    /// <summary>UpdateUiLang: Обновляет состояние UiLang.</summary>
    /// <param name="uiLang">Входные данные: значение параметра uiLang (string).</param>
    public void UpdateUiLang(string uiLang)
    {
        if (!string.IsNullOrWhiteSpace(uiLang) && uiLang != _uiLang)
            _uiLang = uiLang;
    }

    /// <summary>UpdateApiKey: Обновляет состояние ApiKey.</summary>
    /// <param name="apiKey">Входные данные: значение параметра apiKey (string).</param>
    public void UpdateApiKey(string apiKey) => _apiKey = apiKey;

    /// <summary>UpdateConfig: Обновляет состояние Config.</summary>
    /// <param name="apiKey">Входные данные: значение параметра apiKey (string).</param>
    /// <param name="apiUrl">Входные данные: значение параметра apiUrl (string?).</param>
    /// <param name="model">Входные данные: значение параметра model (string?).</param>
    public void UpdateConfig(string apiKey, string? apiUrl = null, string? model = null)
    {
        _apiKey = apiKey;
        if (!string.IsNullOrWhiteSpace(apiUrl)) _apiUrl = apiUrl.Trim();
        if (!string.IsNullOrWhiteSpace(model)) _model = model.Trim();
    }

    /// <summary>IsLocalUrl: Проверяет состояние IsLocalUrl.</summary>
    /// <param name="url">Входные данные: значение параметра url (string?).</param>
    /// <returns>Выходные данные: результирующее значение типа bool.</returns>
    private static bool IsLocalUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        return url.Contains("localhost", StringComparison.OrdinalIgnoreCase) ||
               url.Contains("127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
               url.Contains("0.0.0.0", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>GetCached: Возвращает Cached.</summary>
    /// <param name="entry)">Входные данные: значение параметра entry) (StartupEntry).</param>
    /// <returns>Выходные данные: результирующее значение типа StartupRating?.</returns>
    public StartupRating? GetCached(StartupEntry entry) => _cache.Get(entry);

    /// <summary>PrefetchAsync: Выполняет операцию PrefetchAsync.</summary>
    /// <param name="entry">Входные данные: значение параметра entry (StartupEntry).</param>
    public async Task PrefetchAsync(StartupEntry entry)
    {
        if (entry.AiRating > 0) return;
        if (_cache.Get(entry) != null) return;
        if (string.IsNullOrWhiteSpace(_apiKey) && !IsLocalUrl(_apiUrl)) return;

        var rating = await FetchRatingAsync(entry);
        if (rating != null)
            _cache.Set(entry, rating);
    }

    /// <summary>
    /// Принудительно запрашивает свежий AI-рейтинг для записи, сбрасывая сетевой предохранитель Circuit Breaker.
    /// </summary>
    /// <param name="entry">Оцениваемая запись автозапуска.</param>
    /// <returns>Полученный рейтинг или null в случае ошибки.</returns>
    public async Task<StartupRating?> ForceFetchRatingAsync(StartupEntry entry)
    {
        // Немедленно сбрасываем Circuit Breaker при ручном запросе пользователя
        _networkCooldownUntil = DateTime.MinValue;
        _consecutiveNetworkErrors = 0;

        _logger.LogInformation("ForceFetchRatingAsync: немедленный запрос к AI для {Name} ({Model})", entry.Name, _model);
        var rating = await FetchRatingAsync(entry);
        if (rating != null)
        {
            _cache.Set(entry, rating);
            _logger.LogInformation("ForceFetchRatingAsync: успешно получен рейтинг {Score} для {Name}", rating.Score, entry.Name);
        }
        else
        {
            _logger.LogWarning("ForceFetchRatingAsync: AI не вернул рейтинг для {Name}", entry.Name);
        }

        return rating;
    }

    public async Task<(bool Success, string Message)> TestConnectionAsync(string? apiKey, string? apiUrl, string? model)
    {
        var url = string.IsNullOrWhiteSpace(apiUrl) ? _apiUrl : apiUrl.Trim();
        var mdl = string.IsNullOrWhiteSpace(model) ? _model : model.Trim();
        var key = apiKey?.Trim() ?? _apiKey;

        if (string.IsNullOrWhiteSpace(key) && !IsLocalUrl(url))
        {
            return (false, "API-ключ не указан");
        }

        try
        {
            var body = new
            {
                model = mdl,
                messages = new[] { new { role = "user", content = "Respond with JSON: {\"status\": \"ok\"}" } },
                temperature = 0.1,
                max_tokens = 400
            };
            var jsonBody = JsonSerializer.Serialize(body);
            using var content = new StringContent(jsonBody, System.Text.Encoding.UTF8, "application/json");
            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            if (!string.IsNullOrWhiteSpace(key))
                request.Headers.Add("Authorization", $"Bearer {key}");
            else if (IsLocalUrl(url))
                request.Headers.Add("Authorization", "Bearer ollama");

            request.Content = content;
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var response = await _http.SendAsync(request, cts.Token);

            if (response.IsSuccessStatusCode)
            {
                return (true, $"Соединение успешно! Модель '{mdl}' активна.");
            }

            var errBody = await response.Content.ReadAsStringAsync();
            if ((int)response.StatusCode == 401 || (int)response.StatusCode == 403)
                return (false, $"Ошибка авторизации ({response.StatusCode}): неверный API-ключ");
            if ((int)response.StatusCode == 404)
                return (false, $"Эндпоинт или модель '{mdl}' не найдены (404)");
            if ((int)response.StatusCode == 429)
                return (false, "Превышен лимит запросов API (429 Rate Limit)");

            return (false, $"Ошибка сервера {(int)response.StatusCode}: {errBody}");
        }
        catch (TaskCanceledException)
        {
            return (false, "Превышено время ожидания ответа (таймаут 20 сек)");
        }
        catch (HttpRequestException ex)
        {
            return (false, $"Сетевая ошибка: {ex.Message}");
        }
        catch (Exception ex)
        {
            return (false, $"Ошибка: {ex.Message}");
        }
    }

    /// <summary>PrefetchAllAsync: Выполняет операцию PrefetchAllAsync.</summary>
    /// <param name="entries">Входные данные: значение параметра entries (List<StartupEntry>).</param>
    public async Task PrefetchAllAsync(List<StartupEntry> entries)
    {
        using var _prof = ExecutionProfiler.Measure("Service.Ai.PrefetchAll");
        if (!await _prefetchLock.WaitAsync(0)) return;

        try
        {
            // SHA256-кеш: копируем рейтинг на записи с таким же хешем
            var byHash = entries.Where(e => !string.IsNullOrEmpty(e.SHA256))
                .GroupBy(e => e.SHA256, StringComparer.OrdinalIgnoreCase);
            foreach (var grp in byHash)
            {
                var rated = grp.FirstOrDefault(e => _cache.Get(e) != null);
                if (rated == null) continue;
                var src = _cache.Get(rated);
                if (src == null) continue;
                foreach (var e in grp.Where(e => _cache.Get(e) == null))
                    _cache.Set(e, new StartupRating
                    {
                        Score = src.Score,
                        Category = src.Category,
                        Risk = src.Risk,
                        Recommendation = src.Recommendation
                    });
            }

            // Рейтинг: если ключ не задан и эндпоинт не локальный — пропускаем
            if (string.IsNullOrWhiteSpace(_apiKey) && !IsLocalUrl(_apiUrl))
            {
                _logger.LogDebug("AI-рейтинг отключён: не задан API-ключ");
                return;
            }

            var unrated = entries.Where(e => _cache.Get(e) == null).ToList();
            if (unrated.Count == 0)
            {
                _logger.LogInformation("AI-рейтинг: все {Count} записей оценены", entries.Count);
                return;
            }

            _logger.LogInformation("AI API ({Url}): оцениваю {Count}/{Total} записей через {Model}",
                _apiUrl, unrated.Count, entries.Count, _model);

            var processed = 0;
            var errors = 0;
            foreach (var entry in unrated)
            {
                if (_cache.Get(entry) != null) continue;
                // Окно остывания после повторных 429: ждём восстановления лимита
                if (DateTime.UtcNow < _cooldownUntil)
                {
                    var wait = (int)(_cooldownUntil - DateTime.UtcNow).TotalMilliseconds + 1000;
                    _logger.LogWarning("AI API: пауза из-за rate limit, жду {Wait}ms", wait);
                    await Task.Delay(wait);
                }
                var rating = await FetchRatingAsync(entry);
                if (rating != null)
                {
                    _cache.Set(entry, rating);
                    processed++;
                }
                else
                {
                    errors++;
                }
                if ((processed + errors) % 10 == 0)
                {
                    _logger.LogInformation("AI: обработано {Done}/{Total} (успешно {Ok}, ошибок {Err})",
                        processed + errors, unrated.Count, processed, errors);
                }
            }

            _logger.LogInformation("AI API: оценено {Ok}, ошибок {Err}", processed, errors);
        }
        finally
        {
            _prefetchLock.Release();
        }
    }

    // Предохранитель сетевых сбоев (Circuit Breaker): блокирует вызовы при отсутствии связи с внешним миром
    private DateTime _networkCooldownUntil = DateTime.MinValue;
    private int _consecutiveNetworkErrors;
    private const int NetworkCooldownMinutes = 5;
    private const int NetworkErrorThreshold = 2;

    /// <summary>
    /// Проверяет, является ли исключение сетевым сбоем сокета или DNS независимо от системной локализации Windows.
    /// </summary>
    private static bool IsNetworkConnectionError(Exception ex)
    {
        if (ex is HttpRequestException httpEx)
        {
            if (httpEx.HttpRequestError is HttpRequestError.NameResolutionError
                or HttpRequestError.ConnectionError
)
            {
                return true;
            }
        }

        var cur = ex;
        while (cur != null)
        {
            if (cur is System.Net.Sockets.SocketException)
                return true;
            cur = cur.InnerException;
        }

        return false;
    }

    /// <summary>FetchRatingAsync: Выполняет операцию FetchRatingAsync с поддержкой Circuit Breaker.</summary>
    /// <param name="entry">Входные данные: значение параметра entry (StartupEntry).</param>
    /// <returns>Выходные данные: результирующее значение типа Task<StartupRating?>.</returns>
    /// <summary>
    /// Проверяет, является ли запись компонентом нашего собственного проекта StartupMonitor.
    /// </summary>
    private static bool IsSelfProjectEntry(StartupEntry entry)
    {
        var name = entry.Name ?? "";
        var path = entry.FilePath ?? "";
        var pub = entry.Publisher ?? "";
        var cmd = entry.CommandLine ?? "";

        return name.Contains("StartupMonitor", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Startup Monitor", StringComparison.OrdinalIgnoreCase)
            || path.Contains("StartupMonitor", StringComparison.OrdinalIgnoreCase)
            || pub.Contains("StartupMonitor", StringComparison.OrdinalIgnoreCase)
            || cmd.Contains("StartupMonitor", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<StartupRating?> FetchRatingAsync(StartupEntry entry)
    {
        using var _prof = ExecutionProfiler.Measure("Service.Ai.FetchRating");

        // Собственные файлы проекта StartupMonitor всегда получают высшую оценку 5 звёзд (Score=1)
        if (IsSelfProjectEntry(entry))
        {
            var isRu = _uiLang == "ru";
            return new StartupRating
            {
                Score = 1,
                Category = isRu ? "Безопасность и мониторинг" : "Security & Monitoring",
                Risk = isRu
                    ? "Абсолютно доверенное ПО: официальный компонент защитного комплекса Startup Monitor. Обеспечивает непрерывный мониторинг реестра, контроль автозагрузки, сетевой экран и защиту от вредоносных модулей."
                    : "Fully trusted software: official Startup Monitor component. Provides continuous registry monitoring, startup security, firewall filtering, and protection against unauthorized persistence.",
                Recommendation = isRu
                    ? "Разрешить автозапуск. Критически важный компонент для мониторинга системы и предотвращения угроз."
                    : "Allow startup. Vital component for system defense and persistence monitoring."
            };
        }

        // Circuit Breaker: если сеть недоступна, не пытаемся слать запросы
        if (DateTime.UtcNow < _networkCooldownUntil)
        {
            _logger.LogWarning("Groq API: запрос отклонён Circuit Breaker (сеть недоступна до {Until} UTC)", _networkCooldownUntil);
            return null;
        }

        var waitedFor429 = false;
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            try
            {
                var prompt = BuildPrompt(entry);
                var body = new
                {
                    model = _model,
                    messages = new[] { new { role = "user", content = prompt } },
                    temperature = 0.1,
                    max_tokens = 2000,
                    response_format = new { type = "json_object" }
                };
                var jsonBody = JsonSerializer.Serialize(body);
                var content = new StringContent(jsonBody, System.Text.Encoding.UTF8, "application/json");

                using var request = new HttpRequestMessage(HttpMethod.Post, _apiUrl);
                if (!string.IsNullOrWhiteSpace(_apiKey))
                    request.Headers.Add("Authorization", $"Bearer {_apiKey.Trim()}");
                else if (IsLocalUrl(_apiUrl))
                    request.Headers.Add("Authorization", "Bearer ollama");

                request.Content = content;

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                var response = await _http.SendAsync(request, cts.Token);

                if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                {
                    var retryAfter = response.Headers.RetryAfter?.Delta?.TotalMilliseconds;
                    // Не ждём Retry-After больше 2 минут — запись останется неоценённой
                    // и подхватится следующим проходом (иначе цикл виснет на 10-15 минут)
                    var delayMs = retryAfter.HasValue ? Math.Clamp((long)retryAfter.Value, 1000L, 120_000L) : 60_000L;
                    var delay = (int)delayMs;
                    if (waitedFor429)
                    {
                        _cooldownUntil = DateTime.UtcNow.AddMinutes(CooldownMinutes);
                        _logger.LogWarning("Groq API 429 для {Name}, повторный — пропускаю, остывание {Min} мин",
                            entry.Name, CooldownMinutes);
                        return null;
                    }
                    waitedFor429 = true;
                    _logger.LogWarning("Groq API 429 для {Name}, жду {Delay}ms", entry.Name, delay);
                    await Task.Delay(delay);
                    continue;
                }

                if (!response.IsSuccessStatusCode)
                {
                    var err = await response.Content.ReadAsStringAsync();
                    if ((int)response.StatusCode is 400 or 401 or 403)
                    {
                        _logger.LogWarning("Groq API {Code} для {Name}: {Error} — ретрай отключён",
                            response.StatusCode, entry.Name, err);
                        return null;
                    }
                    if (attempt < 5)
                    {
                        _logger.LogWarning("Groq API {Code} для {Name}, попытка {Attempt}: {Error}",
                            response.StatusCode, entry.Name, attempt, err);
                        await Task.Delay(2000);
                        continue;
                    }
                    _logger.LogWarning("Groq API {Code} для {Name}: {Error}",
                        response.StatusCode, entry.Name, err);
                    return null;
                }

                var json = await response.Content.ReadAsStringAsync();
                _consecutiveNetworkErrors = 0;
                return ParseResponse(json, entry.Name);
            }
            catch (Exception ex) when (IsNetworkConnectionError(ex))
            {
                var errors = Interlocked.Increment(ref _consecutiveNetworkErrors);
                _logger.LogWarning("Groq API: сетевой сбой подключения ({Attempt}/5): {Message}", attempt, ex.Message);

                if (errors >= NetworkErrorThreshold)
                {
                    _networkCooldownUntil = DateTime.UtcNow.AddMinutes(NetworkCooldownMinutes);
                    _logger.LogWarning("Groq API: зафиксировано {Errors} сетевых ошибок подряд. Включён Circuit Breaker на {Minutes} мин.",
                        errors, NetworkCooldownMinutes);
                    return null;
                }

                if (attempt < 5)
                {
                    await Task.Delay(2000);
                    continue;
                }
                return null;
            }
            catch (TaskCanceledException)
            {
                _logger.LogWarning("Groq API таймаут для {Name}, попытка {Attempt}", entry.Name, attempt);
                if (attempt < 5)
                {
                    await Task.Delay(2000);
                    continue;
                }
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Ошибка Groq API для {Name}, попытка {Attempt}", entry.Name, attempt);
                if (attempt < 5)
                {
                    await Task.Delay(2000);
                    continue;
                }
                return null;
            }

        }

        return null;
    }

    /// <summary>BuildPrompt: Выполняет операцию BuildPrompt.</summary>
    /// <param name="entry">Входные данные: значение параметра entry (StartupEntry).</param>
    /// <returns>Выходные данные: результирующее значение типа string.</returns>
    internal string BuildPrompt(StartupEntry entry)
    {
        FileInfo? fileInfo = null;
        FileVersionInfo? fv = null;
        try
        {
            if (!string.IsNullOrEmpty(entry.FilePath) && !entry.FilePath.StartsWith(@"\\") && File.Exists(entry.FilePath))
            {
                fileInfo = new FileInfo(entry.FilePath);
                fv = FileVersionInfo.GetVersionInfo(entry.FilePath);
            }
        }
        catch { }

        // Язык ответа — язык интерфейса пользователя ("ru" или "en")
        var isRu = _uiLang == "ru";
        var programLang = string.IsNullOrWhiteSpace(entry.Language) ? (isRu ? "Н/Д" : "N/A") : entry.Language;

        var cmdLine = !string.IsNullOrWhiteSpace(entry.CommandLine)
            ? entry.CommandLine.Trim()
            : (!string.IsNullOrWhiteSpace(entry.FilePath) ? entry.FilePath.Trim() : (isRu ? "Н/Д" : "N/A"));

        var filePath = !string.IsNullOrWhiteSpace(entry.FilePath)
            ? entry.FilePath.Trim()
            : (isRu ? "Н/Д" : "N/A");

        var locationDetail = !string.IsNullOrWhiteSpace(entry.LocationDetail)
            ? entry.LocationDetail.Trim()
            : (isRu ? "Н/Д" : "N/A");

        var company = fv?.CompanyName?.Trim();
        var publisher = !string.IsNullOrWhiteSpace(entry.Publisher)
            ? entry.Publisher.Trim()
            : (!string.IsNullOrWhiteSpace(company) ? company : (isRu ? "Н/Д" : "N/A"));

        var productName = !string.IsNullOrWhiteSpace(fv?.ProductName) ? fv.ProductName.Trim() : (isRu ? "Н/Д" : "N/A");
        var fileDesc = !string.IsNullOrWhiteSpace(fv?.FileDescription) ? fv.FileDescription.Trim() : (isRu ? "Н/Д" : "N/A");
        var fileVer = !string.IsNullOrWhiteSpace(fv?.FileVersion) ? fv.FileVersion.Trim() : (isRu ? "Н/Д" : "N/A");
        var prodVer = !string.IsNullOrWhiteSpace(fv?.ProductVersion) ? fv.ProductVersion.Trim() : (isRu ? "Н/Д" : "N/A");
        var copyright = !string.IsNullOrWhiteSpace(fv?.LegalCopyright) ? fv.LegalCopyright.Trim() : (isRu ? "Н/Д" : "N/A");

        var sha256 = !string.IsNullOrWhiteSpace(entry.SHA256) ? entry.SHA256.Trim() : (isRu ? "Н/Д" : "N/A");

        var isSignedStrRu = entry.SignatureStatus switch
        {
            DigitalSignatureStatus.Valid => $"Да (Подлинная цифровая подпись Authenticode: {entry.SignerName ?? entry.Publisher}, УЦ: {entry.SignatureIssuer ?? "Доверенный корень Windows"})",
            DigitalSignatureStatus.InvalidSignature => "КРИТИЧЕСКАЯ УГРОЗА: Цифровая подпись нарушена / файл модифицирован (Tampered binary)!",
            DigitalSignatureStatus.UntrustedRoot => $"Самоподписанный / Недоверенный корневой центр: {entry.SignerName ?? "Неизвестен"}",
            DigitalSignatureStatus.Expired => $"Истёк срок действия сертификата: {entry.SignerName}",
            DigitalSignatureStatus.Revoked => $"ВНИМАНИЕ: Сертификат отозван издателем: {entry.SignerName}",
            DigitalSignatureStatus.ExplicitDistrust => "КРИТИЧЕСКИ: Сертификат в чёрном списке Windows (Explicitly Distrusted)!",
            _ => entry.IsSigned
                ? $"Да (Издатель: {entry.Publisher})"
                : (!string.IsNullOrEmpty(company) ? $"Нет подписи (В PE-ресурсах указана компания: {company})" : "Нет подписи / неподписан")
        };

        var isSignedStrEn = entry.SignatureStatus switch
        {
            DigitalSignatureStatus.Valid => $"Yes (Valid Authenticode signature: {entry.SignerName ?? entry.Publisher}, CA: {entry.SignatureIssuer ?? "Windows Trusted Root"})",
            DigitalSignatureStatus.InvalidSignature => "CRITICAL THREAT: Digital signature is invalid / file has been tampered with!",
            DigitalSignatureStatus.UntrustedRoot => $"Self-signed or Untrusted Root CA: {entry.SignerName ?? "Unknown"}",
            DigitalSignatureStatus.Expired => $"Certificate has expired: {entry.SignerName}",
            DigitalSignatureStatus.Revoked => $"WARNING: Certificate revoked by CA: {entry.SignerName}",
            DigitalSignatureStatus.ExplicitDistrust => "CRITICAL: Certificate explicitly untrusted in Windows!",
            _ => entry.IsSigned
                ? $"Yes (Publisher: {entry.Publisher})"
                : (!string.IsNullOrEmpty(company) ? $"Unsigned (PE header claims company: {company})" : "No signature / Unsigned")
        };

        var fileSizeRu = fileInfo != null
            ? $"{fileInfo.Length} байт ({fileInfo.Length / 1024.0:F1} КБ)"
            : "Файл не найден на диске";

        var fileSizeEn = fileInfo != null
            ? $"{fileInfo.Length} bytes ({fileInfo.Length / 1024.0:F1} KB)"
            : "File not found on disk";

        var statusRu = entry.Status switch
        {
            EntryStatus.Enabled => "Включён (активен при старте системы)",
            EntryStatus.Disabled => "Отключён пользователем",
            EntryStatus.Blocked => "Заблокирован",
            EntryStatus.Delayed => $"Отложенный запуск ({entry.DelaySeconds} сек)",
            _ => entry.Status.ToString()
        };

        var statusEn = entry.Status switch
        {
            EntryStatus.Enabled => "Enabled (active at system startup)",
            EntryStatus.Disabled => "Disabled by user",
            EntryStatus.Blocked => "Blocked",
            EntryStatus.Delayed => $"Delayed startup ({entry.DelaySeconds}s)",
            _ => entry.Status.ToString()
        };

        var locationRu = entry.Location switch
        {
            EntryLocation.RegistryRun => "Реестр (Run)",
            EntryLocation.RegistryRunOnce => "Реестр (RunOnce)",
            EntryLocation.StartupFolder => "Папка автозагрузки (Startup Folder)",
            EntryLocation.Service => "Служба Windows (Service)",
            EntryLocation.TaskScheduler => "Планировщик заданий (TaskScheduler)",
            EntryLocation.Winlogon => "Раздел Winlogon",
            EntryLocation.RegistryPolicies => "Групповая политика (Explorer Run)",
            EntryLocation.ActiveSetup => "Active Setup",
            EntryLocation.ShellExtension => "Расширение оболочки (ShellExtension)",
            EntryLocation.BrowserHelperObject => "Browser Helper Object",
            EntryLocation.AppInitDll => "AppInit DLL",
            EntryLocation.BootExecute => "BootExecute / Драйвер",
            EntryLocation.ImageFileExecutionOptions => "Перехват отладчика (IFEO)",
            _ => entry.Location.ToString()
        };

        var threatBlockRu = "";
        var threatBlockEn = "";
        if (entry.ThreatInfo != null && entry.ThreatInfo.HasThreat)
        {
            var indicators = entry.ThreatInfo.Indicators != null && entry.ThreatInfo.Indicators.Count > 0
                ? string.Join("; ", entry.ThreatInfo.Indicators)
                : (isRu ? "Н/Д" : "N/A");

            threatBlockRu = $@"
- ВНИМАНИЕ (Эвристика командной строки):
  * Уровень угрозы: {entry.ThreatInfo.ThreatLevel}
  * Обнаружение: {entry.ThreatInfo.BadgeText}
  * Подозрительные признаки: {entry.ThreatInfo.Description}
  * Индикаторы: {indicators}";

            threatBlockEn = $@"
- WARNING (Command Line Threat Heuristics):
  * Threat Level: {entry.ThreatInfo.ThreatLevel}
  * Detection: {entry.ThreatInfo.BadgeText}
  * Suspicious details: {entry.ThreatInfo.Description}
  * Indicators: {indicators}";
        }

        var repBlockRu = "";
        var repBlockEn = "";
        if (!string.IsNullOrWhiteSpace(entry.KasperskyZone) && entry.KasperskyZone != "Grey" && entry.KasperskyZone != "NotFound")
        {
            repBlockRu = $@"
- Репутация Kaspersky OpenTIP:
  * Зона: {entry.KasperskyZone}
  * Детект: {entry.KasperskyDetection}
  * Статус файла: {entry.KasperskyFileStatus}";

            repBlockEn = $@"
- Kaspersky OpenTIP Reputation:
  * Zone: {entry.KasperskyZone}
  * Detection: {entry.KasperskyDetection}
  * File Status: {entry.KasperskyFileStatus}";
        }

        var rawBlockRu = "";
        var rawBlockEn = "";
        if (entry.RawData?.Count > 0)
        {
            var rawItems = entry.RawData
                .Where(kv => !string.IsNullOrWhiteSpace(kv.Key) && !string.IsNullOrWhiteSpace(kv.Value) && kv.Key != "CommandLine" && kv.Key != "FilePath")
                .Take(5)
                .Select(kv => $"{kv.Key}: {kv.Value}")
                .ToList();
            if (rawItems.Count > 0)
            {
                rawBlockRu = $"\n- Дополнительные параметры: {string.Join("; ", rawItems)}";
                rawBlockEn = $"\n- Additional parameters: {string.Join("; ", rawItems)}";
            }
        }

        if (isRu)
        {
            return $@"Ты — ведущий эксперт по информационной безопасности и системному администрированию Windows.
Проанализируй запись автозапуска Windows со всеми предоставленными параметрами.
Твоя цель: подробно объяснить пользователю понятным языком, ЧТО ЭТО ЗА ПРОГРАММА ИЛИ СЛУЖБА, каково её назначение, что делают параметры командной строки, нужна ли она при старте Windows, и оценить степень безопасности.

Отвечай СТРОГО НА РУССКОМ ЯЗЫКЕ!

ОС: {WindowsVersion}
Параметры элемента автозапуска:
- Имя элемента: {entry.Name}
- Командная строка: {cmdLine}
- Путь к исполняемому файлу: {filePath}
- Точка автозапуска: {locationRu} ({locationDetail})
- Текущий статус: {statusRu}
- Название продукта / программы: {productName}
- Описание файла: {fileDesc}
- Компания / Издатель: {publisher}
- Цифровая подпись: {isSignedStrRu}
- Версия: {fileVer} (версия продукта: {prodVer})
- Авторские права: {copyright}
- Размер файла: {fileSizeRu}
- Хеш SHA256: {sha256}
- Язык ресурсов программы: {programLang}{threatBlockRu}{repBlockRu}{rawBlockRu}

Инструкция по анализу (составь ответ в поле ""risk""):
1. ЧТО ЭТО ЗА ЭЛЕМЕНТ (обязательно): Чётко укажи, к какому приложению, разработчику или системной службе Windows относится данный файл или команда, и какую функцию он выполняет в операционной системе.
2. РАЗБОР КОМАНДНОЙ СТРОКИ (обязательно): Если в командной строке есть ключи, аргументы или пути (например --autostart, -minimized, /background, флаги скриптов), подробно объясни, что каждый из них делает при старте. Если аргументов нет — укажи это.
3. НЕОБХОДИМОСТЬ В АВТОЗАПУСКЕ (обязательно): Оцени, обязательно ли этой программе стартовать вместе с Windows (критично для системы/работы), либо её можно безболезненно отключить в автозагрузке для ускорения старта ПК, запуская вручную при необходимости.
4. ВЕРДИКТ БЕЗОПАСНОСТИ: Легитимность программы, типичность расположения, подлинность подписи, отсутствие признаков вредоносной активности, телеметрии, майнера или трояна.

Шкала оценок (score 1-10):
- 1-3 (Безопасно / allow): Системный компонент Windows, драйвер, легитимная утилита или доверенное ПО с действительной подписью.
- 4-6 (Внимание / review): Автообновление, телеметрия, оптимизатор, рекламное ПО, не критичное ПО, замедляющее запуск, или неподписанный файл.
- 7-10 (Опасно / block): Вредоносная программа, троян, подозрительный скрипт, обфускация, маскировка под системный файл.

Категории (category): Системный компонент | Полезная утилита | Стороннее ПО | Фоновое автообновление | Телеметрия | Подозрительное ПО | Вредоносная программа

Ответь ИСКЛЮЧИТЕЛЬНО валидным JSON-объектом без лишних символов и без markdown-блоков:
{{""score"": <целое число 1-10>, ""category"": ""<категория на русском языке>"", ""risk"": ""<подробное и понятное человеку объяснение по всем 4 пунктам: что это за программа, что делают аргументы, нужна ли в автозапуске, вердикт безопасности. 4-8 предложений на русском языке>"", ""recommendation"": ""<чёткая рекомендация: Разрешить автозапуск / Рекомендуется отключить для ускорения ПК / Заблокировать и удалить, с кратким пояснением на русском языке>""}}";
        }

        return $@"You are a leading Windows cybersecurity and system administration expert.
Analyze this Windows startup entry with all provided parameters.
Your goal is to clearly explain to the user WHAT THIS PROGRAM OR SERVICE IS, what its purpose is, what its command-line launch arguments do, whether it is needed at Windows boot, and evaluate its safety.

Respond STRICTLY IN ENGLISH!

OS: {WindowsVersion}
Startup Entry Parameters:
- Entry Name: {entry.Name}
- Launch Command Line: {cmdLine}
- Executable File Path: {filePath}
- Startup Location: {entry.Location} ({locationDetail})
- Current Status: {statusEn}
- Product / Software Suite: {productName}
- File Description: {fileDesc}
- Company / Publisher: {publisher}
- Digital Signature: {isSignedStrEn}
- File Version: {fileVer} (Product Version: {prodVer})
- Copyright: {copyright}
- File Size: {fileSizeEn}
- SHA256 Hash: {sha256}
- Program Language: {programLang}{threatBlockEn}{repBlockEn}{rawBlockEn}

Analysis Guidelines (construct your answer in the ""risk"" field):
1. WHAT THIS ELEMENT IS (mandatory): Clearly explain which application, vendor, or Windows system component this file or command belongs to, and what function it performs in the operating system.
2. COMMAND LINE ANALYSIS (mandatory): If the command line contains flags, parameters, or switches (e.g. --autostart, -minimized, /background, script flags), explain what each of them does at boot. If no arguments are present, state so.
3. BOOT NECESSITY (mandatory): Evaluate whether this program is essential at Windows boot (critical for system/work), or whether it can safely be disabled to speed up PC boot time and launched manually when needed.
4. SECURITY VERDICT: Assess legitimacy, whether the path is standard, signature validity, and absence of malware, telemetry, miners, or unwanted software.

Score Scale (score 1-10):
- 1-3 (Safe / allow): Windows system component, driver, legitimate utility, or trusted software with valid signature.
- 4-6 (Review / suspicious): Auto-updater, telemetry, bloatware, non-critical background task slowing down boot, or unsigned binary.
- 7-10 (Dangerous / block): Malware, trojan, suspicious script, obfuscation, masquerading as a system file.

Categories (category): System component | Useful utility | Third-party software | Background auto-updater | Telemetry | Suspicious software | Malware

Respond ONLY with a valid JSON object without any extra text or markdown wrappers:
{{""score"": <integer 1-10>, ""category"": ""<category in English>"", ""risk"": ""<comprehensive and clear human-readable explanation covering all 4 points: what this program is, what the arguments do, whether it is needed at boot, security verdict. 4-8 sentences in English>"", ""recommendation"": ""<actionable recommendation: Allow startup / Recommend disabling for faster boot / Block and remove, with a concise explanation in English>""}}";
    }

    /// <summary>
    /// Выполняет разбор JSON-ответа модели с поддержкой reasoning/thought блоков для рассуждающих моделей.
    /// </summary>
    /// <param name="json">Сырой JSON-ответ API.</param>
    /// <param name="entryName">Имя анализируемой записи для журнала ошибок.</param>
    /// <returns>Экземпляр StartupRating или null при ошибке разбора.</returns>
    internal StartupRating? ParseResponse(string json, string entryName)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
                return null;

            var choice = choices[0];
            if (!choice.TryGetProperty("message", out var messageElem))
                return null;

            string? text = null;
            if (messageElem.TryGetProperty("content", out var contentProp) && contentProp.ValueKind == JsonValueKind.String)
            {
                text = contentProp.GetString();
            }

            // Если основной content пустой, проверяем reasoning/thought поля (reasoning, reasoning_content, thought)
            // Это критически важно для рассуждающих моделей (openai/gpt-oss-120b, deepseek-r1 и др.)
            if (string.IsNullOrWhiteSpace(text))
            {
                if (messageElem.TryGetProperty("reasoning", out var reasoningProp) && reasoningProp.ValueKind == JsonValueKind.String)
                {
                    text = reasoningProp.GetString();
                }
                else if (messageElem.TryGetProperty("reasoning_content", out var reasoningContentProp) && reasoningContentProp.ValueKind == JsonValueKind.String)
                {
                    text = reasoningContentProp.GetString();
                }
                else if (messageElem.TryGetProperty("thought", out var thoughtProp) && thoughtProp.ValueKind == JsonValueKind.String)
                {
                    text = thoughtProp.GetString();
                }
            }

            if (string.IsNullOrWhiteSpace(text)) return null;

            var cleaned = text.Trim();

            // Если в ответе содержится markdown-блок ```json ... ```
            var jsonBlockMatch = System.Text.RegularExpressions.Regex.Match(cleaned, @"```(?:json)?\s*(\{[\s\S]*?\})\s*```");
            if (jsonBlockMatch.Success)
            {
                cleaned = jsonBlockMatch.Groups[1].Value.Trim();
            }
            else
            {
                // Если markdown-блока нет, но есть JSON-объект в тексте (например, внутри reasoning/thought)
                var firstBrace = cleaned.IndexOf('{');
                var lastBrace = cleaned.LastIndexOf('}');
                if (firstBrace >= 0 && lastBrace > firstBrace)
                {
                    cleaned = cleaned.Substring(firstBrace, lastBrace - firstBrace + 1).Trim();
                }
                else
                {
                    if (cleaned.StartsWith("```json")) cleaned = cleaned[7..];
                    if (cleaned.EndsWith("```")) cleaned = cleaned[..^3];
                    cleaned = cleaned.Trim();
                }
            }

            if (cleaned.StartsWith('<') && cleaned.Contains('>'))
            {
                var start = cleaned.IndexOf('>') + 1;
                var end = cleaned.LastIndexOf('<');
                if (start > 0 && end > start)
                    cleaned = cleaned[start..end].Trim();
            }

            cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"(?<!\\)\\(?![""\\/bfnrt]|u[0-9a-fA-F]{4})", @"\\");

            var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var rating = JsonSerializer.Deserialize<StartupRating>(cleaned, jsonOptions);
            if (rating != null)
                rating.Score = Math.Clamp(rating.Score, 1, 10);
            return rating;
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Groq API: не удалось распарсить ответ для {Name}: {Error}",
                entryName, ex.Message);
            return null;
        }
    }

    /// <summary>Кэш оценок сетевых процессов для исключения повторных запросов к API.</summary>
    private readonly ConcurrentDictionary<string, (string Purpose, string BlockingRisk, string Recommendation, string RecommendedAction)> _networkEvaluationsCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Выполняет анализ сетевого процесса с помощью LLM (Groq / OpenAI) для формирования контекстной справки и рекомендации.
    /// </summary>
    /// <param name="processName">Имя исполняемого файла (например, "chrome.exe").</param>
    /// <param name="processPath">Полный путь к исполняемому файлу на диске.</param>
    /// <param name="remoteAddress">Удалённый IP-адрес назначения.</param>
    /// <param name="remotePort">Удалённый порт назначения.</param>
    /// <returns>Кортеж с назначением, рисками, рекомендацией и действием или null при сбое.</returns>
    public async Task<(string Purpose, string BlockingRisk, string Recommendation, string RecommendedAction)?> EvaluateNetworkProcessAsync(
        string processName, string? processPath, string remoteAddress, int remotePort)
    {
        using var _prof = ExecutionProfiler.Measure("Service.Ai.EvaluateNetworkProcess");

        var pName = string.IsNullOrWhiteSpace(processName) ? "unknown.exe" : processName.Trim();
        var pPath = string.IsNullOrWhiteSpace(processPath) ? string.Empty : processPath.Trim();
        var remote = string.IsNullOrWhiteSpace(remoteAddress) ? "0.0.0.0" : remoteAddress.Trim();

        // 1. Собственные компоненты StartupMonitor всегда считаются доверенными
        if (pName.Contains("StartupMonitor", StringComparison.OrdinalIgnoreCase) ||
            pPath.Contains("StartupMonitor", StringComparison.OrdinalIgnoreCase))
        {
            return (
                "Официальный служебный компонент защитного комплекса Startup Monitor.",
                "Блокировка нарушит мониторинг автозапуска, контроль портов и межсетевой экран.",
                "Разрешить сетевое взаимодействие. Процесс полностью доверен.",
                "AllowAlways"
            );
        }

        // 2. Системные доверенные процессы Windows
        if (StartupMonitor.Service.Services.Firewall.NetworkConnectionAlertService.IsTrustedProcess(pName, pPath))
        {
            return (
                "Критически важный системный процесс операционной системы Windows.",
                "Блокировка может нарушить сетевой стек, службы домена, работу DNS или обновление Windows.",
                "Разрешить подключение, процесс является неотъемлемой частью операционной системы.",
                "AllowAlways"
            );
        }

        // 3. Проверка локального кэша
        var cacheKey = $"{pPath.ToLowerInvariant()}|{pName.ToLowerInvariant()}|{remote}:{remotePort}";
        if (_networkEvaluationsCache.TryGetValue(cacheKey, out var cachedVerdict))
        {
            return cachedVerdict;
        }

        // 4. Circuit Breaker и проверка наличия ключа
        if (DateTime.UtcNow < _networkCooldownUntil)
        {
            _logger.LogWarning("AiRatingService: запрос для {Process} отклонён (Circuit Breaker активен до {Until} UTC)", pName, _networkCooldownUntil);
            return null;
        }

        if (string.IsNullOrWhiteSpace(_apiKey) && !IsLocalUrl(_apiUrl))
        {
            _logger.LogDebug("AiRatingService: оценка сетевого процесса пропущена (не задан API-ключ)");
            return null;
        }

        FileVersionInfo? fv = null;
        try
        {
            if (!string.IsNullOrEmpty(pPath) && File.Exists(pPath))
            {
                fv = FileVersionInfo.GetVersionInfo(pPath);
            }
        }
        catch { }

        var company = !string.IsNullOrWhiteSpace(fv?.CompanyName) ? fv.CompanyName.Trim() : "Не указан";
        var prodName = !string.IsNullOrWhiteSpace(fv?.ProductName) ? fv.ProductName.Trim() : pName;
        var fileDesc = !string.IsNullOrWhiteSpace(fv?.FileDescription) ? fv.FileDescription.Trim() : "Не указано";
        var fileVer = !string.IsNullOrWhiteSpace(fv?.FileVersion) ? fv.FileVersion.Trim() : "Не указана";

        var prompt = BuildNetworkEvaluationPrompt(pName, pPath, prodName, fileDesc, company, fileVer, remote, remotePort);

        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                var body = new
                {
                    model = _model,
                    messages = new[] { new { role = "user", content = prompt } },
                    temperature = 0.1,
                    max_tokens = 1000,
                    response_format = new { type = "json_object" }
                };

                var jsonBody = JsonSerializer.Serialize(body);
                using var content = new StringContent(jsonBody, System.Text.Encoding.UTF8, "application/json");
                using var request = new HttpRequestMessage(HttpMethod.Post, _apiUrl);

                if (!string.IsNullOrWhiteSpace(_apiKey))
                    request.Headers.Add("Authorization", $"Bearer {_apiKey.Trim()}");
                else if (IsLocalUrl(_apiUrl))
                    request.Headers.Add("Authorization", "Bearer ollama");

                request.Content = content;

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(25));
                var response = await _http.SendAsync(request, cts.Token);

                if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                {
                    if (attempt < 3)
                    {
                        await Task.Delay(2000);
                        continue;
                    }
                    return null;
                }

                if (!response.IsSuccessStatusCode)
                {
                    var err = await response.Content.ReadAsStringAsync();
                    _logger.LogWarning("AiRatingService: ошибка {Status} при оценке процесса {Name}: {Error}", response.StatusCode, pName, err);
                    return null;
                }

                var respJson = await response.Content.ReadAsStringAsync();
                var verdict = ParseNetworkEvaluationResponse(respJson, pName);
                if (verdict != null)
                {
                    _networkEvaluationsCache[cacheKey] = verdict.Value;
                    return verdict;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "AiRatingService: исключение при сетевой оценке процесса {Process} (попытка {Attempt})", pName, attempt);
            }
        }

        return null;
    }

    /// <summary>Формирует промпт для оценки сетевой активности приложения экспертом ИИ.</summary>
    private string BuildNetworkEvaluationPrompt(
        string processName, string processPath, string productName, string fileDesc,
        string company, string fileVersion, string remoteAddress, int remotePort)
    {
        return $@"Ты — ведущий эксперт по сетевой безопасности Windows и системному администрированию.
Проанализируй сетевую активность процесса и дай оценку: каково назначение программы, чем грозит блокировка сетевого подключения и какова твоя рекомендация.

Параметры процесса и подключения:
- Имя процесса: {processName}
- Путь к файлу: {processPath}
- Название продукта: {productName}
- Описание файла: {fileDesc}
- Компания / Издатель: {company}
- Версия файла: {fileVersion}
- Удалённый адрес назначения: {remoteAddress}:{remotePort}
- ОС: {WindowsVersion}

Требования к ответу:
1. Назначение программы (purpose): Что это за приложение, легитимно ли оно, для чего создано. 2-4 предложения на русском языке.
2. Риски блокировки (blocking_risk): Чем грозит блокировка сетевого соединения пользователю. Разбери влияние на:
   - системные компоненты Windows и службы;
   - сетевую печать, локальные сетевые ресурсы и сетевые диски;
   - доступ в интернет и внешние веб-сервисы;
   - корректную работу приложения (сбои, потеря синхронизации, зависания). 2-4 предложения на русском языке.
3. Рекомендация (recommendation): Чёткая рекомендация пользователю на русском языке, 1-3 предложения.
4. Рекомендуемое действие (recommended_action): Строго одно из следующих значений:
   - ""AllowSession"" (разрешить для текущей сессии)
   - ""AllowAlways"" (разрешить навсегда / доверенное системное ПО)
   - ""BlockSession"" (заблокировать на эту сессию)
   - ""BlockAlways"" (заблокировать навсегда / опасное ПО)

Ответь ИСКЛЮЧИТЕЛЬНО валидным JSON-объектом без лишних символов и без markdown-блоков:
{{{{
  ""purpose"": ""<описание назначения программы на русском языке, 2-4 предложения>"",
  ""blocking_risk"": ""<подробное описание рисков блокировки для ОС, печати, сети и приложения на русском языке, 2-4 предложения>"",
  ""recommendation"": ""<чёткая рекомендация пользователю на русском языке, 1-3 предложения>"",
  ""recommended_action"": ""AllowSession|AllowAlways|BlockSession|BlockAlways""
}}}}";
    }

    /// <summary>Парсит JSON-ответ нейросети по оценке сетевого процесса с поддержкой reasoning/thought.</summary>
    private (string Purpose, string BlockingRisk, string Recommendation, string RecommendedAction)? ParseNetworkEvaluationResponse(string json, string processName)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
                return null;

            var choice = choices[0];
            if (!choice.TryGetProperty("message", out var messageElem))
                return null;

            string? text = null;
            if (messageElem.TryGetProperty("content", out var contentProp) && contentProp.ValueKind == JsonValueKind.String)
                text = contentProp.GetString();

            if (string.IsNullOrWhiteSpace(text))
            {
                if (messageElem.TryGetProperty("reasoning", out var reasoningProp) && reasoningProp.ValueKind == JsonValueKind.String)
                    text = reasoningProp.GetString();
                else if (messageElem.TryGetProperty("reasoning_content", out var reasoningContentProp) && reasoningContentProp.ValueKind == JsonValueKind.String)
                    text = reasoningContentProp.GetString();
                else if (messageElem.TryGetProperty("thought", out var thoughtProp) && thoughtProp.ValueKind == JsonValueKind.String)
                    text = thoughtProp.GetString();
            }

            if (string.IsNullOrWhiteSpace(text)) return null;

            var cleaned = text.Trim();
            var jsonBlockMatch = System.Text.RegularExpressions.Regex.Match(cleaned, @"```(?:json)?\s*(\{[\s\S]*?\})\s*```");
            if (jsonBlockMatch.Success)
            {
                cleaned = jsonBlockMatch.Groups[1].Value.Trim();
            }
            else
            {
                var firstBrace = cleaned.IndexOf('{');
                var lastBrace = cleaned.LastIndexOf('}');
                if (firstBrace >= 0 && lastBrace > firstBrace)
                    cleaned = cleaned.Substring(firstBrace, lastBrace - firstBrace + 1).Trim();
            }

            cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"(?<!\\)\\(?![""\\/bfnrt]|u[0-9a-fA-F]{4})", @"\\");

            using var innerDoc = JsonDocument.Parse(cleaned);
            var root = innerDoc.RootElement;

            string purpose = GetJsonStringProperty(root, "purpose", "Назначение не определено.");
            string blockingRisk = GetJsonStringProperty(root, "blocking_risk", "blockingRisk", "Особых рисков для критических служб не выявлено.");
            string recommendation = GetJsonStringProperty(root, "recommendation", "Применяйте стандартные правила брандмауэра.");
            string rawAction = GetJsonStringProperty(root, "recommended_action", "recommendedAction", "AllowSession");

            string action = rawAction.Trim() switch
            {
                var a when a.Equals("AllowAlways", StringComparison.OrdinalIgnoreCase) => "AllowAlways",
                var a when a.Equals("AllowSession", StringComparison.OrdinalIgnoreCase) => "AllowSession",
                var a when a.Equals("BlockAlways", StringComparison.OrdinalIgnoreCase) => "BlockAlways",
                var a when a.Equals("BlockSession", StringComparison.OrdinalIgnoreCase) => "BlockSession",
                var a when a.Contains("Block", StringComparison.OrdinalIgnoreCase) => "BlockSession",
                _ => "AllowSession"
            };

            return (purpose, blockingRisk, recommendation, action);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("AiRatingService: не удалось распарсить JSON оценки сетевого процесса для {Name}: {Error}",
                processName, ex.Message);
            return null;
        }
    }

    /// <summary>Вспомогательный метод извлечения строкового свойства из JsonElement с резервными ключами.</summary>
    private static string GetJsonStringProperty(JsonElement element, string primaryKey, string defaultValue)
    {
        if (element.TryGetProperty(primaryKey, out var prop) && prop.ValueKind == JsonValueKind.String)
        {
            var val = prop.GetString();
            if (!string.IsNullOrWhiteSpace(val)) return val.Trim();
        }
        return defaultValue;
    }

    /// <summary>Вспомогательный метод извлечения строкового свойства из JsonElement с альтернативным именем.</summary>
    private static string GetJsonStringProperty(JsonElement element, string primaryKey, string secondaryKey, string defaultValue)
    {
        if (element.TryGetProperty(primaryKey, out var p1) && p1.ValueKind == JsonValueKind.String)
        {
            var val = p1.GetString();
            if (!string.IsNullOrWhiteSpace(val)) return val.Trim();
        }
        if (element.TryGetProperty(secondaryKey, out var p2) && p2.ValueKind == JsonValueKind.String)
        {
            var val = p2.GetString();
            if (!string.IsNullOrWhiteSpace(val)) return val.Trim();
        }
        return defaultValue;
    }

    public void Dispose() => _http.Dispose();
}
