# -*- coding: utf-8 -*-
"""
Запуск гетерогенного роя субагентов профиля TRIAD-REVIEW по редизайну и эргономике Сетевого экрана.
Анализ: глобальный аудит всех функций, организация истории подключений, белых/черных списков,
графиков сетевой активности, сохранение палитры Fluent Design/Neon и выработка 5 детальных примеров.
Соответствует регламенту AI-Core Manifest 3.0:
- 3 независимых движка с ортогональными ролями
- Изоляция песочниц (mkdtemp, копирование auth.json, подмена USERPROFILE/HOME)
- STDIN передача промпта
- Атомарный JSON-мониторинг logs/live_status.json
"""

import os
import sys
import time
import json
import shutil
import tempfile
import subprocess
from concurrent.futures import ThreadPoolExecutor

# Принудительная инициализация кодировки UTF-8 в Windows
if sys.platform == "win32":
    try:
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")
        sys.stderr.reconfigure(encoding="utf-8", errors="replace")
    except Exception:
        pass

WORKSPACE = r"D:\Nas\Coding\StartupMonitor"
LOGS_DIR = os.path.join(WORKSPACE, "logs")
STATUS_FILE = os.path.join(LOGS_DIR, "live_status.json")
GLOBAL_CONFIG = r"C:\Users\Gms\.config\opencode"
GLOBAL_AUTH_DIR = r"C:\Users\Gms\.local\share\opencode"

os.makedirs(LOGS_DIR, exist_ok=True)

PROMPT_AGENT_1 = """Ты — ведущий UI/UX архитектор десктопных систем безопасности (Windows 11 Fluent Design).
Задача: Провести глобальный аудит сетевого экрана StartupMonitor (NetworkSecurityHubView) и разработать кардинальные улучшения эргономики и архитектуры интерфейса.

Файлы для анализа:
- src/StartupMonitor.UI/Views/NetworkSecurityHubView.xaml (.cs)
- src/StartupMonitor.UI/Views/Dialogs/ConnectionHistoryWindow.xaml (.cs)
- src/StartupMonitor.UI/Views/Dialogs/NetworkConnectionAlertWindow.xaml (.cs)

Ключевые ограничения:
1. Цветовая гамма строго сохраняется: Fluent Design Windows 11 (ApplicationBackgroundBrush, CardBackground, CardStroke), неоновые акценты (NeonCyan #00D2D3, NeonEmerald #10B981, NeonCrimson #EF4444, NeonAmber #F59E0B, DarkEmerald #122620, DarkCrimson #2A1215).
2. Анализ лучших практик: GlassWire (таймлайн, группировка по приложениям и хостам), Little Snitch / LuLu (матрица правил, временные правила), Portmaster (app-centric вид, индикация входящего/исходящего), Simplewall / Fort Firewall (компактность, быстрые фильтры).

Что необходимо сделать:
1. Глобальный структурный анализ текущих компонентов и проблем юзабилити (перегруженность шапки, 7 разрозненных кнопок-вкладок, дублирование функционала между Live, История, Правила).
2. Архитектура новой навигации: как скомпоновать экран, чтобы пользователь не путался.
3. Разработать подробные описания и структуру для 5 вариантов компоновки страницы (с акцентом на Master-Detail, App-Centric, Dashboard с KPI карточками, Timeline Stream).
4. Выдай подробный отчет с готовой XAML структурой блоков.
"""

PROMPT_AGENT_2 = """Ты — ведущий специалист по визуализации данных и телеметрии сетевой безопасности в WPF.
Задача: Полностью переосмыслить вкладку «История подключений» и интеграцию с «Живыми соединениями» (Live Sockets).

Файлы для анализа:
- src/StartupMonitor.UI/Views/NetworkSecurityHubView.xaml (вкладка Live и вкладка История)
- src/StartupMonitor.UI/Views/Dialogs/ConnectionHistoryWindow.xaml (.cs)
- src/StartupMonitor.Shared/Models/Firewall/NetworkConnectionHistoryItem.cs
- src/StartupMonitor.Service/Services/Firewall/NetworkConnectionAlertService.cs

Проблема:
Сейчас «Активные соединения» живут на вкладке 0, а «История» — на вкладке 6 (а также в отдельном окне). Пользователю неудобно переключаться между текущими сокетами и недавними событиями, нет понимания динамики трафика и всплесков подозрительной активности.

Что необходимо сделать:
1. Предложи идеальную концепцию организации истории подключений:
   - Объединение Live и History или интеллектуальный сплит-режим (Live Stream + Historic Log).
   - Временные диапазоны и группировки: по процессам (дерево/аккордеон), по удаленным хостам/доменам, по странам.
   - Визуализация: добавление интерактивного графика сетевой активности (sparkline / гистограмма событий за 1ч, 24ч, 7 дней) без тяжелых сторонних библиотек (нативный WPF Canvas / Polyline / Shape или легковесный микро-чарт).
   - Быстрые фильтры (Pill-кнопки): «Все», «Заблокированные», «Алерты», «Внешний WAN», «Внутренний LAN».
   - Контекстные действия над записями: создание правил в 1 клик, Whois, VirusTotal, копирование IP/URL, добавление в белый/черный список.
2. Проработай структуру интеграции графиков в цветовую палитру проекта (неоновые всплески на темном/светлом фоне).
3. Выдай детальные XAML и C# примеры реализации контролов истории и графиков.
"""

PROMPT_AGENT_3 = """Ты — ведущий системный проектировщик политик безопасности и Access Control List (ACL).
Задача: Разработать совершенную модель организации Белых и Чёрных списков программ и их связи с Windows Firewall в StartupMonitor.

Файлы для анализа:
- src/StartupMonitor.UI/Views/NetworkSecurityHubView.xaml (вкладки Белый список, Чёрный список, Правила)
- src/StartupMonitor.Service/Services/Firewall/NetworkConnectionAlertService.cs
- src/StartupMonitor.Service/Services/Firewall/WindowsFirewallService.cs

Проблема:
Сейчас Белый список (вкладка 1) и Чёрный список (вкладка 2) разнесены по разным вкладкам, а Правила брандмауэра Windows — на вкладке 3. Пользователю приходится прыгать между тремя экранами, чтобы понять статус одного приложения. Кроме того, нет визуального отражения временных правил (сессионных разрешений).

Что необходимо сделать:
1. Предложи идеальную концепцию управления списками:
   - Вариант А: Единая матрица контроля доступа («Матрица приложений / Access Control Matrix») с трехпозиционными переключателями состояния для каждого процесса: [🟢 Разрешено (Белый)] / [🟡 По запросу (Алерты)] / [🔴 Заблокировано (Чёрный)].
   - Вариант Б: Умные раздельные списки с быстрой фильтрацией, счетчиками бейджей и возможностью переноса drag-and-drop или одним кликом (Promote/Demote).
2. Защита от ошибок пользователя: системные процессы Windows (svchost, ntoskrnl) и критические службы — индикация системных тегов, защита от случайной блокировки жизненно важных компонентов.
3. Интеграция с правилами Windows Firewall: как показать, откуда взялся запрет (из локального черного списка StartupMonitor или из корпоративного/системного правила Windows Firewall).
4. Выдай подробный XAML код и спецификацию для 5 дизайн-вариантов матрицы правил.
"""

AGENTS = [
    {
        "id": "Agent-1-UX-Architecture",
        "role": "Глобальный UI/UX аудит, эргономика и 5 дизайн-концепций",
        "model": "opencode/deepseek-v4.1-flash-free",
        "fallback_model": "opencode/beam-free",
        "prompt": PROMPT_AGENT_1
    },
    {
        "id": "Agent-2-History-Timeline",
        "role": "Организация истории, таймлайна и графиков сетевой активности",
        "model": "opencode/beam-free",
        "fallback_model": "opencode/glm-5.3-flash-free",
        "prompt": PROMPT_AGENT_2
    },
    {
        "id": "Agent-3-Access-Control",
        "role": "Организация Белых/Черных списков и матрицы политик брандмауэра",
        "model": "opencode/nemotron-3.5-super-vl-free",
        "fallback_model": "openrouter/nvidia/nemotron-3.5-super-vl:free",
        "prompt": PROMPT_AGENT_3
    }
]

class SwarmRunner:
    def __init__(self, agents):
        self.agents = agents
        self.live_state = {}
        self.start_times = {}

    def _update_status(self, aid, task, status, last_line="", elapsed=0.0):
        self.live_state[aid] = {
            "agent_id": aid,
            "task": task,
            "status": status,
            "last_line": last_line[:300],
            "elapsed_seconds": round(elapsed, 1),
            "timestamp": time.time()
        }
        tmp = STATUS_FILE + ".tmp"
        try:
            with open(tmp, "w", encoding="utf-8") as f:
                json.dump(self.live_state, f, ensure_ascii=False, indent=2)
            os.replace(tmp, STATUS_FILE)
        except Exception:
            pass

    def _setup_sandbox(self, aid):
        sandbox_dir = tempfile.mkdtemp(prefix=f"opencode_{aid}_")
        user_config = os.path.join(sandbox_dir, ".config", "opencode")
        user_auth = os.path.join(sandbox_dir, ".local", "share", "opencode")
        os.makedirs(user_config, exist_ok=True)
        os.makedirs(user_auth, exist_ok=True)

        if os.path.exists(GLOBAL_CONFIG):
            for item in os.listdir(GLOBAL_CONFIG):
                s = os.path.join(GLOBAL_CONFIG, item)
                d = os.path.join(user_config, item)
                if os.path.isfile(s):
                    shutil.copy2(s, d)
                elif os.path.isdir(s) and item != "node_modules":
                    shutil.copytree(s, d, dirs_exist_ok=True)

        if os.path.exists(GLOBAL_AUTH_DIR):
            for item in os.listdir(GLOBAL_AUTH_DIR):
                s = os.path.join(GLOBAL_AUTH_DIR, item)
                d = os.path.join(user_auth, item)
                if os.path.isfile(s):
                    shutil.copy2(s, d)

        env = os.environ.copy()
        env["USERPROFILE"] = sandbox_dir
        env["HOME"] = sandbox_dir
        env["LOCALAPPDATA"] = os.path.join(sandbox_dir, "AppData", "Local")
        env["APPDATA"] = os.path.join(sandbox_dir, "AppData", "Roaming")
        os.makedirs(env["LOCALAPPDATA"], exist_ok=True)
        os.makedirs(env["APPDATA"], exist_ok=True)
        return sandbox_dir, env

    def _run_agent(self, agent):
        aid = agent["id"]
        role = agent["role"]
        model = agent["model"]
        fallback = agent.get("fallback_model")
        prompt = agent["prompt"]

        sandbox_dir, env = self._setup_sandbox(aid)
        start_time = time.time()
        self.start_times[aid] = start_time
        self._update_status(aid, role, "starting", f"Инициализация песочницы ({model})...", 0.0)

        for attempt_model in [model, fallback, "opencode/beam-free"]:
            if not attempt_model:
                continue
            active_model = attempt_model
            cmd = ["opencode", "run", "--pure", "--dir", WORKSPACE, "-m", active_model]
            self._update_status(aid, role, "running", f"Запуск генерации через {active_model}...", time.time() - start_time)

            try:
                proc = subprocess.Popen(
                    cmd,
                    stdin=subprocess.PIPE,
                    stdout=subprocess.PIPE,
                    stderr=subprocess.STDOUT,
                    cwd=WORKSPACE,
                    env=env,
                    text=True,
                    encoding="utf-8",
                    errors="replace",
                    bufsize=1,
                    shell=True
                )

                proc.stdin.write(prompt)
                proc.stdin.close()

                lines = []
                while True:
                    line = proc.stdout.readline()
                    if not line and proc.poll() is not None:
                        break
                    if line:
                        lines.append(line)
                        clean = line.strip()
                        if clean:
                            self._update_status(aid, role, "running", clean, time.time() - start_time)

                code = proc.wait()
                output = "".join(lines)

                if code == 0 and len(output.strip()) > 100:
                    out_file = os.path.join(LOGS_DIR, f"{aid}_report.md")
                    with open(out_file, "w", encoding="utf-8") as f:
                        f.write(output)
                    self._update_status(aid, role, "completed", f"Успешно, отчет сохранен: {out_file}", time.time() - start_time)
                    shutil.rmtree(sandbox_dir, ignore_errors=True)
                    return True
                else:
                    self._update_status(aid, role, "warning", f"Ошибка {active_model} (код {code}), пробую резервную...", time.time() - start_time)
            except Exception as ex:
                self._update_status(aid, role, "error", f"Исключение {active_model}: {ex}", time.time() - start_time)

        self._update_status(aid, role, "failed", f"Не удалось выполнить субагентом ({aid})", time.time() - start_time)
        shutil.rmtree(sandbox_dir, ignore_errors=True)
        return False

    def run_all(self):
        print(f"[*] Старт гетерогенного роя TRIAD-REVIEW ({len(self.agents)} агента)...")
        with ThreadPoolExecutor(max_workers=len(self.agents)) as executor:
            futures = [executor.submit(self._run_agent, a) for a in self.agents]
            for f in futures:
                f.result()
        print("[+] Все субагенты роя завершили работу.")

if __name__ == "__main__":
    runner = SwarmRunner(AGENTS)
    runner.run_all()

