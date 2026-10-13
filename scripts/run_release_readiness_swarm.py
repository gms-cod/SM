import os
import sys
import time
from concurrent.futures import ThreadPoolExecutor, as_completed
from pathlib import Path

# Подключаем раннер OpenCode
sys.path.insert(0, r"d:\Nas_sync\Projects\Gemeni+opencode")
from src.opencode_runner import run_subagent_task

ROOT_DIR = Path(r"d:\Nas_sync\Coding\StartupMonitor")
AUDIT_DIR = ROOT_DIR / "docs" / "audit_release"
AUDIT_DIR.mkdir(parents=True, exist_ok=True)

SWARM_AGENTS = [
    {
        "id": "agent1_scanner_clsid",
        "title": "Agent 1: SystemScanner, CLSID & Registry Discovery Audit",
        "output_file": str(AUDIT_DIR / "AGENT_1_SCANNER_CLSID.md"),
        "files": [
            str(ROOT_DIR / "src/StartupMonitor.Service/Services/SystemScannerService.cs"),
            str(ROOT_DIR / "src/StartupMonitor.Service/Services/EntryOperations.cs"),
            str(ROOT_DIR / "src/StartupMonitor.Service/Services/WatcherHelper.cs"),
        ],
        "prompt": (
            "Ты — элитный реверс-инженер и системный разработчик Windows internals .NET 8.\n"
            "ПРОВЕДИ ГЛУБОКИЙ АУДИТ ПОДСИСТЕМЫ СКАНИРОВАНИЯ АВТОЗАПУСКА НА ГОТОВНОСТЬ К ПУБЛИЧНОМУ РЕЛИЗУ ДЛЯ ВСЕХ:\n"
            "1. Выяви слабые места в SystemScannerService и EntryOperations:\n"
            "   - Где код может упасть при отсутствии прав доступа к системным веткам реестра (AccessDenied)?\n"
            "   - Корректна ли очистка устаревших записей (CleanupStaleEntries)? Нет ли риска удаления системных компонентов Windows?\n"
            "   - Проблема производительности CLSID: сканируются ли тысячи ненужных COM классов или только реальные ContextMenu/ShellExtensions?\n"
            "2. Выяви недоделанные или заброшенные места (TODO, пустые catch, заглушки, полупустые проверки).\n"
            "3. Выяви лишённые смысла или избыточные части (двойной парсинг, мертвый код, фантомные вызовы).\n"
            "4. Вердикт: можно ли этот модуль отдавать в публичный релиз для миллионов пользователей? Точные рекомендации и патчи с номерами строк."
        ),
    },
    {
        "id": "agent2_watchers_wmi",
        "title": "Agent 2: Background Watchers, WMI & Scheduler Engines Audit",
        "output_file": str(AUDIT_DIR / "AGENT_2_WATCHERS_WMI.md"),
        "files": [
            str(ROOT_DIR / "src/StartupMonitor.Service/Watchers/RegistryWatcher.cs"),
            str(ROOT_DIR / "src/StartupMonitor.Service/Watchers/ServiceWatcher.cs"),
            str(ROOT_DIR / "src/StartupMonitor.Service/Watchers/TaskSchedulerWatcher.cs"),
            str(ROOT_DIR / "src/StartupMonitor.Service/Watchers/WmiSubscriptionWatcher.cs"),
            str(ROOT_DIR / "src/StartupMonitor.Service/Services/StartupFolderWatcher.cs"),
        ],
        "prompt": (
            "Ты — ведущий системный архитектор фоновых служб Windows и многопоточности .NET 8.\n"
            "ПРОВЕДИ ПОЛНЫЙ АУДИТ ФОНОВЫХ ВОТЧЕРОВ НА ПРЕДМЕТ ГОТОВНОСТИ К ВЫПУСКУ ДЛЯ ВСЕХ ПОЛЬЗОВАТЕЛЕЙ:\n"
            "1. Слабые места фонового мониторинга:\n"
            "   - Что происходит, если служба WMI (winmgmt) или Планировщик задач (Schedule) на компьютере пользователя остановлены или повреждены?\n"
            "   - Нет ли утечек COM-объектов (TaskScheduler, WMI ManagementScope/EventWatcher, дескрипторы реестра)?\n"
            "   - Как обрабатывается остановка службы (CancellationToken / Dispose)? Нет ли риска зависания службы при завершении работы Windows (Shutdown)?\n"
            "2. Недоделанные элементы: корректен ли дебаунс (debounce) событий реестра и файловой системы? Не теряются ли события при пакетной установке софта?\n"
            "3. Избыточный/бессмысленный функционал.\n"
            "4. Итоговый вердикт по надежности и список необходимых исправлений."
        ),
    },
    {
        "id": "agent3_process_inspector",
        "title": "Agent 3: Process Inspector, Metrics & Win32 P/Invoke Safety Audit",
        "output_file": str(AUDIT_DIR / "AGENT_3_PROCESS_INSPECTOR.md"),
        "files": [
            str(ROOT_DIR / "src/StartupMonitor.Service/Services/ProcessMetricsTracker.cs"),
            str(ROOT_DIR / "src/StartupMonitor.Service/Services/ProcessNetworkTracker.cs"),
            str(ROOT_DIR / "src/StartupMonitor.Service/Services/ProcessMetadataEnricher.cs"),
            str(ROOT_DIR / "src/StartupMonitor.Service/Services/ProcessInspectorService.cs"),
            str(ROOT_DIR / "src/StartupMonitor.Service/Services/ProcessStartupLinker.cs"),
        ],
        "prompt": (
            "Ты — эксперт по Win32 API, P/Invoke, безопасности ядра Windows и реверс-инжинирингу процессов.\n"
            "ПРОВЕДИ ГЛУБОКИЙ АУДИТ ДИСПЕТЧЕРА ПРОЦЕССОВ И СЕТЕВЫХ СОКЕТОВ:\n"
            "1. Слабые места безопасности и стабильности:\n"
            "   - Как модули ведут себя на защищенных процессах Windows (Protected Process Light / PPL, Anti-Cheat вроде Vanguard/EasyAntiCheat, процессы антивирусов)?\n"
            "   - Утечки нативных дескрипторов (SafeHandle, OpenProcess, CloseHandle) при P/Invoke вызовах.\n"
            "   - Корректность и устойчивость вызовов GetExtendedTcpTable / GetExtendedUdpTable (IPv4 и IPv6).\n"
            "2. Недоделанные механизмы: связывание процессов с автозапуском (ProcessStartupLinker) — насколько надежна эта связь в реальных условиях?\n"
            "3. Лишённые смысла вызовы: где сжигаются такты процессора на опрос процессов каждые 1.5 сек?\n"
            "4. Можно ли отдавать в релиз? Детальные рекомендации."
        ),
    },
    {
        "id": "agent4_firewall_port_defender",
        "title": "Agent 4: Firewall Engine & Port Defender Core Audit",
        "output_file": str(AUDIT_DIR / "AGENT_4_FIREWALL_PORT_DEFENDER.md"),
        "files": [
            str(ROOT_DIR / "src/StartupMonitor.Service/Services/Firewall/FirewallGuardService.cs"),
            str(ROOT_DIR / "src/StartupMonitor.Service/Services/Firewall/PortDefenderCoreService.cs"),
            str(ROOT_DIR / "src/StartupMonitor.Service/Services/Firewall/PortDefenderEscalationService.cs"),
            str(ROOT_DIR / "src/StartupMonitor.Service/Services/Firewall/SlidingWindowTracker.cs"),
            str(ROOT_DIR / "src/StartupMonitor.Shared/Models/Firewall/FirewallModels.cs"),
        ],
        "prompt": (
            "Ты — ведущий эксперт по сетевой безопасности Windows, Firewall API и защите от атак.\n"
            "ПРОВЕДИ АУДИТ НОВОГО МОДУЛЯ FIREWALL И PORT DEFENDER НА ПРЕДМЕТ РЕЛИЗА ДЛЯ ПУБЛИЧНОГО ДОСТУПА:\n"
            "1. Главные риски и слабые места:\n"
            "   - Защита от блокировки самого себя: надёжно ли защищены RFC 1918 (192.168.x.x, 10.x.x.x, 172.16.x.x), 127.0.0.1, шлюз, DNS? Пользователь не потеряет доступ по RDP к своему серверу при ложном срабатывании?\n"
            "   - Надежность механизма авто-разбана (Auto-Unban): что будет, если служба аварийно завершится во время бана? Правило в брандмауэре останется навсегда?\n"
            "   - Устойчивость к переполнению (SlidingWindowTracker): что происходит при атаке со 100 000 разных IP?\n"
            "2. Недоделанные части и заглушки: какие сценарии правил netsh/WFP не обработаны?\n"
            "3. Лишённые смысла абстракции.\n"
            "4. Готовность к релизу и вердикт."
        ),
    },
    {
        "id": "agent5_ipc_pipeline",
        "title": "Agent 5: IPC Pipe Pipeline & Named Pipe Security Audit",
        "output_file": str(AUDIT_DIR / "AGENT_5_IPC_PIPELINE.md"),
        "files": [
            str(ROOT_DIR / "src/StartupMonitor.Service/Pipe/PipeServer.cs"),
            str(ROOT_DIR / "src/StartupMonitor.UI/Services/PipeClient.cs"),
            str(ROOT_DIR / "src/StartupMonitor.Shared/Models/PipeMessage.cs"),
            str(ROOT_DIR / "src/StartupMonitor.Shared/Models/JsonDataStore.cs"),
        ],
        "prompt": (
            "Ты — эксперт по межпроцессному взаимодействию (IPC), Named Pipes и безопасности Windows ACL.\n"
            "ПРОВЕДИ ПОЛНЫЙ АУДИТ СВЯЗИ МЕЖДУ СЛУЖБОЙ SYSTEM И ПОЛЬЗОВАТЕЛЬСКИМ ИНТЕРФЕЙСОМ (UI):\n"
            "1. Безопасность PipeServer:\n"
            "   - Настроен ли PipeSecurity? Может ли непривилегированный вредоносный процесс подключиться к PipeServer службы и отправлять команды на удаление файлов/записей реестра?\n"
            "   - Валидация входных сообщений: защита от отказа в обслуживании (DoS) через отправку гигантских JSON-пакетов или битых строк.\n"
            "2. Надежность PipeClient:\n"
            "   - Что происходит с UI, если служба перезапускается, падает или еще не запущена? Зависает ли интерфейс?\n"
            "   - Авто-переподключение (reconnection) и восстановление состояния.\n"
            "3. Недоделанные или фантомные типы сообщений в PipeMessage.\n"
            "4. Итоговая оценка безопасности и устойчивости."
        ),
    },
    {
        "id": "agent6_security_heuristics_ai",
        "title": "Agent 6: Security, Authenticode, Kaspersky, AI & Cloud APIs Audit",
        "output_file": str(AUDIT_DIR / "AGENT_6_SECURITY_HEURISTICS_AI.md"),
        "files": [
            str(ROOT_DIR / "src/StartupMonitor.Shared/Helpers/AuthenticodeVerifier.cs"),
            str(ROOT_DIR / "src/StartupMonitor.Shared/Helpers/CliHeuristicAnalyzer.cs"),
            str(ROOT_DIR / "src/StartupMonitor.Service/Services/KasperskyScanService.cs"),
            str(ROOT_DIR / "src/StartupMonitor.Service/Services/AiRatingService.cs"),
            str(ROOT_DIR / "src/StartupMonitor.Service/Services/BlacklistManager.cs"),
            str(ROOT_DIR / "src/StartupMonitor.Service/Services/AutoGuardService.cs"),
        ],
        "prompt": (
            "Ты — эксперт по кибербезопасности, анализу вредоносного ПО и интеграциям с Threat Intelligence.\n"
            "ПРОВЕДИ АУДИТ ПОДСИСТЕМ БЕЗОПАСНОСТИ НА ПРЕДМЕТ ПУБЛИКАЦИИ В OPEN-SOURCE / ДЛЯ ПОЛЬЗОВАТЕЛЕЙ:\n"
            "1. Ошибки и уязвимости:\n"
            "   - Корректность AuthenticodeVerifier: проверяется ли отзыв сертификатов, валидность цепочки, или только факт наличия подписи?\n"
            "   - Ложные срабатывания CliHeuristicAnalyzer: не заблокирует ли AutoGuard легитимный софт пользователя (PowerShell скрипты сисадминов, установщики Steam/Discord)?\n"
            "   - Облачные сервисы (Kaspersky OpenTIP, Groq AI): корректность работы без интернета, без ключей API, таймауты, лимиты запросов.\n"
            "2. Утечка конфиденциальности: передаются ли приватные данные пользователя на внешние API?\n"
            "3. Недоделанные эвристики и мертвые правила.\n"
            "4. Готовность для рядовых пользователей и рекомендации."
        ),
    },
    {
        "id": "agent7_update_and_installer",
        "title": "Agent 7: Update System, Installer & Lifecycle Production Readiness Audit",
        "output_file": str(AUDIT_DIR / "AGENT_7_UPDATE_AND_INSTALLER.md"),
        "files": [
            str(ROOT_DIR / "src/StartupMonitor.Service/Services/Update/GitHubUpdateService.cs"),
            str(ROOT_DIR / "src/StartupMonitor.UI/Views/Dialogs/UpdateAvailableWindow.xaml.cs"),
            str(ROOT_DIR / "installers/StartupMonitor.iss"),
            str(ROOT_DIR / "scripts/publish.ps1"),
            str(ROOT_DIR / "scripts/build-installer.ps1"),
        ],
        "prompt": (
            "Ты — ведущий инженер релизной инфраструктуры, безопасности обновлений и Windows installers.\n"
            "ПРОВЕДИ ГЛУБОКИЙ АУДИТ МЕХАНИЗМА САМООБНОВЛЕНИЯ И ИНСТАЛЛЯТОРА:\n"
            "1. Безопасность GitHubUpdateService:\n"
            "   - Проверка хэша SHA-256 скачанного обновления: невозможна ли подмена файла или выполнение произвольного бинарника?\n"
            "   - Валидация URL репозитория: может ли злоумышленник подсунуть фишинговый репозиторий через настройки?\n"
            "   - Обработка сбоев: что если интернет оборвался посреди загрузки файла?\n"
            "2. Инсталлятор Inno Setup (StartupMonitor.iss):\n"
            "   - Корректность регистрации и остановки службы StartupMonitor при установке/обновлении/удалении.\n"
            "   - Права доступа к папке установки в Program Files и исключениям антивируса.\n"
            "   - Чистота деинсталляции: удаляются ли все временные файлы, правила брандмауэра и службы?\n"
            "3. Недоделки и шероховатости.\n"
            "4. Готовность к релизу."
        ),
    },
    {
        "id": "agent8_ui_architecture_mvvm",
        "title": "Agent 8: UI Architecture, MVVM, Performance & UX Glitches Audit",
        "output_file": str(AUDIT_DIR / "AGENT_8_UI_ARCHITECTURE_MVVM.md"),
        "files": [
            str(ROOT_DIR / "src/StartupMonitor.UI/ViewModels/MainViewModel.cs"),
            str(ROOT_DIR / "src/StartupMonitor.UI/ViewModels/TaskManagerViewModel.cs"),
            str(ROOT_DIR / "src/StartupMonitor.UI/Views/MainWindow.xaml.cs"),
            str(ROOT_DIR / "src/StartupMonitor.UI/Views/MainWindow.xaml"),
            str(ROOT_DIR / "src/StartupMonitor.UI/App.xaml.cs"),
        ],
        "prompt": (
            "Ты — элитный WPF / XAML / .NET 8 архитектор клиентских приложений высокой отзывчивости.\n"
            "ПРОВЕДИ ПОЛНЫЙ АУДИТ ПОЛЬЗОВАТЕЛЬСКОГО ИНТЕРФЕЙСА (UI) НА ГОТОВНОСТЬ К ПУБЛИЧНОМУ ВЫПУСКУ:\n"
            "1. Слабые места и зависания (Freezes):\n"
            "   - Выполняются ли тяжелые дисковые/IPC операции на потоке UI (UI Thread / Dispatcher)?\n"
            "   - Утечки памяти (Memory Leaks) при длительной работе: отписка от событий, связывание таймеров, накопление ObservableCollection при обновлении TaskManager.\n"
            "   - Обработка падений: перехватываются ли все DispatcherUnhandledException и TaskScheduler.UnobservedTaskException?\n"
            "2. Поведение при первом старте: что увидит пользователь, если служба еще не запустилась? Будет ли красивое сообщение или белый/пустой экран?\n"
            "3. Недоделанные элементы UI: неработающие кнопки, битые привязки (Binding errors), странные артефакты разметки.\n"
            "4. Список конкретных исправлений для безупречного UX."
        ),
    },
    {
        "id": "agent9_dialogs_remote_logging",
        "title": "Agent 9: UI Dialogs, Remote Alerts (Telegram/Discord) & Logging Audit",
        "output_file": str(AUDIT_DIR / "AGENT_9_DIALOGS_REMOTE_LOGGING.md"),
        "files": [
            str(ROOT_DIR / "src/StartupMonitor.UI/Views/Dialogs/PortDefenderConfigWindow.xaml.cs"),
            str(ROOT_DIR / "src/StartupMonitor.UI/Views/Dialogs/EntryDetailWindow.xaml.cs"),
            str(ROOT_DIR / "src/StartupMonitor.UI/Views/Dialogs/SystemInfoWindow.xaml.cs"),
            str(ROOT_DIR / "src/StartupMonitor.Service/Services/NotificationService.cs"),
            str(ROOT_DIR / "src/StartupMonitor.Service/Services/Remote/RemoteNotificationService.cs"),
            str(ROOT_DIR / "src/StartupMonitor.UI/Services/LogReaderService.cs"),
        ],
        "prompt": (
            "Ты — ведущий инженер по интеграциям, уведомлениям и системной телеметрии.\n"
            "ПРОВЕДИ АУДИТ ДИАЛОГОВЫХ ОКОН, УВЕДОМЛЕНИЙ И ЖУРНАЛИРОВАНИЯ:\n"
            "1. Слабые места RemoteNotificationService (Telegram / Discord):\n"
            "   - Валидация токенов и chat_id: падает ли сервис при неверном токене?\n"
            "   - Rate Limiting: блокировка отправки при шквале событий автозапуска (например, установка софта создает 20 записей — спамит ли бот 20 сообщений подряд?).\n"
            "   - Безопасность хранения токенов в конфигурационном файле.\n"
            "2. Логирование: не забивается ли диск пользователя гигабайтами логов при долгой работе (ротация логов)?\n"
            "3. Недоделки в диалоговых окнах: валидация ввода IP/портов в PortDefenderConfigWindow.\n"
            "4. Готовность для публичного релиза и рекомендации."
        ),
    },
    {
        "id": "agent10_release_suitability",
        "title": "Agent 10: Production Release Readiness, Dead Code & Public Suitability Audit",
        "output_file": str(AUDIT_DIR / "AGENT_10_RELEASE_SUITABILITY.md"),
        "files": [
            str(ROOT_DIR / "src/StartupMonitor.Service/Program.cs"),
            str(ROOT_DIR / "src/StartupMonitor.Shared/ServiceManager.cs"),
            str(ROOT_DIR / "src/StartupMonitor.UI/Services/AutostartService.cs"),
            str(ROOT_DIR / "src/StartupMonitor.Shared/Helpers/SystemProtectionHelper.cs"),
            str(ROOT_DIR / "README.md"),
            str(ROOT_DIR / "docs/architecture.md"),
        ],
        "prompt": (
            "Ты — Главный Архитектор Релиза (Release Lead) и QA-директор коммерческого ПО для Windows.\n"
            "ПРОВЕДИ ИТОГОВЫЙ АУДИТ ВСЕГО ПРОЕКТА: МОЖНО ЛИ ЕГО ВЫКЛАДЫВАТЬ ДЛЯ ВСЕХ ПОЛЬЗОВАТЕЛЕЙ?\n"
            "1. Чувствительные данные и следы разработки:\n"
            "   - Есть ли в коде захардкоженные пути разработчика (например d:\\Nas_sync...)?\n"
            "   - Есть ли тестовые токены, пароли, секретные ключи?\n"
            "2. Мертвый код и заброшенные элементы: что в проекте лишнее, устаревшее или сбивающее с толку?\n"
            "3. Поведение программы под правами обычного пользователя (Non-Admin):\n"
            "   - Что произойдет, если пользователь запустит UI без прав администратора? Будет ли понятный запрос UAC или краш программы?\n"
            "   - Корректно ли работает ServiceManager при попытке управления службой без повышенных привилегий?\n"
            "4. Соответствие документации (README.md) реальным возможностям приложения.\n"
            "5. ГЛАВНЫЙ ВЕРДИКТ: 'ГОТОВ К РЕЛИЗУ', 'ГОТОВ С ОГОВОРКАМИ' или 'НЕ ГОТОВ'. Чёткий чеклист блокирующих проблем."
        ),
    },
]

def run_agent(task):
    agent_id = task["id"]
    title = task["title"]
    out_file = task["output_file"]
    files = task["files"]
    prompt = task["prompt"]

    print(f"[START] {title}...")
    start_time = time.time()
    try:
        result = run_subagent_task(
            prompt=prompt,
            files=files,
            work_dir=str(ROOT_DIR),
            variant="medium",
            auto_approve=True,
            timeout_seconds=450
        )
        duration = time.time() - start_time
        
        output_text = result.stdout if result.stdout else result.stderr
        content = f"# {title}\n\n- Время анализа: {duration:.1f} сек\n- Статус: {'Успех' if result.success else 'Сбой'}\n\n---\n\n{output_text}\n"
        with open(out_file, "w", encoding="utf-8") as f:
            f.write(content)
        
        status_tag = "DONE" if result.success else "WARN"
        print(f"[{status_tag}] {title} ({duration:.1f}s) -> {out_file}")
        return {"id": agent_id, "title": title, "success": result.success, "duration": duration, "file": out_file}
    except Exception as ex:
        duration = time.time() - start_time
        err_msg = f"# {title}\n\n- Ошибка: {str(ex)}\n"
        with open(out_file, "w", encoding="utf-8") as f:
            f.write(err_msg)
        print(f"[ERROR] {title} ({duration:.1f}s): {ex}")
        return {"id": agent_id, "title": title, "success": False, "duration": duration, "error": str(ex)}

def main():
    print("=" * 70)
    print("ЗАПУСК РОЯ OPENCODE: 10 СУБАГЕНТОВ АУДИТА ГОТОВНОСТИ К РЕЛИЗУ")
    print("Модель: opencode/muse-spark-1.3-contributor-free (1M токенов, 0$ cost)")
    print("Конкурентность: 5 параллельных воркеров (без конфликтов и 429 Rate Limit)")
    print("=" * 70)

    start_all = time.time()
    results = []

    # 5 параллельных потоков - оптимальный баланс без конфликтов API и ресурсов
    with ThreadPoolExecutor(max_workers=5) as executor:
        futures = {executor.submit(run_agent, task): task for task in SWARM_AGENTS}
        for future in as_completed(futures):
            res = future.result()
            results.append(res)

    total_time = time.time() - start_all
    print("\n" + "=" * 70)
    print(f"ВСЕ 10 СУБАГЕНТОВ РОЯ ЗАВЕРШИЛИ РАБОТУ ЗА {total_time:.1f} сек!")
    print("=" * 70)
    for r in results:
        status = "УСПЕХ" if r.get("success") else "ПРЕДУПРЕЖДЕНИЕ"
        print(f"- [{status}] {r['title']} ({r['duration']:.1f}s)")

if __name__ == "__main__":
    main()
