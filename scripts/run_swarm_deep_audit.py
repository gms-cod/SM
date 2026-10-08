import sys
import os
import time
from concurrent.futures import ThreadPoolExecutor

sys.path.insert(0, r"d:\Nas_sync\Projects\Gemeni+opencode")
from src.opencode_runner import run_subagent_task

SWARM_TASKS = [
    {
        "id": "agent_scanner",
        "title": "OpenCode Swarm Agent 1: SystemScanner & CLSID Explorer Audit",
        "output_file": r"d:\Nas_sync\Coding\StartupMonitor\docs\SWARM_1_SCANNER_CLSID.md",
        "files": [
            r"d:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.Service\Services\SystemScannerService.cs",
            r"d:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.Service\Services\EntryOperations.cs",
            r"d:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.Service\Pipe\PipeServer.cs",
        ],
        "prompt": (
            "Ты — элитный реверс-инженер и системный разработчик Windows internals.\n"
            "ПРОБЛЕМА: Профилировщик зафиксировал, что Service.ScanAll длится 12.3 секунды, вызывая ResolveAndEnrichEntry 7817 раз!\n"
            "Причина: ScanClsidEntries сканирует все 7485 записей HKCR\\CLSID и для каждой делает дисковые операции, проверку подписей и эвристики.\n"
            "ЗАДАЧА:\n"
            "1. Проанализируй ScanClsidEntries, ScanAll и CleanupStaleEntries.\n"
            "2. Предложи способ оптимизации: фильтрация только реально зарегистрированных в Explorer контекстных меню (HKCR\\*\\shellex, Directory\\shellex), инкрементальный скан CLSID или отложенная ленивая проверка.\n"
            "3. Найди потенциальные баги и глюки (удаление нужных системных записей при cleanup, гонки потоков при параллельной модификации config.Entries).\n"
            "4. Составь отчет с точными номерами строк, механикой проблемы и эффектом (ms, % CPU)."
        ),
    },
    {
        "id": "agent_watchers",
        "title": "OpenCode Swarm Agent 2: Watchers, WMI & SCM Audit",
        "output_file": r"d:\Nas_sync\Coding\StartupMonitor\docs\SWARM_2_WATCHERS_WMI.md",
        "files": [
            r"d:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.Service\Watchers\RegistryWatcher.cs",
            r"d:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.Service\Watchers\ServiceWatcher.cs",
            r"d:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.Service\Watchers\TaskSchedulerWatcher.cs",
            r"d:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.Service\Watchers\WmiSubscriptionWatcher.cs",
            r"d:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.Service\Services\WatcherHelper.cs",
        ],
        "prompt": (
            "Ты — эксперт по многопоточности и подсистемам событий Windows.\n"
            "ЗАДАЧА:\n"
            "1. Проанализируй все фоновые вотчеры (реестр, службы, задачи, WMI, папки автозапуска).\n"
            "2. Изучи дебаунс (debounce) событий, блокировки ThreadPool в RegNotifyChangeKeyValue, циклы поллинга WMI (частота 10-15с), освобождение COM-объектов (TaskScheduler/WMI ManagementScope/EventWatcher).\n"
            "3. Найди потенциальные глюки: потеря событий при быстром создании файлов, дубликаты записей в хранилище, зависание потоков при остановке службы (CancellationToken/Dispose).\n"
            "4. Составь отчет с точными рекомендациями и оценкой снижения нагрузки."
        ),
    },
    {
        "id": "agent_metrics_network",
        "title": "OpenCode Swarm Agent 3: ProcessMetricsTracker & Network Sockets Audit",
        "output_file": r"d:\Nas_sync\Coding\StartupMonitor\docs\SWARM_3_METRICS_NETWORK.md",
        "files": [
            r"d:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.Service\Services\ProcessMetricsTracker.cs",
            r"d:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.Service\Services\ProcessNetworkTracker.cs",
            r"d:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.Service\Services\ProcessMetadataEnricher.cs",
            r"d:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.Shared\Models\ProcessInfo.cs",
        ],
        "prompt": (
            "Ты — специалист по низкоуровневому P/Invoke, IP Helper API и оптимизации диспетчеров задач.\n"
            "ФАКТ: ProcessMetricsTracker опрашивает все процессы каждые 1.5 сек. ProcessNetworkTracker собирает TCP IPv4/IPv6, UDP IPv4/IPv6 и EStats.\n"
            "ЗАДАЧА:\n"
            "1. Проанализируй аллокации (Dictionary, List, перебор открытых дескрипторов процессов OpenProcess/CloseHandle, CreateToolhelp32Snapshot).\n"
            "2. Где происходят утечки хэндлов или задержки (например, обращение к системным процессам с Access Denied, античит-драйверы, зависание NtQuerySystemInformation/QueryFullProcessImageName)?\n"
            "3. Как оптимизировать сбор сети и метрик, если открыто 300+ процессов (переиспользование буферов Marshal, пропуск неактивных процессов)?\n"
            "4. Составь отчет с точным кодом и оценкой снижения CPU/RAM."
        ),
    },
    {
        "id": "agent_ui_rendering",
        "title": "OpenCode Swarm Agent 4: WPF Virtualization & UI Thread Bottlenecks",
        "output_file": r"d:\Nas_sync\Coding\StartupMonitor\docs\SWARM_4_UI_RENDERING.md",
        "files": [
            r"d:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.UI\Views\MainWindow.xaml",
            r"d:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.UI\ViewModels\MainViewModel.cs",
            r"d:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.UI\ViewModels\TaskManagerViewModel.cs",
            r"d:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.UI\Converters\DesignHelperConverters.cs",
        ],
        "prompt": (
            "Ты — архитектор WPF и высокопроизводительного UI.\n"
            "ЗАДАЧА:\n"
            "1. Проанализируй MainWindow.xaml на предмет срыва аппаратной виртуализации (VirtualizingStackPanel.IsVirtualizing, ScrollViewer, Template, ItemsCanFocus).\n"
            "2. Изучи конвертеры (DesignHelperConverters, KasperskyIndicatorConverter) — создаются ли новые объекты в UI потоке при каждом тике или прокрутке?\n"
            "3. Исследуй работу TaskManagerViewModel: слияние коллекций (In-Place Diff vs Clear+Add), мерцание строк, сброс фокуса при сортировке и автообновлении раз в 1.5с.\n"
            "4. Найди сценарии зависания UI-потока (длинные вызовы Dispatcher.Invoke, синхронные проверки файлов) и предложи архитектурные решения."
        ),
    },
    {
        "id": "agent_ipc_data_flow",
        "title": "OpenCode Swarm Agent 5: IPC Pipes, Large Object Heap (LOH) & Storage",
        "output_file": r"d:\Nas_sync\Coding\StartupMonitor\docs\SWARM_5_IPC_LOH_STORAGE.md",
        "files": [
            r"d:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.Shared\Models\PipeMessage.cs",
            r"d:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.Shared\Storage\JsonDataStore.cs",
            r"d:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.UI\Services\PipeClient.cs",
            r"d:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.Service\Pipe\PipeServer.cs",
            r"d:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.Service\Pipe\EventPipeServer.cs",
        ],
        "prompt": (
            "Ты — ведущий эксперт по GC, оптимизации памяти (LOH, Gen 0/1/2) и высоконагруженному IPC.\n"
            "ЗАДАЧА:\n"
            "1. Проанализируй протокол Named Pipe: сериализация JSON строк (StreamWriter/StreamReader), двойная упаковка Payload, отсутствие буферизации.\n"
            "2. Оцени аллокации в LOH (>85 000 байт): размер снимков ProcessInfo (сотни процессов со строками путей и иконок), вызывающие фрагментацию LOH и частые сборы мусора Gen 2.\n"
            "3. Проанализируй JsonDataStore: сохранение конфига, дедупликация, сериализация/десериализация при мутациях.\n"
            "4. Предложи решения: ArrayPool, Memory<byte>, дельта-обновления снимков процессов, бинарный/оптимизированный JSON стриминг."
        ),
    },
    {
        "id": "agent_glitches_bugs",
        "title": "OpenCode Swarm Agent 6: Deep Glitch & Edge Case Bug Hunter",
        "output_file": r"d:\Nas_sync\Coding\StartupMonitor\docs\SWARM_6_GLITCH_BUG_HUNTER.md",
        "files": [
            r"d:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.UI\Views\MainWindow.xaml.cs",
            r"d:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.UI\App.xaml.cs",
            r"d:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.Service\Services\BlacklistManager.cs",
            r"d:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.Shared\Helpers\SystemProtectionHelper.cs",
            r"d:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.Service\Services\AutoGuards\AutoGuardEngine.cs",
        ],
        "prompt": (
            "Ты — ведущий QA-архитектор безопасности и отказоустойчивости системного ПО Windows.\n"
            "ЗАДАЧА: Проведи глубочайший поиск скрытых багов, краевых случаев (Edge Cases) и системных глюков:\n"
            "1. Ложные срабатывания Auto-Guards и BlacklistManager (блокировка легитимных обновлений драйверов, фоновых апдейтеров Chrome/Edge, антивирусов).\n"
            "2. Ошибки взаимодействия UI и Службы: рассинхронизация при перезапуске службы, зависание в трее, множественные экземпляры UI, потеря настроек в ui-settings.json.\n"
            "3. Гонки при удалении/восстановлении записей, обработка прав доступа Session 0 vs Session 1 (CreateProcessAsUser, токен пользователя).\n"
            "4. Составь список топ-10 критических уязвимостей/глюков с воспроизведением, риском и защитным патчем."
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
    print(f"=== Launching 6 OpenCode Swarm Subagents in Parallel ===", flush=True)
    start_all = time.time()
    with ThreadPoolExecutor(max_workers=6) as executor:
        futures = [executor.submit(run_task, t) for t in SWARM_TASKS]
        results = [f.result() for f in futures]
    total_time = time.time() - start_all
    print(f"=== All 6 OpenCode Swarm Subagents Completed in {total_time:.1f}s ===", flush=True)
    for r in results:
        print(f"  - {r[0]}: Success={r[1]}, Time={r[2]:.1f}s", flush=True)

if __name__ == "__main__":
    main()
