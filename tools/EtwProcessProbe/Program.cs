// <copyright file="Program.cs" company="StartupMonitor">
// Copyright (c) StartupMonitor. All rights reserved.
// </copyright>

using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using StartupMonitor.Service.Services.ProcessLifecycle;

namespace EtwProcessProbe;

/// <summary>
/// Изолированный тестовый инструмент и зонд верификации Event Tracing for Windows (ETW).
/// Позволяет автономно тестировать захват реальных событий Start/Stop процессов под Windows 10/11
/// без необходимости развёртывания системной службы SCM.
/// </summary>
public static class Program
{
    private sealed class ConsoleSimpleLogger<T> : ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var msg = formatter(state, exception);
            var color = logLevel switch
            {
                LogLevel.Error or LogLevel.Critical => ConsoleColor.Red,
                LogLevel.Warning => ConsoleColor.Yellow,
                LogLevel.Information => ConsoleColor.Cyan,
                _ => ConsoleColor.Gray
            };
            var prev = Console.ForegroundColor;
            Console.ForegroundColor = color;
            Console.WriteLine($"[{logLevel}] {msg}");
            Console.ForegroundColor = prev;
        }
    }

    /// <summary>
    /// Главная точка входа тестового зонда ETW.
    /// </summary>
    /// <param name="args">Аргументы командной строки.</param>
    /// <returns>Код возврата процесса (0 - успех, >0 - ошибка или отказ в доступе).</returns>
    public static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        if (args.Length > 0 && args[0].Equals("--helper", StringComparison.OrdinalIgnoreCase))
        {
            return RunHelperProcess(args);
        }

        return await RunEtwProbeAsync(args);
    }

    /// <summary>
    /// Режим вспомогательного дочернего процесса (Helper).
    /// Печатает свой PID, время старта и штатно завершается через заданный таймаут.
    /// </summary>
    private static int RunHelperProcess(string[] args)
    {
        var current = Process.GetCurrentProcess();
        var durationMs = 300;
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i].Equals("--duration", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(args[i + 1], out var d))
            {
                durationMs = d;
            }
        }

        Console.WriteLine($"HELPER_STARTED: PID={current.Id}, TimeUtc={current.StartTime.ToUniversalTime():O}");
        Thread.Sleep(durationMs);
        Console.WriteLine($"HELPER_EXITING: PID={current.Id}");
        return 0;
    }

    /// <summary>
    /// Главный сценарий зонда: инициализирует native-сессию ETW, читатель событий и проверяет захват helper-процессов.
    /// </summary>
    private static async Task<int> RunEtwProbeAsync(string[] args)
    {
        Console.WriteLine("===============================================================================");
        Console.WriteLine("    StartupMonitor: Изолированный диагностический зонд ETW Process Probe");
        Console.WriteLine("===============================================================================");

        var isElevated = IsAdministrator();
        Console.WriteLine($"Привилегии процесса: {(isElevated ? "Администратор (Elevated)" : "Обычный пользователь (Non-elevated)")}");

        if (!isElevated)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("[ВНИМАНИЕ] Для запуска реальной native-сессии SystemTraceProvider требуются права Администратора.");
            Console.WriteLine("Пожалуйста, перезапустите этот инструмент от имени Администратора (Run as Administrator).");
            Console.ResetColor();
            return 2;
        }

        int targetProcessCount = 5;
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i].Equals("--count", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(args[i + 1], out var c) && c > 0)
            {
                targetProcessCount = c;
            }
        }

        var logger = new ConsoleSimpleLogger<ProcessLifecycleSupervisor>();
        var sessionName = "StartupMonitor.Probe." + Guid.NewGuid().ToString("N")[..8];

        Console.WriteLine($"1. Инициализация сессии ETW '{sessionName}'...");
        if (!NativeProcessTraceSession.TryStart(
            out var session,
            out var errorMessage,
            out var errorCode,
            sessionName: sessionName,
            logger: logger) || session == null)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[ОШИБКА] Не удалось создать сессию ETW (Win32 Code: {errorCode}): {errorMessage}");
            Console.ResetColor();
            return 1;
        }

        using (session)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"Сессия ETW успешно создана (Handle: 0x{session.SessionHandle:X16}).");
            Console.ResetColor();

            var ingress = new LifecycleIngress(capacity: 1024, byteBudget: 4 * 1024 * 1024);
            var epoch = Guid.NewGuid();
            using var cts = new CancellationTokenSource();

            var readerTask = Task.Factory.StartNew(
                () =>
                {
                    try
                    {
                        EtwProcessReader.Run(session, ingress, epoch, logger, cts.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        // Штатная отмена читателя при завершении теста
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Читатель ETW завершился с исключением: {ex.Message}");
                    }
                },
                cts.Token,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default);

            Console.WriteLine("2. Ожидание готовности потребителя ETW (Ready Handshake)...");
            await Task.Delay(500);

            var launchedPids = new ConcurrentDictionary<int, DateTime>();
            var caughtStarts = new ConcurrentDictionary<int, ProcessLifecycleEvent>();
            var caughtStops = new ConcurrentDictionary<int, ProcessLifecycleEvent>();

            var collectorCts = new CancellationTokenSource();
            var collectorTask = Task.Run(async () =>
            {
                while (!collectorCts.Token.IsCancellationRequested)
                {
                    if (ingress.TryRead(out var evt))
                    {
                        if (evt != null && evt.Kind == LifecycleEventKind.Started && launchedPids.ContainsKey(evt.ProcessId))
                        {
                            caughtStarts[evt.ProcessId] = evt;
                            Console.WriteLine($"[ETW EVENT] Пойман старт процесса PID {evt.ProcessId} ({evt.ImageName})");
                        }
                        else if (evt != null && evt.Kind == LifecycleEventKind.Stopped && launchedPids.ContainsKey(evt.ProcessId))
                        {
                            caughtStops[evt.ProcessId] = evt;
                            Console.WriteLine($"[ETW EVENT] Поймана остановка процесса PID {evt.ProcessId}");
                        }
                    }
                    else
                    {
                        try
                        {
                            await Task.Delay(20, collectorCts.Token);
                        }
                        catch (OperationCanceledException)
                        {
                            break;
                        }
                    }
                }
            });

            Console.WriteLine($"3. Запуск {targetProcessCount} тестовых helper-процессов...");
            var currentExe = Process.GetCurrentProcess().MainModule?.FileName ?? "EtwProcessProbe.exe";

            for (int i = 0; i < targetProcessCount; i++)
            {
                var psi = new ProcessStartInfo(currentExe, "--helper --duration 200")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true
                };

                var proc = Process.Start(psi);
                if (proc != null)
                {
                    launchedPids[proc.Id] = DateTime.UtcNow;
                    Console.WriteLine($"   -> Запущен helper #{i + 1}: PID {proc.Id}");
                    proc.WaitForExit();
                }
            }

            Console.WriteLine("4. Ожидание выгрузки буферов ядра ETW (drain buffers)...");
            var timeout = Stopwatch.StartNew();
            while (timeout.ElapsedMilliseconds < 5000)
            {
                if (caughtStarts.Count >= targetProcessCount && caughtStops.Count >= targetProcessCount)
                {
                    break;
                }
                await Task.Delay(100);
            }

            collectorCts.Cancel();
            try { await collectorTask; } catch { }

            cts.Cancel();
            session.Dispose();
            try { await readerTask; } catch { }

            Console.WriteLine("-------------------------------------------------------------------------------");
            Console.WriteLine("Результаты верификации:");
            Console.WriteLine($"   Запущено тестовых helper-ов:      {targetProcessCount}");
            Console.WriteLine($"   Поймано событий ProcessStart:     {caughtStarts.Count} из {targetProcessCount}");
            Console.WriteLine($"   Поймано событий ProcessStop:      {caughtStops.Count} из {targetProcessCount}");
            Console.WriteLine($"   Потери очереди Ingress (Dropped): {ingress.Dropped}");
            Console.WriteLine($"   Ошибки парсинга (ParseFailures):  {ingress.ParseFailures}");
            Console.WriteLine("-------------------------------------------------------------------------------");

            if (caughtStarts.Count == targetProcessCount && caughtStops.Count == targetProcessCount)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("[УСПЕХ] Все 100% событий Start и Stop короткоживущих процессов успешно пойманы!");
                Console.ResetColor();
                return 0;
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("[ВНИМАНИЕ] Часть событий буферизована ядром или не успела дойти до истечения таймаута.");
                Console.ResetColor();
                return 1;
            }
        }
    }

    /// <summary>
    /// Проверяет, запущен ли текущий процесс с повышенными привилегиями Администратора.
    /// </summary>
    private static bool IsAdministrator()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }
}
