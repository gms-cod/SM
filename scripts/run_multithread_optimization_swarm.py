import sys
import os
import time
from concurrent.futures import ThreadPoolExecutor

sys.path.insert(0, r"d:\Nas_sync\Projects\Gemeni+opencode")
from src.opencode_runner import run_subagent_task

TASKS = [
    {
        "id": "service_sleep_idle",
        "title": "Subagent 1: Service Background Loops & Idle Zero-CPU Engine",
        "output_file": r"d:\Nas_sync\Coding\StartupMonitor\docs\AUDIT_SWARM_1_SERVICE_IDLE.md",
        "files": [
            r"d:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.Service\Pipe\PipeServer.cs",
            r"d:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.Service\Services\Firewall\ProcessPortDefenderService.cs",
            r"d:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.Service\Services\Firewall\NetworkConnectionAlertService.cs",
            r"d:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.Service\Services\EntryOperations.cs",
        ],
        "prompt": (
            "Проведи глубокий технический аудит службы StartupMonitor.Service на предмет перевода всех подсистем в режим строгого нулевого потребления процессора (0% CPU Idle), когда программа или её модули не активны.\n"
            "Изучи:\n"
            "1. BackgroundCleanupLoop в PipeServer.cs: постоянный опрос каждые 10 сек (KillProcessesByRules, JsonDataStore.Load, CleanupStaleEntries). Как перевести на реактивные события от Watcher'ов реестра/процессов (WaitHandle, ManualResetEventSlim, Channel) вместо бесконечного polling-цикла?\n"
            "2. ProcessPortDefenderService: таймер _pollTimer. Как полностью глушить таймер, когда правила выключены (IsGloballyEnabled = false или Rules.Count == 0), и возобновлять только при активации?\n"
            "3. NetworkConnectionAlertService: фоновый скан ScanNewConnections. Как усыплять при отсутствии подключенных клиентов UI через Pipe?\n"
            "4. KillProcessesByRules: предотвращение холостого сканирования процессов, если в Blacklist нет активных правил NotifyAndRemove.\n"
            "Выдай конкретные архитектурные решения с примерами кода и оценкой экономии CPU."
        ),
    },
    {
        "id": "scanner_multithreading",
        "title": "Subagent 2: Startup Scanners Core Parallelization (Registry, WMI, Tasks)",
        "output_file": r"d:\Nas_sync\Coding\StartupMonitor\docs\AUDIT_SWARM_2_SCANNER_PARALLEL.md",
        "files": [
            r"d:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.Service\Scanners\RegistryScanner.cs",
            r"d:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.Service\Scanners\TaskSchedulerScanner.cs",
            r"d:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.Service\Scanners\WmiScanner.cs",
            r"d:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.Service\Services\StartupScanner.cs",
        ],
        "prompt": (
            "Проведи глубокий технический аудит сканеров автозапуска (StartupScanner, RegistryScanner, TaskSchedulerScanner, WmiScanner) на предмет многопоточного параллельного выполнения по всем ядрам процессора.\n"
            "Изучи:\n"
            "1. Сейчас сканирование мест автозагрузки (HKLM Run, HKCU Run, Startup Folder, Services, Scheduled Tasks, WMI) происходит последовательно или частично параллельно? Где блокируется один поток процессора на операциях ввода-вывода реестра и диска?\n"
            "2. Как распараллелить опрос нескольких пользовательских кустов (HKEY_USERS для всех профилей) и веток HKLM/HKCU через Parallel.ForEach / Task.WhenAll с пулом потоков ThreadPool?\n"
            "3. Как организовать конвейерную обработку (Producer-Consumer через System.Threading.Channels.Channel<StartupEntry>) для параллельного парсинга командных строк, извлечения иконок и цифровых подписей на нескольких ядрах одновременно?\n"
            "Предложи пошаговый план внедрения многопоточности с замерами производительности."
        ),
    },
    {
        "id": "telemetry_multicore",
        "title": "Subagent 3: TaskManager & Realtime Telemetry Core Load Balancing",
        "output_file": r"d:\Nas_sync\Coding\StartupMonitor\docs\AUDIT_SWARM_3_TELEMETRY_MULTICORE.md",
        "files": [
            r"d:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.Service\Services\ProcessMetricsTracker.cs",
            r"d:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.Service\Services\ProcessMetadataEnricher.cs",
            r"d:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.Service\Services\ProcessNetworkTracker.cs",
            r"d:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.UI\ViewModels\TaskManagerViewModel.cs",
        ],
        "prompt": (
            "Проведи глубокий аудит подсистем телеметрии процессов (Диспетчер задач, сбор CPU/RAM/Диск/Сеть) на предмет масштабирования по ядрам процессора и устранения перегрузки одного ядра.\n"
            "Изучи:\n"
            "1. ProcessMetricsTracker.CollectSnapshot: как распределить опрос ~300 процессов по доступным ядрам (Partitioner.Create, Parallel.For) с минимальными накладными расходами на контекстные переключения?\n"
            "2. ProcessNetworkTracker.GetSockets: параллельный опрос TCP IPv4, TCP IPv6, UDP таблиц через Task.WhenAll вместо последовательного вызова IP Helper API.\n"
            "3. TaskManagerViewModel в UI: как разгрузить UI-поток при сортировке, фильтрации и обновлении ObservableCollection. Вынос группировки и поиска в фоновый поток (Background Task) с пакетной отправкой изменений в Dispatcher.\n"
            "4. Механизм деактивации: полное обнуление нагрузки на CPU (Stop/Pause сбора метрик), когда вкладка 'Диспетчер задач' закрыта или окно свернуто в трей.\n"
            "Предоставь детальный план оптимизации и архитектурные рекомендации."
        ),
    },
    {
        "id": "toolbox_heavy_engines",
        "title": "Subagent 4: Toolbox Heavy Utility Engines Parallelization & Deactivation",
        "output_file": r"d:\Nas_sync\Coding\StartupMonitor\docs\AUDIT_SWARM_4_TOOLBOX_PARALLEL.md",
        "files": [
            r"d:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.Service\Services\Toolbox\DiskAnalyzerService.cs",
            r"d:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.Service\Services\Toolbox\DuplicateFinderService.cs",
            r"d:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.Service\Services\Toolbox\FileLocksmithService.cs",
            r"d:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.UI\Views\ToolboxView.xaml.cs",
        ],
        "prompt": (
            "Проведи глубокий технический аудит тяжелых утилит Toolbox (DiskAnalyzer, DuplicateFinder, FileLocksmith) на предмет многопоточного ускорения по всем ядрам CPU и гарантированной остановки при неактивности.\n"
            "Изучи:\n"
            "1. DiskAnalyzerService: сканирование файловой системы и подсчет размеров папок. Как реализовать Work-Stealing параллельный обход каталогов (через ConcurrentQueue или Task-based дерево), чтобы задействовать все ядра SSD/NVMe без блокировки?\n"
            "2. DuplicateFinderService: вычисление хешей файлов (SHA256). Как распараллелить стадию хеширования файлов одинакового размера на N ядер процессора с ограничением очереди чтения диска?\n"
            "3. FileLocksmithService: опрос заблокированных файлов через Restart Manager API. Параллельный опрос дескрипторов.\n"
            "4. Мгновенная остановка и деактивация: корректный CancellationToken во всех методах утилит. Гарантия, что при уходе со страницы Toolbox или закрытии окна все фоновые задачи немедленно прекращают работу и освобождают 100% ресурсов CPU.\n"
            "Сформируй пошаговые рекомендации и паттерны реализации."
        ),
    },
    {
        "id": "ipc_pipeline_concurrency",
        "title": "Subagent 5: IPC Pipe Pipeline, Dispatching & Serialization Concurrency",
        "output_file": r"d:\Nas_sync\Coding\StartupMonitor\docs\AUDIT_SWARM_5_IPC_CONCURRENCY.md",
        "files": [
            r"d:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.Service\Pipe\PipeServer.cs",
            r"d:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.Service\Pipe\EventPipeServer.cs",
            r"d:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.UI\Services\PipeClient.cs",
            r"d:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.Shared\Models\PipeMessage.cs",
        ],
        "prompt": (
            "Проведи глубокий технический аудит конвейера межпроцессного взаимодействия (IPC Named Pipes) на предмет параллелизации обработки запросов и устранения бутылочных горлышек блокировок.\n"
            "Изучи:\n"
            "1. PipeServer.ProcessClientRequestsAsync: как сейчас обрабатываются входящие запросы клиентов? Последовательно в одном цикле чтения StreamReader.ReadLineAsync или параллельно через Task.Run?\n"
            "2. Блокировка _sendLock в PipeClient.cs: конкуренция между высокочастотными фоновыми таймерами (live-соединения 2с, алерты 2.5с) и пользовательскими операциями (создание/удаление правил, смена настроек). Как разделить очереди сообщений или сделать мульти-плексирование каналов?\n"
            "3. Оптимизация сериализации: асинхронная потоковая десериализация Utf8JsonReader / System.Text.Json بدون создания промежуточных строковых буферов в LOH.\n"
            "4. Реактивная модель событий: отправка событий только активным подписчикам с немедленным засыпанием генераторов событий при отсутствии подключенных UI-клиентов.\n"
            "Выдай детальный архитектурный план оптимизации IPC с примерами многопоточного диспетчера."
        ),
    },
]

def run_task(task):
    print(f"[*] Starting {task['title']}...")
    start_time = time.time()
    result = run_subagent_task(
        prompt=task["prompt"],
        work_dir=r"d:\Nas_sync\Coding\StartupMonitor",
        files=task["files"],
        variant="medium",
        timeout_seconds=600,
    )
    elapsed = time.time() - start_time
    print(f"[+] Finished {task['title']} in {elapsed:.1f}s (Success: {result.success})")
    
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
    print("=== Launching 5 OpenCode Subagents in Parallel (Multi-core & Zero-CPU Audit) ===")
    start_total = time.time()
    os.makedirs(r"d:\Nas_sync\Coding\StartupMonitor\docs", exist_ok=True)
    
    with ThreadPoolExecutor(max_workers=5) as executor:
        results = list(executor.map(run_task, TASKS))
        
    total_elapsed = time.time() - start_total
    print(f"\n=== All 5 Subagents Completed in {total_elapsed:.1f}s ===")
    for task_id, success, elapsed in results:
        status_str = "SUCCESS" if success else "FAILED"
        print(f" - [{status_str}] {task_id}: {elapsed:.1f}s")

if __name__ == "__main__":
    main()

