import sys
import os
import time
from concurrent.futures import ThreadPoolExecutor

sys.path.insert(0, r"d:\Nas_sync\Projects\Gemeni+opencode")
from src.opencode_runner import run_subagent_task

TASKS = [
    {
        "id": "firewall_engine",
        "title": "Subagent 1: Windows Firewall COM Engine & Rule Management",
        "output_file": r"d:\Nas_sync\Coding\StartupMonitor\docs\SWARM_FIREWALL_ENGINE.md",
        "files": [
            r"d:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.Service\Services\ProcessNetworkTracker.cs",
            r"d:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.Shared\Models\ProcessMetrics.cs",
        ],
        "prompt": (
            "Ты — ведущий системный архитектор сетевой безопасности Windows и .NET 8.\n"
            "Спроектируй класс WindowsFirewallService для управления стандартным брандмауэром Windows (Windows Advanced Firewall) "
            "через COM-интерфейс INetFwPolicy2 / INetFwRule / INetFwRules (Type.GetTypeFromProgID(\"HNetCfg.FwPolicy2\")).\n\n"
            "ТРЕБОВАНИЯ:\n"
            "1. Блокировка/разблокировка удаленного IP (Inbound/Outbound):\n"
            "   - Быстрое добавление IP в агрегированное правило блокировки StartupMonitor_Blocked_IPs (для минимизации оверхеда правил брандмауэра) либо одиночные правила.\n"
            "   - Удаление IP / разблокировка.\n"
            "2. Блокировка/разблокировка портов (TCP/UDP):\n"
            "   - Для всей системы или с привязкой к конкретному исполняемому файлу (ApplicationName / FullPath).\n"
            "   - Определение текущего статуса порта/IP (заблокирован ли он уже брандмауэром Windows).\n"
            "3. Архитектура надежности:\n"
            "   - Служба работает от имени SYSTEM (полные права без UAC).\n"
            "   - Корректное освобождение COM-объектов (Marshal.ReleaseComObject) без утечек RCW.\n"
            "   - Обработка ошибок (сервис брандмауэра выключен, профили Domain/Private/Public).\n"
            "Подготовь подробный C# код сервиса, DTO моделей и архитектурную схему."
        ),
    },
    {
        "id": "defender_engine",
        "title": "Subagent 2: RDP Defender Pattern & Rate-Limiting Flood Protection",
        "output_file": r"d:\Nas_sync\Coding\StartupMonitor\docs\SWARM_PORT_DEFENDER.md",
        "files": [
            r"d:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.Service\Services\ProcessNetworkTracker.cs",
            r"d:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.Service\Services\ProcessMetricsTracker.cs",
        ],
        "prompt": (
            "Ты — эксперт по защите от атак типа Brute-Force, Connection Flood и Port Scanning на платформе Windows.\n"
            "Проведи глубокий анализ концепции RDP Defender (RdpGuard, Fail2ban для Windows, EvlWatcher) и спроектируй сервис ProcessPortDefenderService "
            "для защиты ЛЮБОЙ программы и ЛЮБОГО входящего порта в StartupMonitor.\n\n"
            "ТРЕБОВАНИЯ:\n"
            "1. Алгоритм детекции флуда / перебора:\n"
            "   - Мониторинг входящих подключений на основе периодических снимков сокетов из ProcessNetworkTracker (GetExtendedTcpTable) и Connection Tracking Table.\n"
            "   - Скользящее временное окно (Sliding Window): порог N подключений за интервал времени на выбор (в секунду, в минуту, в час).\n"
            "2. Механизм временного бана (Temporary Ban) и разбана:\n"
            "   - Автоматическое добавление атакующего IP в брандмауэр Windows на заданное время (например 15 мин, 1 час, 24 часа).\n"
            "   - Фоновый воркер AutoUnbanWorker с таймером и безопасным снятием бана.\n"
            "   - Эскалация бана: при повторных атаках после разбана — увеличение срока (х2, х4 или постоянный бан).\n"
            "3. Белый список (Whitelist):\n"
            "   - Локальные IP (127.0.0.1, ::1), подсети RFC 1918 (10.0.0.0/8, 172.16.0.0/12, 192.168.0.0/16), шлюз по умолчанию.\n"
            "   - Пользовательский список исключений.\n"
            "4. Защита от переполнения памяти (LRU-кэш, автоочистка неактивных записей трекинга сокетов).\n"
            "Подготовь полную архитектуру сервиса, алгоритм, C# прототип и формулы скользящего окна."
        ),
    },
    {
        "id": "ui_ux_variants",
        "title": "Subagent 3: WPF UI/UX Design & 4 Interface Variants",
        "output_file": r"d:\Nas_sync\Coding\StartupMonitor\docs\SWARM_UI_UX_VARIANTS.md",
        "files": [
            r"d:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.UI\Views\Dialogs\ProcessConnectionsWindow.xaml",
            r"d:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.UI\Views\Dialogs\ProcessConnectionsWindow.xaml.cs",
            r"d:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.UI\Views\MainWindow.xaml",
        ],
        "prompt": (
            "Ты — ведущий UI/UX дизайнер и эксперт WPF (Wpf.Ui, Fluent Design, Windows 11).\n"
            "Спроектируй User-Friendly интерфейс для управления сетевой безопасностью, блокировкой IP/портов и защитой Port Defender в StartupMonitor.\n\n"
            "ТРЕБОВАНИЯ:\n"
            "Создай 4 детальных варианта интерфейса с подробной XAML-разметкой, мокапами, схемами взаимодействия и описанием UX:\n\n"
            "ВАРИАНТ 1: «Контекстный инспектор в ProcessConnectionsWindow»\n"
            "- Добавление в существующее окно сетевых соединений контекстного меню на строках: «Заблокировать IP», «Заблокировать порт», «Настроить Port Defender».\n"
            "- Бейджи статуса Firewall в таблице сокетов ([Разрешено], [Заблокирован], [Под защитой]).\n"
            "- Верхняя карточка со сводкой статуса защиты и кнопкой «Включить Defender».\n\n"
            "ВАРИАНТ 2: «Dedicated Network Security Hub» (Центр сетевого экрана в MainWindow)\n"
            "- Отдельная вкладка/раздел приложения с живой сводкой, картой входящих соединений, списком активных временных банов, белым списком и переключателями правил.\n\n"
            "ВАРИАНТ 3: «Interactive Flyout / Modal Wizard» (Визард настройки защиты)\n"
            "- Удобный диалог/панель настройки правила: выбор порога (слайдер/число), выбор единицы времени (сек/мин/час), длительность бана (15 мин/1 ч/24 ч/навсегда), белый список.\n\n"
            "ВАРИАНТ 4: «Shield Badges в Диспетчере задач»\n"
            "- Интеграция индикаторов защиты (щит с активным состоянием) прямо в строки процессов Диспетчера задач с быстрым всплывающим flyout-меню.\n\n"
            "Для каждого варианта предоставь полный XAML код компонентов, состояния тем (Dark/Light) и пользовательский сценарий (User Journey)."
        ),
    },
    {
        "id": "ipc_deployment_plan",
        "title": "Subagent 4: IPC Architecture, Data Contracts & Deployment Roadmap",
        "output_file": r"d:\Nas_sync\Coding\StartupMonitor\docs\SWARM_IPC_ROADMAP.md",
        "files": [
            r"d:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.Shared\Models\PipeMessage.cs",
            r"d:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.Service\Pipe\PipeServer.cs",
            r"d:\Nas_sync\Coding\StartupMonitor\src\StartupMonitor.UI\Services\PipeClient.cs",
        ],
        "prompt": (
            "Ты — ведущий инженер по распределенным системам, IPC и выпуску релизов .NET 8.\n"
            "Спроектируй протокол взаимодействия между UI и службой для функционала Firewall/Defender, структуры данных и пошаговый план внедрения.\n\n"
            "ТРЕБОВАНИЯ:\n"
            "1. Shared DTO модели данных:\n"
            "   - FirewallRuleDto, BlockIpRequest, BlockPortRequest, PortDefenderConfigDto, ActiveBanDto, UnbanIpRequest, DefenderAttackEventDto.\n"
            "2. Named Pipe IPC сообщения (PipeMessageTypes):\n"
            "   - GetFirewallStatus, ToggleFirewallRule, ConfigurePortDefender, GetActiveBans, UnbanIp, ResetDefenderStats.\n"
            "3. Безопасность и Zero-Crash:\n"
            "   - Защита от самоблокировки (валидация IP перед добавлением правила: запрет блокировки шлюза, DNS, 127.0.0.1).\n"
            "   - Персистентность правил и банов в settings.json.\n"
            "   - Восстановление состояния при перезапуске службы.\n"
            "4. Детальный пошаговый план развертывания (Этапы 1–6) для включения в файл release_firewall.md.\n"
            "Предоставь готовые C# DTO классы, методы обработчиков в PipeServer/PipeClient и матрицу рисков."
        ),
    },
]

def run_task(task):
    print(f"[*] Starting {task['title']}...", flush=True)
    start_time = time.time()
    os.makedirs(os.path.dirname(task["output_file"]), exist_ok=True)
    result = run_subagent_task(
        prompt=task["prompt"],
        work_dir=r"d:\Nas_sync\Coding\StartupMonitor",
        files=task["files"],
        variant="high",
        timeout_seconds=600,
    )
    elapsed = time.time() - start_time
    print(f"[+] Finished {task['title']} in {elapsed:.1f}s (Success: {result.success})", flush=True)
    if result.stdout:
        with open(task["output_file"], "w", encoding="utf-8") as f:
            f.write(result.stdout)

def main():
    print(f"=== Launching OpenCode Subagent Swarm ({len(TASKS)} parallel workers) ===")
    start_all = time.time()
    with ThreadPoolExecutor(max_workers=len(TASKS)) as executor:
        futures = [executor.submit(run_task, t) for t in TASKS]
        for f in futures:
            f.result()
    total_time = time.time() - start_all
    print(f"=== Swarm completed in {total_time:.1f}s ===")

if __name__ == "__main__":
    main()
