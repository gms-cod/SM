using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using System.Text.Json.Serialization;
using StartupMonitor.Shared.Models;
using StartupMonitor.Shared.Models.Firewall;
using StartupMonitor.Shared.Diagnostics;
using StartupMonitor.Shared.Models.Snapshot;

namespace StartupMonitor.UI.Services;

/// <summary>Клиент именованного канала (Named Pipe) для общения со службой StartupMonitor.Service.</summary>
public class PipeClient : IDisposable
{
    /// <summary>Основной канал для отправки запросов и получения ответов.</summary>
    private NamedPipeClientStream? _pipe;

    /// <summary>Читатель основного канала.</summary>
    private StreamReader? _reader;

    /// <summary>Писатель основного канала.</summary>
    private StreamWriter? _writer;

    /// <summary>Имя основного канала (совпадает с именем на стороне службы).</summary>
    private readonly string _pipeName = "StartupMonitor";

    /// <summary>Настройки JSON-сериализации (регистронезависимые имена свойств).</summary>
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>Событие подключения к каналу.</summary>
    public event EventHandler? Connected;

    /// <summary>Событие отключения от канала.</summary>
    public event EventHandler? Disconnected;

    /// <summary>Событие получения батча сообщений от службы (event-pipe).</summary>
    public event EventHandler<List<PipeMessage>>? EventsReceived;

    /// <summary>Событие получения среза метрик процессов для Диспетчера задач.</summary>
    public event EventHandler<SystemPerformanceSnapshot>? ProcessSnapshotReceived;

    /// <summary>Событие обновления прогресса загрузки дистрибутива обновления.</summary>
    public event EventHandler<UpdateProgressInfo>? UpdateProgressReceived;

    /// <summary>Флаг подключения к основному каналу.</summary>
    public bool IsConnected => _pipe?.IsConnected ?? false;

    /// <summary>Семафор для последовательной отправки запросов (один запрос за раз).</summary>
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    /// <summary>Максимальное время ожидания ответа от службы (70 с — с запасом на сканирование).</summary>
    private static readonly TimeSpan ResponseTimeout = TimeSpan.FromSeconds(70);

    /// <summary>Отдельный канал для приёма событий от службы.</summary>
    private NamedPipeClientStream? _eventPipe;

    /// <summary>Читатель event-канала.</summary>
    private StreamReader? _eventReader;

    /// <summary>Токен отмены для цикла чтения event-канала.</summary>
    private CancellationTokenSource? _eventCts;

    /// <summary>Подключается к службе по именованному каналу. В случае успеха запускает event-pipe.</summary>
    public async Task ConnectAsync()
    {
        try
        {
            _pipe?.Dispose();
            _pipe = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            await _pipe.ConnectAsync(3000);

            _reader = new StreamReader(_pipe);
            _writer = new StreamWriter(_pipe) { AutoFlush = true };

            Connected?.Invoke(this, EventArgs.Empty);
            StartEventPipe();
        }
        catch (Exception)
        {
            _pipe?.Dispose();
            _pipe = null;
            Disconnected?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Запускает фоновое чтение event-канала для получения асинхронных уведомлений от службы.</summary>
    private void StartEventPipe()
    {
        _eventCts?.Cancel();
        _eventCts?.Dispose();
        _eventPipe?.Dispose();
        _eventCts = new CancellationTokenSource();
        _ = ConnectAndReadEventsAsync(_eventCts);
    }

    /// <summary>Фоновый цикл: подключается к event-каналу и читает сообщения до отмены или разрыва.</summary>
    /// <param name="cts">Токен отмены для остановки цикла.</param>
    private async Task ConnectAndReadEventsAsync(CancellationTokenSource cts)
    {
        // Цикл жизни соединения: сервер закрыл pipe или ошибка — пересоздаём и подключаемся заново
        while (!cts.IsCancellationRequested)
        {
            try
            {
                _eventPipe?.Dispose();
                _eventPipe = new NamedPipeClientStream(".", "StartupMonitorEvents", PipeDirection.In, PipeOptions.Asynchronous);
                await _eventPipe.ConnectAsync(3000, cts.Token);
                _eventReader = new StreamReader(_eventPipe);

                while (!cts.IsCancellationRequested && _eventPipe.IsConnected)
                {
                    var line = await _eventReader.ReadLineAsync(cts.Token);
                    if (line == null) break;

                    List<PipeMessage>? batch = null;
                    try
                    {
                        batch = JsonSerializer.Deserialize<List<PipeMessage>>(line, JsonOptions);
                    }
                    catch
                    {
                        continue;
                    }

                    if (batch != null && batch.Count > 0)
                    {
                        EventsReceived?.Invoke(this, batch);
                        foreach (var msg in batch)
                        {
                            if (msg.Type == PipeMessageTypes.ProcessesSnapshot && !string.IsNullOrEmpty(msg.Payload))
                            {
                                try
                                {
                                    var snapshot = JsonSerializer.Deserialize<SystemPerformanceSnapshot>(msg.Payload, JsonOptions);
                                    if (snapshot != null)
                                        ProcessSnapshotReceived?.Invoke(this, snapshot);
                                }
                                catch { }
                            }
                            else if (msg.Type == PipeMessageTypes.DownloadUpdateProgress && !string.IsNullOrEmpty(msg.Payload))
                            {
                                try
                                {
                                    var prog = JsonSerializer.Deserialize<UpdateProgressInfo>(msg.Payload, JsonOptions);
                                    if (prog != null)
                                        UpdateProgressReceived?.Invoke(this, prog);
                                }
                                catch { }
                            }
                        }
                    }
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception) { }

            try { await Task.Delay(2000, cts.Token); }
            catch (OperationCanceledException) { break; }
        }
    }

    /// <summary>Отправляет запрос службе и ожидает ответ с таймаутом.</summary>
    /// <param name="request">Сообщение-запрос.</param>
    /// <param name="ct">Токен отмены операции.</param>
    /// <returns>Ответ службы или null при таймауте/разрыве.</returns>
    public async Task<PipeMessage?> SendAsync(PipeMessage request, CancellationToken ct = default)
    {
        using var _prof = ExecutionProfiler.Measure("UI.Pipe.Send." + request.Type);
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(10));
        try
        {
            await _sendLock.WaitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        try
        {
            if (_writer == null || _pipe?.IsConnected != true)
                return null;

            var json = JsonSerializer.Serialize(request, JsonOptions);
            await _writer.WriteLineAsync(json.AsMemory(), ct);
            await _writer.FlushAsync(ct);

            // Таймаут: если сервис завис, не ждём вечно — рвём соединение и переподключаемся
            using var responseCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            responseCts.CancelAfter(ResponseTimeout);
            string? response;
            try
            {
                response = await _reader!.ReadLineAsync(responseCts.Token);
            }
            catch (OperationCanceledException)
            {
                if (!ct.IsCancellationRequested)
                {
                    try { _pipe?.Dispose(); } catch { }
                    Disconnected?.Invoke(this, EventArgs.Empty);
                }
                return null;
            }
            if (response == null) return null;

            return JsonSerializer.Deserialize<PipeMessage>(response, JsonOptions);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        finally
        {
            try
            {
                _sendLock.Release();
            }
            catch (ObjectDisposedException) { }
        }
    }

    /// <summary>Запрашивает у службы полный список записей автозапуска.</summary>
    public async Task<List<StartupEntry>> GetAllEntriesAsync()
    {
        var response = await SendAsync(new PipeMessage
        {
            Type = PipeMessageTypes.GetAllEntries
        });

        if (response?.Type == PipeMessageTypes.GetAllEntriesResponse && !string.IsNullOrEmpty(response.Payload))
        {
            return JsonSerializer.Deserialize<List<StartupEntry>>(response.Payload, JsonOptions) ?? [];
        }

        return [];
    }

    /// <summary>
    /// Запрашивает дельту записей автозапуска по известной клиенту ревизии.
    /// Быстрый путь опроса: при отсутствии изменений сервер отвечает NotModified
    /// без сериализации списка (такт 0.5–1 мс, 0 аллокаций в LOH).
    /// </summary>
    /// <param name="currentRevision">Ревизия, известная клиенту (0 = первая синхронизация).</param>
    /// <returns>(isModified, newRevision, entries). При isModified=false entries=null.</returns>
    public async Task<(bool IsModified, long NewRevision, List<StartupEntry>? Entries)> GetEntriesDeltaAsync(long currentRevision)
    {
        try
        {
            var response = await SendAsync(new PipeMessage
            {
                Type = PipeMessageTypes.GetEntriesDelta,
                Payload = JsonSerializer.Serialize(new EntriesRevisionRequest(currentRevision), JsonOptions)
            });

            if (response == null)
                return await FallbackFullEntriesAsync(currentRevision);

            // Быстрый путь: изменений нет — мгновенный выход, 0 аллокаций в LOH
            if (response.Type == PipeMessageTypes.NotModified)
                return (false, currentRevision, null);

            if (response.Type == PipeMessageTypes.GetEntriesDeltaResponse && !string.IsNullOrEmpty(response.Payload))
            {
                try
                {
                    var delta = JsonSerializer.Deserialize<EntriesDeltaResponse>(response.Payload, JsonOptions);
                    if (delta != null && delta.IsModified)
                        return (true, delta.NewRevision, delta.Entries);
                    if (delta != null && !delta.IsModified)
                        return (false, delta.NewRevision, null);
                }
                catch { }
                return await FallbackFullEntriesAsync(currentRevision);
            }

            // Старая версия сервера (не знает GetEntriesDelta): fallback на полный список
            return await FallbackFullEntriesAsync(currentRevision);
        }
        catch
        {
            return await FallbackFullEntriesAsync(currentRevision);
        }
    }

    /// <summary>Fallback: полный список записей при ошибке дельты или старой версии сервера.</summary>
    private async Task<(bool IsModified, long NewRevision, List<StartupEntry>? Entries)> FallbackFullEntriesAsync(long currentRevision)
    {
        try
        {
            var entries = await GetAllEntriesAsync();
            // Ревизию не обновляем (сервер её не сообщил): следующий такт снова попробует дельту
            return (true, currentRevision, entries);
        }
        catch
        {
            return (false, currentRevision, null);
        }
    }

    /// <summary>Запрашивает текущий чёрный список.</summary>
    public async Task<List<BlacklistRule>> GetBlacklistAsync()
    {
        var response = await SendAsync(new PipeMessage
        {
            Type = PipeMessageTypes.GetBlacklist
        });

        if (response?.Type == PipeMessageTypes.GetBlacklistResponse && !string.IsNullOrEmpty(response.Payload))
        {
            return JsonSerializer.Deserialize<List<BlacklistRule>>(response.Payload, JsonOptions) ?? [];
        }

        return [];
    }

    /// <summary>Отключает (помечает Disabled) запись автозапуска.</summary>
    public async Task DisableEntryAsync(string entryId)
    {
        await SendAsync(new PipeMessage
        {
            Type = PipeMessageTypes.DisableEntry,
            Payload = JsonSerializer.Serialize(entryId, JsonOptions)
        });
    }

    /// <summary>Включает (помечает Enabled) запись автозапуска.</summary>
    public async Task EnableEntryAsync(string entryId)
    {
        await SendAsync(new PipeMessage
        {
            Type = PipeMessageTypes.EnableEntry,
            Payload = JsonSerializer.Serialize(entryId, JsonOptions)
        });
    }

    /// <summary>Добавляет правило в чёрный список на стороне службы.</summary>
    public async Task AddToBlacklistAsync(BlacklistRule rule)
    {
        await SendAsync(new PipeMessage
        {
            Type = PipeMessageTypes.AddToBlacklist,
            Payload = JsonSerializer.Serialize(rule, JsonOptions)
        });
    }

    /// <summary>Удаляет правило из чёрного списка по его идентификатору.</summary>
    public async Task RemoveFromBlacklistAsync(string ruleId)
    {
        await SendAsync(new PipeMessage
        {
            Type = PipeMessageTypes.RemoveFromBlacklist,
            Payload = JsonSerializer.Serialize(ruleId, JsonOptions)
        });
    }

    /// <summary>Запрашивает текстовый статус службы.</summary>
    public async Task<string?> GetStatusAsync()
    {
        var response = await SendAsync(new PipeMessage { Type = PipeMessageTypes.GetStatus });

        if (response?.Type == PipeMessageTypes.StatusResponse && !string.IsNullOrEmpty(response.Payload))
            return response.Payload;

        return null;
    }

    /// <summary>Удаляет запись из системы (реестр/файл) через службу.</summary>
    public async Task<bool> RemoveFromSystemAsync(string entryId)
    {
        var response = await SendAsync(new PipeMessage
        {
            Type = PipeMessageTypes.RemoveFromSystem,
            Payload = JsonSerializer.Serialize(entryId, JsonOptions)
        });
        return response?.Type == PipeMessageTypes.Success;
    }

    /// <summary>Запрашивает список удалённых записей.</summary>
    public async Task<List<RemovedEntry>> GetRemovedEntriesAsync()
    {
        var response = await SendAsync(new PipeMessage { Type = PipeMessageTypes.GetRemovedEntries });
        if (response?.Type == PipeMessageTypes.GetRemovedEntriesResponse && !string.IsNullOrEmpty(response.Payload))
        {
            return JsonSerializer.Deserialize<List<RemovedEntry>>(response.Payload, JsonOptions) ?? [];
        }
        return [];
    }

    /// <summary>Запрашивает историю изменений.</summary>
    public async Task<List<HistoryEntry>> GetHistoryAsync()
    {
        var response = await SendAsync(new PipeMessage { Type = PipeMessageTypes.GetHistory });
        if (response?.Type == PipeMessageTypes.GetHistoryResponse && !string.IsNullOrEmpty(response.Payload))
        {
            return JsonSerializer.Deserialize<List<HistoryEntry>>(response.Payload, JsonOptions) ?? [];
        }
        return [];
    }

    /// <summary>Запрашивает словарь AI-рейтингов (Id → StartupRating).</summary>
    public async Task<Dictionary<string, StartupRating>> GetRatingsAsync()
    {
        var response = await SendAsync(new PipeMessage { Type = PipeMessageTypes.GetRatings });
        if (response?.Type == PipeMessageTypes.GetRatingsResponse && !string.IsNullOrEmpty(response.Payload))
        {
            return JsonSerializer.Deserialize<Dictionary<string, StartupRating>>(response.Payload, JsonOptions) ?? [];
        }
        return [];
    }

    /// <summary>Запускает очистку устаревших записей на стороне службы.</summary>
    public async Task CleanStaleAsync()
    {
        await SendAsync(new PipeMessage { Type = PipeMessageTypes.CleanStale });
    }

    /// <summary>Отправляет службе настройки для AI-рейтинга (ключ API, URL, модель).</summary>
    public async Task SetAiConfigAsync(string apiKey, string? apiUrl = null, string? model = null)
    {
        var payload = JsonSerializer.Serialize(new AiConfigPayload(apiKey, apiUrl, model), JsonOptions);
        await SendAsync(new PipeMessage
        {
            Type = PipeMessageTypes.SetAiConfig,
            Payload = payload
        });
    }

    /// <summary>Проверяет подключение к AI-провайдеру через службу.</summary>
    public async Task<(bool Success, string Message)> TestAiConnectionAsync(string apiKey, string? apiUrl = null, string? model = null)
    {
        var payload = JsonSerializer.Serialize(new AiConfigPayload(apiKey, apiUrl, model), JsonOptions);
        var response = await SendAsync(new PipeMessage
        {
            Type = PipeMessageTypes.TestAiConnection,
            Payload = payload
        });

        if (response?.Type == PipeMessageTypes.TestAiConnectionResponse && !string.IsNullOrEmpty(response.Payload))
        {
            try
            {
                var result = JsonSerializer.Deserialize<TestAiResultPayload>(response.Payload, JsonOptions);
                if (result != null)
                    return (result.Success, result.Message);
            }
            catch { }
        }

        return (false, LocalizationService.Instance["PipeAiTestNoResult"]);
    }

    /// <summary>Отправляет конфигурацию удалённых уведомлений (Telegram / Discord) службе.</summary>
    public async Task SetRemoteNotificationConfigAsync(
        bool telegramEnabled,
        string telegramBotToken,
        string telegramChatId,
        bool discordWebhookEnabled,
        string discordWebhookUrl,
        bool notifyOnNewEntry,
        bool notifyOnThreat,
        bool notifyOnBlocked)
    {
        var payload = JsonSerializer.Serialize(new RemoteNotificationConfigPayload(
            telegramEnabled,
            telegramBotToken,
            telegramChatId,
            discordWebhookEnabled,
            discordWebhookUrl,
            notifyOnNewEntry,
            notifyOnThreat,
            notifyOnBlocked), JsonOptions);

        await SendAsync(new PipeMessage
        {
            Type = PipeMessageTypes.SetRemoteNotificationConfig,
            Payload = payload
        });
    }

    /// <summary>Запрашивает проверку канала удалённых уведомлений ("telegram", "discord" или "all").</summary>
    public async Task<(bool Success, string Message)> TestRemoteNotificationAsync(string channel)
    {
        var payload = JsonSerializer.Serialize(new TestRemoteNotificationPayload(channel), JsonOptions);
        var response = await SendAsync(new PipeMessage
        {
            Type = PipeMessageTypes.TestRemoteNotification,
            Payload = payload
        });

        if (response?.Type == PipeMessageTypes.TestRemoteNotificationResponse && !string.IsNullOrEmpty(response.Payload))
        {
            try
            {
                var result = JsonSerializer.Deserialize<TestRemoteNotificationResultPayload>(response.Payload, JsonOptions);
                if (result != null)
                    return (result.Success, result.Message);
            }
            catch { }
        }

        return (false, LocalizationService.Instance["PipeRemoteTestNoResult"]);
    }

    /// <summary>Отправляет конфигурацию Kaspersky OpenTIP службе.</summary>
    public async Task SetKasperskyConfigAsync(bool enabled, string apiKey, string apiUrl)
    {
        var payload = JsonSerializer.Serialize(new KasperskyConfigDto
        {
            Enabled = enabled,
            ApiKey = apiKey,
            ApiUrl = apiUrl
        }, JsonOptions);

        await SendAsync(new PipeMessage
        {
            Type = PipeMessageTypes.SetKasperskyConfig,
            Payload = payload
        });
    }

    /// <summary>Отправляет конфигурацию автоматических политик защиты Auto-Guards службе.</summary>
    public async Task SetAutoGuardConfigAsync(AutoGuardConfigPayload config)
    {
        var payload = JsonSerializer.Serialize(config, JsonOptions);
        await SendAsync(new PipeMessage
        {
            Type = PipeMessageTypes.SetAutoGuardConfig,
            Payload = payload
        });
    }

    /// <summary>Проверяет подключение к API Kaspersky OpenTIP через службу.</summary>
    public async Task<KasperskyTestResult> TestKasperskyConnectionAsync(string apiKey, string apiUrl)
    {
        var payload = JsonSerializer.Serialize(new KasperskyConfigDto
        {
            Enabled = true,
            ApiKey = apiKey,
            ApiUrl = apiUrl
        }, JsonOptions);

        var response = await SendAsync(new PipeMessage
        {
            Type = PipeMessageTypes.TestKasperskyConnection,
            Payload = payload
        });

        if (response?.Type == PipeMessageTypes.TestKasperskyConnectionResponse && !string.IsNullOrEmpty(response.Payload))
        {
            try
            {
                var result = JsonSerializer.Deserialize<KasperskyTestResult>(response.Payload, JsonOptions);
                if (result != null) return result;
            }
            catch { }
        }

        return new KasperskyTestResult
        {
            Success = false,
            Message = LocalizationService.Instance["PipeKasperskyTestNoResult"]
        };
    }

    /// <summary>Запрашивает сканирование отдельной записи через Kaspersky OpenTIP.</summary>
    public async Task<StartupEntry?> ScanKasperskyEntryAsync(string entryId)
    {
        var response = await SendAsync(new PipeMessage
        {
            Type = PipeMessageTypes.ScanKasperskyEntry,
            Payload = JsonSerializer.Serialize(entryId, JsonOptions)
        });

        if (response?.Type == PipeMessageTypes.ScanKasperskyEntryResponse && !string.IsNullOrEmpty(response.Payload))
        {
            try
            {
                return JsonSerializer.Deserialize<StartupEntry>(response.Payload, JsonOptions);
            }
            catch { }
        }

        return null;
    }

    /// <summary>Устанавливает настройку "убивать процесс при отключении".</summary>
    public async Task SetKillOnDisableAsync(bool value)
    {
        await SendAsync(new PipeMessage
        {
            Type = PipeMessageTypes.SetKillOnDisable,
            Payload = JsonSerializer.Serialize(value, JsonOptions)
        });
    }

    /// <summary>Отправляет настройки троттлинга сканирования реестра по загрузке CPU.</summary>
    public async Task SetCpuThrottleAsync(bool enabled, int threshold)
    {
        await SendAsync(new PipeMessage
        {
            Type = PipeMessageTypes.SetCpuThrottle,
            Payload = JsonSerializer.Serialize(new CpuThrottlePayload(enabled, threshold), JsonOptions)
        });
    }

    /// <summary>Отправляет настройку строгого режима (блокировка новых записей).</summary>
    public async Task SetStrictModeAsync(bool enabled)
    {
        await SendAsync(new PipeMessage
        {
            Type = PipeMessageTypes.SetStrictMode,
            Payload = JsonSerializer.Serialize(new StrictModePayload(enabled), JsonOptions)
        });
    }

    /// <summary>Синхронизирует язык интерфейса ("ru", "en") со службой.</summary>
    public async Task SetLanguageAsync(string lang)
    {
        await SendAsync(new PipeMessage
        {
            Type = PipeMessageTypes.SetLanguage,
            Payload = lang
        });
    }

    /// <summary>Запрашивает переоценку AI-рейтинга для указанной записи.</summary>
    public async Task ReRateEntryAsync(string entryId)
    {
        await SendAsync(new PipeMessage
        {
            Type = PipeMessageTypes.ReRateEntry,
            Payload = JsonSerializer.Serialize(entryId, JsonOptions)
        });
    }

    /// <summary>Восстанавливает удалённую запись в системе.</summary>
    public async Task<bool> RestoreEntryAsync(string removedId)
    {
        var response = await SendAsync(new PipeMessage
        {
            Type = PipeMessageTypes.RestoreEntry,
            Payload = JsonSerializer.Serialize(removedId, JsonOptions)
        });
        return response?.Type == PipeMessageTypes.Success;
    }

    /// <summary>Подписывается на регулярный поток метрик процессов (для вкладки Диспетчера задач).</summary>
    public async Task SubscribeProcessMetricsAsync()
    {
        await SendAsync(new PipeMessage
        {
            Type = PipeMessageTypes.SubscribeProcessMetrics
        });
    }

    /// <summary>Отписывается от потока метрик процессов (при уходе с вкладки, снижая CPU службы до 0%).</summary>
    public async Task UnsubscribeProcessMetricsAsync()
    {
        await SendAsync(new PipeMessage
        {
            Type = PipeMessageTypes.UnsubscribeProcessMetrics
        });
    }

    /// <summary>Запрашивает актуальный снимок процессов и системной нагрузки.</summary>
    public async Task<SystemPerformanceSnapshot?> GetProcessesAsync()
    {
        var response = await SendAsync(new PipeMessage
        {
            Type = PipeMessageTypes.GetProcesses
        });

        if (response?.Type == PipeMessageTypes.ProcessesSnapshot && !string.IsNullOrEmpty(response.Payload))
        {
            return JsonSerializer.Deserialize<SystemPerformanceSnapshot>(response.Payload, JsonOptions);
        }

        return null;
    }

    /// <summary>Завершает процесс по идентификатору (PID).</summary>
    public async Task<ProcessActionResultPayload?> KillProcessAsync(int pid, string? processName = null)
    {
        var response = await SendAsync(new PipeMessage
        {
            Type = PipeMessageTypes.KillProcessById,
            Payload = JsonSerializer.Serialize(new KillProcessPayload(pid, processName), JsonOptions)
        });

        if (!string.IsNullOrEmpty(response?.Payload))
        {
            try
            {
                return JsonSerializer.Deserialize<ProcessActionResultPayload>(response.Payload, JsonOptions);
            }
            catch { }
        }

        return new ProcessActionResultPayload(response?.Type == PipeMessageTypes.Success, response?.Payload ?? "");
    }

    /// <summary>Блокирует процесс в Чёрном списке и завершает его.</summary>
    public async Task<ProcessActionResultPayload?> BlockProcessAsync(int pid, string filePath, string? processName = null)
    {
        var response = await SendAsync(new PipeMessage
        {
            Type = PipeMessageTypes.BlockProcess,
            Payload = JsonSerializer.Serialize(new BlockProcessPayload(pid, filePath, processName), JsonOptions)
        });

        if (!string.IsNullOrEmpty(response?.Payload))
        {
            try
            {
                return JsonSerializer.Deserialize<ProcessActionResultPayload>(response.Payload, JsonOptions);
            }
            catch { }
        }

        return new ProcessActionResultPayload(response?.Type == PipeMessageTypes.Success, response?.Payload ?? "");
    }

    /// <summary>Приостанавливает выполнение процесса по PID.</summary>
    public async Task<ProcessActionResultPayload?> SuspendProcessAsync(int pid, string? processName = null)
    {
        var response = await SendAsync(new PipeMessage
        {
            Type = PipeMessageTypes.SuspendProcess,
            Payload = JsonSerializer.Serialize(new SuspendProcessPayload(pid, processName), JsonOptions)
        });

        if (!string.IsNullOrEmpty(response?.Payload))
        {
            try
            {
                return JsonSerializer.Deserialize<ProcessActionResultPayload>(response.Payload, JsonOptions);
            }
            catch { }
        }

        return new ProcessActionResultPayload(response?.Type == PipeMessageTypes.Success, response?.Payload ?? "");
    }

    /// <summary>Возобновляет выполнение ранее приостановленного процесса по PID.</summary>
    public async Task<ProcessActionResultPayload?> ResumeProcessAsync(int pid, string? processName = null)
    {
        var response = await SendAsync(new PipeMessage
        {
            Type = PipeMessageTypes.ResumeProcess,
            Payload = JsonSerializer.Serialize(new ResumeProcessPayload(pid, processName), JsonOptions)
        });

        if (!string.IsNullOrEmpty(response?.Payload))
        {
            try
            {
                return JsonSerializer.Deserialize<ProcessActionResultPayload>(response.Payload, JsonOptions);
            }
            catch { }
        }

        return new ProcessActionResultPayload(response?.Type == PipeMessageTypes.Success, response?.Payload ?? "");
    }

    /// <summary>Переводит запись в режим отложенного запуска с указанным интервалом задержки.</summary>
    public async Task<(bool Success, string? Error)> SetDelayedEntryAsync(string entryId, int delaySeconds = 30)
    {
        var response = await SendAsync(new PipeMessage
        {
            Type = PipeMessageTypes.SetDelayedEntry,
            Payload = JsonSerializer.Serialize(new SetDelayedEntryPayload(entryId, delaySeconds), JsonOptions)
        });

        if (response?.Type == PipeMessageTypes.Success)
            return (true, null);

        return (false, response?.Payload ?? LocalizationService.Instance["PipeDelaySetError"]);
    }

    /// <summary>Отменяет отложенный запуск записи и возвращает обычный автозапуск.</summary>
    public async Task<(bool Success, string? Error)> CancelDelayedEntryAsync(string entryId)
    {
        var response = await SendAsync(new PipeMessage
        {
            Type = PipeMessageTypes.CancelDelayedEntry,
            Payload = JsonSerializer.Serialize(new CancelDelayedEntryPayload(entryId), JsonOptions)
        });

        if (response?.Type == PipeMessageTypes.Success)
            return (true, null);

        return (false, response?.Payload ?? LocalizationService.Instance["PipeDelayCancelError"]);
    }

    /// <summary>Выполняет принудительный тестовый запуск отложенной программы.</summary>
    public async Task<(bool Success, string Message)> LaunchDelayedEntryAsync(string entryId)
    {
        var response = await SendAsync(new PipeMessage
        {
            Type = PipeMessageTypes.LaunchDelayedEntry,
            Payload = JsonSerializer.Serialize(new LaunchDelayedEntryPayload(entryId), JsonOptions)
        });

        if (response?.Type == PipeMessageTypes.Success)
            return (true, response.Payload ?? LocalizationService.Instance["PipeLaunchSuccess"]);

        return (false, response?.Payload ?? LocalizationService.Instance["PipeLaunchError"]);
    }

    /// <summary>Создаёт новый контрольный снимок автозапуска системы.</summary>
    public async Task<(bool Success, string? Error, BaselineSnapshot? Snapshot)> CreateSnapshotAsync(string? name = null, string? description = null)
    {
        var response = await SendAsync(new PipeMessage
        {
            Type = PipeMessageTypes.CreateSnapshot,
            Payload = JsonSerializer.Serialize(new CreateSnapshotPayload(name, description), JsonOptions)
        });

        if (response?.Type == PipeMessageTypes.Success && !string.IsNullOrEmpty(response.Payload))
        {
            try
            {
                var snapshot = JsonSerializer.Deserialize<BaselineSnapshot>(response.Payload, JsonOptions);
                return (true, null, snapshot);
            }
            catch (Exception ex)
            {
                return (false, string.Format(LocalizationService.Instance["PipeSnapshotParseError"], ex.Message), null);
            }
        }

        return (false, response?.Payload ?? LocalizationService.Instance["PipeSnapshotCreateError"], null);
    }

    /// <summary>Запрашивает список всех сохранённых контрольных снимков.</summary>
    public async Task<List<BaselineSnapshot>> GetSnapshotsAsync()
    {
        var response = await SendAsync(new PipeMessage
        {
            Type = PipeMessageTypes.GetSnapshots
        });

        if (response?.Type == PipeMessageTypes.Success && !string.IsNullOrEmpty(response.Payload))
        {
            try
            {
                var list = JsonSerializer.Deserialize<List<BaselineSnapshot>>(response.Payload, JsonOptions);
                return list ?? [];
            }
            catch
            {
                return [];
            }
        }

        return [];
    }

    /// <summary>Удаляет контрольный снимок по его Id.</summary>
    public async Task<(bool Success, string? Message)> DeleteSnapshotAsync(string snapshotId)
    {
        var response = await SendAsync(new PipeMessage
        {
            Type = PipeMessageTypes.DeleteSnapshot,
            Payload = JsonSerializer.Serialize(new DeleteSnapshotPayload(snapshotId), JsonOptions)
        });

        var ok = response?.Type == PipeMessageTypes.Success;
        return (ok, response?.Payload);
    }

    /// <summary>Сравнивает текущее состояние автозапуска со снимком («Было — Стало»).</summary>
    public async Task<(bool Success, string? Error, SnapshotComparisonResult? Result)> CompareSnapshotAsync(string snapshotId)
    {
        var response = await SendAsync(new PipeMessage
        {
            Type = PipeMessageTypes.CompareSnapshot,
            Payload = JsonSerializer.Serialize(new CompareSnapshotPayload(snapshotId), JsonOptions)
        });

        if (response?.Type == PipeMessageTypes.Success && !string.IsNullOrEmpty(response.Payload))
        {
            try
            {
                var result = JsonSerializer.Deserialize<SnapshotComparisonResult>(response.Payload, JsonOptions);
                return (true, null, result);
            }
            catch (Exception ex)
            {
                return (false, string.Format(LocalizationService.Instance["PipeCompareParseError"], ex.Message), null);
            }
        }

        return (false, response?.Payload ?? LocalizationService.Instance["PipeCompareError"], null);
    }

    /// <summary>Запрашивает у службы отчёт анализатора времени загрузки Windows (Boot Time Tracer).</summary>
    public async Task<StartupMonitor.Shared.Models.BootTime.BootTimeReport?> GetBootTimeReportAsync()
    {
        var response = await SendAsync(new PipeMessage
        {
            Type = PipeMessageTypes.GetBootTimeReport,
            Payload = "{}"
        });

        if (response?.Type == PipeMessageTypes.Success && !string.IsNullOrEmpty(response.Payload))
        {
            try
            {
                return JsonSerializer.Deserialize<StartupMonitor.Shared.Models.BootTime.BootTimeReport>(response.Payload, JsonOptions);
            }
            catch
            {
                return null;
            }
        }

        return null;
    }

    /// <summary>Приостанавливает глобальный мониторинг автозапуска в службе.</summary>
    public async Task<bool> PauseMonitoringAsync()
    {
        var response = await SendAsync(new PipeMessage
        {
            Type = PipeMessageTypes.PauseMonitoring,
            Payload = "{}"
        });
        return response?.Type == PipeMessageTypes.Success;
    }

    /// <summary>Возобновляет глобальный мониторинг автозапуска в службе.</summary>
    public async Task<bool> ResumeMonitoringAsync()
    {
        var response = await SendAsync(new PipeMessage
        {
            Type = PipeMessageTypes.ResumeMonitoring,
            Payload = "{}"
        });
        return response?.Type == PipeMessageTypes.Success;
    }

    /// <summary>Запрашивает текущее состояние мониторинга (активен / приостановлен).</summary>
    public async Task<MonitoringStatePayload?> GetMonitoringStateAsync()
    {
        var response = await SendAsync(new PipeMessage
        {
            Type = PipeMessageTypes.GetMonitoringState,
            Payload = "{}"
        });

        if (response?.Type == PipeMessageTypes.MonitoringStateResponse && !string.IsNullOrEmpty(response.Payload))
        {
            try
            {
                return JsonSerializer.Deserialize<MonitoringStatePayload>(response.Payload, JsonOptions);
            }
            catch
            {
                return null;
            }
        }
        return null;
    }

    /// <summary>Запрашивает расширенную информацию о службе (PID, версия, аптайм).</summary>
    public async Task<ServiceInfoPayload?> GetServiceInfoAsync()
    {
        var response = await SendAsync(new PipeMessage
        {
            Type = PipeMessageTypes.GetServiceInfo,
            Payload = "{}"
        });

        if (response?.Type == PipeMessageTypes.ServiceInfoResponse && !string.IsNullOrEmpty(response.Payload))
        {
            try
            {
                return JsonSerializer.Deserialize<ServiceInfoPayload>(response.Payload, JsonOptions);
            }
            catch
            {
                return null;
            }
        }
        return null;
    }

    /// <summary>Запрашивает список активных сетевых соединений (сокетов) для процесса (или всех процессов).</summary>
    public async Task<List<ProcessNetworkSocket>> GetProcessNetworkConnectionsAsync(int pid = -1)
    {
        var response = await SendAsync(new PipeMessage
        {
            Type = PipeMessageTypes.GetProcessNetworkConnections,
            Payload = pid > 0 ? pid.ToString() : "{}"
        });

        if (response?.Type == PipeMessageTypes.ProcessNetworkConnectionsResponse && !string.IsNullOrEmpty(response.Payload))
        {
            try
            {
                return JsonSerializer.Deserialize<List<ProcessNetworkSocket>>(response.Payload, JsonOptions) ?? [];
            }
            catch
            {
                return [];
            }
        }
        return [];
    }

    /// <summary>Запрашивает список загруженных модулей (DLL) и дескрипторов (Handles) для процесса.</summary>
    public async Task<ProcessModulesResponse?> GetProcessModulesAsync(int pid, bool includeHandles = true)
    {
        var request = new GetProcessModulesRequest
        {
            ProcessId = pid,
            IncludeHandles = includeHandles
        };

        var response = await SendAsync(new PipeMessage
        {
            Type = PipeMessageTypes.GetProcessModules,
            Payload = JsonSerializer.Serialize(request, JsonOptions)
        });

        if (response?.Type == PipeMessageTypes.ProcessModulesResponse && !string.IsNullOrEmpty(response.Payload))
        {
            try
            {
                return JsonSerializer.Deserialize<ProcessModulesResponse>(response.Payload, JsonOptions);
            }
            catch (Exception ex)
            {
                return new ProcessModulesResponse
                {
                    ProcessId = pid,
                    Success = false,
                    ErrorMessage = $"Ошибка десериализации ответа: {ex.Message}"
                };
            }
        }

        return new ProcessModulesResponse
        {
            ProcessId = pid,
            Success = false,
            ErrorMessage = response?.Payload ?? "Служба не вернула ответ на запрос модулей процесса."
        };
    }

    /// <summary>Запрашивает проверку наличия обновлений на GitHub через службу.</summary>
    public async Task<UpdateCheckResult?> CheckForUpdatesAsync()
    {
        var uiVersion = typeof(PipeClient).Assembly.GetName().Version?.ToString(3) ?? "1.3.2";
        var req = new CheckForUpdatesRequest(uiVersion);
        var response = await SendAsync(new PipeMessage
        {
            Type = PipeMessageTypes.CheckForUpdates,
            Payload = JsonSerializer.Serialize(req, JsonOptions)
        });
        if (response?.Type == PipeMessageTypes.CheckForUpdatesResponse && !string.IsNullOrWhiteSpace(response.Payload))
        {
            try
            {
                return JsonSerializer.Deserialize<UpdateCheckResult>(response.Payload, JsonOptions);
            }
            catch { }
        }
        return null;
    }

    /// <summary>Отправляет команду на скачивание пакета обновления.</summary>
    public async Task<bool> DownloadUpdateAsync()
    {
        var response = await SendAsync(new PipeMessage { Type = PipeMessageTypes.DownloadUpdate });
        return response?.Type == PipeMessageTypes.Success;
    }

    /// <summary>Отправляет команду на установку скачанного обновления.</summary>
    public async Task<(bool Success, string Message)> InstallUpdateAsync()
    {
        var response = await SendAsync(new PipeMessage { Type = PipeMessageTypes.InstallUpdate });
        if (response?.Type == PipeMessageTypes.InstallUpdateResponse && !string.IsNullOrWhiteSpace(response.Payload))
        {
            try
            {
                using var doc = JsonDocument.Parse(response.Payload);
                var success = doc.RootElement.TryGetProperty("success", out var s) && s.GetBoolean();
                var msg = doc.RootElement.TryGetProperty("message", out var m) ? m.GetString() ?? "" : "";
                return (success, msg);
            }
            catch { }
        }
        return (false, response?.Payload ?? "Ошибка при запуске обновления.");
    }

    /// <summary>Запрашивает статус процесса загрузки обновления.</summary>
    public async Task<UpdateProgressInfo?> GetUpdateStatusAsync()
    {
        var response = await SendAsync(new PipeMessage { Type = PipeMessageTypes.GetUpdateStatus });
        if (response?.Type == PipeMessageTypes.UpdateStatusResponse && !string.IsNullOrWhiteSpace(response.Payload))
        {
            try
            {
                return JsonSerializer.Deserialize<UpdateProgressInfo>(response.Payload, JsonOptions);
            }
            catch { }
        }
        return null;
    }

    /// <summary>Сохраняет настройки параметров автообновления.</summary>
    public async Task<bool> SetUpdateSettingsAsync(UpdateSettingsPayload settings)
    {
        var response = await SendAsync(new PipeMessage
        {
            Type = PipeMessageTypes.SetUpdateSettings,
            Payload = JsonSerializer.Serialize(settings, JsonOptions)
        });
        return response?.Type == PipeMessageTypes.Success;
    }

    /// <summary>Запрашивает актуальный статус брандмауэра Windows и управляемых правил.</summary>
    public async Task<FirewallStatusDto?> GetFirewallStatusAsync(CancellationToken ct = default)
    {
        var response = await SendAsync(new PipeMessage { Type = PipeMessageTypes.GetFirewallStatus }, ct);
        if (response != null && !string.IsNullOrWhiteSpace(response.Payload))
        {
            try
            {
                return JsonSerializer.Deserialize<FirewallStatusDto>(response.Payload, JsonOptions);
            }
            catch { }
        }
        return null;
    }

    /// <summary>Отправляет запрос на блокировку IP-адреса в брандмауэре Windows.</summary>
    public async Task<FirewallOperationResult> BlockIpAsync(BlockIpRequest request, CancellationToken ct = default)
    {
        if (request == null)
            return FirewallOperationResult.Fail("Запрос блокировки IP не может быть null.");

        var response = await SendAsync(new PipeMessage
        {
            Type = PipeMessageTypes.BlockIp,
            Payload = JsonSerializer.Serialize(request, JsonOptions)
        }, ct);

        if (response != null && !string.IsNullOrWhiteSpace(response.Payload))
        {
            try
            {
                var result = JsonSerializer.Deserialize<FirewallOperationResult>(response.Payload, JsonOptions);
                if (result != null) return result;
            }
            catch { }
        }
        return FirewallOperationResult.Fail(response?.Payload ?? "Не удалось заблокировать IP-адрес.");
    }

    /// <summary>Отправляет запрос на разблокировку IP-адреса в брандмауэре Windows.</summary>
    public async Task<FirewallOperationResult> UnblockIpAsync(string ip, FirewallDirection direction = FirewallDirection.Both, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(ip))
            return FirewallOperationResult.Fail("IP-адрес не указан.");

        var req = new UnblockIpRequest { Ip = ip.Trim(), Direction = direction };
        var response = await SendAsync(new PipeMessage
        {
            Type = PipeMessageTypes.UnblockIp,
            Payload = JsonSerializer.Serialize(req, JsonOptions)
        }, ct);

        if (response != null && !string.IsNullOrWhiteSpace(response.Payload))
        {
            try
            {
                var result = JsonSerializer.Deserialize<FirewallOperationResult>(response.Payload, JsonOptions);
                if (result != null) return result;
            }
            catch { }
        }
        return FirewallOperationResult.Fail(response?.Payload ?? "Не удалось разблокировать IP-адрес.");
    }

    /// <summary>Отправляет запрос на блокировку порта в брандмауэре Windows.</summary>
    public async Task<FirewallOperationResult> BlockPortAsync(BlockPortRequest request, CancellationToken ct = default)
    {
        if (request == null)
            return FirewallOperationResult.Fail("Запрос блокировки порта не может быть null.");

        var response = await SendAsync(new PipeMessage
        {
            Type = PipeMessageTypes.BlockPort,
            Payload = JsonSerializer.Serialize(request, JsonOptions)
        }, ct);

        if (response != null && !string.IsNullOrWhiteSpace(response.Payload))
        {
            try
            {
                var result = JsonSerializer.Deserialize<FirewallOperationResult>(response.Payload, JsonOptions);
                if (result != null) return result;
            }
            catch { }
        }
        return FirewallOperationResult.Fail(response?.Payload ?? "Не удалось заблокировать порт.");
    }

    /// <summary>Отправляет запрос на разблокировку порта в брандмауэре Windows.</summary>
    public async Task<FirewallOperationResult> UnblockPortAsync(int port, FirewallProtocol protocol = FirewallProtocol.Tcp, FirewallDirection direction = FirewallDirection.Inbound, string? appPath = null, CancellationToken ct = default)
    {
        var req = new UnblockPortRequest
        {
            Port = port,
            Protocol = protocol,
            Direction = direction,
            ApplicationPath = appPath
        };

        var response = await SendAsync(new PipeMessage
        {
            Type = PipeMessageTypes.UnblockPort,
            Payload = JsonSerializer.Serialize(req, JsonOptions)
        }, ct);

        if (response != null && !string.IsNullOrWhiteSpace(response.Payload))
        {
            try
            {
                var result = JsonSerializer.Deserialize<FirewallOperationResult>(response.Payload, JsonOptions);
                if (result != null) return result;
            }
            catch { }
        }
        return FirewallOperationResult.Fail(response?.Payload ?? "Не удалось разблокировать порт.");
    }

    /// <summary>Запрашивает актуальную конфигурацию правил защиты Port Defender.</summary>
    public async Task<PortDefenderConfigDto?> GetPortDefenderConfigAsync(CancellationToken ct = default)
    {
        var response = await SendAsync(new PipeMessage { Type = PipeMessageTypes.GetPortDefenderConfig }, ct);
        if (response != null && !string.IsNullOrWhiteSpace(response.Payload))
        {
            try
            {
                return JsonSerializer.Deserialize<PortDefenderConfigDto>(response.Payload, JsonOptions);
            }
            catch { }
        }
        return null;
    }

    /// <summary>Отправляет новую конфигурацию правил защиты Port Defender.</summary>
    public async Task<bool> SetPortDefenderConfigAsync(PortDefenderConfigDto config, CancellationToken ct = default)
    {
        if (config == null) return false;
        var response = await SendAsync(new PipeMessage
        {
            Type = PipeMessageTypes.SetPortDefenderConfig,
            Payload = JsonSerializer.Serialize(config, JsonOptions)
        }, ct);
        return response?.Type == PipeMessageTypes.Success;
    }

    /// <summary>Запрашивает список текущих активных блокировок службы Port Defender.</summary>
    public async Task<List<ActiveBanDto>> GetActiveBansAsync(CancellationToken ct = default)
    {
        var response = await SendAsync(new PipeMessage { Type = PipeMessageTypes.GetActiveBans }, ct);
        if (response != null && !string.IsNullOrWhiteSpace(response.Payload))
        {
            try
            {
                var bans = JsonSerializer.Deserialize<List<ActiveBanDto>>(response.Payload, JsonOptions);
                if (bans != null) return bans;
            }
            catch { }
        }
        return new List<ActiveBanDto>();
    }

    /// <summary>Отправляет команду на снятие всех активных блокировок Port Defender.</summary>
    public async Task<bool> ClearAllBansAsync(CancellationToken ct = default)
    {
        var response = await SendAsync(new PipeMessage { Type = PipeMessageTypes.ClearAllBans }, ct);
        return response?.Type == PipeMessageTypes.Success;
    }

    /// <summary>Запрашивает штатную остановку фоновой службы (мягкое завершение хоста).</summary>
    /// <param name="timeoutMs">Таймаут ожидания ответа от службы в миллисекундах.</param>
    /// <returns>True, если служба подтвердила остановку (Success).</returns>
    public async Task<bool> RequestServiceShutdownAsync(int timeoutMs = 3000)
    {
        try
        {
            using var cts = new CancellationTokenSource(timeoutMs);
            var response = await SendAsync(new PipeMessage
            {
                Type = PipeMessageTypes.ShutdownService
            }, cts.Token);
            return response?.Type == PipeMessageTypes.Success;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Освобождает все ресурсы: каналы, читатели, писатели, токены отмены.</summary>
    public void Dispose()
    {
        try { _eventCts?.Cancel(); } catch { }
        try { _eventReader?.Dispose(); } catch { }
        try { _eventPipe?.Dispose(); } catch { }
        try { _pipe?.Dispose(); } catch { }
        try { _reader?.Dispose(); } catch { }
        try { _writer?.Dispose(); } catch { }
        try { _eventCts?.Dispose(); } catch { }
        try { _sendLock?.Dispose(); } catch { }
        _pipe = null;
        _reader = null;
        _writer = null;
        _eventPipe = null;
        _eventReader = null;
    }
}
