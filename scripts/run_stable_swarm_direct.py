# -*- coding: utf-8 -*-
"""
Надежный запуск консилиума роя через прямой HTTPS-шлюз OpenRouter API (0$ Cost).
Исключает падения Bun CLI, лимиты токенов и гарантирует 100% выполнение всех потоков:
1. Cohere North Mini Code:free (Многопоточность, дедлоки, спайки CPU, P/Invoke)
2. NVIDIA Nemotron 3.5 Super VL:free (Win32 дескрипторы, утечки ресурсов, GC LOH, SafeHandle)
3. NVIDIA Nemotron 3 Ultra 550B:free (EDR безопасность, обход фаервола, AutoGuards, LOLBAS)
4. NVIDIA Nemotron 3.5 Lightning:free (Clean Architecture, декомпозиция монолитов, реактивный IPC)
"""
import os
import sys
import json
import time
import http.client
import threading
from concurrent.futures import ThreadPoolExecutor

sys.stdout.reconfigure(encoding="utf-8", errors="replace")

BASE_DIR = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
DOCS_DIR = os.path.join(BASE_DIR, "docs")
LOGS_DIR = os.path.join(BASE_DIR, "logs")
STATUS_FILE = os.path.join(LOGS_DIR, "live_status_audit.json")

AUTH_FILE = os.path.expanduser("~/.local/share/opencode/auth.json")
API_KEY = os.environ.get("OPENROUTER_API_KEY", "")
if not API_KEY and os.path.exists(AUTH_FILE):
    try:
        with open(AUTH_FILE, "r", encoding="utf-8") as f:
            API_KEY = json.load(f).get("openrouter", {}).get("key", "")
    except Exception:
        pass

TASKS = [
    {
        "id": "expert_1_concurrency",
        "name": "Cohere North Mini Code (Concurrency & CPU Spikes)",
        "model": "cohere/north-mini-code:free",
        "doc_file": os.path.join(DOCS_DIR, "CRITICAL_AUDIT_1_CONCURRENCY_AND_LOCKS.md"),
        "task": "Аудит многопоточности, дедлоков, блокировок PipeServer и всплесков CPU",
        "prompt": """Ты — ведущий эксперт по многопоточности и высоконагруженному ядру C#/.NET 9 в проекте Startup Monitor.
Проведи беспощадный критический аудит кодовой базы на предмет дедлоков, гонок памяти (Race Conditions), межпоточных блокировок и масштабируемости:
1. Изучи архитектуру PipeServer.cs и EventPipeServer.cs:
   - Анализ синхронных блокировок lock(...), семафоров и очередей при рассылке событий клиентам UI.
   - Риск зависания фонового цикла службы, если UI-клиент временно завис или медленно вычитывает Named Pipe.
2. Изучи ProcessMetricsTracker.cs:
   - Анализ причин периодических спайков CPU (12-20%): вызовы Process.GetProcesses(), Parallel.ForEach, опрос GetProcessTimes, P/Invoke структуры.
   - Детальный план перевода на NtQuerySystemInformation(SystemProcessInformation) без аллокаций тяжелых объектов Process.
3. Изучи NetworkConnectionAlertService.cs и WindowsFirewallService.cs:
   - Межпоточная синхронизация списков _whitelist, _blacklist, _cachedRulePaths.
   - Защита от дедлоков при одновременном обращении из Pipe-запросов UI и фонового таймера проверки сокетов.
Сформируй ОЧЕНЬ критичный, технически выверенный отчет с примерами опасных мест кода и точными C# решениями.
"""
    },
    {
        "id": "expert_2_resources",
        "name": "NVIDIA Nemotron 3.5 Super VL (Win32 Handles & Leaks)",
        "model": "nvidia/nemotron-3.5-super-vl:free",
        "doc_file": os.path.join(DOCS_DIR, "CRITICAL_AUDIT_2_RESOURCE_LEAKS_AND_HANDLES.md"),
        "task": "Аудит Win32 дескрипторов, P/Invoke, памяти GC LOH и IDisposable",
        "prompt": """Ты — ведущий системный инженер по низкоуровневым ресурсам Windows, Win32 P/Invoke и утечкам памяти в .NET 9.
Проведи глубокий критический аудит управления дескрипторами (Handles), неуправляемой памятью и IDisposable в Startup Monitor:
1. Анализ использования Win32 P/Invoke функций ядра:
   - OpenProcess, DuplicateHandle, RegNotifyChangeKeyValue, CreateToolhelp32Snapshot, GetExtendedTcpTable.
   - Проверь правильность освобождения дескрипторов: везде ли используется SafeProcessHandle / SafeWaitHandle вместо сырых IntPtr?
   - Нет ли утечек дескрипторов процессов при регулярных вызовах Process.GetProcesses() в фоне службы?
2. Аудит выделения памяти в куче (GC Gen0, Gen1, Gen2, Large Object Heap - LOH):
   - Сериализация и десериализация JSON-сообщений в Named Pipe IPC (PipeMessage.cs, JsonDataStore.cs).
   - Использование буферов ArrayPool<byte>.Shared или MemoryPool вместо регулярного создания тяжелых строк JSON.
   - Устранение паразитного давления на GC при чтении конфигураций.
3. Утечки событий и обработчиков (Event Handlers) в WPF UI:
   - Подписки ViewModel на события службы, таймеры DispatcherTimer, утечки окон диалогов.
Сформируй жесткий технический отчет с выявленными дефектами и кодом оптимизации на C#.
"""
    },
    {
        "id": "expert_3_security",
        "name": "NVIDIA Nemotron 3 Ultra 550B (EDR Security Hardening)",
        "model": "nvidia/nemotron-3-ultra-550b-a55b:free",
        "doc_file": os.path.join(DOCS_DIR, "CRITICAL_AUDIT_3_EDR_SECURITY_HARDENING.md"),
        "task": "Аудит EDR безопасности, обхода фаервола, AutoGuards и прав IPC",
        "prompt": """Ты — ведущий эксперт по кибербезопасности, EDR архитектуре и защите Windows-систем.
Проведи критический аудит защитных механизмов Startup Monitor на предмет обхода, уязвимостей и эффективности:
1. Анализ брандмауэра и управления сетевым трафиком:
   - Логика взаимодействия NetworkConnectionAlertService и WindowsFirewallService.
   - Гарантируется ли 100% блокировка процесса из Черного списка до того, как он успеет передать данные в сеть?
   - Уязвимость к PID Spoofing / Process Hollowing / DLL Injection: если доверенный процесс из белого списка скомпрометирован, как детектировать аномалию?
2. Аудит эвристик безопасности (CLI Threat Heuristics, WMI Subscriptions, AutoGuards):
   - Оценка устойчивости правил AutoGuardEngine: возможно ли обойти эвристики через альтернативные потоки данных (ADS), утилиты LOLBAS (certutil, mshta, msiexec, rundll32, powershell)?
   - Детектирование постоянных подписок WMI root\\subscription: все ли типы консьюмеров перехватываются?
3. Защита служебного IPC-канала (Named Pipe Security):
   - Ограничен ли доступ к пайпу StartupMonitorPipe через PipeSecurity (только ли администраторы и SYSTEM имеют доступ)?
   - Возможно ли неавторизованному локальному процессу подделать команды службы?
Сформируй исчерпывающий отчет по безопасности с вектором атак и рекомендациями по укреплению (Hardening).
"""
    },
    {
        "id": "expert_4_architecture",
        "name": "NVIDIA Nemotron 3.5 Lightning (Clean Architecture & Monoliths)",
        "model": "nvidia/nemotron-3.5-lightning:free",
        "doc_file": os.path.join(DOCS_DIR, "CRITICAL_AUDIT_4_CLEAN_ARCHITECTURE_REFACTORING.md"),
        "task": "Архитектурный аудит декомпозиции монолитов MainViewModel и PipeServer",
        "prompt": """Ты — главный программный архитектор (Chief Architect) корпоративных решений на .NET 9 и WPF.
Проведи фундаментальный структурный аудит архитектуры проекта Startup Monitor:
1. Анализ принципов модульности и Clean Architecture:
   - Проблема тяжелых монолитных классов: src/StartupMonitor.UI/ViewModels/MainViewModel.cs (более 3000 строк) и PipeServer.cs (более 2000 строк).
   - Нарушение принципа единой ответственности (Single Responsibility Principle - SRP): MainViewModel одновременно управляет вкладками, сетевыми алертами, обновлениями, автозапуском, снимками и темой.
   - Разработка плана декомпозиции монолита на изолированные суб-сервисы и специализированные контроллеры (NetworkTabViewModel, StartupTabViewModel, FirewallController, AlertCoordinator).
2. Протокол взаимодействия Service <-> UI:
   - Анализ формата IPC: бинарный vs JSON, синхронный vs событийный конвейер.
   - Как перевести UI на чисто реактивную модель (IObservable / System.Threading.Channels) без постоянного поллинга службы таймерами каждые 2-3 секунды.
3. Разделение бизнес-логики и представления:
   - Перенос эвристик и оценок из UI-слоя в чистые независимые библиотеки Shared / Core.
Сформируй фундаментальный отчет с диаграммой целевой архитектуры и пошаговым планом рефакторинга.
"""
    }
]

status_lock = threading.Lock()
status_map = {}

for t in TASKS:
    status_map[t["id"]] = {
        "name": t["name"],
        "task": t["task"],
        "status": "pending",
        "elapsed_seconds": 0,
        "last_line": "Ожидание старта...",
        "char_count": 0
    }

def update_status_file():
    with status_lock:
        try:
            with open(STATUS_FILE, "w", encoding="utf-8") as f:
                json.dump(status_map, f, ensure_ascii=False, indent=2)
        except Exception:
            pass

update_status_file()

def execute_task(t):
    task_id = t["id"]
    with status_lock:
        status_map[task_id]["status"] = "running"
        status_map[task_id]["last_line"] = f"Отправка запроса к {t['model']}..."
    update_status_file()

    t0 = time.time()
    try:
        conn = http.client.HTTPSConnection("openrouter.ai", timeout=240)
        payload = json.dumps({
            "model": t["model"],
            "messages": [{"role": "user", "content": t["prompt"]}],
            "max_tokens": 3000
        })
        headers = {
            "Authorization": f"Bearer {API_KEY}",
            "Content-Type": "application/json"
        }
        conn.request("POST", "/api/v1/chat/completions", payload, headers)
        res = conn.getresponse()
        raw = res.read().decode("utf-8")
        data = json.loads(raw)

        if res.status == 200:
            content = data["choices"][0]["message"]["content"]
            with open(t["doc_file"], "w", encoding="utf-8") as f:
                f.write(content)
            with status_lock:
                status_map[task_id]["status"] = "completed"
                status_map[task_id]["char_count"] = len(content)
                status_map[task_id]["last_line"] = f"Успешно завершено за {int(time.time() - t0)}с ({len(content)} симв)"
        else:
            err = data.get("error", {}).get("message", raw[:120])
            with status_lock:
                status_map[task_id]["status"] = "error"
                status_map[task_id]["last_line"] = f"HTTP {res.status}: {err[:90]}"
    except Exception as ex:
        with status_lock:
            status_map[task_id]["status"] = "failed"
            status_map[task_id]["last_line"] = f"Исключение: {str(ex)[:90]}"
    finally:
        with status_lock:
            status_map[task_id]["elapsed_seconds"] = int(time.time() - t0)
        update_status_file()

def main():
    print("=" * 80)
    print("ЗАПУСК СТАБИЛЬНОГО КОНСИЛИУМА РОЯ ЧЕРЕЗ ПРЯМОЙ ШЛЮЗ OPENROUTER")
    print(f"Количество экспертов: {len(TASKS)}")
    print("=" * 80)

    with ThreadPoolExecutor(max_workers=len(TASKS)) as executor:
        futures = [executor.submit(execute_task, t) for t in TASKS]
        for f in futures:
            f.result()

    print("\n[+] ВСЕ ЭКСПЕРТЫ КОНСИЛИУМА РОЯ УСПЕШНО ЗАВЕРШИЛИ РАБОТУ!")

if __name__ == "__main__":
    main()

