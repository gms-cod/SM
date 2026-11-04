# -*- coding: utf-8 -*-
"""
Тестовый многопоточный раннер роя OpenCode с живым стримингом статуса.
Запускает ровно 3 РАЗНЫХ субагента параллельно через ThreadPoolExecutor:
  1. Nemotron 3.5 Super VL (opencode/nemotron-3.5-super-vl-free)
  2. DeepSeek V4.1 Flash (opencode/deepseek-v4.1-flash-free)
  3. LongCat 2.5 Preview (opencode/longcat-2.5-preview-free)
Обновляет файл текущего прогресса logs/live_status_test.json в реальном времени.
"""
import os
import sys
import time
import json
import subprocess
from concurrent.futures import ThreadPoolExecutor

WORKSPACE = r"D:\Nas\Coding\StartupMonitor"
OUTPUT_DIR = os.path.join(WORKSPACE, "logs")
STATUS_FILE = os.path.join(OUTPUT_DIR, "live_status_test.json")
BENCH_DIR = r"C:\Users\Gms\AppData\Local\Temp\opencode\bench"

os.makedirs(OUTPUT_DIR, exist_ok=True)
os.makedirs(BENCH_DIR, exist_ok=True)

PROMPT_1 = "Ты — эксперт по архитектуре C#. Назови 3 главных правила чистого асинхронного кода в .NET 8. Ответь кратко по пунктам на русском языке."
PROMPT_2 = "Ты — эксперт по многопоточности. В чем разница между lock и SemaphoreSlim в C#? Ответь кратко на русском языке."
PROMPT_3 = "Ты — эксперт по оптимизации памяти. Назови 3 способа избежать аллокаций в Large Object Heap (LOH) в .NET 8. Ответь кратко на русском языке."

AGENTS = [
    {
        "id": "agent1_nemotron",
        "name": "Субагент 1: Архитектура (Nemotron 3.5 Super VL)",
        "model": "opencode/nemotron-3.5-super-vl-free",
        "prompt": PROMPT_1,
        "out_file": os.path.join(OUTPUT_DIR, "TEST_AGENT_1_NEMOTRON.md")
    },
    {
        "id": "agent2_deepseek",
        "name": "Субагент 2: Логика и многопоточность (DeepSeek V4.1 Flash)",
        "model": "opencode/deepseek-v4.1-flash-free",
        "prompt": PROMPT_2,
        "out_file": os.path.join(OUTPUT_DIR, "TEST_AGENT_2_DEEPSEEK.md")
    },
    {
        "id": "agent3_longcat",
        "name": "Субагент 3: Оптимизация памяти (LongCat 2.5 Preview)",
        "model": "opencode/longcat-2.5-preview-free",
        "prompt": PROMPT_3,
        "out_file": os.path.join(OUTPUT_DIR, "TEST_AGENT_3_LONGCAT.md")
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

        update_live_state(aid, "done" if success else "failed", last_line, elapsed)
        print(f"[+] ФИНИШ: {agent_meta['name']} за {elapsed:.1f}с (код: {proc.returncode})", flush=True)

        header = f"# {agent_meta['name']}\n\n- **Модель:** `{agent_meta['model']}`\n- **Время ответа:** {elapsed:.1f} сек\n\n---\n\n"
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
    print("=== ЗАПУСК ПАРАЛЛЕЛЬНОГО РОЯ: 3 РАЗНЫХ СУБАГЕНТА OPENCODE      ===", flush=True)
    print("==================================================================", flush=True)
    start_total = time.time()

    with ThreadPoolExecutor(max_workers=3) as executor:
        futures = [executor.submit(run_agent, a) for a in AGENTS]
        results = [f.result() for f in futures]

    total_sec = time.time() - start_total
    print("==================================================================", flush=True)
    print(f"=== ВСЕ 3 СУБАГЕНТА ЗАВЕРШИЛИ РАБОТУ ЗА {total_sec:.1f} СЕК ===", flush=True)
    print("==================================================================", flush=True)
    for aid, ok, el in results:
        print(f"  - {aid}: {'УСПЕХ' if ok else 'СБОЙ'} ({el:.1f} с)", flush=True)

if __name__ == "__main__":
    main()

