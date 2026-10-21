# -*- coding: utf-8 -*-
"""
Единый каноничный стандарт запуска гетерогенного роя OpenCode (run_cpu_spike_swarm.py).
Запускает 3 самые быстрые бесплатные модели для экспресс-подтверждения и сквозного аудита всех изменений:
1. opencode/nemotron-3.5-super-vl-free (5.62 c) — Валидация сетевого мониторинга и оптимизации COM WindowsFirewallService
2. opencode/beam-free (5.70 c) — Валидация автообновления, автономного сторожа и интерактивного запуска в сессии пользователя
3. opencode/deepseek-v4.1-flash-free (8.00 c) — Валидация поядерного балансировщика, Time-Slicing и защиты от гонок
"""
import os
import sys
import time
import json
import subprocess
from concurrent.futures import ThreadPoolExecutor

# Принудительная установка UTF-8 для вывода в консоль Windows
if sys.platform == "win32":
    try:
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")
        sys.stderr.reconfigure(encoding="utf-8", errors="replace")
    except Exception:
        pass

WORKSPACE = r"D:\Nas\Coding\StartupMonitor"
OUTPUT_DIR = os.path.join(WORKSPACE, "logs")
STATUS_FILE = os.path.join(OUTPUT_DIR, "live_status_cpu_spike.json")
BASE_TEMP = r"C:\Users\Gms\AppData\Local\Temp\opencode"

os.makedirs(OUTPUT_DIR, exist_ok=True)
os.makedirs(BASE_TEMP, exist_ok=True)

PROMPT_1_FIREWALL_NET = """Ты — ведущий системный инженер-валидатор C#/.NET.
Задача: Проведи экспертную верификацию внесенных оптимизаций сетевого мониторинга и фаервола в StartupMonitor:

Проанализируй файлы в репозитории:
1. src/StartupMonitor.Service/Services/WindowsFirewallService.cs:
   - Метод GetApplicationRulePaths() — считывает только путь приложения без Reflection по 13 полям через IDispatch.
   - Метод Thread.Yield() каждые 50 правил при обходе коллекции.
2. src/StartupMonitor.Service/Services/Firewall/NetworkConnectionAlertService.cs:
   - Использование GetApplicationRulePaths() вместо GetAllRules() при сопоставлении сокетов с правилами брандмауэра.
   - Отсутствие лавинообразных повторных сканирований и утечек ресурсов COM.

Оцени:
- Устранены ли 25% всплески CPU при проверке правил сокетов?
- Соответствует ли код стандартам Zero-Crash?
- Дай четкое заключение на русском языке.
"""

PROMPT_2_UPDATER = """Ты — ведущий инженер по архитектуре обновлений и интерактивным сессиям Windows.
Задача: Проведи экспертную верификацию исправления проблемы: «после автоматического обновления программа не перезапускается».

Проанализируй файлы в репозитории:
1. src/StartupMonitor.Service/Services/Update/GitHubUpdateService.cs:
   - Метод InstallUpdate() — подготовка и запуск сторожевого .cmd скрипта в активной интерактивной сессии пользователя через UserSessionLauncher.Launch().
   - Корректность синтаксиса batch-файла (цикл ожидания, пауза 3с, самоудаление через del \"%~f0\").
2. src/StartupMonitor.UI/Views/Dialogs/UpdateAvailableWindow.xaml.cs:
   - Замена сбойной однострочной команды cmd /c (где goto приводил к вылету) на автономный файл сторожа.
3. installers/StartupMonitor.iss:
   - Секция [Run] и взаимодействие с UserSessionLauncher при тихой установке (/SILENT /VERYSILENT).

Оцени:
- Гарантирован ли перезапуск UI на рабочем столе пользователя при обновлении из UI и при обновлении службой?
- Дай четкое заключение на русском языке.
"""

PROMPT_3_CPU_CORES = """Ты — флагман логики, системных алгоритмов и многопоточности Windows/C#.
Задача: Проведи экспертную верификацию поядерного балансировщика, Time-Slicing и защиты от штормов реестра/планировщика.

Проанализируй файлы в репозитории:
1. src/StartupMonitor.Shared/Helpers/CpuUsageHelper.cs:
   - Поядерный опрос через NtQuerySystemInformation(SystemProcessorPerformanceInformation).
   - GetLeastLoadedProcessorCore(), AssignCurrentThreadToIdealCore(coreIndex) через SetThreadIdealProcessor.
   - SetCurrentThreadLowPriority(), WaitForCpuHeadroomAsync(60.0).
2. src/StartupMonitor.Service/Services/OtherStartupLocationsWatcher.cs:
   - Исправление P/Invoke сигнатуры RegNotifyChangeKeyValue (Win32 каноничная сигнатура).
   - Single-flight шлюз (SemaphoreSlim), Debounce 2с, привязка потока к свободному ядру.
3. src/StartupMonitor.Service/Services/TaskSchedulerWatcher.cs:
   - Кэширование task.Definition, адаптивный опрос (30с -> 60с -> 180с), Time-Slicing пауза 5мс каждые 25 задач.
4. src/StartupMonitor.Service/Services/WmiSubscriptionWatcher.cs:
   - Увеличение интервалов WITHIN до 30 секунд.

Оцени:
- Проверь код на гонки (Race Conditions), дедлоки и корректность освобождения ресурсов.
- Подтверди устранение всплесков 25% CPU (100% одного ядра).
- Дай детальное заключение на русском языке.
"""

AGENTS = [
    {
        "id": "agent1_nemotron",
        "name": "Субагент 1 (Nemotron 3.5 Super VL)",
        "task": "Аудит сетевого мониторинга и оптимизации COM WindowsFirewallService",
        "model": "opencode/nemotron-3.5-super-vl-free",
        "prompt": PROMPT_1_FIREWALL_NET,
        "dir": WORKSPACE,
        "out_file": os.path.join(OUTPUT_DIR, "VALIDATION_AGENT_1_NEMOTRON.md")
    },
    {
        "id": "agent2_beam",
        "name": "Субагент 2 (Beam)",
        "task": "Аудит автообновления: автономный сторож и Session 0 UserSessionLauncher",
        "model": "opencode/beam-free",
        "prompt": PROMPT_2_UPDATER,
        "dir": WORKSPACE,
        "out_file": os.path.join(OUTPUT_DIR, "VALIDATION_AGENT_2_BEAM.md")
    },
    {
        "id": "agent3_deepseek",
        "name": "Субагент 3 (DeepSeek V4.1 Flash)",
        "task": "Аудит поядерного балансировщика, Time-Slicing и устранения гонок",
        "model": "opencode/deepseek-v4.1-flash-free",
        "prompt": PROMPT_3_CPU_CORES,
        "dir": WORKSPACE,
        "out_file": os.path.join(OUTPUT_DIR, "VALIDATION_AGENT_3_DEEPSEEK.md")
    }
]

status_data = {
    agent["id"]: {
        "name": agent["name"],
        "task": agent["task"],
        "model": agent["model"],
        "status": "pending",
        "last_line": "Ожидание старта...",
        "elapsed_seconds": 0,
        "remaining_est": "~15-60s",
        "completed": False,
        "out_file": agent["out_file"]
    }
    for agent in AGENTS
}

def update_status_file():
    try:
        with open(STATUS_FILE, "w", encoding="utf-8") as f:
            json.dump(status_data, f, ensure_ascii=False, indent=2)
    except Exception:
        pass

def run_agent(agent):
    agent_id = agent["id"]
    model = agent["model"]
    prompt = agent["prompt"]
    work_dir = agent["dir"]
    out_file = agent["out_file"]
    
    start_time = time.time()
    status_data[agent_id]["status"] = "running"
    status_data[agent_id]["last_line"] = "Запуск процесса OpenCode..."
    update_status_file()
    
    cmd = ["opencode", "run", "--pure", "--dir", work_dir, "-m", model]
    
    full_output = []
    try:
        proc = subprocess.Popen(
            cmd,
            stdin=subprocess.PIPE,
            stdout=subprocess.PIPE,
            stderr=subprocess.STDOUT,
            text=True,
            encoding="utf-8",
            errors="replace",
            bufsize=1
        )
        
        proc.stdin.write(prompt)
        proc.stdin.close()
        
        for line in proc.stdout:
            clean_line = line.strip()
            full_output.append(line)
            if clean_line:
                elapsed = int(time.time() - start_time)
                status_data[agent_id]["elapsed_seconds"] = elapsed
                status_data[agent_id]["last_line"] = clean_line[:120]
                status_data[agent_id]["remaining_est"] = f"~{max(5, 45 - elapsed)}s"
                update_status_file()
                
        proc.wait(timeout=3600)
        elapsed = int(time.time() - start_time)
        status_data[agent_id]["status"] = "completed"
        status_data[agent_id]["completed"] = True
        status_data[agent_id]["elapsed_seconds"] = elapsed
        status_data[agent_id]["remaining_est"] = "0s"
        status_data[agent_id]["last_line"] = f"Завершено успешно за {elapsed}с."
        update_status_file()
        
        with open(out_file, "w", encoding="utf-8") as f:
            f.write("".join(full_output))
            
        return True, f"{agent['name']} завершил аудит за {elapsed}с."
    except Exception as e:
        elapsed = int(time.time() - start_time)
        status_data[agent_id]["status"] = "failed"
        status_data[agent_id]["last_line"] = f"Ошибка: {str(e)[:100]}"
        update_status_file()
        return False, f"{agent['name']} завершился с ошибкой: {e}"

def main():
    print("=" * 70)
    print("ЗАПУСК ВАЛИДАЦИОННОГО РОЯ OPENCODE (САМЫЕ БЫСТРЫЕ МОДЕЛИ)")
    print("=" * 70)
    for a in AGENTS:
        print(f"  * {a['name']} -> {a['model']}")
    print("-" * 70)
    
    update_status_file()
    
    start_all = time.time()
    with ThreadPoolExecutor(max_workers=len(AGENTS)) as executor:
        futures = [executor.submit(run_agent, agent) for agent in AGENTS]
        
        while any(not f.done() for f in futures):
            time.sleep(2)
            elapsed_total = int(time.time() - start_all)
            print(f"[{elapsed_total}s] Мониторинг роя: " + ", ".join([f"{a['id']}: {status_data[a['id']]['status']}" for a in AGENTS]))
            
        for f in futures:
            res_ok, res_msg = f.result()
            print(f"Результат: {res_msg}")
            
    print("=" * 70)
    print(f"Все агенты завершили работу за {int(time.time() - start_all)} с!")
    print(f"Отчеты сохранены в каталог: {OUTPUT_DIR}")
    print("=" * 70)

if __name__ == "__main__":
    main()
