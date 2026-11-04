# -*- coding: utf-8 -*-
"""
Многопоточный раннер роя субагентов для верификации всех сегодняшних изменений (v1.9.0 - v1.9.1).
Запускает 3 РАЗНЫХ субагента параллельно:
  1. DeepSeek V4.1 Flash: Ядро, безопасность IPC (LPE), клон-снимок хранилища, Zero-Crash реестра.
  2. Nemotron 3.5 Super VL: Логика брандмауэра, белый список программ/портов, подавление повторных алертов, PID=0.
  3. LongCat 2.5 Preview: Удаление Toolbox, чистота IPC, целостность UI, локализация и тесты.
Живой стриминг статуса ведется в logs/audit_today/live_status.json.
"""
import os
import sys
import time
import json
import subprocess
from concurrent.futures import ThreadPoolExecutor

WORKSPACE = r"D:\Nas\Coding\StartupMonitor"
OUTPUT_DIR = os.path.join(WORKSPACE, "logs", "audit_today")
STATUS_FILE = os.path.join(OUTPUT_DIR, "live_status.json")
BENCH_DIR = r"C:\Users\Gms\AppData\Local\Temp\opencode\bench"

os.makedirs(OUTPUT_DIR, exist_ok=True)
os.makedirs(BENCH_DIR, exist_ok=True)

def read_file_safe(rel_path):
    fpath = os.path.join(WORKSPACE, rel_path)
    try:
        with open(fpath, "r", encoding="utf-8", errors="replace") as f:
            content = f.read()
        return f"\n\n### ФАЙЛ: {rel_path}\n```csharp\n{content}\n```\n"
    except Exception as e:
        return f"\n\n### ФАЙЛ: {rel_path} (Ошибка чтения: {e})\n"

# --- Направление 1: Ядро и безопасность ---
DIR1_FILES = [
    r"src\StartupMonitor.Service\Pipe\PipeSecurityFactory.cs",
    r"src\StartupMonitor.Shared\Models\JsonDataStore.cs",
    r"src\StartupMonitor.Service\Services\RegistryWatcher.cs",
    r"src\StartupMonitor.Service\Pipe\EventPipeServer.cs",
    r"src\StartupMonitor.Shared\Models\StartupEntry.cs",
    r"src\StartupMonitor.Shared\Models\AppConfig.cs",
]

PROMPT_DIR1 = """Ты — ведущий эксперт по многопоточности .NET 8 / C# и низкоуровневой безопасности Windows IPC.
Проведи независимый строгий аудит сегодняшних изменений Ядра и Безопасности (P0-блокеры):

Фокус проверки:
1. PipeSecurityFactory.cs:
   - Ликвидация LPE: проверка удаления AuthenticatedUserSid и снятия флага PipeAccessRights.CreateNewInstance у InteractiveSid.
   - Исключена ли возможность для локального непривилегированного пользователя перехватить пайп или создать поддельный инстанс?
2. JsonDataStore.cs:
   - Декуплинг живого кэша: Load() возвращает глубокую изолированную копию CloneSnapshot().
   - Исключены ли гонки и повреждение кэша при чтении списка автозагрузки, черного списка и настроек?
   - Механизм Flush(flushToDisk: true), атомарная замена через File.Replace, ротация *.backup и автовосстановление при сбое.
   - Инкремент Revision при любых модификациях.
3. RegistryWatcher.cs:
   - Изоляция коллбэка ThreadPool OnRegistryKeyChanged в try/catch (Zero-Crash службы).
   - Корректность 500мс Debounce и Single-Flight (_isProcessing / Interlocked), исключение дублирования событий.
4. EventPipeServer.cs:
   - Генерация ID клиентов через Interlocked.Increment(ref _nextClientId).
   - Корректность освобождения стримов и ресурсов в блоке finally при разрыве связи.

Предоставь подробный отчет на русском языке: подтверди корректность реализации, укажи оставшиеся риски или краевые случаи, если они есть.
"""

# --- Направление 2: Файрвол и алерты ---
DIR2_FILES = [
    r"src\StartupMonitor.Service\Services\Firewall\NetworkConnectionAlertService.cs",
    r"src\StartupMonitor.Service\Services\WindowsFirewallService.cs",
    r"src\StartupMonitor.Service\Services\ProcessNetworkTracker.cs",
    r"src\StartupMonitor.Shared\Models\Firewall\NetworkAlertModels.cs",
    r"src\StartupMonitor.UI\Views\NetworkSecurityHubView.xaml.cs",
    r"src\StartupMonitor.UI\Dialogs\NetworkConnectionAlertWindow.xaml.cs",
]

PROMPT_DIR2 = """Ты — ведущий системный архитектор сетевой безопасности Windows и брандмауэра.
Проведи независимый строгий аудит сегодняшних изменений Брандмауэра и сетевых алертов (P1):

Фокус проверки:
1. NetworkConnectionAlertService.cs:
   - Требование пользователя: При выборе «Добавить в белый список» программа заносится в FirewallWhitelistedApps и создается постоянное разрешающее правило брандмауэра — повторные всплывающие окна для этой программы исключены.
   - Требование пользователя: Добавлена опция «Разрешить порт» (AllowPort) с сохранением в FirewallAllowedPorts — исключены ли повторные алерты для соединений на этот порт?
   - Проверка метода ShouldAlert: корректно ли отсекаются известные процессы, системные сокеты, белый список и порты?
2. ProcessNetworkTracker.cs:
   - Требование пользователя: Сокеты с PID = 0 (TIME_WAIT, CLOSE_WAIT, Kernel сокеты) именуются как "System (Kernel/Idle)".
   - Тихий режим: Проверка флага FirewallSilentPid0 — подавляются ли алерты для PID = 0, если тумблер включен?
3. WindowsFirewallService.cs:
   - Потокобезопасность: метод GetAllRules() защищен lock (_sync). Исключены ли COM-коллизии и падения при конкурентном доступе?
4. UI и IPC интеграция:
   - Корректность привязки тумблера ChkSilentPid0 на странице файрвола и передачи изменений в фоновую службу.
   - Корректность действий в диалоговом окне NetworkConnectionAlertWindow.

Предоставь структурированный технический отчет на русском языке с оценкой надежности и соответствия требованиям.
"""

# --- Направление 3: Toolbox, UI, IPC и Локализация ---
DIR3_FILES = [
    r"src\StartupMonitor.Service\Pipe\PipeServer.cs",
    r"src\StartupMonitor.UI\Services\PipeClient.cs",
    r"src\StartupMonitor.Shared\Models\PipeMessage.cs",
    r"src\StartupMonitor.Service\Services\Ai\AiRatingService.cs",
    r"src\StartupMonitor.Service\Services\NotificationService.cs",
    r"src\StartupMonitor.UI\Views\MainWindow.xaml.cs",
    r"src\StartupMonitor.UI\Services\LocalizationService.cs",
]

PROMPT_DIR3 = """Ты — главный QA-архитектор и эксперт по целостности интерфейса WPF и протоколов IPC .NET 8.
Проведи независимый строгий сквозной аудит сегодняшних изменений по очистке проекта, оптимизации и локализации:

Фокус проверки:
1. Полное удаление модуля Toolbox (Tools):
   - Удалены ли все следы Toolbox из UI (MainWindow, навигация, страницы)?
   - Очищены ли IPC-команды в PipeServer и PipeClient (JunkClean, FileLocksmith, LanScan, Awake)?
   - Нет ли висячих вызовов, ломающих сборку или вызывающих NullReferenceException в рантайме?
2. Оптимизация сервисов:
   - AiRatingService.cs: удалена ли 5-секундная искусственная задержка в блоке finally? Корректен ли Circuit Breaker Math.Clamp(0, 10)? Безопасны ли UNC-пути?
   - NotificationService.cs: ограничена ли очередь уведомлений MaxQueueCapacity = 10_000 (DropOldest)?
3. Локализация и темы:
   - Паритет 716/716 ключей в Strings.resx и Strings.ru.resx (StatusEnabledShort).
   - Корректность переключения языков ru/en и стабильность Dark/Light тем.
4. Готовность к релизу v1.9.1:
   - Все 1232 теста успешно пройдены. Оценка общей чистоты архитектуры и отсутствия регрессий.

Предоставь подробный отчет на русском языке с итоговым вердиктом о готовности продукта к промышленной эксплуатации.
"""

AGENTS = [
    {
        "id": "agent1_deepseek",
        "name": "Субагент 1: Ядро и безопасность (DeepSeek V4.1 Flash)",
        "model": "opencode/deepseek-v4.1-flash-free",
        "prompt": PROMPT_DIR1,
        "files": DIR1_FILES,
        "out_file": os.path.join(OUTPUT_DIR, "AUDIT_1_CORE_SECURITY_DEEPSEEK.md")
    },
    {
        "id": "agent2_nemotron",
        "name": "Субагент 2: Файрвол и сетевые алерты (Nemotron 3.5 Super VL)",
        "model": "opencode/nemotron-3.5-super-vl-free",
        "prompt": PROMPT_DIR2,
        "files": DIR2_FILES,
        "out_file": os.path.join(OUTPUT_DIR, "AUDIT_2_FIREWALL_ALERTS_NEMOTRON.md")
    },
    {
        "id": "agent3_longcat",
        "name": "Субагент 3: Очистка Toolbox, UI и локализация (LongCat 2.5 Preview)",
        "model": "opencode/longcat-2.5-preview-free",
        "prompt": PROMPT_DIR3,
        "files": DIR3_FILES,
        "out_file": os.path.join(OUTPUT_DIR, "AUDIT_3_TOOLBOX_CLEANUP_LONGCAT.md")
    }
]

LIVE_STATE = {}

def update_live_state(agent_id, status, last_line="", elapsed=0.0):
    LIVE_STATE[agent_id] = {
        "status": status,
        "last_line": last_line[:300],
        "elapsed_seconds": round(elapsed, 1),
        "timestamp": time.time()
    }
    tmp = STATUS_FILE + ".tmp"
    try:
        with open(tmp, "w", encoding="utf-8") as f:
            json.dump(LIVE_STATE, f, ensure_ascii=False, indent=2)
        os.replace(tmp, STATUS_FILE)
    except Exception:
        pass

def run_agent(agent_meta):
    aid = agent_meta["id"]
    start_time = time.time()
    last_line = "Чтение исходного кода исследуемых файлов..."
    update_live_state(aid, "running", last_line, 0.0)
    print(f"[*] СТАРТ: {agent_meta['name']} (модель: {agent_meta['model']})...", flush=True)

    code_payload = "".join([read_file_safe(f) for f in agent_meta["files"]])
    full_prompt = (
        f"{agent_meta['prompt']}\n\n"
        f"Ниже предоставлен актуальный исходный код измененных модулей:\n{code_payload}\n\n"
        f"Проведи глубокий, критический анализ кода и выдай подробный технический отчет прямо сейчас!"
    )

    cmd = [
        "opencode", "run",
        "--pure",
        "--dir", BENCH_DIR,
        "-m", agent_meta["model"]
    ]

    try:
        proc = subprocess.Popen(
            cmd,
            stdin=subprocess.PIPE,
            stdout=subprocess.PIPE,
            stderr=subprocess.STDOUT,
            cwd=BENCH_DIR,
            text=True,
            encoding="utf-8",
            errors="replace",
            bufsize=1
        )

        proc.stdin.write(full_prompt)
        proc.stdin.close()

        stdout_lines = []
        while True:
            line = proc.stdout.readline()
            if not line and proc.poll() is not None:
                break
            if line:
                stdout_lines.append(line)
                clean = line.strip()
                if clean and len(clean) > 2:
                    last_line = clean
                    update_live_state(aid, "running", last_line, time.time() - start_time)

            if time.time() - start_time > 3600:
                proc.kill()
                update_live_state(aid, "timeout", "Таймаут 3600с", 3600.0)
                raise TimeoutError("Timeout 3600s")

        proc.wait()
        elapsed = time.time() - start_time
        full_stdout = "".join(stdout_lines)
        success = (proc.returncode == 0)

        update_live_state(aid, "done" if success else "failed", last_line, elapsed)
        print(f"[+] ФИНИШ: {agent_meta['name']} за {elapsed:.1f}с (код: {proc.returncode})", flush=True)

        header = (
            f"# {agent_meta['name']}\n\n"
            f"- **Модель:** `{agent_meta['model']}`\n"
            f"- **Время аудита:** {elapsed:.1f} сек\n"
            f"- **Статус:** {'Успешно' if success else f'Сбой (код {proc.returncode})'}\n\n---\n\n"
        )
        with open(agent_meta["out_file"], "w", encoding="utf-8") as f:
            f.write(header + full_stdout)

        return aid, success, elapsed
    except Exception as exc:
        elapsed = time.time() - start_time
        update_live_state(aid, "error", str(exc), elapsed)
        print(f"[-] ОШИБКА: {agent_meta['name']} - {exc}", flush=True)
        return aid, False, elapsed

def main():
    print("==================================================================", flush=True)
    print("=== АУДИТ СЕГОДНЯШНИХ ИЗМЕНЕНИЙ РОЕМ ИЗ 3 РАЗНЫХ СУБАГЕНТОВ     ===", flush=True)
    print("==================================================================", flush=True)
    start_total = time.time()

    with ThreadPoolExecutor(max_workers=3) as executor:
        futures = [executor.submit(run_agent, a) for a in AGENTS]
        results = [f.result() for f in futures]

    total_sec = time.time() - start_total
    print("==================================================================", flush=True)
    print(f"=== ВСЕ 3 СУБАГЕНТА ЗАВЕРШИЛИ АУДИТ ЗА {total_sec:.1f} СЕК ===", flush=True)
    print("==================================================================", flush=True)
    for aid, ok, el in results:
        print(f"  - {aid}: {'УСПЕХ' if ok else 'СБОЙ'} ({el:.1f} с)", flush=True)

if __name__ == "__main__":
    main()

