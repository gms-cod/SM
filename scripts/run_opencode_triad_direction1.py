# -*- coding: utf-8 -*-
"""
Канонический многопоточный раннер роя OpenCode для Startup Monitor.
Запускает ровно 3 РАЗНЫХ субагента OpenCode параллельно через ThreadPoolExecutor.
Каждую секунду обновляет файл текущего прогресса с последней выведенной строкой.
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
STATUS_FILE = os.path.join(OUTPUT_DIR, "live_status.json")
os.makedirs(OUTPUT_DIR, exist_ok=True)

TARGET_FILES = [
    os.path.join(WORKSPACE, r"src\StartupMonitor.Service\Pipe\EventPipeServer.cs"),
    os.path.join(WORKSPACE, r"src\StartupMonitor.Service\Pipe\PipeServer.cs"),
    os.path.join(WORKSPACE, r"src\StartupMonitor.UI\Services\PipeClient.cs"),
    os.path.join(WORKSPACE, r"src\StartupMonitor.Shared\Models\PipeMessage.cs"),
]

PROMPT_ALPHA = """Ты — ведущий эксперт по системному многопоточному программированию .NET 8 / C#.
Проведи независимый строгий аудит Направления 1: Ядро, многопоточность, IPC.

Фокус исследования:
1. Проверить неблокирующую широковещательную рассылку EventPipeServer: ConnectedClient, SemaphoreSlim(1,1), таймауты 2000 мс, очистку _clients, корректность отписки и Dispose.
2. Проверить PipeServer: обработка входящих запросов, конкурентный доступ, чтение/запись без блокировок, отсутствие скрытых сохранений при операциях чтения.
3. Проверить PipeClient: обработка сбоев сокета, перехват IOException и ObjectDisposedException, гарантированный вызов Disconnected, авто-реконнект.
4. Выявить ЛЮБЫЕ потенциальные гонки (race conditions), взаимные блокировки (deadlocks), утечки дескрипторов Pipe/Mutex/Semaphore, ThreadPool starvation или LOH-нагрузки.

Предоставь подробный технический отчет на русском языке с указанием файлов, номеров строк, степени риска (P0-P3) и кодом решений.
"""

PROMPT_BETA = """Ты — ведущий специалист по многопоточным протоколам и надежности Windows Services на C# (.NET 8).
Проведи независимый стресс-аудит Направления 1: Архитектура IPC и устойчивость к сбоям клиентов.

Фокус исследования:
1. Стресс-сценарии: поведение сервера при внезапном закрытии UI-клиента (Process Kill), медленном чтении клиентом (Slow Reader / Buffer bloat), отправке мусорных пакетов.
2. Жизненный цикл семафоров и потоков: очистка отвалившихся клиентов из _clients, отмена таймаутов, безопасное уничтожение CancellationTokenSource.
3. Анализ аллокаций памяти: сериализация PipeMessage, работа StreamReader/StreamWriter, предотвращение фрагментации памяти (LOH).
4. Проверка исключений: все ли точки вызова защищены от падения фоновой службы Windows (NT AUTHORITY\\SYSTEM).

Предоставь подробный отчет на русском языке с конкретными выводами, краевыми случаями (edge cases) и кодом решений.
"""

PROMPT_GAMMA = """Ты — архитектор высоконагруженных систем .NET 8 и эксперт по низкоуровневому Named Pipe IPC.
Проведи глубокий аудит Направления 1: Производительность, масштабируемость и дедлок-анализ IPC подсистемы.

Фокус исследования:
1. Матрица взаимных блокировок (Deadlock Matrix): проверка всех вложенных lock, Monitor, SemaphoreSlim.WaitAsync между PipeServer, EventPipeServer и PipeClient.
2. Буферизация и пропускная способность: размеры буферов NamedPipeServerStream, NamedPipeClientStream, неблокирующий Flush/WriteAsync, поведение при всплеске из 1000 событий в секунду.
3. Обратная связь UI: корректность реактивной обработки входящих пакетов в PipeClient, передача в UI-поток через Dispatcher без блокировки сетевого потока.
4. Верификация исправлений аудита v1.7.6: подтвердить устранение дефектов №1, №5, №6, №9, №29.

Предоставь структурированный технический отчет на русском языке с оценкой стабильности и точными участками кода, требующими оптимизации.
"""

AGENTS = [
    {
        "id": "agent1_deepseek",
        "name": "Субагент 1: Логика и многопоточность (DeepSeek V4.1 Flash)",
        "model": "opencode/deepseek-v4.1-flash-free",
        "prompt": PROMPT_ALPHA,
        "out_file": os.path.join(OUTPUT_DIR, "AGENT_1_IPC_CONCURRENCY.md"),
    },
    {
        "id": "agent2_spacebunny",
        "name": "Субагент 2: Стресс-сценарии и отказоустойчивость (Space Bunny)",
        "model": "opencode/space-bunny-free",
        "prompt": PROMPT_BETA,
        "out_file": os.path.join(OUTPUT_DIR, "AGENT_2_IPC_STRESS.md"),
    },
    {
        "id": "agent3_longcat",
        "name": "Субагент 3: Дедлок-матрица и производительность (LongCat 2.5 Preview)",
        "model": "opencode/longcat-2.5-preview-free",
        "prompt": PROMPT_GAMMA,
        "out_file": os.path.join(OUTPUT_DIR, "AGENT_3_IPC_DEADLOCK_PERF.md"),
    },
]

LIVE_STATE = {}

def update_live_state(agent_id, status, last_line="", elapsed=0.0):
    LIVE_STATE[agent_id] = {
        "status": status,
        "last_line": last_line[:200],
        "elapsed_seconds": round(elapsed, 1),
        "timestamp": time.time(),
    }
    try:
        with open(STATUS_FILE, "w", encoding="utf-8") as f:
            json.dump(LIVE_STATE, f, ensure_ascii=False, indent=2)
    except Exception:
        pass

def run_agent(agent_meta):
    aid = agent_meta["id"]
    print(f"[*] СТАРТ: {agent_meta['name']} (Модель: {agent_meta['model']})...", flush=True)
    update_live_state(aid, "running", "Инициализация процесса opencode...", 0.0)
    
    start_time = time.time()
    
    # Читаем содержимое целевых файлов и встраиваем прямо в тело задачи
    files_payload = []
    for fpath in TARGET_FILES:
        try:
            with open(fpath, "r", encoding="utf-8") as f:
                code = f.read()
            rel_name = os.path.relpath(fpath, WORKSPACE)
            files_payload.append(f"### ФАЙЛ: {rel_name}\n```csharp\n{code}\n```")
        except Exception as e:
            files_payload.append(f"### ФАЙЛ: {fpath} (Ошибка чтения: {e})")
            
    payload_str = "\n\n".join(files_payload)
    full_prompt = (
        f"{agent_meta['prompt']}\n\n"
        f"ВАЖНО: Исходный код всех файлов предоставлен ниже. Немедленно приступай к глубокому анализу, "
        f"поиску дефектов, проверке дедлоков и гонок. Не задавай уточняющих вопросов, выдай полный исчерпывающий технический отчет прямо сейчас!\n\n"
        f"{payload_str}"
    )
    
    # Запуск OpenCode CLI с передачей промпта через stdin (обход ограничения Windows на 8191 символ в командной строке)
    cmd = ["opencode", "run", "--pure", "--model", agent_meta["model"], "--variant", "high"]
    
    stdout_lines = []
    last_meaningful_line = "Запуск рассуждений..."
    
    try:
        proc = subprocess.Popen(
            cmd,
            cwd=WORKSPACE,
            stdout=subprocess.PIPE,
            stderr=subprocess.STDOUT,
            stdin=subprocess.PIPE,
            text=True,
            bufsize=1,
            encoding="utf-8",
            errors="replace",
            shell=False,
        )
        
        # Отправляем промпт через стандартный ввод и закрываем stdin
        proc.stdin.write(full_prompt)
        proc.stdin.close()
        
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
