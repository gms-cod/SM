# -*- coding: utf-8 -*-
"""
Глобальный архитектурный консилиум роя субагентов для выявления критических недочетов в структуре Startup Monitor.
Запуск 5 независимых экспертов:
1. DeepSeek-Flash (Гонки, дедлоки, IPC NamedPipe, ProcessMetricsTracker, блокировки)
2. Nemotron-Zen (Win32 Handles, P/Invoke, SafeHandle, память, GC LOH, Disposable)
3. Nemotron-Super (EDR безопасность, обход фаервола, подделка PID/токенов, WMI эвристики)
4. Gemini 4 Argon (Архитектура ядра, Clean Architecture, разгрузка монолитов MainViewModel/PipeServer)
5. GLM 5.3 Flash (Отказоустойчивость, Zero-Crash, Session 0 изоляция, инсталлятор, автозапуск)
"""
import os
import sys
import json
import time
import shutil
import tempfile
import threading
import subprocess
import http.client
from concurrent.futures import ThreadPoolExecutor

sys.stdout.reconfigure(encoding="utf-8", errors="replace")

BASE_DIR = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
LOGS_DIR = os.path.join(BASE_DIR, "logs")
DOCS_DIR = os.path.join(BASE_DIR, "docs")
STATUS_FILE = os.path.join(LOGS_DIR, "live_status_audit.json")

AUTH_FILE = os.path.expanduser(r"~/.local/share/opencode/auth.json")
OPENROUTER_KEY = os.environ.get("OPENROUTER_API_KEY", "")
if not OPENROUTER_KEY and os.path.exists(AUTH_FILE):
    try:
        with open(AUTH_FILE, "r", encoding="utf-8") as f:
            ad = json.load(f)
            OPENROUTER_KEY = ad.get("openrouter", {}).get("key") or ad.get("openrouter", {}).get("apiKey", "")
    except Exception:
        pass

PROMPT_AGENT_1 = """Ты — ведущий эксперт по многопоточности и высоконагруженному ядру C#/.NET 9 в проекте Startup Monitor.
Проведи беспощадный критический аудит кодовой базы на предмет дедлоков, гонок памяти (Race Conditions), межпоточных блокировок и масштабируемости:
1. Изучи `src/StartupMonitor.Service/Pipe/PipeServer.cs` и `EventPipeServer.cs`:
   - Выяви все синхронные блокировки `lock(...)`, семафоры и таймауты при рассылке событий клиентам UI.
   - Есть ли риск зависания фонового цикла службы, если UI-клиент временно завис или медленно читает Named Pipe?
2. Изучи `src/StartupMonitor.Service/Services/ProcessMetricsTracker.cs`:
   - Анализ периодических спайков CPU: вызовы `Process.GetProcesses()`, `Parallel.ForEach`, опрос `GetProcessTimes`, P/Invoke структуры.
   - Как перевести опрос на `NtQuerySystemInformation(SystemProcessInformation)` без единой аллокации объектов `Process` и ликвидировать спайки нагрузки 12-20% CPU.
3. Изучи `NetworkConnectionAlertService.cs` и `WindowsFirewallService.cs`:
   - Межпоточная синхронизация списков `_whitelist`, `_blacklist`, `_cachedRulePaths`.
   - Защита от дедлоков при одновременном обращении из Pipe-запросов UI и фонового таймера проверки сокетов.
Сформируй ОЧЕНЬ критичный, аргументированный отчет с примерами опасных мест кода и точными рецептами исправления на C#.
Сохрани подробный отчет в docs/CRITICAL_AUDIT_1_CONCURRENCY_AND_LOCKS.md.
"""

PROMPT_AGENT_2 = """Ты — ведущий системный инженер по низкоуровневым ресурсам Windows, Win32 P/Invoke и утечкам памяти в .NET 9.
Проведи глубокий критический аудит управления дескрипторами (Handles), неуправляемой памятью и IDisposable в Startup Monitor:
1. Проанализируй использование Win32 P/Invoke функций ядра:
   - `OpenProcess`, `DuplicateHandle`, `RegNotifyChangeKeyValue`, `CreateToolhelp32Snapshot`, `GetExtendedTcpTable`.
   - Проверь правильность освобождения дескрипторов: везде ли используется `SafeProcessHandle` / `SafeWaitHandle` вместо `IntPtr`?
   - Нет ли утечек дескрипторов процессов при вызовах `Process.GetProcesses()` или `Process.GetProcessById()` в фоне службы?
2. Аудит выделения памяти в куче (GC Gen0, Gen1, Gen2, Large Object Heap - LOH):
   - Сериализация и десериализация JSON-сообщений в Named Pipe IPC (`PipeMessage.cs`, `JsonDataStore.cs`).
   - Использование буферов `ArrayPool<byte>.Shared` или `MemoryPool` вместо регулярного создания строк JSON.
   - Глубокое клонирование конфигураций `AppConfig` в `JsonDataStore.Load()` — как устранить паразитное давление на GC.
3. Утечки событий и обработчиков (Event Handlers) в WPF UI:
   - Подписки ViewModel на события службы, таймеры DispatcherTimer, утечки окон диалогов.
Сформируй жесткий технический отчет с выявленными дефектами и кодом оптимизации на C#.
Сохрани подробный отчет в docs/CRITICAL_AUDIT_2_RESOURCE_LEAKS_AND_HANDLES.md.
"""

PROMPT_AGENT_3 = """Ты — ведущий эксперт по кибербезопасности, EDR архитектуре и защите Windows-систем.
Проведи критический аудит защитных механизмов Startup Monitor на предмет обхода, уязвимостей и эффективности:
1. Анализ брандмауэра и управления сетевым трафиком:
   - Логика взаимодействия `NetworkConnectionAlertService` и `WindowsFirewallService`.
   - Гарантируется ли 100% блокировка процесса из Черного списка до того, как он успеет передать данные в сеть?
   - Уязвимость к PID Spoofing / Process Hollowing / DLL Injection: если доверенный процесс из белого списка скомпрометирован или в него внедрен шеллкод, как детектировать аномалию?
2. Аудит эвристик безопасности (CLI Threat Heuristics, WMI Subscriptions, AutoGuards):
   - Оценка устойчивости правил `AutoGuardEngine`: возможно ли обойти эвристики через альтернативные потоки данных (ADS), утилиты LOLBAS (certutil, mshta, msiexec, rundll32, powershell)?
   - Детектирование постоянных подписок WMI `root\\subscription`: все ли типы консьюмеров перехватываются?
3. Защита служебного IPC-канала (Named Pipe Security):
   - Ограничен ли доступ к пайпу `StartupMonitorPipe` через `PipeSecurity` (только ли администраторы и SYSTEM имеют доступ)?
   - Возможно ли неавторизованному локальному процессу подделать команды службы (например, добавить свой бинарник в белый список)?
Сформируй исчерпывающий отчет по безопасности с вектором атак и рекомендациями по укреплению (Hardening).
Сохрани подробный отчет в docs/CRITICAL_AUDIT_3_EDR_SECURITY_HARDENING.md.
"""

PROMPT_AGENT_4 = """Ты — главный программный архитектор (Chief Architect) корпоративных решений на .NET 9 и WPF.
Проведи фундаментальный структурный аудит архитектуры проекта Startup Monitor:
1. Анализ принципов модульности и Clean Architecture:
   - Проблема тяжелых монолитных классов: `src/StartupMonitor.UI/ViewModels/MainViewModel.cs` (более 3000 строк) и `PipeServer.cs` (более 2000 строк).
   - Нарушение принципа единой ответственности (Single Responsibility Principle - SRP): `MainViewModel` одновременно управляет вкладками, сетевыми алертами, обновлениями, автозапуском, снимками и темой.
   - Разработка плана декомпозиции монолита на изолированные суб-сервисы и специализированные контроллеры (NetworkTabViewModel, StartupTabViewModel, FirewallController, AlertCoordinator).
2. Протокол взаимодействия Service <-> UI:
   - Анализ формата IPC: бинарный vs JSON, синхронный vs событийный конвейер.
   - Как перевести UI на чисто реактивную модель (IObservable / System.Threading.Channels) без постоянного поллинга службы таймерами каждые 2-3 секунды.
3. Разделение бизнес-логики и представления:
   - Перенос эвристик и оценок из UI-слоя в чистые независимые библиотеки Shared / Core.
Сформируй фундаментальный отчет с диаграммой целевой архитектуры и пошаговым планом рефакторинга.
Сохрани подробный отчет в docs/CRITICAL_AUDIT_4_CLEAN_ARCHITECTURE_REFACTORING.md.
"""

PROMPT_AGENT_5 = """Ты — ведущий инженер по отказоустойчивости (Reliability & DevOps), инсталляции и жизненному циклу Windows приложений.
Проведи критический аудит стабильности запуска, восстановления после сбоев и развертывания Startup Monitor:
1. Анализ изоляции сессий Windows (Session 0 vs Session 1+):
   - Как служба (`NT AUTHORITY\\SYSTEM`) взаимодействует с интерактивным рабочим столом пользователя.
   - Надежность запуска пользовательского интерфейса через `UserSessionLauncher.Launch(...)` при тихом обновлении и сетевых алертах.
   - Исключение сбоев `CreateProcessAsUser` при заблокированном экране, смене пользователей (Fast User Switching) и RDP-сессиях.
2. Жизненный цикл и механизм автовосстановления (Watchdog & Zero-Crash):
   - Что происходит, если фоновая служба аварийно завершилась (необработанное исключение P/Invoke, OOM, AccessViolationException)?
   - Настроены ли действия службы при сбое в SCM (Service Control Manager: First failure -> Restart, Second -> Restart)?
   - Корректная обработка сигнала завершения работы Windows (`ServiceBase.OnShutdown`, `WM_QUERYENDSESSION`) с сохранением всех кэшей.
3. Инсталлятор Inno Setup (`StartupMonitor.iss`) и автообновление:
   - Чистота деинсталляции, исключение блокировок файлов при обновлении поверх работающей программы, корректность автозапуска в `HKCU\\Run`.
Сформируй подробный отчет по повышению надежности до промышленного стандарта 99.99% (Fault-Tolerant Standard).
Сохрани подробный отчет в docs/CRITICAL_AUDIT_5_FAULT_TOLERANCE_AND_LIFECYCLE.md.
"""

AGENTS = [
    {
        "id": "agent_1_concurrency",
        "name": "DeepSeek V4.1 Flash (Concurrency & Deadlocks)",
        "model": "opencode/deepseek-v4.1-flash-free",
        "type": "opencode",
        "doc_file": os.path.join(DOCS_DIR, "CRITICAL_AUDIT_1_CONCURRENCY_AND_LOCKS.md"),
        "prompt": PROMPT_AGENT_1,
        "task": "Аудит многопоточности, дедлоков, IPC NamedPipe и всплесков CPU"
    },
    {
        "id": "agent_2_resources",
        "name": "Nemotron 3.5 Super VL (Win32 Handles & Leaks)",
        "model": "opencode/nemotron-3.5-super-vl-free",
        "type": "opencode",
        "doc_file": os.path.join(DOCS_DIR, "CRITICAL_AUDIT_2_RESOURCE_LEAKS_AND_HANDLES.md"),
        "prompt": PROMPT_AGENT_2,
        "task": "Аудит Win32 дескрипторов, P/Invoke, памяти GC LOH и IDisposable"
    },
    {
        "id": "agent_3_security",
        "name": "Nemotron 3.5 Super VL OpenRouter (EDR Hardening)",
        "model": "openrouter/nvidia/nemotron-3.5-super-vl:free",
        "type": "opencode",
        "doc_file": os.path.join(DOCS_DIR, "CRITICAL_AUDIT_3_EDR_SECURITY_HARDENING.md"),
        "prompt": PROMPT_AGENT_3,
        "task": "Аудит EDR безопасности, обхода фаервола, AutoGuards и прав IPC"
    },
    {
        "id": "agent_4_architecture",
        "name": "Google Gemini 4 Argon (Clean Architecture & Monoliths)",
        "model": "google/gemini-4-argon",
        "type": "openrouter_direct",
        "doc_file": os.path.join(DOCS_DIR, "CRITICAL_AUDIT_4_CLEAN_ARCHITECTURE_REFACTORING.md"),
        "prompt": PROMPT_AGENT_4,
        "task": "Архитектурный аудит декомпозиции монолитов MainViewModel и PipeServer"
    },
    {
        "id": "agent_5_reliability",
        "name": "GLM 5.3 Flash (Zero-Crash & Lifecycle)",
        "model": "opencode/glm-5.3-flash-free",
        "type": "opencode",
        "doc_file": os.path.join(DOCS_DIR, "CRITICAL_AUDIT_5_FAULT_TOLERANCE_AND_LIFECYCLE.md"),
        "prompt": PROMPT_AGENT_5,
        "task": "Аудит отказоустойчивости, Session 0 изоляции, автовосстановления и инсталлятора"
    }
]

status_lock = threading.Lock()
status_map = {}

for ag in AGENTS:
    status_map[ag["id"]] = {
        "name": ag["name"],
        "task": ag["task"],
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

def run_openrouter_direct(ag):
    agent_id = ag["id"]
    with status_lock:
        status_map[agent_id]["status"] = "running"
        status_map[agent_id]["last_line"] = "Инициализация OpenRouter HTTPS сессии..."
    update_status_file()

    start_time = time.time()
    try:
        conn = http.client.HTTPSConnection("openrouter.ai", timeout=180)
        payload = json.dumps({
            "model": ag["model"],
            "messages": [{"role": "user", "content": ag["prompt"]}],
            "max_tokens": 3500
        })
        headers = {
            "Authorization": f"Bearer {OPENROUTER_KEY}",
            "Content-Type": "application/json"
        }
        with status_lock:
            status_map[agent_id]["last_line"] = "Отправка запроса в Gemini 4 Argon..."
        update_status_file()

        conn.request("POST", "/api/v1/chat/completions", payload, headers)
        res = conn.getresponse()
        raw = res.read().decode("utf-8")
        data = json.loads(raw)
        
        if res.status == 200:
            content = data["choices"][0]["message"]["content"]
            with open(ag["doc_file"], "w", encoding="utf-8") as f:
                f.write(content)
            with status_lock:
                status_map[agent_id]["status"] = "completed"
                status_map[agent_id]["char_count"] = len(content)
                status_map[agent_id]["last_line"] = f"Успешно завершено за {int(time.time() - start_time)} сек"
        else:
            err_msg = data.get("error", {}).get("message", raw[:200])
            with status_lock:
                status_map[agent_id]["status"] = "error"
                status_map[agent_id]["last_line"] = f"HTTP {res.status}: {err_msg[:100]}"
    except Exception as ex:
        with status_lock:
            status_map[agent_id]["status"] = "failed"
            status_map[agent_id]["last_line"] = f"Исключение: {str(ex)[:100]}"
    finally:
        with status_lock:
            status_map[agent_id]["elapsed_seconds"] = int(time.time() - start_time)
        update_status_file()

def run_opencode_cli(ag):
    agent_id = ag["id"]
    sandbox_dir = tempfile.mkdtemp(prefix=f"swarm_audit_{agent_id}_")
    
    # Изоляция песочницы для предотвращения блокировок SQLite базы OpenCode
    auth_src = os.path.expanduser(r"~/.local/share/opencode/auth.json")
    if os.path.exists(auth_src):
        auth_dst_dir = os.path.join(sandbox_dir, ".local", "share", "opencode")
        os.makedirs(auth_dst_dir, exist_ok=True)
        try:
            shutil.copy2(auth_src, os.path.join(auth_dst_dir, "auth.json"))
        except Exception:
            pass

    env = os.environ.copy()
    env["USERPROFILE"] = sandbox_dir
    env["HOME"] = sandbox_dir
    env["OPENCODE_DIR"] = sandbox_dir
    env["APPDATA"] = os.path.join(sandbox_dir, "AppData", "Roaming")
    env["LOCALAPPDATA"] = os.path.join(sandbox_dir, "AppData", "Local")
    os.makedirs(env["APPDATA"], exist_ok=True)
    os.makedirs(env["LOCALAPPDATA"], exist_ok=True)

    with status_lock:
        status_map[agent_id]["status"] = "running"
        status_map[agent_id]["last_line"] = f"Запуск процесса opencode ({ag['model']})..."
    update_status_file()

    cmd = ["opencode", "run", "--pure", "--dir", BASE_DIR, "-m", ag["model"]]
    start_time = time.time()
    try:
        proc = subprocess.Popen(
            cmd,
            stdin=subprocess.PIPE,
            stdout=subprocess.PIPE,
            stderr=subprocess.STDOUT,
            cwd=BASE_DIR,
            env=env,
            text=True,
            encoding="utf-8",
            errors="replace"
        )
        proc.stdin.write(ag["prompt"])
        proc.stdin.close()

        output_lines = []
        for line in proc.stdout:
            clean = line.strip()
            if clean:
                with status_lock:
                    status_map[agent_id]["last_line"] = clean[:140]
            output_lines.append(line)
            with status_lock:
                status_map[agent_id]["elapsed_seconds"] = int(time.time() - start_time)
                status_map[agent_id]["char_count"] = sum(len(l) for l in output_lines)
            update_status_file()

        proc.wait(timeout=3600)
        full_text = "".join(output_lines)
        with open(ag["doc_file"], "w", encoding="utf-8") as f:
            f.write(full_text)

        with status_lock:
            status_map[agent_id]["status"] = "completed" if proc.returncode == 0 else "error"
            status_map[agent_id]["last_line"] = f"Завершено за {int(time.time() - start_time)} сек"
    except Exception as ex:
        with status_lock:
            status_map[agent_id]["status"] = "failed"
            status_map[agent_id]["last_line"] = f"Ошибка: {str(ex)[:100]}"
    finally:
        with status_lock:
            status_map[agent_id]["elapsed_seconds"] = int(time.time() - start_time)
        update_status_file()
        try:
            shutil.rmtree(sandbox_dir, ignore_errors=True)
        except Exception:
            pass

def dispatch_agent(ag):
    if ag["type"] == "openrouter_direct":
        run_openrouter_direct(ag)
    else:
        run_opencode_cli(ag)

def main():
    print("=" * 80)
    print("СТАРТ ГЛОБАЛЬНОГО АРХИТЕКТУРНОГО КОНСИЛИУМА РОЯ СУБАГЕНТОВ (5 ЭКСПЕРТОВ)")
    print(f"Рабочий каталог: {BASE_DIR}")
    print(f"Файл статуса мониторинга: {STATUS_FILE}")
    print("=" * 80)

    for ag in AGENTS:
        print(f"[*] Регистрация эксперта: [{ag['id']}] -> {ag['name']} ({ag['model']})")

    executor = ThreadPoolExecutor(max_workers=len(AGENTS))
    for ag in AGENTS:
        executor.submit(dispatch_agent, ag)

    print("\n[+] ВСЕ 5 СУБАГЕНТОВ УСПЕШНО ИНИЦИАЛИЗИРОВАНЫ И ЗАПУЩЕНЫ В ПАРАЛЛЕЛЬНЫХ ПОТОКАХ!")
    executor.shutdown(wait=True)
    print("\n[+] ВСЕ ЭКСПЕРТЫ РОЯ УСПЕШНО ЗАВЕРШИЛИ ГЛОБАЛЬНЫЙ АУДИТ.")

if __name__ == "__main__":
    main()

