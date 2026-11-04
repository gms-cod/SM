import sys
import os
import time
from concurrent.futures import ThreadPoolExecutor

sys.path.insert(0, r"D:\Nas\Projects\Gemeni+opencode")
from src.opencode_runner import run_subagent_task

WORKSPACE = r"d:\Nas\Coding\StartupMonitor"
MODEL = "opencode/deepseek-v4.1-flash-free"

TASKS = [
    {
        "id": "agent_1_smart_sleep",
        "title": "Субагент 1: Smart Sleep, ActivityGate и 0.0% CPU Idle в службе",
        "output_file": os.path.join(WORKSPACE, "docs", "AUDIT_DEEPSEEK_5_AGENT_1_SLEEP.md"),
        "files": [
            os.path.join(WORKSPACE, r"src\StartupMonitor.Service\Pipe\PipeServer.cs"),
            os.path.join(WORKSPACE, r"src\StartupMonitor.Service\Services\Firewall\ProcessPortDefenderService.cs"),
            os.path.join(WORKSPACE, r"src\StartupMonitor.Service\Services\Firewall\NetworkConnectionAlertService.cs"),
            os.path.join(WORKSPACE, r"src\StartupMonitor.Service\Services\EntryOperations.cs"),
        ],
        "prompt": (
            "Ты — ведущий системный архитектор Windows Services в .NET 8.\n"
            "Сформируй реализационный код для перевода службы в режим 0% CPU Idle:\n"
            "1. Класс ActivityGate и SleepAwareLoop в StartupMonitor.Service.Services.Runtime.\n"
            "2. Учёт активных UI-клиентов в PipeServer (Interlocked счётчик).\n"
            "3. Полная остановка NetworkConnectionAlertService и ProcessPortDefenderService при отсутствии активных окон или правил.\n"
            "4. Пропуск KillProcessesByRules при отсутствии активных правил в Blacklist.\n"
            "Выдай готовые, чистые и компилируемые C# фрагменты кода."
        ),
    },
    {
        "id": "agent_2_scan_channel_pipeline",
        "title": "Субагент 2: Мульти-ядерный конвейер Channel<StartupEntry> для сканеров автозапуска",
        "output_file": os.path.join(WORKSPACE, "docs", "AUDIT_DEEPSEEK_5_AGENT_2_PIPELINE.md"),
        "files": [
            os.path.join(WORKSPACE, r"src\StartupMonitor.Service\Services\SystemScannerService.cs"),
            os.path.join(WORKSPACE, r"src\StartupMonitor.Shared\Helpers\AuthenticodeVerifier.cs"),
            os.path.join(WORKSPACE, r"src\StartupMonitor.Service\Services\EntryOperations.cs"),
        ],
        "prompt": (
            "Ты — эксперт по многопоточности TPL и каналам System.Threading.Channels в .NET 8.\n"
            "Сформируй реализационный код для конвейера сканирования автозапуска:\n"
            "1. Потокобезопасный AuthenticodeVerifier без Stampede-эффекта и грубого Clear().\n"
            "2. Оптимизация _enrichCache до O(1) вытеснения устаревших элементов.\n"
            "3. Конвейер Channel<StartupEntry>: параллельные производители реестра (HKLM, HKCU, HKEY_USERS через Parallel.ForEachAsync) и N потребителей на ядрах процессора для ResolveAndEnrichEntry.\n"
            "Выдай готовые фрагменты кода на C#."
        ),
    },
    {
        "id": "agent_3_ui_tray_throttle",
        "title": "Субагент 3: Заморозка таймеров при сворачивании в трей и разгрузка UI Dispatcher",
        "output_file": os.path.join(WORKSPACE, "docs", "AUDIT_DEEPSEEK_5_AGENT_3_UI_TRAY.md"),
        "files": [
            os.path.join(WORKSPACE, r"src\StartupMonitor.UI\ViewModels\MainViewModel.cs"),
            os.path.join(WORKSPACE, r"src\StartupMonitor.UI\Views\MainWindow.xaml.cs"),
            os.path.join(WORKSPACE, r"src\StartupMonitor.UI\Services\PipeClient.cs"),
        ],
        "prompt": (
            "Ты — ведущий разработчик WPF и высокоскоростного UI.\n"
            "Сформируй код заморозки фоновых таймеров в трее и разгрузки Dispatcher:\n"
            "1. Метод SuspendAllBackgroundActivity() и ResumeAllBackgroundActivity() в MainViewModel (остановка _pollingTimer, _themePollTimer, _cpuUsageTimer, _logWatchTimer, UnsubscribeProcessMetrics).\n"
            "2. Привязка к StateChanged / IsVisibleChanged / RestoreFromTray в MainWindow.xaml.cs.\n"
            "3. Вынос WindowHelper.GetProcessIdsWithWindows() и парсинга больших JSON в фоновые потоки.\n"
            "Выдай готовый C# код."
        ),
    },
    {
        "id": "agent_4_network_struct_keys",
        "title": "Субагент 4: ProcessNetworkTracker и устранение GC-мусора при сборе сокетов",
        "output_file": os.path.join(WORKSPACE, "docs", "AUDIT_DEEPSEEK_5_AGENT_4_NETWORK.md"),
        "files": [
            os.path.join(WORKSPACE, r"src\StartupMonitor.Service\Services\ProcessNetworkTracker.cs"),
            os.path.join(WORKSPACE, r"src\StartupMonitor.Service\Services\ProcessMetricsTracker.cs"),
        ],
        "prompt": (
            "Ты — эксперт по высокопроизводительному P/Invoke и оптимизации выделения памяти в .NET 8.\n"
            "Сформируй код оптимизации ProcessNetworkTracker и ProcessMetricsTracker:\n"
            "1. Замена строковых ключей сокетов на компактные readonly struct SocketKey4 и SocketKey6 без аллокаций в куче.\n"
            "2. Переиспользование буферов AllocHGlobal через ReAllocHGlobal / EnsureBuffer.\n"
            "3. Кэширование снимка Toolhelp32 (Parent PID, Threads) в ProcessMetricsTracker на 5 секунд.\n"
            "Выдай готовый C# код."
        ),
    },
    {
        "id": "agent_5_toolbox_parallel_engines",
        "title": "Субагент 5: Многопоточное ускорение утилит Toolbox (DiskAnalyzer, DuplicateFinder, Hashes)",
        "output_file": os.path.join(WORKSPACE, "docs", "AUDIT_DEEPSEEK_5_AGENT_5_TOOLBOX.md"),
        "files": [
            os.path.join(WORKSPACE, r"src\StartupMonitor.UI\Services\HashAndSecurityHelper.cs"),
            os.path.join(WORKSPACE, r"src\StartupMonitor.Service\Services\Toolbox\JunkCleanerService.cs"),
            os.path.join(WORKSPACE, r"src\StartupMonitor.Service\Services\Toolbox\FileLocksmithService.cs"),
        ],
        "prompt": (
            "Ты — специалист по параллельным алгоритмам файлового I/O в .NET 8.\n"
            "Сформируй код оптимизации утилит Toolbox:\n"
            "1. Параллельный расчет контрольных сумм и хешей (SHA256, SHA1, MD5) на всех ядрах процессора.\n"
            "2. Параллельный скан категорий мусорных файлов в JunkCleanerService через Parallel.ForEachAsync.\n"
            "3. Гарантированная отмена через CancellationToken всех запущенных операций Toolbox при уходе со вкладки.\n"
            "Выдай готовый C# код."
        ),
    },
]

def run_task(task):
    print(f"[*] Starting {task['title']}...", flush=True)
    start_time = time.time()
    result = run_subagent_task(
        prompt=task["prompt"],
        model=MODEL,
        work_dir=WORKSPACE,
        files=task["files"],
        variant="medium",
        timeout_seconds=600,
    )
    elapsed = time.time() - start_time
    print(f"[+] Finished {task['title']} in {elapsed:.1f}s (Success: {result.success})", flush=True)
    
    header = f"# {task['title']}\n\n"
    header += f"- Модель субагента: `{MODEL}`\n"
    header += f"- Время выполнения: {elapsed:.1f} сек\n"
    header += f"- Статус: {'Успешно' if result.success else 'Ошибка (Exit code: ' + str(result.exit_code) + ')'}\n\n---\n\n"
    content = header + (result.stdout or "")
    if result.stderr:
        content += f"\n\n### Логи выполнения / Предупреждения:\n```\n{result.stderr}\n```\n"
        
    os.makedirs(os.path.dirname(task["output_file"]), exist_ok=True)
    with open(task["output_file"], "w", encoding="utf-8") as f:
        f.write(content)
    return task["id"], result.success, elapsed

def main():
    print(f"=== Запуск параллельного роя из 5 субагентов OpenCode ({MODEL}) ===", flush=True)
    start_all = time.time()
    with ThreadPoolExecutor(max_workers=5) as executor:
        futures = [executor.submit(run_task, t) for t in TASKS]
        results = [f.result() for f in futures]
    total_time = time.time() - start_all
    print(f"=== Рой из 5 субагентов успешно завершил работу за {total_time:.1f} сек ===", flush=True)
    for tid, success, el in results:
        print(f"  - {tid}: {'OK' if success else 'FAIL'} ({el:.1f}s)", flush=True)

if __name__ == "__main__":
    main()

