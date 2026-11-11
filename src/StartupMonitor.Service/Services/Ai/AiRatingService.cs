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
        string? apiUrl = null, string? model = null, HttpMessageHandler? httpHandler = null)
    {
        // Явный обработчик позволяет проверять объединение и отмену HTTP без реальной сети.
        _http = httpHandler == null ? new HttpClient() : new HttpClient(httpHandler);
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

    /// <summary>Определяет локальный HTTP(S)-эндпоинт по хосту, не доверяя пути, имени пользователя и похожим доменам.</summary>
    /// <param name="url">Абсолютный адрес проверяемого провайдера.</param>
    /// <returns>Признак localhost, loopback или точного адреса 0.0.0.0 для совместимости с существующей настройкой.</returns>
    private static bool IsLocalUrl(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)) return false;
        var host = uri.Host.Trim('[', ']');
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase)) return true;
        return System.Net.IPAddress.TryParse(host, out var address) &&
               (System.Net.IPAddress.IsLoopback(address) || address.Equals(System.Net.IPAddress.Any));
    }

    /// <summary>Проверяет возможность фонового запроса: локальному провайдеру ключ не требуется.</summary>
    /// <param name="apiKey">Ключ удалённого провайдера, если задан.</param>
    /// <param name="apiUrl">Адрес настроенного провайдера.</param>
    /// <returns>Признак наличия ключа либо локального адреса.</returns>
    internal static bool CanRequestRatings(string? apiKey, string? apiUrl) =>
        !string.IsNullOrWhiteSpace(apiKey) || IsLocalUrl(apiUrl);

    /// <summary>GetCached: Возвращает Cached.</summary>
    /// <param name="entry)">Входные данные: значение параметра entry) (StartupEntry).</param>
    /// <returns>Выходные данные: результирующее значение типа StartupRating?.</returns>
    public StartupRating? GetCached(StartupEntry entry) => _cache.Get(entry);

    /// <summary>PrefetchAsync: Выполняет операцию PrefetchAsync.</summary>
    /// <param name="entry">Входные данные: значение параметра entry (StartupEntry).</param>
    /// <param name="cancellationToken">Отмена остановки службы, передаваемая до HTTP и retry-задержек.</param>
    public async Task PrefetchAsync(StartupEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        cancellationToken.ThrowIfCancellationRequested();
        if (_cache.Get(entry) != null) return;
        if (!CanRequestRatings(_apiKey, _apiUrl)) return;

        var fingerprint = StartupRatingContext.Compute(entry);
        var rating = await FetchRatingAsync(entry, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (rating != null)
            _cache.TrySetAutomatic(entry, rating, fingerprint);
    }

    /// <summary>
    /// Принудительно запрашивает свежий AI-рейтинг для записи, сбрасывая сетевой предохранитель Circuit Breaker.
    /// </summary>
    /// <param name="entry">Оцениваемая запись автозапуска.</param>
    /// <param name="cancellationToken">Отмена ручного запроса при остановке службы.</param>
    /// <returns>Полученный рейтинг или null в случае ошибки.</returns>
    public async Task<StartupRating?> ForceFetchRatingAsync(StartupEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        cancellationToken.ThrowIfCancellationRequested();
        // Немедленно сбрасываем Circuit Breaker при ручном запросе пользователя
        _networkCooldownUntil = DateTime.MinValue;
        _consecutiveNetworkErrors = 0;

        _logger.LogInformation("ForceFetchRatingAsync: немедленный запрос к AI для {Name} ({Model})", entry.Name, _model);
        var fingerprint = StartupRatingContext.Compute(entry);
        var rating = await FetchRatingAsync(entry, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var applied = rating != null && _cache.TrySetAutomatic(entry, rating, fingerprint);
        if (applied && rating is not null)
        {
            _logger.LogInformation("ForceFetchRatingAsync: успешно получен рейтинг {Score} для {Name}", rating.Score, entry.Name);
        }
        else
        {
            _logger.LogWarning("ForceFetchRatingAsync: AI не вернул рейтинг для {Name}", entry.Name);
        }

        return applied ? rating : null;
    }

    /// <summary>Проверяет провайдер с ограничением ожидания и сквозной отменой службы.</summary>
    public async Task<(bool Success, string Message)> TestConnectionAsync(string? apiKey, string? apiUrl, string? model, CancellationToken cancellationToken = default)
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
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _networkLifetime.Token);
            cts.CancelAfter(TimeSpan.FromSeconds(20));
            using var response = await _http.SendAsync(request, cts.Token);

            if (response.IsSuccessStatusCode)
            {
                return (true, $"Соединение успешно! Модель '{mdl}' активна.");
            }

            var errBody = await response.Content.ReadAsStringAsync(cts.Token);
            if ((int)response.StatusCode == 401 || (int)response.StatusCode == 403)
                return (false, $"Ошибка авторизации ({response.StatusCode}): неверный API-ключ");
            if ((int)response.StatusCode == 404)
                return (false, $"Эндпоинт или модель '{mdl}' не найдены (404)");
            if ((int)response.StatusCode == 429)
                return (false, "Превышен лимит запросов API (429 Rate Limit)");

            return (false, $"Ошибка сервера {(int)response.StatusCode}: {errBody}");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested || _networkLifetime.IsCancellationRequested)
        {
            throw;
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
    /// <param name="cancellationToken">Отмена всего пакета, в том числе cooldown, retry и незавершённого HTTP.</param>
    public async Task PrefetchAllAsync(List<StartupEntry> entries, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entries);
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _networkLifetime.Token);
        cancellationToken = lifetime.Token;
        cancellationToken.ThrowIfCancellationRequested();
        using var _prof = ExecutionProfiler.Measure("Service.Ai.PrefetchAll");
        if (!await _prefetchLock.WaitAsync(0, cancellationToken)) return;

        try
        {
            // Один хеш файла не определяет аргументы и точку запуска; объединяем только одинаковый контекст.
            var byHash = entries.Where(e => !string.IsNullOrEmpty(e.SHA256))
                .GroupBy(RatingsCache.BuildKey, StringComparer.Ordinal);
            foreach (var grp in byHash)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var rated = grp.FirstOrDefault(e => _cache.Get(e) != null);
                if (rated == null) continue;
                var src = _cache.Get(rated);
                if (src == null) continue;
                foreach (var e in grp.Where(e => _cache.Get(e) == null))
                    _cache.TrySetAutomatic(e, new StartupRating
                    {
                        Score = src.Score,
                        Category = src.Category,
                        Risk = src.Risk,
                        Recommendation = src.Recommendation
                    }, StartupRatingContext.Compute(e));
            }

            // Рейтинг: если ключ не задан и эндпоинт не локальный — пропускаем
            if (!CanRequestRatings(_apiKey, _apiUrl))
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
                cancellationToken.ThrowIfCancellationRequested();
                if (_cache.Get(entry) != null) continue;
                // Окно остывания после повторных 429: ждём восстановления лимита
                if (DateTime.UtcNow < _cooldownUntil)
                {
                    var wait = (int)(_cooldownUntil - DateTime.UtcNow).TotalMilliseconds + 1000;
                    _logger.LogWarning("AI API: пауза из-за rate limit, жду {Wait}ms", wait);
                    await Task.Delay(wait, cancellationToken);
                }
                var fingerprint = StartupRatingContext.Compute(entry);
                var rating = await FetchRatingAsync(entry, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                if (rating != null && _cache.TrySetAutomatic(entry, rating, fingerprint))
                {
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

    /// <summary>Получает рейтинг с сетевым предохранителем и ограничением каждой попытки; остановка отменяет запрос и ожидание повтора.</summary>
    /// <param name="entry">Оцениваемая запись автозапуска.</param>
    /// <param name="cancellationToken">Токен отмены оценки.</param>
    /// <returns>Рейтинг провайдера либо null при отсутствии результата.</returns>
    private async Task<StartupRating?> FetchRatingAsync(StartupEntry entry, CancellationToken cancellationToken)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _networkLifetime.Token);
        cancellationToken = lifetime.Token;
        cancellationToken.ThrowIfCancellationRequested();
        using var _prof = ExecutionProfiler.Measure("Service.Ai.FetchRating");

        // Имя и командная строка не подтверждают подлинность продукта: все записи проходят одинаковую оценку.
        // Circuit Breaker: если сеть недоступна, не пытаемся слать запросы
        if (DateTime.UtcNow < _networkCooldownUntil)
        {
            _logger.LogWarning("Groq API: запрос отклонён Circuit Breaker (сеть недоступна до {Until} UTC)", _networkCooldownUntil);
            return null;
        }

        var waitedFor429 = false;
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
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

                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(TimeSpan.FromSeconds(30));
                using var response = await _http.SendAsync(request, cts.Token);

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
                    await Task.Delay(delay, cancellationToken);
                    continue;
                }

                if (!response.IsSuccessStatusCode)
                {
                    var err = await response.Content.ReadAsStringAsync(cts.Token);
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
                        await Task.Delay(2000, cancellationToken);
                        continue;
                    }
                    _logger.LogWarning("Groq API {Code} для {Name}: {Error}",
                        response.StatusCode, entry.Name, err);
                    return null;
                }

                var json = await response.Content.ReadAsStringAsync(cts.Token);
                _consecutiveNetworkErrors = 0;
                return ParseResponse(json, entry.Name);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
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
                    await Task.Delay(2000, cancellationToken);
                    continue;
                }
                return null;
            }
            catch (TaskCanceledException)
            {
                _logger.LogWarning("Groq API таймаут для {Name}, попытка {Attempt}", entry.Name, attempt);
                if (attempt < 5)
                {
                    await Task.Delay(2000, cancellationToken);
                    continue;
                }
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Ошибка Groq API для {Name}, попытка {Attempt}", entry.Name, attempt);
                if (attempt < 5)
                {
                    await Task.Delay(2000, cancellationToken);
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
    private readonly ConcurrentDictionary<string, (DateTime ExpiresAt, (string Purpose, string BlockingRisk, string Recommendation, string RecommendedAction) Verdict)> _networkEvaluationsCache = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Общие выполняющиеся оценки объединяют запрос карточки и фонового обогащения.</summary>
    private readonly ConcurrentDictionary<string, NetworkEvaluationWork> _networkInFlight = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _networkSlots = new(4, 4);
    private readonly CancellationTokenSource _networkLifetime = new();

    /// <summary>Одна отменяемая оценка с числом ожидающих потребителей; отмена одного окна не обрывает чужое ожидание.</summary>
    private sealed class NetworkEvaluationWork
    {
        public readonly object Sync = new();
        public readonly CancellationTokenSource Cancellation;
        public readonly Lazy<Task<(string Purpose, string BlockingRisk, string Recommendation, string RecommendedAction)?>> Task;
        public int Waiters;

        /// <summary>Создаёт ленивую общую задачу, отменяемую при остановке сервиса либо уходе последнего потребителя.</summary>
        public NetworkEvaluationWork(CancellationToken lifetime, Func<CancellationToken, Task<(string Purpose, string BlockingRisk, string Recommendation, string RecommendedAction)?>> factory)
        {
            Cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
            Task = new(() => factory(Cancellation.Token), LazyThreadSafetyMode.ExecutionAndPublication);
        }
    }

    /// <summary>
    /// Выполняет анализ сетевого процесса с помощью LLM (Groq / OpenAI) для формирования контекстной справки и рекомендации.
    /// </summary>
    /// <param name="processName">Имя исполняемого файла (например, "chrome.exe").</param>
    /// <param name="processPath">Полный путь к исполняемому файлу на диске.</param>
    /// <param name="remoteAddress">Удалённый IP-адрес назначения.</param>
    /// <param name="remotePort">Удалённый порт назначения.</param>
    /// <returns>Кортеж с назначением, рисками, рекомендацией и действием или null при сбое.</returns>
    public async Task<(string Purpose, string BlockingRisk, string Recommendation, string RecommendedAction)?> EvaluateNetworkProcessAsync(
        string processName, string? processPath, string remoteAddress, int remotePort, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_networkLifetime.IsCancellationRequested) return null;
        var key = $"{processPath?.Trim().ToLowerInvariant()}|{processName?.Trim().ToLowerInvariant()}|{remoteAddress}:{remotePort}|{_apiUrl}|{_model}|{_uiLang}";
        if (_networkEvaluationsCache.TryGetValue(key, out var cached) && cached.ExpiresAt > DateTime.UtcNow) return cached.Verdict;
        _networkEvaluationsCache.TryRemove(key, out _);
        if (_networkInFlight.Count >= 128 && !_networkInFlight.ContainsKey(key)) return null;
        NetworkEvaluationWork work;
        while (true)
        {
            work = _networkInFlight.GetOrAdd(key, _ => new NetworkEvaluationWork(_networkLifetime.Token,
                token => RunNetworkEvaluationAsync(key, processName ?? string.Empty, processPath, remoteAddress, remotePort, token)));
            lock (work.Sync)
            {
                if (work.Cancellation.IsCancellationRequested)
                {
                    ((ICollection<KeyValuePair<string, NetworkEvaluationWork>>)_networkInFlight).Remove(new(key, work));
                    continue;
                }
                work.Waiters++;
                break;
            }
        }
        var task = work.Task.Value;
        try { return await task.WaitAsync(cancellationToken).ConfigureAwait(false); }
        finally
        {
            lock (work.Sync)
            {
                if (--work.Waiters == 0)
                {
                    ((ICollection<KeyValuePair<string, NetworkEvaluationWork>>)_networkInFlight).Remove(new(key, work));
                    if (!task.IsCompleted) work.Cancellation.Cancel();
                    _ = task.ContinueWith(_ => work.Cancellation.Dispose(), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                }
            }
        }
    }

    /// <summary>Ограничивает параллелизм сетевых оценок и хранит только ограниченный по сроку подтверждённый результат.</summary>
    private async Task<(string Purpose, string BlockingRisk, string Recommendation, string RecommendedAction)?> RunNetworkEvaluationAsync(
        string key, string processName, string? processPath, string remoteAddress, int remotePort, CancellationToken cancellationToken)
    {
        await _networkSlots.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var verdict = await EvaluateNetworkProcessCoreAsync(processName, processPath, remoteAddress, remotePort, cancellationToken).ConfigureAwait(false);
            if (verdict.HasValue && !cancellationToken.IsCancellationRequested)
            {
                _networkEvaluationsCache[key] = (DateTime.UtcNow.AddMinutes(30), verdict.Value);
                foreach (var old in _networkEvaluationsCache.Where(pair => pair.Value.ExpiresAt <= DateTime.UtcNow).ToArray())
                    _networkEvaluationsCache.TryRemove(old.Key, out _);
                while (_networkEvaluationsCache.Count > 512)
                    _networkEvaluationsCache.TryRemove(_networkEvaluationsCache.MinBy(pair => pair.Value.ExpiresAt).Key, out _);
            }
            return verdict;
        }
        finally { _networkSlots.Release(); }
    }

    /// <summary>Выполняет единственный запрос оценки с проверкой отмены и доверенной идентичности.</summary>
    private async Task<(string Purpose, string BlockingRisk, string Recommendation, string RecommendedAction)?> EvaluateNetworkProcessCoreAsync(
        string processName, string? processPath, string remoteAddress, int remotePort, CancellationToken cancellationToken)
    {
        using var _prof = ExecutionProfiler.Measure("Service.Ai.EvaluateNetworkProcess");

        var pName = string.IsNullOrWhiteSpace(processName) ? "unknown.exe" : processName.Trim();
        var pPath = string.IsNullOrWhiteSpace(processPath) ? string.Empty : processPath.Trim();
        var remote = string.IsNullOrWhiteSpace(remoteAddress) ? "0.0.0.0" : remoteAddress.Trim();

        // 1. Собственные компоненты StartupMonitor всегда считаются доверенными
        if (StartupMonitor.Service.Services.Firewall.NetworkConnectionAlertService.IsTrustedProcess(pName, pPath) &&
            (Path.GetFileName(pPath).Equals("StartupMonitor.Service.exe", StringComparison.OrdinalIgnoreCase) ||
             Path.GetFileName(pPath).Equals("StartupMonitor.UI.exe", StringComparison.OrdinalIgnoreCase)))
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
        // 4. Circuit Breaker, пауза Rate Limit и проверка наличия ключа
        if (DateTime.UtcNow < _cooldownUntil)
        {
            _logger.LogWarning("AiRatingService: запрос для {Process} пропущен (пауза Rate Limit до {Until} UTC)", pName, _cooldownUntil);
            return null;
        }

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

                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(TimeSpan.FromSeconds(25));
                using var response = await _http.SendAsync(request, cts.Token).ConfigureAwait(false);

                if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                {
                    var retryAfter = response.Headers.RetryAfter?.Delta?.TotalMilliseconds;
                    var delayMs = retryAfter.HasValue ? Math.Clamp((long)retryAfter.Value, 1000L, 30_000L) : 5_000L;
                    _cooldownUntil = DateTime.UtcNow.AddMinutes(CooldownMinutes);
                    _logger.LogWarning("AiRatingService: 429 Too Many Requests для {Name}, пауза {Delay}мс, остывание {Min} мин",
                        pName, delayMs, CooldownMinutes);
                    if (attempt < 3)
                    {
                        await Task.Delay((int)delayMs, cancellationToken).ConfigureAwait(false);
                        continue;
                    }
                    return null;
                }

                if (!response.IsSuccessStatusCode)
                {
                    var err = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                    _logger.LogWarning("AiRatingService: ошибка {Status} при оценке процесса {Name}: {Error}", response.StatusCode, pName, err);
                    return null;
                }

                var respJson = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                var verdict = ParseNetworkEvaluationResponse(respJson, pName);
                if (verdict != null)
                {
                    return verdict;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
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

            string purpose = GetJsonStringProperty(root, "purpose", string.Empty);
            string blockingRisk = GetJsonStringProperty(root, "blocking_risk", "blockingRisk", string.Empty);
            string recommendation = GetJsonStringProperty(root, "recommendation", string.Empty);
            string rawAction = GetJsonStringProperty(root, "recommended_action", "recommendedAction", string.Empty);
            if (string.IsNullOrWhiteSpace(purpose) || string.IsNullOrWhiteSpace(blockingRisk) || string.IsNullOrWhiteSpace(recommendation))
                return null;

            string? action = rawAction.Trim() switch
            {
                var a when a.Equals("AllowAlways", StringComparison.OrdinalIgnoreCase) => "AllowAlways",
                var a when a.Equals("AllowSession", StringComparison.OrdinalIgnoreCase) => "AllowSession",
                var a when a.Equals("BlockAlways", StringComparison.OrdinalIgnoreCase) => "BlockAlways",
                var a when a.Equals("BlockSession", StringComparison.OrdinalIgnoreCase) => "BlockSession",
                _ => null
            };
            if (action == null) return null;
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

    public void Dispose()
    {
        _networkLifetime.Cancel();
        _networkEvaluationsCache.Clear();
        _http.Dispose();
    }
}
