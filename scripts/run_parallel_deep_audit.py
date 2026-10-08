import sys
import os
import time
from concurrent.futures import ThreadPoolExecutor

sys.path.insert(0, r"d:\Nas_sync\Projects\Gemeni+opencode")
from src.opencode_runner import run_subagent_task

TASKS = [
    {
        "id": "scanner_service",
        "title": "OpenCode Subagent 1: SystemScanner, Watchers & Service Background Load",
        "output_file": r"d:\Nas_sync\Coding\StartupMonitor\docs\OPENCODE_AUDIT_SCANNER_SERVICE.md",
        "files": [
            r"d:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.Service\Services\SystemScannerService.cs",
            r"d:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.Service\Services\EntryOperations.cs",
            r"d:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.Service\Pipe\PipeServer.cs",
            r"d:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.Service\Watchers\RegistryWatcher.cs",
            r"d:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.Service\Watchers\TaskSchedulerWatcher.cs",
            r"d:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.Service\Watchers\ServiceWatcher.cs",
        ],
        "prompt": (
            "Ты — ведущий системный архитектор высокой производительности .NET 8 / Windows internals.\n"
            "Проведи глубокий технический аудит службы StartupMonitor.Service на предмет CPU, RAM, I/O и скрытых багов/глюков.\n"
            "ФАКТЫ ИЗ ПРОФИЛИРОВЩИКА СЛУЖБЫ ЗА ПОСЛЕДНИЕ 5 МИНУТ:\n"
            "- Service.ScanAll выполняется 12.3 секунды!\n"
            "- Service.EntryOps.ResolveEnrich вызывается 7817 раз и занимает 10.1 секунд CPU!\n"
            "- Из 7819 элементов системы: ContextMenuHandler (CLSID) составляет 7485 записей (ScanClsidEntries перебирает весь HKCR\\CLSID и вызывает ResolveAndEnrichEntry на каждую InprocServer32/LocalServer32)!\n"
            "- TaskScheduler скан занимает 1.05 с, Services 0.3 с.\n"
            "- BackgroundCleanupLoop запускает ScanAll каждые 5 минут, нагружая CPU на 12+ секунд.\n\n"
            "ЗАДАЧИ АНАЛИЗА:\n"
            "1. Предложи конкретные архитектурные решения по оптимизации ScanClsidEntries, ResolveAndEnrichEntry и частоты сканирования.\n"
            "2. Найди потенциальные баги, утечки ресурсов, дедлоки, гонки потоков (Race Conditions) между вотчерами и фоновыми циклами.\n"
            "3. Сформируй список топ-рекомендаций с подробным техническим обоснованием эффекта и рисков."
        ),
    },
    {
        "id": "metrics_network",
        "title": "OpenCode Subagent 2: ProcessMetricsTracker, Network & Authenticode",
        "output_file": r"d:\Nas_sync\Coding\StartupMonitor\docs\OPENCODE_AUDIT_METRICS_NETWORK.md",
        "files": [
            r"d:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.Service\Services\ProcessMetricsTracker.cs",
            r"d:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.Service\Services\ProcessNetworkTracker.cs",
            r"d:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.Service\Services\ProcessMetadataEnricher.cs",
            r"d:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.Shared\Helpers\AuthenticodeVerifier.cs",
            r"d:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.Service\Services\ProcessStartupLinker.cs",
        ],
        "prompt": (
            "Ты — эксперт по Win32 API, P/Invoke, низкоуровневой оптимизации и профилированию Windows.\n"
            "Проведи глубокий технический аудит сбора метрик процессов, сети и проверки цифровых подписей.\n"
            "ФАКТЫ ИЗ ПРОФИЛИРОВЩИКА:\n"
            "- ProcessMetricsTracker опрашивает все процессы каждые 1.5 секунды.\n"
            "- ProcessNetworkTracker вызывает GetExtendedTcpTable (IPv4, IPv6) и GetExtendedUdpTable (IPv4, IPv6), перебирая сокеты.\n"
            "- Service.AuthenticodeVerify вызывался 7440 раз за 5 минут!\n"
            "- Перебор дескрипторов, Toolhelp32Snapshot, модулей, IO_COUNTERS.\n\n"
            "ЗАДАЧИ АНАЛИЗА:\n"
            "1. Где происходят лишние вызовы Win32 API и аллокации памяти в непереиспользуемых структурах/коллекциях?\n"
            "2. Какие риски отказов/глюков существуют (например, процессы с правами SYSTEM, защищенные процессы Anti-Cheat, гонки закрытия дескрипторов, исчерпание P/Invoke хэндлов)?\n"
            "3. Сформируй список топ-рекомендаций по оптимизации и устранению потенциальных сбоев."
        ),
    },
    {
        "id": "ui_wpf_ux",
        "title": "OpenCode Subagent 3: WPF UI, Virtualization, Timers & UX Glitches",
        "output_file": r"d:\Nas_sync\Coding\StartupMonitor\docs\OPENCODE_AUDIT_UI_WPF.md",
        "files": [
            r"d:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.UI\ViewModels\MainViewModel.cs",
            r"d:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.UI\ViewModels\TaskManagerViewModel.cs",
            r"d:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.UI\Views\MainWindow.xaml",
            r"d:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.UI\Views\Dialogs\ProcessConnectionsWindow.xaml.cs",
            r"d:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.UI\Services\PipeClient.cs",
        ],
        "prompt": (
            "Ты — ведущий инженер WPF, XAML, Dispatcher и UI Performance в .NET 8.\n"
            "Проведи глубокий технический аудит пользовательского интерфейса StartupMonitor.UI.\n"
            "ФАКТЫ ИЗ ПРОФИЛИРОВЩИКА UI:\n"
            "- При открытии UI происходят множественные запросы в Named Pipe (Delta, Blacklist, Ratings, AutoGuardConfig, RemoteNotificationConfig, Theme).\n"
            "- MainViewModel содержит фоновые таймеры (polling 5с, CPU usage 2с, theme check, log tailing).\n"
            "- Диспетчер задач получает снимки процессов каждые 1.5с (FlatTree / List режим).\n\n"
            "ЗАДАЧИ АНАЛИЗА:\n"
            "1. Поиск утечек памяти и событий в WPF (подписки на INotifyPropertyChanged, ICommand, DispatcherTimer при скрытии окна в трей).\n"
            "2. Виртуализация списков (DataGrid, ListBox, TreeView) и плавность скролла (60 FPS).\n"
            "3. Возможные глюки интерфейса: залипание фокуса, рассинхронизация выделения, моргание темной/светлой темы, зависание UI-потока при сбоях канала Named Pipe.\n"
            "4. Сформируй список топ-рекомендаций с оценкой эффекта и мер защиты от багов."
        ),
    },
]

def run_task(task):
    print(f"[*] Starting {task['title']}...", flush=True)
    start_time = time.time()
    result = run_subagent_task(
        prompt=task["prompt"],
        work_dir=r"d:\Nas_sync\Coding\StartupMonitor",
        files=task["files"],
        variant="medium",
        timeout_seconds=600,
    )
    elapsed = time.time() - start_time
    print(f"[+] Finished {task['title']} in {elapsed:.1f}s (Success: {result.success})", flush=True)
    
    header = f"# {task['title']} (OpenCode muse-spark-1.3-contributor-free)\n\n"
    header += f"- Время выполнения: {elapsed:.1f} сек\n"
    header += f"- Статус: {'Успешно' if result.success else 'Ошибка (Exit code: ' + str(result.exit_code) + ')'}\n\n---\n\n"
    content = header + result.stdout
    if result.stderr:
        content += f"\n\n### Stderr / Warnings:\n```\n{result.stderr}\n```\n"
        
    with open(task["output_file"], "w", encoding="utf-8") as f:
        f.write(content)
    return task["id"], result.success, elapsed

def main():
    print(f"=== Launching 3 OpenCode Subagents in Parallel for Deep Audit ===", flush=True)
    start_all = time.time()
    with ThreadPoolExecutor(max_workers=3) as executor:
        futures = [executor.submit(run_task, t) for t in TASKS]
        results = [f.result() for f in futures]
    total_time = time.time() - start_all
    print(f"=== All 3 OpenCode Subagents Completed in {total_time:.1f}s ===", flush=True)
    for r in results:
        print(f"  - {r[0]}: Success={r[1]}, Time={r[2]:.1f}s", flush=True)

if __name__ == "__main__":
    main()
