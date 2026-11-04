# -*- coding: utf-8 -*-
"""
Универсальный многопоточный оркестратор гетерогенного роя субагентов OpenCode.
Соответствует требованиям AI-Core Manifest 3.0:
  - Одновременный параллельный запуск тройки разных моделей (0$ Cost)
  - Изолированное рабочее окружение (--dir, --pure)
  - Непрерывный живой стриминг активности и последних мыслей в JSON-файл
  - Гарантированный тайм-аут без зависаний процессов
"""
import os
import sys
import time
import json
import subprocess
from concurrent.futures import ThreadPoolExecutor

WORKSPACE = r"D:\Nas\Coding\StartupMonitor"
OUTPUT_DIR = os.path.join(WORKSPACE, "logs")
BENCH_DIR = r"C:\Users\Gms\AppData\Local\Temp\opencode\bench"
STATUS_FILE = os.path.join(OUTPUT_DIR, "live_status.json")

os.makedirs(OUTPUT_DIR, exist_ok=True)
os.makedirs(BENCH_DIR, exist_ok=True)

class OpenCodeSwarm:
    def __init__(self, agents, status_file=STATUS_FILE, timeout=3600):
        """
        Инициализация параметров роя.
        :param agents: Список словарей конфигураций субагентов
        :param status_file: Путь к JSON-файлу живого стриминга
        :param timeout: Максимальное время работы субагента в секундах
        """
        self.agents = agents
        self.status_file = status_file
        self.timeout = timeout
        self.live_state = {}

    def _update_state(self, agent_id, status, last_line="", elapsed=0.0):
        """Атомарно обновляет JSON-файл живого статуса."""
        self.live_state[agent_id] = {
            "status": status,
            "last_line": last_line[:400],
            "elapsed_seconds": round(elapsed, 1),
            "timestamp": time.time()
        }
        tmp_file = self.status_file + ".tmp"
        try:
            with open(tmp_file, "w", encoding="utf-8") as f:
                json.dump(self.live_state, f, ensure_ascii=False, indent=2)
            os.replace(tmp_file, self.status_file)
        except Exception:
            pass

    def _run_single_agent(self, agent_meta):
        """Выполняет запуск одного субагента в изолированном процессе."""
        aid = agent_meta["id"]
        start_time = time.time()
        last_line = "Инициализация процесса субагента..."
        self._update_state(aid, "running", last_line, 0.0)

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

            # Передаем полный промпт через stdin и закрываем поток ввода
            proc.stdin.write(agent_meta["prompt"])
            proc.stdin.close()

            stdout_lines = []
            while True:
                line = proc.stdout.readline()
                if not line and proc.poll() is not None:
                    break
                if line:
                    stdout_lines.append(line)
                    clean_line = line.strip()
                    if clean_line and len(clean_line) > 2:
                        last_line = clean_line
                        self._update_state(aid, "running", last_line, time.time() - start_time)

                if time.time() - start_time > self.timeout:
                    proc.kill()
                    self._update_state(aid, "timeout", f"Превышен лимит времени {self.timeout}с", self.timeout)
                    raise TimeoutError(f"Субагент превысил таймаут {self.timeout} секунд")

            proc.wait()
            elapsed = time.time() - start_time
            full_stdout = "".join(stdout_lines)
            success = (proc.returncode == 0)

            self._update_state(aid, "done" if success else "failed", last_line, elapsed)

            # Сохраняем итоговый markdown-отчет
            if "out_file" in agent_meta:
                header = (
                    f"# {agent_meta.get('name', aid)}\n\n"
                    f"- **Модель:** `{agent_meta['model']}`\n"
                    f"- **Время выполнения:** {elapsed:.1f} сек\n"
                    f"- **Статус:** {'Успешно' if success else f'Сбой (код {proc.returncode})'}\n\n---\n\n"
                )
                with open(agent_meta["out_file"], "w", encoding="utf-8") as f:
                    f.write(header + full_stdout)

            return aid, success, elapsed
        except Exception as exc:
            elapsed = time.time() - start_time
            self._update_state(aid, "error", str(exc), elapsed)
            return aid, False, elapsed

    def execute_swarm(self):
        """Запускает всех субагентов строго одновременно в пуле потоков."""
        worker_count = max(len(self.agents), 3)
        start_total = time.time()

        with ThreadPoolExecutor(max_workers=worker_count) as executor:
            futures = [executor.submit(self._run_single_agent, a) for a in self.agents]
            results = [f.result() for f in futures]

        total_elapsed = time.time() - start_total
        return results, total_elapsed

if __name__ == "__main__":
    print("[*] OpenCode Swarm Runner готов к работе.")

