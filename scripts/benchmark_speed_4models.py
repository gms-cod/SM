# -*- coding: utf-8 -*-
"""
Параллельный бенчмарк скорости 4 моделей OpenCode:
1. opencode/space-bunny-free
2. opencode/muse-spark-1.3-contributor-free
3. opencode/beam-free
4. opencode/glm-5.3-flash-free
"""
import os
import sys
import time
import json
import subprocess
from concurrent.futures import ThreadPoolExecutor

WORKSPACE = r"D:\Nas\Coding\StartupMonitor"
OUTPUT_DIR = os.path.join(WORKSPACE, "logs")
STATUS_FILE = os.path.join(OUTPUT_DIR, "live_speed_test.json")
BASE_TEMP = r"C:\Users\Gms\AppData\Local\Temp\opencode"

os.makedirs(OUTPUT_DIR, exist_ok=True)
os.makedirs(BASE_TEMP, exist_ok=True)

PROMPT = """Ты — ведущий эксперт по многопоточности в .NET 8.
Напиши компактный, высокопроизводительный класс FastCache<TKey, TValue> на C# со следующими требованиями:
1. Потокобезопасность без глобальных блокировок (ConcurrentDictionary + SemaphoreSlim или Lazy).
2. Поддержка скользящего окна устаревания (Sliding Expiration TTL).
3. Метод GetOrCreateAsync(TKey key, TimeSpan ttl, Func<TKey, Task<TValue>> factory) с защитой от повторных одновременных вызовов фабрики (Single-Flight).
4. Краткие подробные комментарии на русском языке.
"""

MODELS = [
    {
        "id": "model1_space_bunny",
        "name": "Space Bunny",
        "model": "opencode/space-bunny-free",
        "role": "Сборка крупных модулей и документации",
        "dir": os.path.join(BASE_TEMP, "bench_speed_bunny"),
        "out_file": os.path.join(OUTPUT_DIR, "SPEED_TEST_1_SPACE_BUNNY.md")
    },
    {
        "id": "model2_muse_spark",
        "name": "Muse Spark 1.3 Contributor",
        "model": "opencode/muse-spark-1.3-contributor-free",
        "role": "Генерация бойлерплейта и тестов (1M)",
        "dir": os.path.join(BASE_TEMP, "bench_speed_muse"),
        "out_file": os.path.join(OUTPUT_DIR, "SPEED_TEST_2_MUSE_SPARK.md")
    },
    {
        "id": "model3_beam",
        "name": "Beam",
        "model": "opencode/beam-free",
        "role": "Экспресс-анализ функций и рефакторинг",
        "dir": os.path.join(BASE_TEMP, "bench_speed_beam"),
        "out_file": os.path.join(OUTPUT_DIR, "SPEED_TEST_3_BEAM.md")
    },
    {
        "id": "model4_glm",
        "name": "GLM 5.3 Flash",
        "model": "opencode/glm-5.3-flash-free",
        "role": "Глубокий сквозной аудит (1M)",
        "dir": os.path.join(BASE_TEMP, "bench_speed_glm"),
        "out_file": os.path.join(OUTPUT_DIR, "SPEED_TEST_4_GLM.md")
    }
]

for m in MODELS:
    os.makedirs(m["dir"], exist_ok=True)

LIVE_STATE = {}

def update_live_state(agent_id, status, last_line="", elapsed=0.0, output_len=0):
    LIVE_STATE[agent_id] = {
        "status": status,
        "last_line": last_line[:200],
        "elapsed_seconds": round(elapsed, 2),
        "output_chars": output_len,
        "timestamp": time.time()
    }
    tmp_file = STATUS_FILE + ".tmp"
    try:
        with open(tmp_file, "w", encoding="utf-8") as f:
            json.dump(LIVE_STATE, f, ensure_ascii=False, indent=2)
        os.replace(tmp_file, STATUS_FILE)
    except Exception:
        pass

def run_benchmark(meta):
    aid = meta["id"]
    start_time = time.time()
    update_live_state(aid, "running", "Старт процесса opencode...", 0.0, 0)
    print(f"[*] СТАРТ: {meta['name']} ({meta['model']})...", flush=True)

    cmd = [
        "opencode", "run",
        "--pure",
        "--dir", meta["dir"],
        "-m", meta["model"]
    ]

    try:
        proc = subprocess.Popen(
            cmd,
            stdin=subprocess.PIPE,
            stdout=subprocess.PIPE,
            stderr=subprocess.STDOUT,
            cwd=meta["dir"],
            text=True,
            encoding="utf-8",
            errors="replace",
            bufsize=1
        )

        proc.stdin.write(PROMPT)
        proc.stdin.close()

        stdout_lines = []
        last_line = "Ожидание генерации..."
        while True:
            line = proc.stdout.readline()
            if not line and proc.poll() is not None:
                break
            if line:
                stdout_lines.append(line)
                stripped = line.strip()
                if stripped and len(stripped) > 2:
                    last_line = stripped
                    update_live_state(aid, "running", last_line, time.time() - start_time, len("".join(stdout_lines)))

            if time.time() - start_time > 1800:
                proc.kill()
                update_live_state(aid, "timeout", "Таймаут 1800с", 1800.0, len("".join(stdout_lines)))
                raise TimeoutError("Timeout 1800s")

        proc.wait()
        elapsed = time.time() - start_time
        full_stdout = "".join(stdout_lines)
        success = (proc.returncode == 0)

        with open(meta["out_file"], "w", encoding="utf-8") as out:
            out.write(f"# Результат бенчмарка скорости: {meta['name']}\n")
            out.write(f"- Модель: {meta['model']}\n")
            out.write(f"- Специализация: {meta['role']}\n")
            out.write(f"- Время генерации: {elapsed:.2f} сек\n")
            out.write(f"- Объем ответа: {len(full_stdout)} символов\n")
            out.write(f"- Код завершения: {proc.returncode}\n\n")
            out.write(full_stdout)

        status_str = "completed" if success else "failed"
        update_live_state(aid, status_str, f"Готово за {elapsed:.2f}с ({len(full_stdout)} симв.)", elapsed, len(full_stdout))
        print(f"[+] ЗАВЕРШЕНО: {meta['name']} за {elapsed:.2f}с ({len(full_stdout)} симв.)", flush=True)
        return {
            "name": meta["name"],
            "model": meta["model"],
            "role": meta["role"],
            "elapsed": elapsed,
            "length": len(full_stdout),
            "success": success
        }

    except Exception as e:
        elapsed = time.time() - start_time
        update_live_state(aid, "error", str(e), elapsed, 0)
        print(f"[-] ОШИБКА: {meta['name']}: {e}", flush=True)
        return {
            "name": meta["name"],
            "model": meta["model"],
            "role": meta["role"],
            "elapsed": elapsed,
            "length": 0,
            "success": False,
            "error": str(e)
        }

def main():
    print("=" * 80, flush=True)
    print(">>> ПАРАЛЛЕЛЬНЫЙ БЕНЧМАРК СКОРОСТИ 4 МОДЕЛЕЙ OPENCODE <<<", flush=True)
    print("=" * 80, flush=True)

    with ThreadPoolExecutor(max_workers=4) as executor:
        futures = [executor.submit(run_benchmark, m) for m in MODELS]

        while any(not f.done() for f in futures):
            time.sleep(5)
            if os.path.exists(STATUS_FILE):
                try:
                    with open(STATUS_FILE, "r", encoding="utf-8") as f:
                        data = json.load(f)
                    print("\n--- [СТРИМИНГ СТАТУСА БЕНЧМАРКА СКОРОСТИ] ---", flush=True)
                    for aid, st in data.items():
                        print(f"[{aid}] {st['status'].upper()} ({st['elapsed_seconds']}с, {st['output_chars']} симв): {st['last_line'][:80]}", flush=True)
                    print("---------------------------------------------", flush=True)
                except Exception:
                    pass

        results = [f.result() for f in futures]

    print("\n" + "=" * 80, flush=True)
    print(">>> СВОДНАЯ ТАБЛИЦА СКОРОСТИ ГЕНЕРАЦИИ <<<", flush=True)
    print("=" * 80, flush=True)
    results_sorted = sorted(results, key=lambda x: x["elapsed"] if x["success"] else 99999)
    for i, r in enumerate(results_sorted, 1):
        status_icon = "✓" if r["success"] else "✗"
        print(f"{i}. [{status_icon}] {r['name']} ({r['model']}): {r['elapsed']:.2f} сек | {r['length']} символов | {r['role']}", flush=True)

    # Сохранение сводки в JSON
    summary_file = os.path.join(OUTPUT_DIR, "speed_benchmark_summary.json")
    with open(summary_file, "w", encoding="utf-8") as f:
        json.dump(results_sorted, f, ensure_ascii=False, indent=2)

if __name__ == "__main__":
    main()

