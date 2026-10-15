using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using StartupMonitor.Service.Services.Remote;
using StartupMonitor.Shared.Models;

namespace StartupMonitor.Service.Services.Update;

/// <summary>
/// Сервис самообновления приложения через GitHub Releases API.
/// Выполняет периодическую проверку версий, фоновую загрузку дистрибутива,
/// валидацию контрольной суммы SHA-256 и запуск тихой установки Inno Setup.
/// </summary>
public sealed class GitHubUpdateService : IDisposable
{
    private const string GitHubApiLatestReleaseUrl = "https://api.github.com/repos/gms-cod/SM/releases/latest";
    private const string GitHubApiFallbackReleaseUrl = "https://api.github.com/repos/gms-cod/Startup-Monitor/releases/latest";
    private static readonly TimeSpan DefaultCheckInterval = TimeSpan.FromHours(12);

    private readonly JsonDataStore _store;
    private readonly NotificationService _notifications;
    private readonly RemoteNotificationService _remoteNotifications;
    private readonly ILogger<GitHubUpdateService> _logger;
    private readonly HttpClient _httpClient;

    private readonly object _lock = new();
    private Timer? _checkTimer;
    private UpdateInfo? _latestUpdateInfo;
    private UpdateProgressInfo _currentProgress = new();
    private CancellationTokenSource? _downloadCts;
    private bool _isDisposed;

    /// <summary>Текущая версия сборки.</summary>
    public static Version CurrentVersion =>
        Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 3, 1);

    /// <summary>Строковое представление текущей версии (например "1.2.2").</summary>
    public static string CurrentVersionString =>
        $"{CurrentVersion.Major}.{CurrentVersion.Minor}.{Math.Max(0, CurrentVersion.Build)}";

    public GitHubUpdateService(
        JsonDataStore store,
        NotificationService notifications,
        RemoteNotificationService remoteNotifications,
        ILogger<GitHubUpdateService> logger,
        HttpClient? httpClient = null)
    {
        _store = store;
        _notifications = notifications;
        _remoteNotifications = remoteNotifications;
        _logger = logger;

        _httpClient = httpClient ?? new HttpClient(new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
            ConnectTimeout = TimeSpan.FromSeconds(15)
        });

        ConfigureHttpClientHeaders(_httpClient);
    }

    /// <summary>Запуск периодического таймера фоновой проверки обновлений.</summary>
    public void StartBackgroundCheck()
    {
        lock (_lock)
        {
            if (_checkTimer != null || _isDisposed) return;
            // Первая проверка через 1 минуту после старта службы, затем каждые 12 часов
            _checkTimer = new Timer(async _ => await OnTimerTickAsync(), null, TimeSpan.FromMinutes(1), DefaultCheckInterval);
            _logger.LogInformation("Update check timer initialized (interval: {Interval}).", DefaultCheckInterval);
        }
    }

    private async Task OnTimerTickAsync()
    {
        try
        {
            var cfg = _store.Load();
            if (!cfg.Settings.AutoCheckUpdates)
            {
                _logger.LogDebug("AutoCheckUpdates is disabled in settings. Skipping background check.");
                return;
            }

            _logger.LogInformation("Performing background update check from GitHub Releases...");
            var result = await CheckForUpdatesAsync(isManual: false, CancellationToken.None);

            if (result.Success && result.UpdateInfo != null && result.UpdateInfo.IsUpdateAvailable)
            {
                var update = result.UpdateInfo;
                if (!string.IsNullOrEmpty(cfg.Settings.SkippedUpdateVersion) &&
                    string.Equals(cfg.Settings.SkippedUpdateVersion.TrimStart('v', 'V'), update.Version.TrimStart('v', 'V'), StringComparison.OrdinalIgnoreCase))
                {
                    // Если инсталлятор был перевыпущен с более свежей датой сборки, не подавляем обновление
                    if (!update.IsNewerByDate)
                    {
                        _logger.LogInformation("Update v{Version} was skipped by user. Suppressing notifications.", update.Version);
                        return;
                    }
                    _logger.LogInformation("Update v{Version} was re-released with newer installer build date ({Date}). Notifying user.", update.Version, update.InstallerDate);
                }

                // Уведомление в трей/UI
                _notifications.Enqueue(new PipeMessage
                {
                    Type = PipeMessageTypes.ActionNotice,
                    Payload = JsonSerializer.Serialize(new ActionNoticePayload(
                        $"Startup Monitor v{update.Version}", "update_available"))
                });

                // Удалённые уведомления (Telegram / Discord)
                await _remoteNotifications.NotifyUpdateAvailableAsync(update.Version, update.Title, update.DownloadUrl);

                // Автоматическое скачивание в фоне (если включено)
                if (cfg.Settings.AutoDownloadUpdates && _currentProgress.Status != "Downloading" && _currentProgress.Status != "Ready")
                {
                    _logger.LogInformation("Auto-downloading update v{Version} in background...", update.Version);
                    _ = Task.Run(async () => await DownloadUpdateAsync(null, CancellationToken.None));
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred during periodic background update check.");
        }
    }

    /// <summary>
    /// Проверяет наличие нового релиза на GitHub.
    /// </summary>
    public Task<UpdateCheckResult> CheckForUpdatesAsync(bool isManual, CancellationToken ct)
        => CheckForUpdatesAsync(isManual, ct, clientVersionString: null, clientBuildDateUtc: null);

    /// <summary>
    /// Проверяет наличие нового релиза на GitHub с раздельной оценкой для службы и UI-клиента.
    /// </summary>
    /// <param name="isManual">Признак ручной проверки (зарезервировано для телеметрии).</param>
    /// <param name="ct">Токен отмены.</param>
    /// <param name="clientVersionString">Версия UI-клиента (например "1.4.0"), null — не оценивать.</param>
    /// <param name="clientBuildDateUtc">UTC-дата сборки/создания UI-клиента.</param>
    public async Task<UpdateCheckResult> CheckForUpdatesAsync(
        bool isManual,
        CancellationToken ct,
        string? clientVersionString,
        DateTime? clientBuildDateUtc = null)
    {
        try
        {
            // Основной URL + резервный (если основной вернул 404).
            string[] releaseUrls = [GitHubApiLatestReleaseUrl, GitHubApiFallbackReleaseUrl];
            HttpResponseMessage? response = null;
            for (var i = 0; i < releaseUrls.Length; i++)
            {
                var url = releaseUrls[i];
                var isLast = i == releaseUrls.Length - 1;
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                var resp = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);

                if (resp.StatusCode == System.Net.HttpStatusCode.NotFound && !isLast)
                {
                    _logger.LogWarning("No releases at {Url} (404 Not Found). Trying fallback {Fallback}...",
                        url, releaseUrls[i + 1]);
                    resp.Dispose();
                    continue;
                }

                response = resp;
                break;
            }

            if (response == null)
            {
                // Оба URL вернули 404 — релизов нет.
                _logger.LogInformation("No releases found on GitHub (404 Not Found). Current version {Current} is considered latest.", CurrentVersionString);
                return new UpdateCheckResult
                {
                    Success = true,
                    UpdateInfo = new UpdateInfo
                    {
                        Version = CurrentVersionString,
                        CurrentVersion = CurrentVersionString,
                        ServiceVersion = CurrentVersionString,
                        ClientVersion = clientVersionString,
                        IsUpdateAvailable = false,
                        IsUpdateAvailableForClient = false,
                        Title = "No releases found on GitHub"
                    }
                };
            }

            using (response)
            {
                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    _logger.LogInformation("No releases found on GitHub (404 Not Found). Current version {Current} is considered latest.", CurrentVersionString);
                    return new UpdateCheckResult
                    {
                        Success = true,
                        UpdateInfo = new UpdateInfo
                        {
                            Version = CurrentVersionString,
                            CurrentVersion = CurrentVersionString,
                            ServiceVersion = CurrentVersionString,
                            ClientVersion = clientVersionString,
                            IsUpdateAvailable = false,
                            IsUpdateAvailableForClient = false,
                            Title = "No releases found on GitHub"
                        }
                    };
                }

                if (!response.IsSuccessStatusCode)
                {
                    var error = $"GitHub API returned {(int)response.StatusCode} {response.ReasonPhrase}";
                    _logger.LogWarning("Update check failed: {Error}", error);
                    return new UpdateCheckResult { Success = false, ErrorMessage = error };
                }

                var jsonStream = await response.Content.ReadAsStreamAsync(ct);
                using var doc = await JsonDocument.ParseAsync(jsonStream, cancellationToken: ct);
                var root = doc.RootElement;

                var tagName = root.TryGetProperty("tag_name", out var tagProp) ? tagProp.GetString() ?? "" : "";
                var title = root.TryGetProperty("name", out var nameProp) ? nameProp.GetString() ?? tagName : tagName;
                var body = root.TryGetProperty("body", out var bodyProp) ? bodyProp.GetString() ?? "" : "";
                var publishedAt = root.TryGetProperty("published_at", out var pubProp) && pubProp.TryGetDateTime(out var dt) ? dt : DateTime.UtcNow;

                var (downloadUrl, fileName, sizeBytes, assetDate) = FindInstallerAsset(root);
                var effectiveInstallerDate = assetDate ?? publishedAt;

                // Парсим версию релиза. Сравнение через CompareVersions: недостающие
                // компоненты (Build/Revision = -1) трактуются как 0, поэтому
                // 1.2.4 == 1.2.4.0, а 1.3.0 > 1.2.4.0 (и наоборот) определяется корректно.
                var cleanTag = CleanVersionTag(tagName);
                var remoteVersion = ParseSemVer(cleanTag);

                var serviceBuildDate = GetLocalBuildDateUtc();
                var serviceAvailable = IsUpdateCandidate(remoteVersion, CurrentVersion, effectiveInstallerDate, serviceBuildDate, out var serviceNewerByDate);

                Version? clientVer = null;
                bool clientAvailable = false;
                bool clientNewerByDate = false;
                if (!string.IsNullOrWhiteSpace(clientVersionString))
                {
                    try { clientVer = ParseSemVer(CleanVersionTag(clientVersionString)); } catch { clientVer = null; }
                    if (clientVer != null)
                    {
                        var effectiveClientDate = clientBuildDateUtc ?? serviceBuildDate;
                        clientAvailable = IsUpdateCandidate(remoteVersion, clientVer, effectiveInstallerDate, effectiveClientDate, out clientNewerByDate);
                    }
                }

                var isAvailable = serviceAvailable || clientAvailable;
                var isNewerByDate = serviceNewerByDate || clientNewerByDate;

                // Извлекаем SHA-256 хэш (из текста релиза или .sha256 ассета)
                var sha256 = ExtractSha256Checksum(body, root);

                var updateInfo = new UpdateInfo
                {
                    Version = cleanTag,
                    Title = title,
                    Changelog = body,
                    ReleaseDate = publishedAt,
                    InstallerDate = effectiveInstallerDate,
                    InstalledBuildDate = serviceBuildDate > DateTime.MinValue ? serviceBuildDate : null,
                    IsNewerByDate = isNewerByDate,
                    DownloadUrl = downloadUrl,
                    FileName = fileName,
                    Sha256 = sha256,
                    SizeBytes = sizeBytes,
                    IsUpdateAvailable = isAvailable,
                    CurrentVersion = CurrentVersionString,
                    ServiceVersion = CurrentVersionString,
                    ClientVersion = clientVersionString,
                    IsUpdateAvailableForClient = clientAvailable
                };

                lock (_lock)
                {
                    _latestUpdateInfo = updateInfo;
                }

                // Обновляем метку времени последней проверки в настройках
                var cfg = _store.Load();
                cfg.Settings.LastUpdateCheckUtc = DateTime.UtcNow;
                _store.Save(cfg);

                _logger.LogInformation("Update check completed. Remote: v{Remote}, Current: v{Current}, Available: {Available}",
                    cleanTag, CurrentVersionString, isAvailable);

                return new UpdateCheckResult
                {
                    Success = true,
                    UpdateInfo = updateInfo
                };
            }
        }
        catch (OperationCanceledException)
        {
            return new UpdateCheckResult { Success = false, ErrorMessage = "Operation cancelled" };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check for updates from GitHub.");
            return new UpdateCheckResult { Success = false, ErrorMessage = ex.Message };
        }
    }

    /// <summary>
    /// Скачивает инсталлятор обновления во временный каталог с отслеживанием прогресса и валидацией SHA-256.
    /// </summary>
    public async Task<UpdateProgressInfo> DownloadUpdateAsync(Action<UpdateProgressInfo>? progressCallback, CancellationToken ct)
    {
        UpdateInfo? update;
        lock (_lock)
        {
            update = _latestUpdateInfo;
            _downloadCts?.Cancel();
            _downloadCts?.Dispose();
            _downloadCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        }

        if (update == null || string.IsNullOrWhiteSpace(update.DownloadUrl))
        {
            var err = "No update available to download. Please check for updates first.";
            _currentProgress = new UpdateProgressInfo { Status = "Failed", ErrorMessage = err };
            progressCallback?.Invoke(_currentProgress);
            return _currentProgress;
        }

        var linkedCt = _downloadCts.Token;

        try
        {
            var targetDir = GetUpdateStorageDirectory();
            Directory.CreateDirectory(targetDir);

            var safeFileName = string.IsNullOrWhiteSpace(update.FileName)
                ? $"StartupMonitorSetup-{update.Version}.exe"
                : update.FileName;

            var targetFilePath = Path.Combine(targetDir, safeFileName);
            var tempFilePath = targetFilePath + ".download";

            if (File.Exists(tempFilePath))
            {
                File.Delete(tempFilePath);
            }

            _logger.LogInformation("Starting download of {FileName} from {Url} to {Path}...", safeFileName, update.DownloadUrl, targetFilePath);

            _currentProgress = new UpdateProgressInfo
            {
                Status = "Downloading",
                TotalBytes = update.SizeBytes,
                BytesDownloaded = 0,
                Percent = 0
            };
            progressCallback?.Invoke(_currentProgress);

            using var response = await _httpClient.GetAsync(update.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, linkedCt);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? update.SizeBytes;
            _currentProgress.TotalBytes = totalBytes;

            await using (var contentStream = await response.Content.ReadAsStreamAsync(linkedCt))
            await using (var fileStream = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write, FileShare.None, 65536, useAsync: true))
            {
                var buffer = new byte[65536];
                int bytesRead;
                long totalRead = 0;
                var stopwatch = Stopwatch.StartNew();
                var lastReport = Stopwatch.StartNew();
                long lastBytes = 0;

                while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, linkedCt)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), linkedCt);
                    totalRead += bytesRead;

                    if (lastReport.ElapsedMilliseconds >= 250 || totalRead == totalBytes)
                    {
                        var elapsedSec = Math.Max(0.001, lastReport.Elapsed.TotalSeconds);
                        var speed = (long)((totalRead - lastBytes) / elapsedSec);
                        var percent = totalBytes > 0 ? (int)(totalRead * 100 / totalBytes) : 0;

                        _currentProgress = new UpdateProgressInfo
                        {
                            Status = "Downloading",
                            BytesDownloaded = totalRead,
                            TotalBytes = totalBytes,
                            Percent = Math.Clamp(percent, 0, 100),
                            SpeedBytesPerSec = speed
                        };

                        progressCallback?.Invoke(_currentProgress);
                        lastReport.Restart();
                        lastBytes = totalRead;
                    }
                }
            }

            // Верификация SHA-256 (если предоставлен)
            _currentProgress = new UpdateProgressInfo
            {
                Status = "Verifying",
                BytesDownloaded = totalBytes,
                TotalBytes = totalBytes,
                Percent = 100
            };
            progressCallback?.Invoke(_currentProgress);

            if (!string.IsNullOrWhiteSpace(update.Sha256))
            {
                _logger.LogInformation("Verifying SHA-256 checksum of downloaded file...");
                var computedHash = await ComputeSha256Async(tempFilePath, linkedCt);
                if (!string.Equals(computedHash, update.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    File.Delete(tempFilePath);
                    var hashMismatchErr = $"SHA-256 mismatch! Expected {update.Sha256}, got {computedHash}";
                    _logger.LogError("{Error}", hashMismatchErr);
                    _currentProgress = new UpdateProgressInfo { Status = "Failed", ErrorMessage = hashMismatchErr };
                    progressCallback?.Invoke(_currentProgress);
                    return _currentProgress;
                }
                _logger.LogInformation("SHA-256 verified successfully: {Hash}", computedHash);
            }

            // Перемещаем завершенный файл на постоянное место
            if (File.Exists(targetFilePath))
            {
                File.Delete(targetFilePath);
            }
            File.Move(tempFilePath, targetFilePath);

            _currentProgress = new UpdateProgressInfo
            {
                Status = "Ready",
                BytesDownloaded = totalBytes,
                TotalBytes = totalBytes,
                Percent = 100,
                DownloadedFilePath = targetFilePath
            };

            progressCallback?.Invoke(_currentProgress);
            _logger.LogInformation("Update package ready for installation: {Path}", targetFilePath);
            return _currentProgress;
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Download was cancelled.");
            _currentProgress = new UpdateProgressInfo { Status = "Failed", ErrorMessage = "Download cancelled" };
            progressCallback?.Invoke(_currentProgress);
            return _currentProgress;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to download update package.");
            _currentProgress = new UpdateProgressInfo { Status = "Failed", ErrorMessage = ex.Message };
            progressCallback?.Invoke(_currentProgress);
            return _currentProgress;
        }
    }

    /// <summary>
    /// Запускает процесс тихой установки скачанного дистрибутива.
    /// </summary>
    public (bool Success, string Message) InstallUpdate()
    {
        lock (_lock)
        {
            var filePath = _currentProgress.DownloadedFilePath;
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                return (false, "Installer file not found. Please download the update first.");
            }

            try
            {
                _logger.LogInformation("Launching silent installer: {Path} /SILENT /NORESTART /SUPPRESSMSGBOXES", filePath);

                var psi = new ProcessStartInfo
                {
                    FileName = filePath,
                    Arguments = "/SILENT /NORESTART /SUPPRESSMSGBOXES",
                    UseShellExecute = true,
                    Verb = "runas"
                };

                Process.Start(psi);
                return (true, "Update installer launched successfully.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to launch update installer.");
                return (false, $"Failed to launch installer: {ex.Message}");
            }
        }
    }

    /// <summary>Возвращает текущую информацию о прогрессе загрузки.</summary>
    public UpdateProgressInfo GetProgress() => _currentProgress;

    /// <summary>Возвращает последнюю полученную информацию об обновлении.</summary>
    public UpdateInfo? GetLatestUpdateInfo() => _latestUpdateInfo;

    private static void ConfigureHttpClientHeaders(HttpClient client)
    {
        client.DefaultRequestHeaders.UserAgent.Clear();
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("StartupMonitor-Updater", CurrentVersionString));
        client.DefaultRequestHeaders.Accept.Clear();
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github.v3+json"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/octet-stream"));
    }

    public static string CleanVersionTag(string rawTag)
    {
        if (string.IsNullOrWhiteSpace(rawTag)) return "0.0.0";
        var cleaned = rawTag.Trim().TrimStart('v', 'V');
        // Отрезаем суффиксы типа -beta, -rc если есть
        var dashIndex = cleaned.IndexOf('-');
        return dashIndex > 0 ? cleaned[..dashIndex] : cleaned;
    }

    public static Version ParseSemVer(string versionStr)
    {
        if (Version.TryParse(versionStr, out var v))
        {
            var build = v.Build < 0 ? 0 : v.Build;
            var rev = v.Revision < 0 ? -1 : v.Revision;
            return rev >= 0 ? new Version(v.Major, v.Minor, build, rev) : new Version(v.Major, v.Minor, build);
        }

        // Если только 1 компонент
        var parts = versionStr.Split('.');
        if (parts.Length == 1 && int.TryParse(parts[0], out var major))
            return new Version(major, 0, 0);

        return new Version(0, 0, 0);
    }

    /// <summary>
    /// Корректное сравнение версий с нормализацией: недостающие Build/Revision (-1)
    /// трактуются как 0. Поэтому 1.2.4 == 1.2.4.0, 1.3.0 &gt; 1.2.4.0 и т.д.
    /// Стандартный оператор Version.op_GreaterThan считает -1 &lt; 0 и даёт
    /// ложное неравенство для пар 3- vs 4-компонентных версий.
    /// </summary>
    public static int CompareVersions(Version a, Version b)
    {
        var c = a.Major.CompareTo(b.Major);
        if (c != 0) return c;
        c = a.Minor.CompareTo(b.Minor);
        if (c != 0) return c;
        var aBuild = a.Build < 0 ? 0 : a.Build;
        var bBuild = b.Build < 0 ? 0 : b.Build;
        c = aBuild.CompareTo(bBuild);
        if (c != 0) return c;
        var aRev = a.Revision < 0 ? 0 : a.Revision;
        var bRev = b.Revision < 0 ? 0 : b.Revision;
        return aRev.CompareTo(bRev);
    }

    /// <summary>True, если remote строго новее current (с нормализацией 3/4 компонентов).</summary>
    public static bool IsNewerVersion(Version remote, Version current)
        => CompareVersions(remote, current) > 0;

    /// <summary>
    /// Определяет, является ли удалённый релиз кандидатом на обновление по версии и времени создания инсталлятора.
    /// Если удалённая версия строго выше локальной — обновление доступно.
    /// Если версии совпадают, проверяется дата создания/обновления инсталлятора: если инсталлятор новее
    /// локальной сборки более чем на 2 минуты — обновление также считается доступным (перевыпущенный билд).
    /// </summary>
    public static bool IsUpdateCandidate(
        Version remoteVersion,
        Version localVersion,
        DateTime? remoteInstallerDateUtc,
        DateTime? localBuildDateUtc,
        out bool isNewerByDate)
    {
        isNewerByDate = false;
        var cmp = CompareVersions(remoteVersion, localVersion);
        if (cmp > 0)
        {
            return true;
        }

        if (cmp == 0)
        {
            // Проверка по времени создания/обновления инсталлятора
            if (remoteInstallerDateUtc.HasValue && localBuildDateUtc.HasValue && localBuildDateUtc.Value > DateTime.MinValue)
            {
                // Запас в 2 минуты для сглаживания погрешностей времени сборки и выгрузки в GitHub Releases
                if (remoteInstallerDateUtc.Value > localBuildDateUtc.Value.AddMinutes(2))
                {
                    isNewerByDate = true;
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Возвращает UTC-дату создания или модификации исполняемого файла текущего процесса/сборки.
    /// </summary>
    public static DateTime GetLocalBuildDateUtc()
    {
        try
        {
            var processPath = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(processPath) && File.Exists(processPath))
            {
                var writeTime = File.GetLastWriteTimeUtc(processPath);
                var createTime = File.GetCreationTimeUtc(processPath);
                return writeTime > createTime ? writeTime : createTime;
            }

            var baseDir = AppContext.BaseDirectory;
            if (!string.IsNullOrEmpty(baseDir))
            {
                var exePath = Path.Combine(baseDir, "StartupMonitor.Service.exe");
                var dllPath = Path.Combine(baseDir, "StartupMonitor.Service.dll");
                var targetPath = File.Exists(exePath) ? exePath : (File.Exists(dllPath) ? dllPath : null);
                if (targetPath != null)
                {
                    var writeTime = File.GetLastWriteTimeUtc(targetPath);
                    var createTime = File.GetCreationTimeUtc(targetPath);
                    return writeTime > createTime ? writeTime : createTime;
                }
            }
        }
        catch { }

        return DateTime.MinValue;
    }

    public static (string DownloadUrl, string FileName, long SizeBytes, DateTime? UpdatedAtUtc) FindInstallerAsset(JsonElement releaseRoot)
    {
        if (!releaseRoot.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
            return ("", "", 0, null);

        JsonElement? bestAsset = null;
        int bestScore = -1;

        foreach (var asset in assets.EnumerateArray())
        {
            var name = asset.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
            if (!name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) continue;

            int score = 0;
            if (name.Contains("StartupMonitor", StringComparison.OrdinalIgnoreCase)) score += 10;
            if (name.Contains("Setup", StringComparison.OrdinalIgnoreCase)) score += 5;

            if (score > bestScore)
            {
                bestScore = score;
                bestAsset = asset;
            }
        }

        if (bestAsset.HasValue)
        {
            var asset = bestAsset.Value;
            var name = asset.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
            var url = asset.TryGetProperty("browser_download_url", out var u) ? u.GetString() ?? "" : "";
            var size = asset.TryGetProperty("size", out var s) && s.TryGetInt64(out var sz) ? sz : 0;
            DateTime? updatedAt = null;
            if (asset.TryGetProperty("updated_at", out var up) && up.TryGetDateTime(out var upDt))
            {
                updatedAt = upDt.ToUniversalTime();
            }
            else if (asset.TryGetProperty("created_at", out var cr) && cr.TryGetDateTime(out var crDt))
            {
                updatedAt = crDt.ToUniversalTime();
            }

            return (url, name, size, updatedAt);
        }

        return ("", "", 0, null);
    }

    public static string? ExtractSha256Checksum(string body, JsonElement releaseRoot)
    {
        if (!string.IsNullOrWhiteSpace(body))
        {
            // Поиск в теле: "SHA256: 4f53..." или "SHA-256: 4f53..."
            var match = Regex.Match(body, @"(?:SHA-?256|hash)\s*[:=]\s*([a-fA-F0-9]{64})", RegexOptions.IgnoreCase);
            if (match.Success) return match.Groups[1].Value.ToLowerInvariant();

            // Поиск изолированного 64-символьного hex-хэша
            var hexMatch = Regex.Match(body, @"\b([a-fA-F0-9]{64})\b");
            if (hexMatch.Success) return hexMatch.Groups[1].Value.ToLowerInvariant();
        }

        return null;
    }

    private static async Task<string> ComputeSha256Async(string filePath, CancellationToken ct)
    {
        using var sha256 = SHA256.Create();
        await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, useAsync: true);
        var hashBytes = await sha256.ComputeHashAsync(stream, ct);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    private static string GetUpdateStorageDirectory()
    {
        try
        {
            var progData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            if (!string.IsNullOrEmpty(progData))
            {
                return Path.Combine(progData, "StartupMonitor", "Updates");
            }
        }
        catch { }

        return Path.Combine(Path.GetTempPath(), "StartupMonitorUpdates");
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_isDisposed) return;
            _isDisposed = true;
            _checkTimer?.Dispose();
            _downloadCts?.Cancel();
            _downloadCts?.Dispose();
            _httpClient.Dispose();
        }
    }
}
