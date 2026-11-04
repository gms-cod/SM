# -*- coding: utf-8 -*-
"""
Канонический многопоточный раннер роя OpenCode для Startup Monitor (Направление 3: Модули расширения, сканеры, файрвол и безопасность).
Запускает ровно 3 РАЗНЫХ субагента OpenCode параллельно через ThreadPoolExecutor:
  1. DeepSeek V4.1 Flash (Логика сканирования, фильтрация, гонки сетевых структур, утечки сокетов)
  2. Space Bunny (Стресс-сценарии: флуд соединений, падение WMI, отказ DNS/Kaspersky API, WinFirewall COM блокировки)
  3. LongCat 2.5 Preview (Масштабируемость, дедлоки между сервисами безопасности, производительность)
Обновляет файл текущего прогресса logs/audit_triad/live_status_dir3.json с последней выведенной строкой.
Лимит времени: 1 час (3600 секунд).
"""
import os
import sys
import time
import json
import subprocess
from concurrent.futures import ThreadPoolExecutor

WORKSPACE = r"D:\Nas\Coding\StartupMonitor"
OUTPUT_DIR = os.path.join(WORKSPACE, "logs", "audit_triad")
STATUS_FILE = os.path.join(OUTPUT_DIR, "live_status_dir3.json")
os.makedirs(OUTPUT_DIR, exist_ok=True)

TARGET_FILES = [
    os.path.join(WORKSPACE, r"src\StartupMonitor.Service\Services\SystemScannerService.cs"),
    os.path.join(WORKSPACE, r"src\StartupMonitor.Service\Services\WindowsFirewallService.cs"),
    os.path.join(WORKSPACE, r"src\StartupMonitor.Service\Services\VirusScan\KasperskyScanService.cs"),
    os.path.join(WORKSPACE, r"src\StartupMonitor.Service\Services\Guards\AutoGuardEngine.cs"),
    os.path.join(WORKSPACE, r"src\StartupMonitor.Service\Services\Firewall\ProcessPortDefenderService.cs"),
    os.path.join(WORKSPACE, r"src\StartupMonitor.Service\Services\Toolbox\JunkCleanerService.cs"),
]

PROMPT_ALPHA = """Ты — ведущий эксперт по системной безопасности, многопоточности .NET 8 / C# и Windows API.
Проведи независимый строгий аудит Направления 3: Модули расширения, сетевой экран, антивирусный шлюз и службы защиты (Security, Scanners & Defenders).

Фокус исследования:
1. SystemScannerService.cs:
   - Многоядерный пайплайн сканирования: параллельный обход дисков/реестра, SemaphoreSlim, обработка AccessDenied и рекурсивных каталогов.
   - Потокобезопасность сбора результатов, предотвращение дублирования записей при одновременном сканировании.
2. WindowsFirewallService.cs & ProcessPortDefenderService.cs:
   - Интеграция с NetFwTypeLib COM API: маршалинг, потоковая модель STA/MTA, освобождение COM-дескрипторов (Marshal.ReleaseComObject).
   - Скользящие окна анализа сетевых соединений (SlidingWindowTracker), предотвращение утечек памяти при постоянном потоке новых IP.
   - Устойчивость блокировки портов и правил брандмауэра при падении или перезапуске службы Windows Firewall (MpsSvc).
3. KasperskyScanService.cs:
   - Проверка хэшей, кэширование результатов, обработка сетевых сбоев, таймаутов и лимитов API.
   - Безопасная десериализация ответов, отсутствие утечек соединений HttpClient.
4. AutoGuardEngine.cs & JunkCleanerService.cs:
   - Автоматическая нейтрализация угроз: предотвращение ложных срабатываний (False Positives) на системных процессах Windows (svchost, csrss, lsass).
   - Очистка временных файлов: защита от удаления заблокированных или используемых файлов, безопасная работа с правами SYSTEM.

Предоставь подробный технический отчет на русском языке с указанием файлов, конкретных номеров строк, степени риска (P0-P3) и C# кодом решений.
"""

PROMPT_BETA = """Ты — ведущий инженер по стресс-тестированию и надежности отказоустойчивых Windows Services на C# (.NET 8).
Проведи независимый стресс-аудит Направления 3: Отказоустойчивость сканеров, файрвола и подсистем активной защиты.

Фокус исследования:
1. Сетевой стресс и атаки:
   - Поведение ProcessPortDefenderService и WindowsFirewallService при SYN-флуде, сканировании портов (10 000 пакетов/сек), исчерпании таблицы соединений Windows.
   - Блокировка COM-объектов файрвола (Deadlock COM RPC) при одновременном изменении правил несколькими процессами.
2. Сбои файловой системы и очистки:
   - Поведение JunkCleanerService при символических ссылках (Junction points, symlinks), попытках выхода за пределы целевой папки (Directory Traversal).
   - Поведение SystemScannerService при наличии гигантских файлов (>100GB), заблокированных файлов реестра или поврежденных секторов диска.
3. Отказоустойчивость внешних сервисов:
   - Полное отключение интернета или блокировка доменов Kaspersky/VirusTotal: не зависает ли цикл сканирования, корректность отката (graceful fallback).
4. Защита процесса службы:
   - Гарантия отсутствия необработанных исключений в BackgroundService, фоновых потоках и таймерах, ведущих к падению службы (Zero-Crash).

Предоставь подробный отчет на русском языке с описанием краевых сценариев (edge cases) и точным кодом исправлений.
"""

PROMPT_GAMMA = """Ты — ведущий системный архитектор системного ПО Windows и оптимизатор производительности .NET 8.
Проведи глубокий аудит Направления 3: Дедлок-матрица, производительность, аллокации и верификация модулей безопасности.

Фокус исследования:
1. Матрица взаимных блокировок (Deadlock Matrix):
   - Вложенные вызовы между SystemScannerService, AutoGuardEngine, WindowsFirewallService и JsonDataStore.
   - Синхронное ожидание асинхронных задач (.Result, .Wait(), GetAwaiter().GetResult()) при вызове COM брандмауэра или проверки подписей.
2. Анализ аллокаций памяти и CPU:
   - Нагрузка при парсинге соединений TCP/UDP (GetExtendedTcpTable), строковые аллокации при форматировании правил файрвола.
   - Эффективность кэшей хэшей и структур сокетов.
3. Верификация исправлений версий v1.7.6 - v1.8.0:
   - Проверить устраненные ранее замечания в JunkCleaner, сканере портов и файрволе.

Предоставь структурированный технический отчет на русском языке с оценкой стабильности подсистем (баллы 1-10) и фрагментами кода для оптимизации.
"""

AGENTS = [
    {
        "id": "agent1_deepseek",
        "name": "Субагент 1: Логика сканеров и файрвола (DeepSeek V4.1 Flash)",
        "model": "opencode/deepseek-v4.1-flash-free",
        "prompt": PROMPT_ALPHA,
        "out_file": os.path.join(OUTPUT_DIR, "AGENT_1_SECURITY_FIREWALL_CONCURRENCY.md")
    },
    {
        "id": "agent2_spacebunny",
        "name": "Субагент 2: Стресс-сценарии и сетевая устойчивость (Space Bunny)",
        "model": "opencode/space-bunny-free",
        "prompt": PROMPT_BETA,
        "out_file": os.path.join(OUTPUT_DIR, "AGENT_2_SECURITY_FIREWALL_STRESS.md")
    },
    {
        "id": "agent3_longcat",
        "name": "Субагент 3: Дедлок-матрица и производительность (LongCat 2.5 Preview)",
        "model": "opencode/longcat-2.5-preview-free",
        "prompt": PROMPT_GAMMA,
        "out_file": os.path.join(OUTPUT_DIR, "AGENT_3_SECURITY_FIREWALL_PERF.md")
    }
]

def update_live_state(agent_id, status, last_line, elapsed):
    current = {}
    if os.path.exists(STATUS_FILE):
        try:
            with open(STATUS_FILE, "r", encoding="utf-8") as f:
                current = json.load(f)
        except Exception:
            current = {}
            
    current[agent_id] = {
        "status": status,
        "last_line": last_line,
        "elapsed_seconds": round(elapsed, 1),
        "timestamp": time.time()
    }
    
    tmp_file = STATUS_FILE + ".tmp"
    with open(tmp_file, "w", encoding="utf-8") as f:
        json.dump(current, f, ensure_ascii=False, indent=2)
    try:
        os.replace(tmp_file, STATUS_FILE)
    except Exception:
        pass

def run_agent(agent_meta):
    aid = agent_meta["id"]
    start_time = time.time()
    last_meaningful_line = "Запуск процесса..."
    update_live_state(aid, "running", last_meaningful_line, 0.0)
    
    print(f"[*] СТАРТ: {agent_meta['name']} (Модель: {agent_meta['model']})...", flush=True)
    
    code_blocks = []
    for tf in TARGET_FILES:
        try:
            with open(tf, "r", encoding="utf-8", errors="replace") as f:
                content = f.read()
            rel_name = os.path.relpath(tf, WORKSPACE)
            code_blocks.append(f"\n--- ФАЙЛ: {rel_name} ---\n{content}\n")
        except Exception as e:
            code_blocks.append(f"\n--- ОШИБКА ЧТЕНИЯ {tf}: {e} ---\n")
            
    full_prompt = (
        agent_meta["prompt"]
        + "\n\nНиже предоставлен исходный код исследуемых модулей безопасности:\n"
        + "\n".join(code_blocks)
        + "\n\nВАЖНО: Проведи максимально строгий и детальный анализ кода, укажи конкретные строки, баги, гонки и предложи точный C# код исправлений!"
    )
    
    cmd = [
        "opencode", "run",
        "-m", agent_meta["model"]
    ]
    
    try:
        proc = subprocess.Popen(
            cmd,
            stdin=subprocess.PIPE,
            stdout=subprocess.PIPE,
            stderr=subprocess.STDOUT,
            cwd=WORKSPACE,
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
                stripped = line.strip()
                if stripped and len(stripped) > 3:
                    last_meaningful_line = stripped
                    update_live_state(aid, "running", last_meaningful_line, time.time() - start_time)
                    
            if time.time() - start_time > 28800:
                proc.kill()
                update_live_state(aid, "timeout", "Превышен лимит 28800 секунд (8 часов)", 28800.0)
                raise TimeoutError("Timeout 28800s")
                
        proc.wait()
        elapsed = time.time() - start_time
        full_stdout = "".join(stdout_lines)
        success = proc.returncode == 0
        
        update_live_state(aid, "done" if success else "failed", last_meaningful_line, elapsed)
        print(f"[+] ФИНИШ: {agent_meta['name']} за {elapsed:.1f}с (Успех: {success}, Код: {proc.returncode})", flush=True)
        
        header = f"# {agent_meta['name']}\n\n"
        header += f"- **Модель:** `{agent_meta['model']}`\n"
        header += f"- **Время анализа:** {elapsed:.1f} сек\n"
        header += f"- **Статус:** {'Успешно' if success else f'Ошибка (код {proc.returncode})'}\n\n---\n\n"
        
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
    print("=== ЗАПУСК ПАРАЛЛЕЛЬНОГО РОЯ ИЗ 3 РАЗНЫХ СУБАГЕНТОВ OPENCODE       ===", flush=True)
    print("=== НАПРАВЛЕНИЕ 3: МОДУЛИ БЕЗОПАСНОСТИ, СКАНЕРЫ И ФАЙРВОЛ          ===", flush=True)
    print("=== ЖИВОЙ СТРИМИНГ ПОСЛЕДНЕЙ СТРОКИ | ТАЙМАУТ: 3600 СЕК (1 ЧАС)   ===", flush=True)
    print("==================================================================", flush=True)
    
    start_total = time.time()
    
    with ThreadPoolExecutor(max_workers=3) as executor:
        futures = [executor.submit(run_agent, a) for a in AGENTS]
        results = [f.result() for f in futures]
        
    total_sec = time.time() - start_total
    print("\n==================================================================", flush=True)
    print(f"=== ВСЕ 3 СУБАГЕНТА ЗАВЕРШИЛИ РАБОТУ ЗА {total_sec:.1f} сек ===", flush=True)
    print("==================================================================", flush=True)
    for aid, ok, el in results:
        print(f"  - {aid}: {'УСПЕХ' if ok else 'СБОЙ'} ({el:.1f} с)", flush=True)

if __name__ == "__main__":
    main()

