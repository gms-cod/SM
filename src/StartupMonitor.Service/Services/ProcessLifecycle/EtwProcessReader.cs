using System;
using System.IO;
using System.Threading;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;
using Microsoft.Extensions.Logging;

namespace StartupMonitor.Service.Services.ProcessLifecycle;

/// <summary>
/// Читает события процессов из собственной запущенной native-сессии ETW SystemTraceProvider.
/// Создание сессии и доказательство владения выполняет NativeProcessTraceSession.
/// </summary>
public static class EtwProcessReader
{
    /// <summary>
    /// Блокирующий метод чтения сессии ETW для выполнения на выделенном рабочем потоке.
    /// Категорически запрещено вызывать в UI-потоке или в Host.StartAsync.
    /// </summary>
    /// <param name="session">Успешно созданная и подтвержденная собственная native-сессия.</param>
    /// <param name="ingress">Ограниченная очередь-буфер для передачи копий событий в reducer.</param>
    /// <param name="sourceEpoch">Уникальная эпоха подключения данного consumer.</param>
    /// <param name="logger">Интерфейс журналирования (без логирования сырых командных строк и конфиденциальных данных).</param>
    /// <param name="ct">Токен отмены, инициируемый супервизором вне callback ETW.</param>
    public static void Run(
        IOwnedProcessTraceSession session,
        LifecycleIngress ingress,
        Guid sourceEpoch,
        ILogger logger,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(ingress);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentException.ThrowIfNullOrWhiteSpace(session.Name);
        if (sourceEpoch == Guid.Empty)
        {
            throw new ArgumentException("Эпоха источника отсутствует или пуста.", nameof(sourceEpoch));
        }

        ct.ThrowIfCancellationRequested();

        using var source = new ETWTraceEventSource(session.Name, TraceEventSourceType.Session);
        long sequence = 0;

        // Копирует поля внутри callback ядра; ошибки передает в ограниченную диагностику ingress без дискового I/O
        void Publish(ProcessTraceData data, LifecycleEventKind kind)
        {
            if (ct.IsCancellationRequested) return;

            try
            {
                if (data.ProcessID < 0)
                {
                    throw new InvalidDataException("ETW payload содержит отрицательный идентификатор процесса (PID).");
                }

                // Сырой QPC используется для сопоставления со снимками высокого разрешения (API помечен Discouraged в библиотеке)
#pragma warning disable CS0618
                var eventQpc = data.TimeStampQPC;
#pragma warning restore CS0618

                var copied = new ProcessLifecycleEvent(
                    sourceEpoch,
                    Interlocked.Increment(ref sequence),
                    kind,
                    data.ProcessID,
                    data.ParentID,
                    data.UniqueProcessKey,
                    eventQpc,
                    data.TimeStamp.ToUniversalTime(),
                    data.ImageFileName ?? string.Empty,
                    data.CommandLine ?? string.Empty);

                ingress.TryPublish(copied);
            }
            catch (Exception ex)
            {
                ingress.ReportParseFailure(ex, kind, sourceEpoch);
            }
        }

        Action<ProcessTraceData> onStart = data => Publish(data, LifecycleEventKind.Started);
        Action<ProcessTraceData> onStop = data => Publish(data, LifecycleEventKind.Stopped);
        Action<ProcessTraceData> onRundown = data => Publish(data, LifecycleEventKind.Rundown);

        source.Kernel.ProcessStart += onStart;
        source.Kernel.ProcessStop += onStop;
        source.Kernel.ProcessDCStart += onRundown;

        // Регистрация отмены: остановка сессии ETW надежно разблокирует вызов source.Process()
        using var registration = ct.Register(() =>
        {
            try
            {
                session.Stop();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Не удалось остановить собственную ETW-сессию {SessionName}", session.Name);
            }
            finally
            {
                try
                {
                    source.Dispose();
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Не удалось освободить ресурсы ETW consumer для сессии {SessionName}", session.Name);
                }
            }
        });

        try
        {
            source.Process();
            ct.ThrowIfCancellationRequested();
            throw new IOException("ETW consumer завершил обработку событий без явного запроса остановки. Требуется восстановление.");
        }
        catch (Exception ex) when (ct.IsCancellationRequested)
        {
            logger.LogDebug(ex, "ETW consumer сессии {SessionName} штатно остановлен по запросу отмены.", session.Name);
            throw new OperationCanceledException("Чтение сессии ETW прервано токеном отмены.", ex, ct);
        }
        finally
        {
            source.Kernel.ProcessStart -= onStart;
            source.Kernel.ProcessStop -= onStop;
            source.Kernel.ProcessDCStart -= onRundown;
        }
    }
}

