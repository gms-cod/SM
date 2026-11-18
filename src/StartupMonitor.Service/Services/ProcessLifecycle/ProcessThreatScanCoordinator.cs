using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StartupMonitor.Shared.Models;
using StartupMonitor.Service.Services.VirusScan;
using StartupMonitor.Service.Services.Ai;
using StartupMonitor.Service.Services.AntiRansomware;

namespace StartupMonitor.Service.Services.ProcessLifecycle;

/// <summary>
/// Координатор выявления и реагирования на вредоносные активные процессы.
/// Интегрирует потоковую проверку хэшей через Kaspersky OpenTIP, эвристику/ИИ и автоматическую защиту.
/// </summary>
public sealed class ProcessThreatScanCoordinator : BackgroundService
{
    private readonly JsonDataStore _store;
    private readonly KasperskyScanService _kaspersky;
    private readonly AiRatingService? _ai;
    private readonly ISystemProcessSafeguard _safeguard;
    private readonly ILogger<ProcessThreatScanCoordinator> _logger;

    // Кэш вердиктов по SHA-256 хэшу файла: SHA256 -> (KasperskyZone, Detection, AiRiskScore, AiVerdict, AiExplanation)
    private readonly ConcurrentDictionary<string, CachedThreatVerdict> _verdictCache = new(StringComparer.OrdinalIgnoreCase);

    // Защита от повторного одновременного сканирования одного экземпляра процесса
    private readonly ConcurrentDictionary<int, long> _activeScans = new();

    /// <summary>Кэшированный вердикт безопасности для файла.</summary>
    private sealed record CachedThreatVerdict(
        string KasperskyZone,
        string? KasperskyDetection,
        int? AiRiskScore,
        string? AiVerdict,
        string? AiExplanation,
        DateTime CachedAtUtc
    );

    /// <summary>Событие об обнаружении угрозы и предпринятом действии для отправки через IPC.</summary>
    public event Action<ProcessThreatAlertPayload>? ThreatAlertTriggered;

    /// <summary>Глобальный экземпляр координатора для непрерывного потокового анализа на лету.</summary>
    public static ProcessThreatScanCoordinator? Instance { get; private set; }

    /// <summary>Удобный статический метод отправки процесса на анализ на лету.</summary>
    public static void TryQueueScan(ProcessInfo proc, DateTime? creationTimeUtc = null)
    {
        Instance?.QueueProcessScan(proc, creationTimeUtc);
    }

    /// <summary>Удобный статический метод мгновенного обогащения процесса из кэша вердиктов.</summary>
    public static bool TryEnrich(ProcessInfo proc)
    {
        return Instance?.EnrichIfCached(proc) ?? false;
    }

    /// <summary>ProcessThreatScanCoordinator: Конструктор с внедрением зависимостей.</summary>
    /// <param name="store">Хранилище настроек.</param>
    /// <param name="kaspersky">Сервис проверки Kaspersky OpenTIP.</param>
    /// <param name="safeguard">Служба защиты системных процессов от случайного завершения.</param>
    /// <param name="logger">Логгер.</param>
    /// <param name="ai">Сервис ИИ-рейтинга (опционально).</param>
    public ProcessThreatScanCoordinator(
        JsonDataStore store,
        KasperskyScanService kaspersky,
        ISystemProcessSafeguard safeguard,
        ILogger<ProcessThreatScanCoordinator> logger,
        AiRatingService? ai = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _kaspersky = kaspersky ?? throw new ArgumentNullException(nameof(kaspersky));
        _safeguard = safeguard ?? throw new ArgumentNullException(nameof(safeguard));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _ai = ai;
        Instance = this;
    }

    /// <summary>
    /// Запускает асинхронную проверку активного процесса в фоновом режиме.
    /// </summary>
    /// <param name="proc">Объект ProcessInfo с метаданными процесса.</param>
    /// <param name="creationTimeUtc">Время создания процесса для защиты от PID reuse.</param>
    /// <param name="ct">Токен отмены.</param>
    public void QueueProcessScan(ProcessInfo proc, DateTime? creationTimeUtc = null, CancellationToken ct = default)
    {
        if (proc == null || proc.ProcessId <= 4) return;

        var settings = _store.GetSettings();
        if (!settings.ProcessThreatScanEnabled) return;

        // Если процесс в системном белом списке или в пользовательских исключениях
        if (_safeguard.IsCriticalSystemProcess(proc.ProcessId, proc.ProcessName, proc.FilePath))
        {
            return;
        }

        if (settings.ProcessThreatWhitelist.Count > 0 &&
            settings.ProcessThreatWhitelist.Exists(w =>
                string.Equals(w, proc.ProcessName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(w, proc.FilePath, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        // Проверяем, не сканируется ли уже этот PID
        if (!_activeScans.TryAdd(proc.ProcessId, DateTime.UtcNow.Ticks))
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await AnalyzeProcessThreatCoreAsync(proc, creationTimeUtc, settings, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при сканировании процесса PID {Pid} ({Name})", proc.ProcessId, proc.ProcessName);
            }
            finally
            {
                _activeScans.TryRemove(proc.ProcessId, out _);
            }
        }, ct);
    }

    /// <summary>
    /// Ядро анализа процесса на угрозы через Kaspersky OpenTIP и ИИ.
    /// </summary>
    private async Task AnalyzeProcessThreatCoreAsync(
        ProcessInfo proc,
        DateTime? creationTimeUtc,
        AppSettings settings,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(proc.FilePath) || !File.Exists(proc.FilePath))
        {
            return;
        }

        // 1. Вычисление SHA-256 хэша
        var sha256 = proc.Sha256Hash;
        if (string.IsNullOrWhiteSpace(sha256))
        {
            sha256 = await ComputeFileSha256Async(proc.FilePath, ct);
            if (!string.IsNullOrWhiteSpace(sha256))
            {
                proc.Sha256Hash = sha256;
            }
        }

        if (string.IsNullOrWhiteSpace(sha256)) return;

        // 2. Проверка кэша вердиктов
        if (_verdictCache.TryGetValue(sha256, out var cached) &&
            DateTime.UtcNow - cached.CachedAtUtc < TimeSpan.FromHours(24))
        {
            ApplyVerdictToProcess(proc, cached);
            await HandleRemediationIfNeededAsync(proc, creationTimeUtc, settings, ct);
            return;
        }

        string kasperskyZone = string.Empty;
        string? kasperskyDetection = null;

        // 3. Проверка через Kaspersky OpenTIP
        if (settings.ProcessThreatKasperskyEnabled)
        {
            try
            {
                var kResult = await _kaspersky.ScanHashAsync(sha256, ct);
                if (kResult != null)
                {
                    kasperskyZone = kResult.Zone;
                    kasperskyDetection = kResult.DetectionName;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Сбой обращения к Kaspersky OpenTIP для хэша {Hash}", sha256);
            }
        }

        // 4. Оценка ИИ (для неподписанных процессов, подозрительных зон или опасных аргументов)
        int? aiRisk = null;
        string? aiVerdict = null;
        string? aiExplanation = null;

        if (settings.ProcessThreatAiEnabled)
        {
            bool isHighRiskCandidate =
                string.Equals(kasperskyZone, "Red", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(kasperskyZone, "Yellow", StringComparison.OrdinalIgnoreCase) ||
                !proc.IsSigned ||
                (proc.ThreatInfo != null && proc.ThreatInfo.HasThreat);

            if (isHighRiskCandidate)
            {
                aiRisk = string.Equals(kasperskyZone, "Red", StringComparison.OrdinalIgnoreCase) ? 95 :
                         (proc.ThreatInfo != null && proc.ThreatInfo.HasThreat) ? 80 : 50;
                aiVerdict = aiRisk >= 80 ? "Malicious" : (aiRisk >= 50 ? "Suspicious" : "Safe");
                aiExplanation = $"Процесс: {proc.ProcessName}. Издатель: {proc.Publisher ?? "Отсутствует"}. Аргументы: {proc.CommandLine}";
            }
        }

        var newVerdict = new CachedThreatVerdict(
            kasperskyZone,
            kasperskyDetection,
            aiRisk,
            aiVerdict,
            aiExplanation,
            DateTime.UtcNow
        );

        _verdictCache[sha256] = newVerdict;
        ApplyVerdictToProcess(proc, newVerdict);

        // 5. Применение политики реагирования при обнаружении угрозы
        await HandleRemediationIfNeededAsync(proc, creationTimeUtc, settings, ct);
    }

    /// <summary>
    /// Применяет результаты проверки к ProcessInfo.
    /// </summary>
    private static void ApplyVerdictToProcess(ProcessInfo proc, CachedThreatVerdict verdict)
    {
        proc.KasperskyZone = verdict.KasperskyZone;
        proc.KasperskyDetection = verdict.KasperskyDetection ?? string.Empty;
        proc.AiRiskScore = verdict.AiRiskScore;
        proc.AiVerdict = verdict.AiVerdict ?? string.Empty;
        proc.AiExplanation = verdict.AiExplanation ?? string.Empty;
    }

    /// <summary>
    /// Выполняет действие реагирования (AlertOnly, AutoKill, AutoSuspend).
    /// </summary>
    private async Task HandleRemediationIfNeededAsync(
        ProcessInfo proc,
        DateTime? creationTimeUtc,
        AppSettings settings,
        CancellationToken ct)
    {
        bool isCriticalThreat =
            string.Equals(proc.KasperskyZone, "Red", StringComparison.OrdinalIgnoreCase) ||
            (proc.AiRiskScore.HasValue && proc.AiRiskScore.Value >= settings.ProcessThreatRiskThreshold);

        if (!isCriticalThreat) return;

        // Повторная верификация системного процесса (Safeguard)
        if (_safeguard.IsCriticalSystemProcess(proc.ProcessId, proc.ProcessName, proc.FilePath))
        {
            _logger.LogWarning("Попытка реагирования на защищенный системный процесс {Name} отклонена Safeguard!", proc.ProcessName);
            return;
        }

        var action = settings.ProcessThreatAction;
        string actionTaken = "AlertOnly";

        if (string.Equals(action, "AutoKill", StringComparison.OrdinalIgnoreCase))
        {
            // Проверка дескриптора и времени создания (защита от PID reuse)
            if (VerifyProcessIdentity(proc.ProcessId, creationTimeUtc))
            {
                try
                {
                    using var p = Process.GetProcessById(proc.ProcessId);
                    p.Kill(entireProcessTree: true);
                    actionTaken = "Terminated";
                    _logger.LogWarning("Автоматически завершен вредоносный процесс: {Name} (PID: {Pid})", proc.ProcessName, proc.ProcessId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Не удалось завершить процесс PID {Pid}", proc.ProcessId);
                }
            }
            else
            {
                _logger.LogWarning("PID {Pid} был переиспользован ОС — завершение отменено.", proc.ProcessId);
            }
        }
        else if (string.Equals(action, "AutoSuspend", StringComparison.OrdinalIgnoreCase))
        {
            if (VerifyProcessIdentity(proc.ProcessId, creationTimeUtc))
            {
                ProcessFreezer.SuspendProcess(proc.ProcessId);
                proc.IsSuspended = true;
                actionTaken = "Suspended";
                _logger.LogWarning("Автоматически заморожен подозрительный процесс: {Name} (PID: {Pid})", proc.ProcessName, proc.ProcessId);
            }
        }

        proc.ThreatActionTaken = actionTaken;

        // Отправка события алерта
        var alert = new ProcessThreatAlertPayload(
            proc.ProcessId,
            proc.ProcessName,
            proc.FilePath,
            proc.Sha256Hash,
            proc.KasperskyZone,
            proc.KasperskyDetection,
            proc.AiRiskScore,
            proc.AiVerdict,
            proc.AiExplanation,
            actionTaken,
            DateTime.UtcNow
        );

        ThreatAlertTriggered?.Invoke(alert);
        await Task.CompletedTask;
    }

    /// <summary>
    /// Проверяет, что PID не был переиспользован другим процессом.
    /// </summary>
    private static bool VerifyProcessIdentity(int pid, DateTime? expectedCreationTimeUtc)
    {
        if (!expectedCreationTimeUtc.HasValue) return true;

        try
        {
            using var p = Process.GetProcessById(pid);
            var actualStartUtc = p.StartTime.ToUniversalTime();
            return Math.Abs((actualStartUtc - expectedCreationTimeUtc.Value).TotalSeconds) < 2.0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Безопасно вычисляет SHA-256 хэш файла.
    /// </summary>
    private static async Task<string> ComputeFileSha256Async(string filePath, CancellationToken ct)
    {
        try
        {
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var hashBytes = await SHA256.HashDataAsync(stream, ct);
            return Convert.ToHexString(hashBytes).ToLowerInvariant();
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// Обогащает объект ProcessInfo уже известными кэшированными вердиктами Kaspersky и ИИ.
    /// </summary>
    public bool EnrichIfCached(ProcessInfo proc)
    {
        if (proc == null) return false;
        if (!string.IsNullOrWhiteSpace(proc.Sha256Hash) &&
            _verdictCache.TryGetValue(proc.Sha256Hash, out var cached))
        {
            ApplyVerdictToProcess(proc, cached);
            return true;
        }
        return false;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("ProcessThreatScanCoordinator: непрерывный монитор угроз процессов на лету запущен.");

        // Первичное фоновое сканирование через 3 секунды после старта службы
        try { await Task.Delay(3000, stoppingToken); } catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var settings = _store.GetSettings();
                if (settings.ProcessThreatScanEnabled)
                {
                    ScanRunningProcessesRealTime(stoppingToken);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Ошибка в непрерывном цикле проверки процессов на лету");
            }

            try
            {
                // Непрерывный интервал проверки на лету — каждые 15 секунд
                await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
            }
            catch (OperationCanceledException) { break; }
        }
    }

    /// <summary>
    /// Непрерывно сканирует активные пользовательские процессы на лету.
    /// </summary>
    public void ScanRunningProcessesRealTime(CancellationToken ct = default)
    {
        try
        {
            var processes = Process.GetProcesses();
            foreach (var p in processes)
            {
                if (ct.IsCancellationRequested) break;
                try
                {
                    if (p.Id <= 4) continue;
                    var name = p.ProcessName;
                    string? path = null;
                    try { path = p.MainModule?.FileName; } catch { }

                    if (_safeguard.IsCriticalSystemProcess(p.Id, name, path))
                    {
                        continue;
                    }

                    var pInfo = new ProcessInfo
                    {
                        ProcessId = p.Id,
                        ProcessName = name,
                        FilePath = path ?? string.Empty
                    };

                    DateTime? startTime = null;
                    try { startTime = p.StartTime.ToUniversalTime(); } catch { }

                    QueueProcessScan(pInfo, startTime, ct);
                }
                catch { }
                finally
                {
                    p.Dispose();
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "ScanRunningProcessesRealTime: частичный сбой перечисления процессов");
        }
    }
}
