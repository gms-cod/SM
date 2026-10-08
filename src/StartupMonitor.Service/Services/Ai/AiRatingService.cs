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
    private const int MinDelayMs = 5000;
    private const int CooldownMinutes = 5;
    private static readonly Random _jitter = new();
    private static readonly string WindowsVersion = RuntimeInformation.OSDescription;

    public AiRatingService(RatingsCache cache, ILogger<AiRatingService> logger, string apiKey, string uiLang = "ru",
        string? apiUrl = null, string? model = null)
    {
        _http = new HttpClient();
        _cache = cache;
        _logger = logger;
        _apiKey = apiKey;
        _uiLang = string.IsNullOrWhiteSpace(uiLang) ? "ru" : uiLang;
        if (!string.IsNullOrWhiteSpace(apiUrl)) _apiUrl = apiUrl.Trim();
        if (!string.IsNullOrWhiteSpace(model)) _model = model.Trim();
    }

    public void UpdateUiLang(string uiLang)
    {
        if (!string.IsNullOrWhiteSpace(uiLang) && uiLang != _uiLang)
            _uiLang = uiLang;
    }

    public void UpdateApiKey(string apiKey) => _apiKey = apiKey;

    public void UpdateConfig(string apiKey, string? apiUrl = null, string? model = null)
    {
        _apiKey = apiKey;
        if (!string.IsNullOrWhiteSpace(apiUrl)) _apiUrl = apiUrl.Trim();
        if (!string.IsNullOrWhiteSpace(model)) _model = model.Trim();
    }

    private static bool IsLocalUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        return url.Contains("localhost", StringComparison.OrdinalIgnoreCase) ||
               url.Contains("127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
               url.Contains("0.0.0.0", StringComparison.OrdinalIgnoreCase);
    }

    public StartupRating? GetCached(StartupEntry entry) => _cache.Get(entry);

    public async Task PrefetchAsync(StartupEntry entry)
    {
        if (entry.AiRating > 0) return;
        if (_cache.Get(entry) != null) return;
        if (string.IsNullOrWhiteSpace(_apiKey) && !IsLocalUrl(_apiUrl)) return;

        var rating = await FetchRatingAsync(entry);
        if (rating != null)
            _cache.Set(entry, rating);
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
                max_tokens = 50
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

    private async Task<StartupRating?> FetchRatingAsync(StartupEntry entry)
    {
        using var _prof = ExecutionProfiler.Measure("Service.Ai.FetchRating");
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
                    var delay = Math.Min((int)(retryAfter ?? 60_000), 120_000);
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
                return ParseResponse(json, entry.Name);
            }
            catch (HttpRequestException ex) when (ex.Message.Contains("No connection could be made"))
            {
                _logger.LogWarning("Groq API недоступен. Проверьте подключение к интернету");
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
            finally
            {
                await Task.Delay(MinDelayMs + _jitter.Next(0, 500));
            }
        }

        return null;
    }

    internal string BuildPrompt(StartupEntry entry)
    {
        var fileInfo = !string.IsNullOrEmpty(entry.FilePath) && File.Exists(entry.FilePath)
            ? new FileInfo(entry.FilePath) : null;
        var fv = fileInfo?.Exists == true
            ? FileVersionInfo.GetVersionInfo(entry.FilePath!) : null;

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

    internal StartupRating? ParseResponse(string json, string entryName)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var text = doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();

            if (string.IsNullOrEmpty(text)) return null;

            var cleaned = text.Trim();
            if (cleaned.StartsWith("```json")) cleaned = cleaned[7..];
            if (cleaned.EndsWith("```")) cleaned = cleaned[..^3];
            cleaned = cleaned.Trim();

            if (cleaned.StartsWith('<') && cleaned.Contains('>'))
            {
                var start = cleaned.IndexOf('>') + 1;
                var end = cleaned.LastIndexOf('<');
                if (start > 0 && end > start)
                    cleaned = cleaned[start..end].Trim();
            }

            cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"(?<!\\)\\(?![""\\/bfnrt]|u[0-9a-fA-F]{4})", @"\\");

            var rating = JsonSerializer.Deserialize<StartupRating>(cleaned);
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

    public void Dispose() => _http.Dispose();
}
