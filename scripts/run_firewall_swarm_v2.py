# -*- coding: utf-8 -*-
"""
Параллельный запуск роя из 3 моделей OpenCode для модернизации Firewall:
1. opencode/deepseek-v4.1-flash-free — Архитектура службы, двойной матчинг, 0% CPU и черный список
2. opencode/nemotron-3.5-super-vl-free — Редизайн страницы Firewall: выделение активных кнопок цветами, читаемость
3. opencode/beam-free — Окно алертов (5 кнопок), глобальный поллер в MainViewModel и ненавязчивый UX
"""
import os
import sys
import time
import json
import subprocess
from concurrent.futures import ThreadPoolExecutor

WORKSPACE = r"D:\Nas\Coding\StartupMonitor"
OUTPUT_DIR = os.path.join(WORKSPACE, "logs")
STATUS_FILE = os.path.join(OUTPUT_DIR, "live_status_firewall_v2.json")
BASE_TEMP = r"C:\Users\Gms\AppData\Local\Temp\opencode"

os.makedirs(OUTPUT_DIR, exist_ok=True)
os.makedirs(BASE_TEMP, exist_ok=True)

PROMPT_1 = """Ты — ведущий C# системный инженер и архитектор Windows Firewall службы.
Задача: разработать архитектуру и чистый код для NetworkConnectionAlertService.cs и AppConfig.cs со следующими требованиями:
1. Автономность сканера: служба должна работать всегда, когда AlertsEnabled = true (hasWork: () => AlertsEnabled), не засыпая при сворачивании UI или смене вкладок.
2. Двойной ключ сопоставления (Path + ProcessName): при добавлении в _whitelistedApps и _blacklistedApps сохранять полный путь и имя файла (opera.exe). Решить проблему дочерних процессов браузеров (Opera, Chrome), где путь может быть подпапкой версии или null.
3. Мгновенная очистка очередей: при резолве приложения немедленно удалять все ожидающие алерты по этому приложению из _pendingById и _queue, чтобы окно не всплывало повторно.
4. Полноценный Чёрный список (BlacklistProgram): создание правила блокировки через WindowsFirewallService + сохранение в конфиг FirewallBlacklistedApps + мгновенная блокировка.
5. Оптимизация 0% CPU: кэшировать список путей правил брандмауэра _firewallRulePathsCache (TTL 60 сек), инвалидировать только при создании/удалении правил, исключив тяжелый COM-перебор каждые 5 сек.
Напиши готовые C# блоки с подробными русскими комментариями.
"""

PROMPT_2 = """Ты — ведущий WPF/XAML дизайнер и UI/UX специалист.
Задача: модернизировать разметку и стили страницы NetworkSecurityHubView.xaml (.cs) для максимальной читаемости и удобства:
1. Выделение кнопок цветом в общем стиле программы (акцентные кисти ApplicationBackgroundBrush, SystemFillColorCriticalBrush, AccentTextFillColorPrimaryBrush и др.):
   - Кнопка создания правила, кнопка блокировки, кнопка пресетов, кнопка анти-телеметрии.
   - Активная кнопка/фильтр (Live, Rules, Port Defender, Presets) должна четко и наглядно выделяться активным фоном/рамкой в едином Fluent-стиле темы Dark/Light.
2. Улучшение читаемости и визуальной эргономики:
   - Четкие бейджи статуса брандмауэра (Зеленый - включен, Красный - выключен).
   - Красивые контрастные бейджи профилей сети (Домен, Частная, Общая).
   - Читаемые колонки в таблице соединений и правил.
Напиши готовые блоки XAML и C# с подробными русскими комментариями.
"""

PROMPT_3 = """Ты — ведущий эксперт по ненавязчивому взаимодействию с пользователем (Non-Intrusive UX) и WPF архитектуре.
Задача:
1. Переработать диалог NetworkConnectionAlertWindow.xaml (.cs):
   - 5 четких, интуитивных кнопок:
     1. [+] Добавить программу в белый список (навсегда, 0 алертов)
     2. [-] Добавить программу в чёрный список (навсегда, 0 алертов)
     3. [~] Разрешить на сеанс (AllowSession)
     4. [x] Запретить на сеанс (BlockSession)
     5. [*] Создать правило для приложения (CreateRule)
   - Ненавязчивый UX: окно не должно перехватывать фокус грубо во время набора текста, таймер обратного отсчета с опцией паузы при наведении мыши, аккуратный тайм-аут по умолчанию (например 30 сек).
2. Перенести опрос алертов в глобальный MainViewModel.cs:
   - Таймер опроса работает непрерывно (раз в 1.5–2 сек), пока приложение активно или свернуто в трей.
   - Алерты всплывают на любой вкладке и при свернутом окне.
Напиши готовые блоки XAML и C# с подробными русскими комментариями.
"""

AGENTS = [
    {
        "id": "agent1_deepseek",
        "name": "Субагент 1: Служба и черный список (DeepSeek V4.1 Flash)",
        "model": "opencode/deepseek-v4.1-flash-free",
        "prompt": PROMPT_1,
        "dir": os.path.join(BASE_TEMP, "bench_fw2_deepseek"),
        "out_file": os.path.join(OUTPUT_DIR, "FIREWALL_V2_AGENT_1_DEEPSEEK.md")
    },
    {
        "id": "agent2_nemotron",
        "name": "Субагент 2: UI страницы Firewall и стили кнопок (Nemotron 3.5 Super VL)",
        "model": "opencode/nemotron-3.5-super-vl-free",
        "prompt": PROMPT_2,
        "dir": os.path.join(BASE_TEMP, "bench_fw2_nemotron"),
        "out_file": os.path.join(OUTPUT_DIR, "FIREWALL_V2_AGENT_2_NEMOTRON.md")
    },
    {
        "id": "agent3_beam",
        "name": "Субагент 3: Ненавязчивые алерты и поллер (Beam)",
        "model": "opencode/beam-free",
        "prompt": PROMPT_3,
        "dir": os.path.join(BASE_TEMP, "bench_fw2_beam"),
        "out_file": os.path.join(OUTPUT_DIR, "FIREWALL_V2_AGENT_3_BEAM.md")
    }
]

for a in AGENTS:
    os.makedirs(a["dir"], exist_ok=True)

LIVE_STATE = {}

def update_live_state(agent_id, status, last_line="", elapsed=0.0):
    LIVE_STATE[agent_id] = {
        "status": status,
        "last_line": last_line[:250],
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

def run_agent(meta):
    aid = meta["id"]
    start_time = time.time()
    update_live_state(aid, "running", "Старт процесса...", 0.0)
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

        proc.stdin.write(meta["prompt"])
        proc.stdin.close()

        stdout_lines = []
        last_line = "Генерация ответа..."
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
                update_live_state(aid, "timeout", "Таймаут 3600с", 3600.0)
                raise TimeoutError("Timeout 3600s")

        proc.wait()
        elapsed = time.time() - start_time
        full_stdout = "".join(stdout_lines)
        success = (proc.returncode == 0)

        with open(meta["out_file"], "w", encoding="utf-8") as out:
            out.write(f"# Результат работы: {meta['name']}\n")
            out.write(f"- Модель: {meta['model']}\n")
            out.write(f"- Время выполнения: {elapsed:.2f} сек\n")
            out.write(f"- Код завершения: {proc.returncode}\n\n")
            out.write(full_stdout)

        status_str = "completed" if success else "failed"
        update_live_state(aid, status_str, f"Готово за {elapsed:.2f}с (rc={proc.returncode})", elapsed)
        print(f"[+] ЗАВЕРШЕНО: {meta['name']} за {elapsed:.2f}с (rc={proc.returncode})", flush=True)
        return success, elapsed, full_stdout

    except Exception as e:
        elapsed = time.time() - start_time
        update_live_state(aid, "error", str(e), elapsed)
        print(f"[-] ОШИБКА: {meta['name']}: {e}", flush=True)
        return False, elapsed, str(e)

def main():
    print("=" * 80, flush=True)
    print(">>> ЗАПУСК РОЯ OPENCODE V2: МОДЕРНИЗАЦИЯ FIREWALL & UI <<<", flush=True)
    print("=" * 80, flush=True)

    with ThreadPoolExecutor(max_workers=3) as executor:
        futures = [executor.submit(run_agent, a) for a in AGENTS]

        # Опрос статуса и вывод статистики раз в 10-15 сек (в консоль каждые 5 минут развернуто)
        last_5min_log = time.time()
        while any(not f.done() for f in futures):
            time.sleep(10)
            now = time.time()
            if os.path.exists(STATUS_FILE):
                try:
                    with open(STATUS_FILE, "r", encoding="utf-8") as f:
                        data = json.load(f)
                    
                    is_5min_tick = (now - last_5min_log >= 300)
                    if is_5min_tick:
                        last_5min_log = now
                        print("\n" + "=" * 80, flush=True)
                        print(f">>> [СТАТИСТИКА РОЯ (5 МИН)]: {time.strftime('%H:%M:%S')} <<<", flush=True)
                        for aid, st in data.items():
                            print(f"[{aid}] {st['status'].upper()} ({st['elapsed_seconds']}с): {st['last_line'][:120]}", flush=True)
                        print("=" * 80 + "\n", flush=True)
                    else:
                        print(".", end="", flush=True)
                except Exception:
                    pass

        results = [f.result() for f in futures]

    print("\n" + "=" * 80, flush=True)
    print(">>> ВСЕ 3 СУБАГЕНТА РОЯ УСПЕШНО ЗАВЕРШИЛИ РАБОТУ <<<", flush=True)
    print("=" * 80, flush=True)

if __name__ == "__main__":
    main()

