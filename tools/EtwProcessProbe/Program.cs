// <copyright file="Program.cs" company="StartupMonitor">
// Copyright (c) StartupMonitor. All rights reserved.
// </copyright>

using System;
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

        try
        {
            return await RunEtwProbeAsync(args);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[ОШИБКА] Проверка ETW не завершена: {ex}");
            return 1;
        }
    }

    /// <summary>
    /// Режим вспомогательного дочернего процесса (Helper).
    /// Печатает свой PID, время старта и штатно завершается через заданный таймаут.
    /// </summary>
    /// <param name="args">Параметры helper; длительность ограничивается для детерминированного завершения.</param>
    /// <returns>Ноль при штатном завершении.</returns>
    private static int RunHelperProcess(string[] args)
    {
        using var current = Process.GetCurrentProcess();
        var durationMs = 300;
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i].Equals("--duration", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(args[i + 1], out var d))
            {
                durationMs = Math.Clamp(d, 1, 5000);
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
    /// <param name="args">Количество процессов от 1 до 128; неизвестные параметры считаются ошибкой.</param>
    /// <returns>Ноль только после подтверждения реальных событий, отсутствия потерь и успешной остановки consumer.</returns>
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
        if (args.Length != 0 && (args.Length != 2 || args[0] != "--count" ||
            !int.TryParse(args[1], out targetProcessCount) || targetProcessCount is < 1 or > 128))
            throw new ArgumentException("Ожидается --count от 1 до 128.", nameof(args));

        var logger = new ConsoleSimpleLogger<ProcessLifecycleSupervisor>();
        // Production-контроллер имеет единственный lease. Зонд запускается только при штатно
        // остановленной службе и использует то же имя; живой владелец блокирует запуск зонда.
        var sessionName = NativeProcessTraceSession.DefaultSessionName;

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

            var ingress = new LifecycleIngress(capacity: 4096, byteBudget: 8 * 1024 * 1024);
            var epoch = Guid.NewGuid();
            using var cts = new CancellationTokenSource();
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            var readerTask = Task.Factory.StartNew(
                () => EtwProcessReader.Run(session, ingress, epoch, logger, cts.Token),
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default);
            var observed = new List<ProcessLifecycleEvent>();
            var expected = new List<(int Pid, string Token)>();
            var currentExe = Environment.ProcessPath ?? throw new IOException("Не найден путь EXE зонда.");
            var probePid = Environment.ProcessId;

            /// <summary>
            /// Единственный reader сохраняет ранние события до регистрации PID в Process.Start.
            /// История ограничена; переполнение завершает проверку ошибкой вместо сокрытия потери.
            /// </summary>
            void Drain()
            {
                while (ingress.TryRead(out var evt))
                {
                    if (evt is null) continue;
                    if (observed.Count >= 16384)
                        throw new IOException("Превышен ограниченный бюджет истории зонда (16384 события).");
                    observed.Add(evt);
                }
            }

            /// <summary>Связывает Start по nonce/PPID, а Stop — по экземпляру ядра и порядку QPC.</summary>
            /// <param name="child">PID и уникальный nonce собственного helper.</param>
            /// <returns>Истина после получения согласованной пары событий данного запуска.</returns>
            bool HasPair((int Pid, string Token) child)
            {
                var start = observed.FirstOrDefault(e => e.SourceEpoch == epoch &&
                    e.Kind == LifecycleEventKind.Started && e.ProcessId == child.Pid &&
                    e.ParentProcessId == probePid && e.CommandLine.Contains(child.Token, StringComparison.Ordinal));
                return start is not null && observed.Any(e => e.SourceEpoch == epoch &&
                    e.Kind == LifecycleEventKind.Stopped && e.ProcessId == child.Pid &&
                    e.OccurredQpc >= start.OccurredQpc &&
                    (start.KernelKey == 0 || e.KernelKey == 0 || e.KernelKey == start.KernelKey));
            }

            /// <summary>Создаёт собственный helper и параллельно выгружает bounded ingress до его завершения.</summary>
            /// <returns>PID и nonce завершившегося собственного helper.</returns>
            async Task<(int Pid, string Token)> LaunchAsync()
            {
                var nonce = Guid.NewGuid().ToString("N");
                var psi = new ProcessStartInfo(currentExe)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true
                };
                foreach (var arg in new[] { "--helper", "--duration", "200", "--probe-token", nonce })
                    psi.ArgumentList.Add(arg);
                using var proc = Process.Start(psi) ?? throw new IOException("Helper не был запущен.");
                var identity = (Pid: proc.Id, Token: nonce);
                var output = proc.StandardOutput.ReadToEndAsync(deadline.Token);
                var exit = proc.WaitForExitAsync(deadline.Token);
                try
                {
                    while (!exit.IsCompleted)
                    {
                        Drain();
                        if (readerTask.IsCompleted)
                            throw new IOException("ETW consumer завершился до окончания проверки.", readerTask.Exception);
                        await Task.Delay(20, deadline.Token);
                    }
                    await exit;
                    if (proc.ExitCode != 0 || !(await output).Contains($"PID={proc.Id},", StringComparison.Ordinal))
                        throw new IOException($"Helper PID {proc.Id} не подтвердил штатное завершение (код {proc.ExitCode}).");
                    Drain();
                    return identity;
                }
                finally
                {
                    if (!proc.HasExited) proc.Kill();
                }
            }

            try
            {
                Console.WriteLine("2. Подтверждение готовности реальной парой Start/Stop отдельного sentinel-helper...");
                var sentinels = new List<(int Pid, string Token)>();
                var readyTimeout = Stopwatch.StartNew();
                while (!sentinels.Any(HasPair))
                {
                    sentinels.Add(await LaunchAsync());
                    Drain();
                    if (readerTask.IsCompleted || readyTimeout.Elapsed > TimeSpan.FromSeconds(10))
                        throw new IOException("Consumer не подтвердил готовность событиями sentinel-helper.", readerTask.Exception);
                    await Task.Delay(250, deadline.Token);
                }
                var initialStats = session.QueryStatistics() ?? throw new IOException("Не получена начальная статистика ядра ETW.");
                Console.WriteLine($"3. Запуск {targetProcessCount} проверяемых helper-процессов...");
                for (var i = 0; i < targetProcessCount; i++) expected.Add(await LaunchAsync());

                var drainTimeout = Stopwatch.StartNew();
                while (!expected.All(HasPair) && drainTimeout.Elapsed < TimeSpan.FromSeconds(10))
                {
                    Drain();
                    if (readerTask.IsCompleted)
                        throw new IOException("ETW consumer завершился при ожидании событий.", readerTask.Exception);
                    await Task.Delay(25, deadline.Token);
                }
                Drain();
                var stats = session.QueryStatistics() ?? throw new IOException("Не получена финальная статистика ядра ETW.");
                var matched = expected.Count(HasPair);
                Console.WriteLine($"Подтверждённые экземпляры Start+Stop: {matched}/{targetProcessCount}; " +
                    $"IngressDropped={ingress.Dropped}, ParseFailures={ingress.ParseFailures}, " +
                    $"EventsLost={stats.EventsLost}, RealTimeBuffersLost={stats.RealTimeBuffersLost}, " +
                    $"InitialEventsLost={initialStats.EventsLost}.");
                if (matched != targetProcessCount || ingress.Dropped != 0 || ingress.ParseFailures != 0 ||
                    stats.EventsLost != 0 || stats.RealTimeBuffersLost != 0 || stats.LogBuffersLost != 0)
                    throw new IOException("Не выполнены критерии полного приёма событий без потерь.");
            }
            finally
            {
                cts.Cancel();
                session.Stop();
                try
                {
                    await readerTask.WaitAsync(TimeSpan.FromSeconds(10));
                }
                catch (OperationCanceledException ex) when (cts.IsCancellationRequested && ex.CancellationToken == cts.Token)
                {
                    Console.WriteLine("Consumer подтвердил штатную остановку по отмене.");
                }
            }
            Console.WriteLine("[УСПЕХ] Реальные события подтверждены; consumer и собственная сессия остановлены.");
            return 0;
        }
    }

    /// <summary>
    /// Проверяет, запущен ли текущий процесс с повышенными привилегиями Администратора.
    /// </summary>
    /// <returns>Признак наличия прав администратора; ошибки проверки не скрываются.</returns>
    private static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }
}
