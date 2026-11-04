# -*- coding: utf-8 -*-
"""
Канонический многопоточный раннер роя OpenCode для Startup Monitor (Направление 2: Фоновые сервисы и хранилище данных).
Запускает ровно 3 РАЗНЫХ субагента OpenCode параллельно через ThreadPoolExecutor:
  1. DeepSeek V4.1 Flash (Логика, транзакции, гонки кэша, атомарность записи)
  2. Space Bunny (Стресс-сценарии, внезапные сбои питания, повреждение JSON, утечки таймеров)
  3. LongCat 2.5 Preview (Сверхдлинный контекст, дедлок-матрица, производительность и аудит изменений v1.7.6-v1.8.0)
Обновляет файл текущего прогресса logs/audit_triad/live_status_dir2.json с последней выведенной строкой.
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
STATUS_FILE = os.path.join(OUTPUT_DIR, "live_status_dir2.json")
os.makedirs(OUTPUT_DIR, exist_ok=True)

TARGET_FILES = [
    os.path.join(WORKSPACE, r"src\StartupMonitor.Shared\Models\JsonDataStore.cs"),
    os.path.join(WORKSPACE, r"src\StartupMonitor.Service\Services\RegistryWatcher.cs"),
    os.path.join(WORKSPACE, r"src\StartupMonitor.Service\Services\NotificationService.cs"),
    os.path.join(WORKSPACE, r"src\StartupMonitor.Service\Services\Ai\AiRatingService.cs"),
    os.path.join(WORKSPACE, r"src\StartupMonitor.Service\Services\SnapshotManager.cs"),
]

PROMPT_ALPHA = """Ты — ведущий эксперт по системному многопоточному программированию .NET 8 / C# и архитектуре Windows-сервисов.
Проведи независимый строгий аудит Направления 2: Фоновые службы, наблюдение за автозагрузкой и хранилище данных (Storage & Watchers).

Фокус исследования:
1. JsonDataStore.cs:
   - Анализ concurrency модели: SemaphoreSlim, блокировки чтения/записи, возврат живой ссылки _cache vs неизменяемая копия/снимок.
   - Механизм атомарной записи на диск (temp file -> replace), защита от повреждения JSON при внезапном отключении питания или сбое процесса.
   - Синхронизация ревизии (_revision) при фоновом добавлении, обновлении и удалении записей. Потеря обновлений в UI при NotModified.
2. RegistryWatcher.cs:
   - Режим отслеживания реестра: RegNotifyChangeKeyValue, потоковая модель, отсутствие утечек дескрипторов (HKEY, WaitHandle).
   - Устойчивость к переименованию, быстрому созданию/удалению ключей, предотвращение шторма событий (debounce).
3. NotificationService.cs & SnapshotManager.cs:
   - Очередь уведомлений, переполнение памяти, обработка сбоев доставки в UI.
   - Снимки состояния (Snapshot), целостность данных при параллельном чтении и записи.
4. AiRatingService.cs:
   - Кэширование оценок, параллельные запросы, обработка сбоев провайдеров ИИ и таймаутов, потокобезопасность словарей.

Предоставь подробный технический отчет на русском языке с указанием файлов, номеров строк, степени риска (P0-P3) и конкретным кодом решений.
"""

PROMPT_BETA = """Ты — ведущий специалист по многопоточной надежности и стресс-тестированию Windows Services на C# (.NET 8).
Проведи независимый стресс-аудит Направления 2: Отказоустойчивость хранилища данных и служб мониторинга.

Фокус исследования:
1. Стресс-сценарии сбоев хранилища:
   - Поведение JsonDataStore при заблокированном файле (antivirus scan, backup tool), нулевом свободном месте на диске, поврежденном JSON файле.
   - Механизм резервных копий (backup.json), восстановление после сбоя.
2. Стресс-сценарии реестра и WMI:
   - Массовое создание 1000 записей реестра за секунду: поведение RegistryWatcher, переполнение очереди, утечки потоков/дескрипторов.
   - Поведение при перезапуске служб или смене пользователя сессии Windows.
3. Пожирание памяти и ресурсов:
   - Анализ аллокаций памяти в JsonDataStore, AiRatingService и NotificationService. Риски утечек ссылок, событий (event handler leaks), таймеров.
4. Проверка исключений:
   - Все ли фоновые циклы и фоновые задачи изолированы try/catch, исключающие падение службы Windows (NT AUTHORITY\\SYSTEM).

Предоставь подробный отчет на русском языке с конкретными выводами, краевыми случаями (edge cases) и кодом решений.
"""

PROMPT_GAMMA = """Ты — ведущий системный архитектор .NET 8 и эксперт по низкоуровневой производительности Windows.
Проведи глубокий аудит Направления 2: Производительность, масштабируемость и дедлок-матрица хранилища данных и наблюдателей.

Фокус исследования:
1. Матрица взаимных блокировок (Deadlock Matrix):
   - Проверка всех цепочек lock и SemaphoreSlim между JsonDataStore, RegistryWatcher, NotificationService, PipeServer и SystemScannerService.
   - Риски дедлока при одновременном вызове SaveAsync и фоновом добавлении записей из RegistryWatcher.
2. Производительность операций чтения и сериализации:
   - Нагрузка на Garbage Collector (LOH/POH), аллокация строк и JSON DOM.
   - Эффективность кэширования и поиска по хэшам записей.
3. Верификация исправлений версий v1.7.6 - v1.8.0:
   - Проверить устранение ранее найденных замечаний в JsonDataStore, кэше рейтингов и фоновых вотчерах.
   - Достаточность тестов и граничных проверок.

Предоставь структурированный технический отчет на русском языке с оценкой стабильности (баллы 1-10) и точными участками кода, требующими оптимизации.
"""

AGENTS = [
    {
        "id": "agent1_deepseek",
        "name": "Субагент 1: Логика хранилища и вотчеров (DeepSeek V4.1 Flash)",
        "model": "opencode/deepseek-v4.1-flash-free",
        "prompt": PROMPT_ALPHA,
        "out_file": os.path.join(OUTPUT_DIR, "AGENT_1_STORAGE_WATCHERS_CONCURRENCY.md")
    },
    {
        "id": "agent2_spacebunny",
        "name": "Субагент 2: Стресс-сценарии и отказоустойчивость (Space Bunny)",
        "model": "opencode/space-bunny-free",
        "prompt": PROMPT_BETA,
        "out_file": os.path.join(OUTPUT_DIR, "AGENT_2_STORAGE_WATCHERS_STRESS.md")
    },
    {
        "id": "agent3_longcat",
        "name": "Субагент 3: Дедлок-матрица и масштабируемость (LongCat 2.5 Preview)",
        "model": "opencode/longcat-2.5-preview-free",
        "prompt": PROMPT_GAMMA,
        "out_file": os.path.join(OUTPUT_DIR, "AGENT_3_STORAGE_WATCHERS_PERF.md")
    }
]

def update_live_state(agent_id, status, last_line, elapsed):
    """Атомарно обновляет json-файл живого статуса субагентов для мониторинга оркестратором."""
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
    
    # Формируем полный контекст файлов
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
        + "\n\nНиже предоставлен исходный код исследуемых модулей:\n"
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
                    
            if time.time() - start_time > 3600:
                proc.kill()
                update_live_state(aid, "timeout", "Превышен лимит 3600 секунд", 3600.0)
                raise TimeoutError("Timeout 3600s")
                
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
    print("=== НАПРАВЛЕНИЕ 2: ХРАНИЛИЩЕ ДАННЫХ И ФОНОВЫЕ СЛУЖБЫ              ===", flush=True)
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

