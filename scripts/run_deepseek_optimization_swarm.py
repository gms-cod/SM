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
        "id": "subagent_1_service_idle",
        "title": "Субагент 1 (DeepSeek V4.1 Flash): Служба Windows, фоновые циклы и спящий режим 0% CPU Idle",
        "output_file": os.path.join(WORKSPACE, "docs", "SWARM_DEEPSEEK_1_SERVICE_IDLE.md"),
        "files": [
            os.path.join(WORKSPACE, r"src\StartupMonitor.Service\Pipe\PipeServer.cs"),
            os.path.join(WORKSPACE, r"src\StartupMonitor.Service\Services\SystemScannerService.cs"),
            os.path.join(WORKSPACE, r"src\StartupMonitor.Service\Services\Firewall\ProcessPortDefenderService.cs"),
            os.path.join(WORKSPACE, r"src\StartupMonitor.Service\Services\Firewall\NetworkConnectionAlertService.cs"),
            os.path.join(WORKSPACE, r"src\StartupMonitor.Service\Services\EntryOperations.cs"),
        ],
        "prompt": (
            "Ты — ведущий системный архитектор и инженер по оптимизации .NET 8 / Windows Services.\n"
            "Проведи глубокий аудит фоновых циклов, таймеров и подпрограмм службы StartupMonitor.Service.\n\n"
            "ЦЕЛИ И ЗАДАЧИ:\n"
            "1. Приостановка неиспользуемых подпрограмм (Smart Sleep / Idle Suspension):\n"
            "   - BackgroundCleanupLoop в PipeServer.cs: сейчас работает постоянный опрос. Как усыплять фоновую активность службы, если нет активных подключений UI-клиентов через Named Pipe?\n"
            "   - ProcessPortDefenderService: таймер _pollTimer. Как полностью глушить опрос, если правила выключены (Rules.Count == 0 или IsGloballyEnabled == false) или порты не задействованы?\n"
            "   - NetworkConnectionAlertService: фоновое сканирование новых сетевых соединений (ScanNewConnections). Как перевести в спящий режим, когда окно мониторинга сети закрыто?\n"
            "2. Переход с Polling на реактивные механизмы:\n"
            "   - Использование ManualResetEventSlim, System.Threading.Channels, CancellationTokenSource и подписок вместо периодических Thread.Sleep и таймеров.\n"
            "3. Замеры самых тормозящих частей службы и оптимизация:\n"
            "   - ResolveAndEnrichEntry, очистка записей, дисковые операции JSON-хранилища.\n\n"
            "Выдай структурированный детальный технический отчет на русском языке с замерами узких мест, архитектурой спящего режима и готовыми примерами кода на C#."
        ),
    },
    {
        "id": "subagent_2_scanners_multithreading",
        "title": "Субагент 2 (DeepSeek V4.1 Flash): Многопоточное распараллеливание сканеров автозапуска по ядрам CPU",
        "output_file": os.path.join(WORKSPACE, "docs", "SWARM_DEEPSEEK_2_SCANNERS_PARALLEL.md"),
        "files": [
            os.path.join(WORKSPACE, r"src\StartupMonitor.Service\Scanners\RegistryScanner.cs"),
            os.path.join(WORKSPACE, r"src\StartupMonitor.Service\Scanners\TaskSchedulerScanner.cs"),
            os.path.join(WORKSPACE, r"src\StartupMonitor.Service\Scanners\WmiScanner.cs"),
            os.path.join(WORKSPACE, r"src\StartupMonitor.Service\Scanners\StartupFolderScanner.cs"),
            os.path.join(WORKSPACE, r"src\StartupMonitor.Service\Services\StartupScanner.cs"),
            os.path.join(WORKSPACE, r"src\StartupMonitor.Shared\Helpers\AuthenticodeVerifier.cs"),
        ],
        "prompt": (
            "Ты — эксперт по параллельным вычислениям, многопоточности (TPL, Parallel.ForEachAsync, SIMD) и Win32 I/O.\n"
            "Проведи глубокий аудит сканеров автозапуска (StartupScanner, RegistryScanner, TaskSchedulerScanner, WmiScanner, StartupFolderScanner).\n\n"
            "ЦЕЛИ И ЗАДАЧИ:\n"
            "1. Замер самых тормозящих частей сканирования:\n"
            "   - Сканирование CLSID/меню проводника (>7000 записей), опрос Планировщика задач, вызовы WinVerifyTrust (AuthenticodeVerifier) и извлечение иконок/хешей.\n"
            "2. Распараллеливание на все доступные ядра процессора (Multi-Core Scaling):\n"
            "   - Как параллельно опрашивать ветки реестра HKLM, HKCU и пользовательские кусты HKEY_USERS через Task.WhenAll / Parallel.ForEachAsync?\n"
            "   - Как развязать чтение реестра/диска и CPU-интенсивную валидацию через конвейер (Producer-Consumer на System.Threading.Channels.Channel<StartupEntry>)?\n"
            "3. Пул потоков и предотвращение насыщения ввода-вывода (Thread starvation, I/O saturation):\n"
            "   - Балансировка степени параллелизма (Environment.ProcessorCount), пулинг буферов ArrayPool/MemoryPool.\n\n"
            "Выдай структурированный детальный технический отчет на русском языке с замерами узких мест, схемой конвейера и готовыми примерами кода на C#."
        ),
    },
    {
        "id": "subagent_3_ui_telemetry_optimization",
        "title": "Субагент 3 (DeepSeek V4.1 Flash): WPF UI, фоновые таймеры трея, диспетчер задач и телеметрия сети",
        "output_file": os.path.join(WORKSPACE, "docs", "SWARM_DEEPSEEK_3_UI_TELEMETRY.md"),
        "files": [
            os.path.join(WORKSPACE, r"src\StartupMonitor.UI\ViewModels\MainViewModel.cs"),
            os.path.join(WORKSPACE, r"src\StartupMonitor.UI\ViewModels\TaskManagerViewModel.cs"),
            os.path.join(WORKSPACE, r"src\StartupMonitor.Service\Services\ProcessMetricsTracker.cs"),
            os.path.join(WORKSPACE, r"src\StartupMonitor.Service\Services\ProcessNetworkTracker.cs"),
            os.path.join(WORKSPACE, r"src\StartupMonitor.UI\Services\PipeClient.cs"),
        ],
        "prompt": (
            "Ты — ведущий инженер по WPF, графическому рендерингу (60 FPS, виртуализация) и высокопроизводительному IPC в .NET 8.\n"
            "Проведи глубокий аудит пользовательского интерфейса StartupMonitor.UI и подсистем телеметрии процессов и сети.\n\n"
            "ЦЕЛИ И ЗАДАЧИ:\n"
            "1. Замер самых ресурсоемких частей UI и телеметрии:\n"
            "   - GetExtendedTcpTable / GetExtendedUdpTable в ProcessNetworkTracker, сбор CPU/RAM процессов каждые 1.5 сек в ProcessMetricsTracker.\n"
            "   - Нагрузка на Dispatcher UI при обновлении дерева процессов и списков автозапуска.\n"
            "2. Приостановка при сворачивании в трей (Tray Inactivity & Dynamic Throttle):\n"
            "   - Остановка DispatcherTimer, таймеров обновления логов, метрик и Named Pipe запросов, когда окно скрыто в трей.\n"
            "   - Мгновенное возобновление при клике на иконку в трее.\n"
            "3. Разгрузка основного потока (UI Offloading):\n"
            "   - Десериализация больших JSON-пакетов Named Pipe и дифференциальное сравнение списков в фоновых потоках (Task.Run) перед передачей в ObservableCollection.\n"
            "   - Оптимизация аллокаций памяти (устранение создания десятков тысяч DTO каждую секунду, кэширование сетевых соединений).\n\n"
            "Выдай структурированный детальный технический отчет на русском языке с замерами узких мест, планом разгрузки Dispatcher и готовыми примерами кода на C#."
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
    print(f"=== Запуск параллельного роя из 3 субагентов OpenCode ({MODEL}) ===", flush=True)
    start_all = time.time()
    with ThreadPoolExecutor(max_workers=3) as executor:
        futures = [executor.submit(run_task, t) for t in TASKS]
        results = [f.result() for f in futures]
    total_time = time.time() - start_all
    print(f"=== Рой субагентов успешно завершил работу за {total_time:.1f} сек ===", flush=True)
    for tid, success, el in results:
        print(f"  - {tid}: {'OK' if success else 'FAIL'} ({el:.1f}s)", flush=True)

if __name__ == "__main__":
    main()

