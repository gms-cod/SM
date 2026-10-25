# -*- coding: utf-8 -*-
"""
Каноничный запуск гетерогенного роя субагентов OpenCode для глобального аудита:
1. 0% CPU Idle и непрерывная защита без запущенного UI (с автоподъемом UI при угрозах).
2. Редизайн страницы Firewall в едином стиле проекта + управление белым и черным списками (просмотр и удаление).
3. Восстановление функционала снимков системы в навигации UI.
"""
import os
import sys
import json
import time
import subprocess
from concurrent.futures import ThreadPoolExecutor

sys.stdout.reconfigure(encoding="utf-8", errors="replace")

BASE_DIR = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
LOGS_DIR = os.path.join(BASE_DIR, "logs")
DOCS_DIR = os.path.join(BASE_DIR, "docs")
os.makedirs(LOGS_DIR, exist_ok=True)
os.makedirs(DOCS_DIR, exist_ok=True)

STATUS_FILE = os.path.join(LOGS_DIR, "live_status_audit.json")

PROMPT_AGENT_1 = """Ты — ведущий системный архитектор .NET 8 и эксперт по низкоуровневой оптимизации Windows-служб.
Проведи детальный аудит службы StartupMonitor.Service на предмет 0% CPU Idle и непрерывной защиты:
1. Как организовать мониторинг сетевых соединений (NetworkConnectionAlertService) так, чтобы при ЗАКРЫТОМ UI защита работала 24/7 без деградации, но при этом строго потребляла 0.0% CPU в простое.
2. Проанализируй кеширование COM-вызовов WindowsFirewallService.GetAllRules(). Как сделать кеш с TTL (например, 60 секунд) и инвалидацией по событию изменения правил.
3. Механизм экстренного реагирования: если UI закрыт, а обнаружена критическая сетевая угроза или неизвестное внешнее подключение программы не из белого списка — как служба должна либо корректно запустить процесс StartupMonitor.UI.exe в пользовательской сессии (с учетом session isolation / WTSEnumerateSessions / CreateProcessAsUser или через тост-уведомление Windows), либо обработать угрозу автономно.
4. Жесткая логика списков:
   - Если программа в белом списке: ей разрешено ВСЁ, НИКАКИХ всплывающих окон и алертов ни при каких обстоятельствах.
   - Если программа в черном списке: ей запрещено ВСЁ (блокирующее правило в Windows Firewall), НИКАКИХ всплывающих окон.
Выдай готовые, выверенные решения на C# с русскими комментариями. Сохрани отчет в docs/AUDIT_SWARM_1_IDLE_AND_HEADLESS_DEFENSE.md.
"""

PROMPT_AGENT_2 = """Ты — ведущий WPF/XAML архитектор и UI/UX дизайнер проекта StartupMonitor.
Проведи полный аудит и разработай проект глубокого редизайна страницы Firewall (src/StartupMonitor.UI/Views/NetworkSecurityHubView.xaml и .xaml.cs):
1. Текущий дизайн перегружен и не соответствует неоновому кибер-стилю главной страницы (светящиеся акценты, карточки с радиусом скругления CornerRadius="8", мягкие тени, полупрозрачные фоны).
2. Разработай структуру вкладок страницы Firewall:
   - Вкладка 1: «Мониторинг сети» (Active Connections) — чистая таблица с индикаторами трафика, статусами Allow/Block и быстрыми действиями.
   - Вкладка 2: «Белый список» (Whitelist) — отдельная таблица/список программ, которым разрешено всё (полный путь, имя, дата добавления), с кнопками «Добавить программу» и «Удалить из списка».
   - Вкладка 3: «Чёрный список» (Blacklist) — отдельная таблица/список заблокированных программ с возможностью убрать из списка (сняв блокирующее правило).
   - Вкладка 4: «Правила брандмауэра» (System Rules) — просмотр и управление всеми правилами Windows Firewall.
   - Вкладка 5: «Port Defender» — защита сетевых портов.
3. Подготовь подробную разметку XAML и C# обработчики для синхронизации белого и черного списков с AppSettings/сервисом через Named Pipe.
Сохрани отчет и макеты кода в docs/AUDIT_SWARM_2_FIREWALL_REDESIGN_LISTS.md.
"""

PROMPT_AGENT_3 = """Ты — ведущий UI-инженер и аналитик интерфейса StartupMonitor.
Проведи аудит пропажи «Снимков системы» (Snapshots) и эргономики навигации:
1. Выясни, почему пользователи жалуются, что в UI «не стало снимков системы». Изучи MainWindow.xaml, MainWindow.xaml.cs, SnapshotsWindow.xaml и SnapshotManager.cs.
2. Проблема: сейчас кнопка «Снимки системы» спрятана как второстепенная кнопка в правом верхнем углу страницы «Обзор» и маленькой иконкой над списком записей. Отдельного раздела в левом боковом меню навигации нет!
3. Разработай добавление полноценного пункта меню в левую навигационную панель MainWindow.xaml:
   - NavItemSnapshots с иконкой фотоаппарата (Camera24 / IconCamera), счетчиком сохраненных снимков и переходом либо на отдельную страницу снимков, либо открытием SnapshotsWindow.
4. Проверь также отображение баннера активного сравнения со снимком («Было — Стало»).
Выдай точные фрагменты XAML и C# с русскими комментариями. Сохрани отчет в docs/AUDIT_SWARM_3_SNAPSHOTS_NAVIGATION.md.
"""

AGENTS = [
    {
        "id": "agent_1_idle_defense",
        "name": "DeepSeek V4.1 Flash (0% Idle & Headless Defense)",
        "model": "opencode/deepseek-v4.1-flash-free",
        "bench_dir": os.path.join(BASE_DIR, "bench_audit1"),
        "doc_file": os.path.join(DOCS_DIR, "AUDIT_SWARM_1_IDLE_AND_HEADLESS_DEFENSE.md"),
        "prompt": PROMPT_AGENT_1,
        "task": "Аудит 0% CPU Idle, кеширование COM правил и запуск UI при угрозах"
    },
    {
        "id": "agent_2_firewall_redesign",
        "name": "Nemotron 3.5 Super VL (Firewall Redesign & Lists)",
        "model": "opencode/nemotron-3.5-super-vl-free",
        "bench_dir": os.path.join(BASE_DIR, "bench_audit2"),
        "doc_file": os.path.join(DOCS_DIR, "AUDIT_SWARM_2_FIREWALL_REDESIGN_LISTS.md"),
        "prompt": PROMPT_AGENT_2,
        "task": "Редизайн Firewall, стиль проекта и управление белым/черным списками"
    },
    {
        "id": "agent_3_snapshots_nav",
        "name": "MiMo v2.6 Flash (Snapshots & Nav)",
        "model": "opencode/mimo-v2.6-flash-free",
        "bench_dir": os.path.join(BASE_DIR, "bench_audit3"),
        "doc_file": os.path.join(DOCS_DIR, "AUDIT_SWARM_3_SNAPSHOTS_NAVIGATION.md"),
        "prompt": PROMPT_AGENT_3,
        "task": "Аудит пропажи снимков системы и интеграция в боковую навигацию"
    }
]

status_map = {}
for ag in AGENTS:
    os.makedirs(ag["bench_dir"], exist_ok=True)
    status_map[ag["id"]] = {
        "name": ag["name"],
        "task": ag["task"],
        "status": "pending",
        "elapsed_seconds": 0,
        "last_line": "Инициализация...",
        "char_count": 0
    }

def update_status_file():
    try:
        with open(STATUS_FILE, "w", encoding="utf-8") as f:
            json.dump(status_map, f, ensure_ascii=False, indent=2)
    except Exception:
        pass

update_status_file()

def run_agent(ag):
    agent_id = ag["id"]
    status_map[agent_id]["status"] = "running"
    status_map[agent_id]["last_line"] = "Запуск CLI opencode..."
    update_status_file()
    
    cmd = [
        "opencode", "run",
        "--pure",
        "--dir", ag["bench_dir"],
        "-m", ag["model"]
    ]
    
    start_time = time.time()
    try:
        proc = subprocess.Popen(
            cmd,
            stdin=subprocess.PIPE,
            stdout=subprocess.PIPE,
            stderr=subprocess.STDOUT,
            cwd=ag["bench_dir"],
            text=True,
            encoding="utf-8",
            errors="replace"
        )
        
        proc.stdin.write(ag["prompt"])
        proc.stdin.close()
        
        output_lines = []
        for line in proc.stdout:
            clean_line = line.strip()
            if clean_line:
                status_map[agent_id]["last_line"] = clean_line[:140]
            output_lines.append(line)
            status_map[agent_id]["elapsed_seconds"] = int(time.time() - start_time)
            status_map[agent_id]["char_count"] = sum(len(l) for l in output_lines)
            update_status_file()
            
        proc.wait(timeout=3600)
        full_text = "".join(output_lines)
        
        with open(ag["doc_file"], "w", encoding="utf-8") as f:
            f.write(full_text)
            
        status_map[agent_id]["status"] = "completed" if proc.returncode == 0 else "error"
        status_map[agent_id]["last_line"] = f"Завершено за {int(time.time() - start_time)} сек"
    except Exception as ex:
        status_map[agent_id]["status"] = "failed"
        status_map[agent_id]["last_line"] = f"Ошибка: {str(ex)[:100]}"
    finally:
        status_map[agent_id]["elapsed_seconds"] = int(time.time() - start_time)
        update_status_file()

print("Старт гетерогенного роя субагентов OpenCode (3 агента параллельно)...")
with ThreadPoolExecutor(max_workers=len(AGENTS)) as executor:
    futures = [executor.submit(run_agent, ag) for ag in AGENTS]

print("Все субагенты успешно завершили работу.")

