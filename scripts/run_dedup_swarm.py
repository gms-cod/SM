# -*- coding: utf-8 -*-
"""
Параллельный запуск гетерогенного роя из 3 моделей OpenCode для устранения повторных алертов автозапуска:
1. opencode/deepseek-v4.1-flash-free — Архитектура службы и подавление дубликатов (NotificationService & RegistryWatcher)
2. opencode/nemotron-3.5-super-vl-free — Защита от спама в UI (TrayNotificationAggregator & MainViewModel)
3. opencode/longcat-2.5-preview-free — Версионирование v1.9.2, сайт www/index.html и тесты
"""
import os
import sys
import time
import json
import subprocess
from concurrent.futures import ThreadPoolExecutor

WORKSPACE = r"D:\Nas\Coding\StartupMonitor"
OUTPUT_DIR = os.path.join(WORKSPACE, "logs")
STATUS_FILE = os.path.join(OUTPUT_DIR, "live_status_dedup.json")
BENCH_DIR = r"C:\Users\Gms\AppData\Local\Temp\opencode\bench"

os.makedirs(OUTPUT_DIR, exist_ok=True)
os.makedirs(BENCH_DIR, exist_ok=True)

PROMPT_1 = """Ты — ведущий инженер C# и эксперт по надежности системных служб Windows.
Задача: разработать решение проблемы повторных всплывающих уведомлений по одной и той же заблокированной записи (например, AdobeAAMUpdater-1.0 в ветке HKLM\\Software\\Microsoft\\Windows\\CurrentVersion\\Run).

Контекст:
1. В RegistryWatcher.cs при любом событии изменения реестра считываются ключи Run.
2. Если запись уже заблокирована в blacklist (FromBlacklist = true, Status = EntryStatus.Blocked), метод RegistryWatcher вызывает ApplyBlacklistAction, который повторно отправляет PipeMessageTypes.BlacklistMatch в NotificationService.
3. NotificationService безусловно ставит его в очередь клиенту UI.

Требования:
1. Предложи точные изменения для NotificationService.cs: скользящий кэш дедупликации (ConcurrentDictionary<string, DateTime> с тайм-аутом например 1 час или Sliding Window), подавляющий дублирование одинаковых уведомлений.
2. Предложи точные изменения для RegistryWatcher.cs: проверка, если запись уже существует в хранилище, помечена как Blocked и ее CommandLine не менялся — не вызывать повторно ApplyBlacklistAction и не ставить повторный алерт.
3. Напиши готовые блоки кода на C# с подробными русскими комментариями.
"""

PROMPT_2 = """Ты — эксперт по WPF и UI архитектуре Windows-приложений.
Задача: разработать защиту от спама уведомлений на стороне клиента StartupMonitor.UI.

Контекст:
Клиентское приложение трея слушает события через Named Pipe (TrayNotificationAggregator.cs, MainViewModel.cs) и выводит всплывающие балуны/уведомления трея при событиях BlacklistMatch, EntryAdded и др. Если служба шлет события, пользователь видит постоянные всплывающие окна.

Требования:
1. Внедрить в TrayNotificationAggregator.cs клиентское окно охлаждения (Cooldown Window) — например, не чаще 1 уведомления по одной и той же записи/пути раз в 15 минут.
2. Обеспечить корректное отображение в UI без мерцания и утечек таймеров.
3. Напиши готовый чистый код на C# с подробными русскими комментариями.
"""

PROMPT_3 = """Ты — ведущий релиз-инженер и архитектор документации.
Задача: подготовить спецификацию релиза v1.9.2 для StartupMonitor.

Контекст:
1. Directory.Build.props сейчас имеет версию 1.9.1 -> обновляем до 1.9.2.
2. В www/index.html нужно обновить блок версии, дату релиза и добавить описание нового функционала v1.9.2:
   - Интеллектуальная дедупликация уведомлений (Smart Notification Deduplication): устранение повторных всплывающих окон по заблокированным программам (Adobe Updater и др.).
   - Автоматическая изоляция мертвых ссылок автозапуска.
   - Двухуровневый Cooldown в службе и трей-клиенте.
3. Опиши юнит-тесты на xUnit/NSubstitute для проверки дедупликации.
4. Сформируй текст для git commit по стандарту Conventional Commits с версией v1.9.2 в начале.
"""

AGENTS = [
    {
        "id": "agent1_deepseek",
        "name": "Субагент 1: Служба и дедупликация (DeepSeek V4.1 Flash)",
        "model": "opencode/deepseek-v4.1-flash-free",
        "prompt": PROMPT_1,
        "out_file": os.path.join(OUTPUT_DIR, "SWARM_AGENT_1_DEEPSEEK.md")
    },
    {
        "id": "agent2_nemotron",
        "name": "Субагент 2: UI и Cooldown трея (Nemotron 3.5 Super VL)",
        "model": "opencode/nemotron-3.5-super-vl-free",
        "prompt": PROMPT_2,
        "out_file": os.path.join(OUTPUT_DIR, "SWARM_AGENT_2_NEMOTRON.md")
    },
    {
        "id": "agent3_longcat",
        "name": "Субагент 3: Релиз v1.9.2, сайт и тесты (LongCat 2.5 Preview)",
        "model": "opencode/longcat-2.5-preview-free",
        "prompt": PROMPT_3,
        "out_file": os.path.join(OUTPUT_DIR, "SWARM_AGENT_3_LONGCAT.md")
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
    tmp_file = STATUS_FILE + ".tmp"
    try:
        with open(tmp_file, "w", encoding="utf-8") as f:
            json.dump(LIVE_STATE, f, ensure_ascii=False, indent=2)
        os.replace(tmp_file, STATUS_FILE)
    except Exception:
        pass

def run_agent(agent_meta):
    aid = agent_meta["id"]
    start_time = time.time()
    last_line = "Запуск процесса и отправка промпта..."
    update_live_state(aid, "running", last_line, 0.0)
    print(f"[*] СТАРТ: {agent_meta['name']} (модель: {agent_meta['model']})...", flush=True)

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

        proc.stdin.write(agent_meta["prompt"])
        proc.stdin.close()

        stdout_lines = []
        while True:
            line = proc.stdout.readline()
            if not line and proc.poll() is not None:
                break
            if line:
                stdout_lines.append(line)
                stripped = line.strip()
                if stripped and len(stripped) > 2:
                    last_line = stripped
                    update_live_state(aid, "running", last_line, time.time() - start_time)

            if time.time() - start_time > 3600:
                proc.kill()
                update_live_state(aid, "timeout", "Таймаут 3600 сек", 3600.0)
                raise TimeoutError("Timeout 3600s")

        proc.wait()
        elapsed = time.time() - start_time
        full_stdout = "".join(stdout_lines)
        success = (proc.returncode == 0)

        with open(agent_meta["out_file"], "w", encoding="utf-8") as out:
            out.write(f"# Результат работы: {agent_meta['name']}\n")
            out.write(f"- Модель: {agent_meta['model']}\n")
            out.write(f"- Время выполнения: {elapsed:.2f} сек\n")
            out.write(f"- Код завершения: {proc.returncode}\n\n")
            out.write(full_stdout)

        final_status = "completed" if success else "failed"
        update_live_state(aid, final_status, f"Завершено за {elapsed:.2f}с (rc={proc.returncode})", elapsed)
        print(f"[+] ЗАВЕРШЕНО: {agent_meta['name']} за {elapsed:.2f}с (rc={proc.returncode})", flush=True)
        return success, elapsed, full_stdout

    except Exception as e:
        elapsed = time.time() - start_time
        update_live_state(aid, "error", str(e), elapsed)
        print(f"[-] ОШИБКА: {agent_meta['name']}: {e}", flush=True)
        return False, elapsed, str(e)

def main():
    print("=" * 70, flush=True)
    print(">>> ЗАПУСК ГЕТЕРОГЕННОГО РОЯ OPENCODE (3 МОДЕЛИ ПАРАЛЛЕЛЬНО) <<<", flush=True)
    print("=" * 70, flush=True)

    with ThreadPoolExecutor(max_workers=3) as executor:
        futures = [executor.submit(run_agent, ag) for ag in AGENTS]
        
        # Мониторинг стриминга каждые 10-15 секунд до завершения
        while any(not f.done() for f in futures):
            time.sleep(10)
            if os.path.exists(STATUS_FILE):
                try:
                    with open(STATUS_FILE, "r", encoding="utf-8") as f:
                        data = json.load(f)
                    print("\n--- [СТРИМИНГ СТАТУСА РОЯ OPENCODE] ---", flush=True)
                    for aid, st in data.items():
                        print(f"[{aid}] {st['status'].upper()} ({st['elapsed_seconds']}с): {st['last_line'][:100]}", flush=True)
                    print("---------------------------------------", flush=True)
                except Exception:
                    pass

        results = [f.result() for f in futures]

    print("\n" + "=" * 70, flush=True)
    print(">>> ВСЕ СУБАГЕНТЫ РОЯ УСПЕШНО ЗАВЕРШИЛИ РАБОТУ <<<", flush=True)
    print("=" * 70, flush=True)

if __name__ == "__main__":
    main()

