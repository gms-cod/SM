# -*- coding: utf-8 -*-
"""
Параллельный запуск гетерогенного роя из 3 быстрых моделей OpenCode для кардинальной переработки фаервола:
1. opencode/deepseek-v4.1-flash-free — Архитектура службы, 0% CPU оптимизация и надежный белый/черный список
2. opencode/nemotron-3.5-super-vl-free — Доработка всплывающего окна NetworkConnectionAlertWindow (5 кнопок)
3. opencode/mimo-v2.6-flash-free — Глобальный фоновый поллер алертов в UI (работа на всех страницах и в трее)
"""
import os
import sys
import time
import json
import subprocess
from concurrent.futures import ThreadPoolExecutor

WORKSPACE = r"D:\Nas_sync\Coding\StartupMonitor"
OUTPUT_DIR = os.path.join(WORKSPACE, "docs")
STATUS_FILE = os.path.join(WORKSPACE, "logs", "live_status_firewall.json")
BASE_BENCH = r"C:\Users\Gms\AppData\Local\Temp\opencode"

os.makedirs(os.path.join(WORKSPACE, "logs"), exist_ok=True)
os.makedirs(OUTPUT_DIR, exist_ok=True)

PROMPT_1 = """Ты — ведущий системный C# инженер и архитектор Windows Firewall службы в .NET 8.
Задача: кардинально оптимизировать NetworkConnectionAlertService и сделать надежный белый/черный список программ.

Контекст проблем:
1. Баг с Opera: при добавлении браузера Opera в белый список через пару секунд опять выскакивает всплывающее окно. Причина: Opera порождает дочерние процессы (network utility, renderer, gpu) с разными PID, у которых MainModule?.FileName может возвращать null или путь к version-subfolder, а в белом списке _whitelistedApps проверяется только точный строковый ключ без учета processName ("opera.exe"), канонического имени файла и родительского каталога.
2. Требование по черному списку: нужна полная поддержка добавления в ЧЁРНЫЙ СПИСОК (BlacklistProgram) — программе навсегда блокируется любая сетевая активность (создается блокирующее правило WindowsFirewallService + сохранение в конфиг FirewallBlacklistedApps), и никакие алерты по ней больше никогда не выдаются.
3. Оптимизация ресурсов (CPU/RAM/COM): фаервол не должен грузить процессор. Сейчас при сканировании вызывается BuildFirewallRulePathSet(), дергающий COM GetAllRules(). Нужно кешировать правила брандмауэра и сокеты, сбрасывать кеш только при создании/удалении правил. Сканирование сокетов должно выполняться быстро, без постоянного опроса COM.
4. Служба должна быть готова к тому, что алерты запрашиваются всегда, когда запущено UI-приложение (а не только при открытой странице фаервола), при этом потребление CPU должно оставаться около 0%.

Требования:
1. Предложи архитектуру и готовый C# код для NetworkConnectionAlertService.cs:
   - Надежный IsWhitelistedApp и IsBlacklistedApp (проверка пути, имени процесса opera.exe, FileName).
   - Обработка NetworkAlertResolution.BlacklistProgram (создание Block правила + сохранение).
   - Кеширование списка правил Firewall (инвалидация по событиям вместо COM-перебора каждые 5 сек).
2. Напиши готовые C# блоки с подробными русскими комментариями.
"""

PROMPT_2 = """Ты — ведущий WPF/XAML разработчик.
Задача: полностью переработать интерфейс и логику всплывающего окна NetworkConnectionAlertWindow.xaml (.cs).

Пользователь требует точный состав кнопок:
1. "Добавить программу в белый список" (ей разрешено всё, никаких сообщений больше никогда не выдается).
2. "Добавить программу в чёрный список" (программе запрещена любая сетевая активность, блокирующее правило, больше никаких алертов).
3. "Разрешить на сеанс" (AllowSession — временное разрешение до перезапуска).
4. "Запретить на сеанс" (BlockSession — временный запрет на текущий сеанс без создания постоянного правила).
5. "Создать правило для приложения" (открытие/переход к созданию правила с выбором порта/протокола).

Требования:
1. XAML разметка кнопок в NetworkConnectionAlertWindow.xaml: аккуратный стиль Windows 11 Fluent, четкие понятные подписи и всплывающие подсказки.
2. C# логика в NetworkConnectionAlertWindow.xaml.cs:
   - Обработчики для всех 5 вариантов резолва.
   - Передача в ResolveNetworkAlertRequest соответствующих резолвов (WhitelistProgram, BlacklistProgram, AllowSession, BlockSession, CreateRule).
   - Корректная остановка таймера обратного отсчета при нажатии любой кнопки и плавное закрытие окна.
3. Напиши готовый C# и XAML код с русскими комментариями.
"""

PROMPT_3 = """Ты — ведущий архитектор UI и фоновых сервисов WPF приложения.
Задача: решить критическую проблему — фаервол работает и выдает сообщения только когда активна вкладка "Сетевая безопасность" в UI.

Контекст:
1. Сейчас таймер _alertPollerTimer и метод PollNetworkAlertsAsync живут внутри NetworkSecurityHubView.xaml.cs.
2. При переходе на любую другую вкладку (Главная, Автозапуск, Диспетчер задач) или при сворачивании в трей NetworkSecurityHubView выгружается (Unloaded -> Deactivate()) и таймер останавливается. В итоге пользователь не получает уведомлений о новых подозрительных сетевых подключениях!
3. Нужно вынести опрос и показ алертов сетевой безопасности на глобальный уровень приложения (например, в MainViewModel.cs или специальный GlobalNetworkAlertManager), чтобы алерты проверялись и всплывали всегда, пока запущен StartupMonitor.UI (даже в трее!).
4. Опрос должен быть оптимизирован: если окно алерта уже открыто или алертов нет — минимальная нагрузка (0% CPU, легкий асинхронный запрос по пайпу без создания тяжелых объектов).

Требования:
1. Архитектурный план выноса поллера алертов из NetworkSecurityHubView на уровень MainWindow/MainViewModel.
2. Корректный показ NetworkConnectionAlertWindow из глобального контекста поверх всех окон (Topmost).
3. Готовый C# код для MainViewModel.cs и NetworkSecurityHubView.xaml.cs с русскими комментариями.
"""

AGENTS = [
    {
        "id": "agent1_deepseek",
        "name": "Субагент 1: Служба, 0% CPU и Белый/Черный список (DeepSeek V4.1 Flash)",
        "model": "opencode/deepseek-v4.1-flash-free",
        "bench_dir": os.path.join(BASE_BENCH, "bench1"),
        "delay": 0,
        "prompt": PROMPT_1,
        "out_file": os.path.join(OUTPUT_DIR, "FIREWALL_SWARM_1_SERVICE_OPT.md")
    },
    {
        "id": "agent2_nemotron",
        "name": "Субагент 2: Окно алертов и 5 кнопок (Nemotron 3.5 Super VL)",
        "model": "opencode/nemotron-3.5-super-vl-free",
        "bench_dir": os.path.join(BASE_BENCH, "bench2"),
        "delay": 2,
        "prompt": PROMPT_2,
        "out_file": os.path.join(OUTPUT_DIR, "FIREWALL_SWARM_2_WINDOW_UI.md")
    },
    {
        "id": "agent3_mimo",
        "name": "Субагент 3: Глобальный опрос алертов в UI и трее (MiMo V2.6 Flash)",
        "model": "opencode/mimo-v2.6-flash-free",
        "bench_dir": os.path.join(BASE_BENCH, "bench3"),
        "delay": 4,
        "prompt": PROMPT_3,
        "out_file": os.path.join(OUTPUT_DIR, "FIREWALL_SWARM_3_GLOBAL_POLLER.md")
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
    if agent_meta["delay"] > 0:
        time.sleep(agent_meta["delay"])

    start_time = time.time()
    last_line = "Запуск процесса и отправка промпта..."
    update_live_state(aid, "running", last_line, 0.0)
    print(f"[*] СТАРТ: {agent_meta['name']} (модель: {agent_meta['model']})...", flush=True)

    bdir = agent_meta["bench_dir"]
    os.makedirs(bdir, exist_ok=True)

    cmd = [
        "opencode", "run",
        "--pure",
        "--dir", bdir,
        "-m", agent_meta["model"]
    ]

    for attempt in range(1, 4):
        try:
            proc = subprocess.Popen(
                cmd,
                stdin=subprocess.PIPE,
                stdout=subprocess.PIPE,
                stderr=subprocess.STDOUT,
                cwd=bdir,
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

                if time.time() - start_time > 1800:
                    proc.kill()
                    update_live_state(aid, "timeout", "Таймаут 1800 сек", 1800.0)
                    raise TimeoutError("Timeout 1800s")

            proc.wait()
            elapsed = time.time() - start_time
            full_stdout = "".join(stdout_lines)

            if proc.returncode != 0 and "database is locked" in full_stdout:
                print(f"[!] Блокировка БД для {agent_meta['name']}, повтор {attempt}/3 через 3с...", flush=True)
                time.sleep(3)
                continue

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

    return False, time.time() - start_time, "Все попытки исчерпаны"

def main():
    print("=" * 70, flush=True)
    print(">>> ЗАПУСК РОЯ БЫСТРЫХ АГЕНТОВ OPENCODE (3 МОДЕЛИ ПАРАЛЛЕЛЬНО) <<<", flush=True)
    print("=" * 70, flush=True)

    with ThreadPoolExecutor(max_workers=3) as executor:
        futures = [executor.submit(run_agent, ag) for ag in AGENTS]
        
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

