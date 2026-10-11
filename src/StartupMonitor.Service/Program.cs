using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using StartupMonitor.Service.Pipe;
using StartupMonitor.Service.Services;
using StartupMonitor.Service.Services.VirusScan;
using StartupMonitor.Shared;
using StartupMonitor.Shared.Diagnostics;
using StartupMonitor.Shared.Models;

// Базовый каталог — папка, где лежит исполнимый файл службы
var baseDir = AppContext.BaseDirectory;
// Путь к файлу конфигурации
var configPath = Path.Combine(baseDir, "startup-monitor.json");
// Режим запуска: --console для отладки в консоли, иначе Windows-служба
var isConsole = args.Contains("--console");

// Единственный экземпляр: две службы пишут в один конфиг и затирают друг друга
using var instanceMutex = new Mutex(true, @"Global\StartupMonitorService", out var createdNew);
if (!createdNew)
{
    if (isConsole)
        Console.WriteLine("Другой экземпляр службы уже запущен — выход.");
    return;
}

// UI служба НЕ запускает: процесс из-под LocalSystem создаётся в сессии 0 (невидим для пользователя).
// UI стартует самим пользователем или из автозапуска пользователя.

// Минимальный приоритет — чтобы не тормозить систему
Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.Idle;

Log.Logger = new LoggerConfiguration()
    .WriteTo.File(Path.Combine(baseDir, "logs", "service-.log"),
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 14)
    .MinimumLevel.Information()
    .CreateLogger();

if (isConsole)
{
    Log.Logger = new LoggerConfiguration()
        .WriteTo.Console()
        .WriteTo.File(Path.Combine(baseDir, "logs", "service-.log"),
            rollingInterval: RollingInterval.Day,
            retainedFileCountLimit: 14)
        .MinimumLevel.Information()
        .MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Warning)
        .CreateLogger();
}

AppDomain.CurrentDomain.UnhandledException += (_, e) =>
{
    Log.Fatal(e.ExceptionObject as Exception, "Необработанное исключение");
    Log.CloseAndFlush();
};

try
{
    Log.Information("StartupMonitor Service starting (mode: {Mode})",
        isConsole ? "console" : "windows-service");

    // Сводный отчёт профайлера производительности каждые 5 минут
    _ = ExecutionProfiler.StartAutoReportAsync(
        TimeSpan.FromMinutes(5),
        msg => Log.Information("PERF REPORT (Service):\n{Report}", msg));

    // Загрузчик данных — единая точка доступа к конфигурации
    var dataStore = new JsonDataStore(configPath);
    var cfg = dataStore.Load();

    // При старте завершаем процессы заблокированных записей и любые процессы,
    // чьи пути совпадают с правилами ЧС (даже без записи в списке — проблема ms-teams)
    try
    {
        EntryOperations.TerminateAllBlocked(cfg.Entries);
        EntryOperations.KillProcessesByRules(cfg.Blacklist);
    }
    catch (Exception ex)
    {
        Log.Warning(ex, "Стартовое завершение заблокированных процессов не выполнено");
    }

    // Настройка DI-контейнера и фоновых служб
    var builder = Host.CreateDefaultBuilder(args)
        .UseSerilog()
        .ConfigureServices(services =>
        {
            services.AddSingleton(dataStore);
            services.AddSingleton<MonitoringStateCoordinator>();
            services.AddSingleton<InitialScanTracker>();
            services.AddSingleton<StartupMonitor.Service.Services.Remote.RemoteNotificationService>();
            services.AddSingleton<NotificationService>();
            services.AddSingleton<BlacklistManager>();
            services.AddSingleton<HashLookupService>();
            services.AddSingleton<SystemScannerService>();
            services.AddSingleton<ProcessStartupLinker>();
            services.AddSingleton<ProcessMetadataEnricher>();
            services.AddSingleton<ProcessMetricsTracker>();
            services.AddHostedService<RegistryWatcher>();
            services.AddHostedService<StartupFolderWatcher>();
            services.AddHostedService<ServiceWatcher>();
            services.AddHostedService<TaskSchedulerWatcher>();
            services.AddHostedService<OtherStartupLocationsWatcher>();
            services.AddSingleton<StartupMonitor.Service.Services.Delayed.DelayedStartupManager>();
            services.AddHostedService(sp => sp.GetRequiredService<StartupMonitor.Service.Services.Delayed.DelayedStartupManager>());
            services.AddSingleton<StartupMonitor.Service.Services.VirusScan.KasperskyScanService>();
            services.AddSingleton<SnapshotManager>();
            services.AddSingleton<StartupMonitor.Service.Services.BootTime.BootTimeService>();
            services.AddSingleton<StartupMonitor.Service.Services.Update.GitHubUpdateService>();
            services.AddHostedService<WmiSubscriptionWatcher>();
            services.AddHostedService<PipeServer>();
            services.AddHostedService<EventPipeServer>();
        });

    // Интеграция с Windows Service (SCM)
    if (!isConsole)
        builder.UseWindowsService(options =>
        {
            options.ServiceName = "StartupMonitor";
        });

    var host = builder.Build();

    if (isConsole)
    {
        Console.WriteLine("=== StartupMonitor Service (console mode) ===");
        Console.WriteLine($"Config: {configPath}");
        Console.WriteLine("Press Ctrl+C to stop.");
        await host.RunAsync();
    }
    else
    {
        await host.RunAsync();
    }
}
catch (Exception ex)
{
    Log.Fatal(ex, "Service terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}
