# -*- coding: utf-8 -*-
"""
Единый запуск гетерогенного роя субагентов для реализации v1.9.3:
- Gemini 4 Argon: Редизайн NetworkSecurityHubView.xaml и .xaml.cs (5 вкладок, белый и черный списки)
- DeepSeek V4.1 Flash: Ядро службы, 0.0% CPU Eco-Sensing, COM кэш с TTL 60с, семантика списков
- Beam-free: Снимки системы в боковом меню MainWindow.xaml + глобальный поллер в MainViewModel
"""
import os
import sys
import json
import time
import subprocess
from concurrent.futures import ThreadPoolExecutor

sys.stdout.reconfigure(encoding="utf-8", errors="replace")

BASE_DIR = r"d:\Nas_sync\Coding\StartupMonitor"
LOGS_DIR = os.path.join(BASE_DIR, "logs")
DOCS_DIR = os.path.join(BASE_DIR, "docs")
os.makedirs(LOGS_DIR, exist_ok=True)
os.makedirs(DOCS_DIR, exist_ok=True)

STATUS_FILE = os.path.join(LOGS_DIR, "live_status_implementation.json")

# Извлекаем ключ OpenRouter для Gemini 4 Argon
auth_file = os.path.expanduser(r"~/.local/share/opencode/auth.json")
openrouter_key = None
if os.path.exists(auth_file):
    try:
        with open(auth_file, "r", encoding="utf-8") as f:
            d = json.load(f)
            openrouter_key = d.get("openrouter", {}).get("key") or d.get("openrouter", {}).get("apiKey")
    except Exception:
        pass

PROMPT_GEMINI_4 = """Ты — ведущий WPF/XAML разработчик проекта StartupMonitor.
Твоя задача — реализовать полный редизайн страницы сетевой безопасности:
Файлы: src/StartupMonitor.UI/Views/NetworkSecurityHubView.xaml и src/StartupMonitor.UI/Views/NetworkSecurityHubView.xaml.cs.

Требования:
1. Стиль: Неоновый кибер-стиль (CardBackgroundFillColorDefaultBrush, CornerRadius="8", CardStrokeColorDefaultBrush, Emerald #10B981, Crimson #EF4444, Neon Cyan #00D2D3, Amber #F59E0B). Высококонтрастный текст (белый #FFFFFF на темных подложках).
2. 5 вкладок:
   - Вкладка 1: «Активные соединения» (Live Connections) с бейджами Allow/Block, GeoIP, кнопками «В белый список», «В чёрный список».
   - Вкладка 2: «Белый список» (Whitelist) — таблица (Программа, Полный путь, Дата добавления), кнопки «Добавить программу...» (OpenFileDialog) и «Удалить из списка». Программам разрешено всё, 0 алертов.
   - Вкладка 3: «Чёрный список» (Blacklist) — таблица заблокированных программ, кнопки «Добавить в чёрный список...» и «Разблокировать (удалить из черного списка)». Программам запрещено всё, 0 алертов.
   - Вкладка 4: «Правила брандмауэра» (System Rules) — просмотр и фильтрация системных правил Windows Firewall.
   - Вкладка 5: «Port Defender» — защищаемые порты и журнал атак/банов.
3. Код C#:
   - ObservableCollection для WhitelistedApps и BlacklistedApps.
   - Загрузка списков через PipeClient (GetFirewallWhitelistAsync, GetFirewallBlacklistAsync) и сохранение.
   - Удаление дублирующего _alertPollerTimer из этого View (он централизован в MainViewModel).
   - Сохранение сигнатур статических методов ShouldShowAlertWindow, BuildResolveRequest, SerializeBackupToJson, MatchesRuleFilter для совместимости с юнит-тестами.

Выдай готовый, полностью рабочий код XAML и C# с подробными русскими комментариями. Запиши результат в docs/CODE_IMPLEMENTATION_FIREWALL_UI.md.
"""

PROMPT_DEEPSEEK = """Ты — ведущий системный архитектор .NET 8 и эксперт по сетевой безопасности ядра Windows.
Твоя задача — реализовать серверную часть сетевой защиты и оптимизации 0.0% CPU:
Файлы:
- src/StartupMonitor.Service/Services/Firewall/NetworkConnectionAlertService.cs
- src/StartupMonitor.Service/Services/WindowsFirewallService.cs
- src/StartupMonitor.Shared/Models/AppConfig.cs

Требования:
1. 0.0% CPU Eco-Sensing режим:
   - При активном UI (_monitorWindowActive == true): частый опрос (раз в 2 сек) с обогащением.
   - При закрытом UI (_monitorWindowActive == false): энергосберегающий режим Eco-Sensing (интервал 5-10 сек, минимальный приоритет, сверхбыстрый вызов GetExtendedTcpTable <0.08 мс, 0.0% CPU).
2. Кэширование COM-правил Windows Firewall:
   - В WindowsFirewallService внедрить кэш путей правил _firewallRulePathsCache с TTL 60 секунд.
   - Использовать FrozenDictionary<string, FirewallRuleInfo> (.NET 8) для O(1) быстродействия без блокировок.
   - Обязательно вызывать Marshal.FinalReleaseComObject для предотвращения утечки COM-памяти.
3. Строгая семантика списков:
   - Приоритет: Blacklist > Whitelist (если путь в обоих списках — безусловная блокировка).
   - Белый список: разрешено всё, СТРОГО 0 всплывающих окон и алертов.
   - Чёрный список: запрещено всё, СТРОГО 0 всплывающих окон + немедленная системная блокировка в Windows Firewall.
   - Добавить FirewallBlacklistedApps в AppSettings и AppConfig.Clone().

Выдай готовый код C# с подробными русскими комментариями. Запиши результат в docs/CODE_IMPLEMENTATION_SERVICE_CORE.md.
"""

PROMPT_BEAM = """Ты — ведущий UI-инженер StartupMonitor.
Твоя задача — вернуть «Снимки системы» в навигацию главного окна и перенести глобальный сетевой поллер в MainViewModel:
Файлы:
- src/StartupMonitor.UI/Views/MainWindow.xaml
- src/StartupMonitor.UI/Views/MainWindow.xaml.cs
- src/StartupMonitor.UI/ViewModels/MainViewModel.cs
- src/StartupMonitor.UI/Resources/Strings.ru.resx и Strings.resx
- src/StartupMonitor.UI/Services/LocalizationService.cs

Требования:
1. Снимки системы:
   - Добавить пункт NavItemSnapshots в левое боковое меню MainWindow.xaml с иконкой фотоаппарата (IconCamera) и круглым синим бейджем количества сохранённых снимков.
   - Свойство SavedSnapshotsCount в MainViewModel с автообновлением через PipeClient.GetSnapshotsAsync().
   - Локализация NavSnapshots («Снимки системы»).
   - Сохранение карточки сравнения «Было — Стало».
2. Глобальный сетевой поллер:
   - В MainViewModel.cs разместить централизованный _networkAlertPollerTimer (интервал 1.5–2 с), работающий поверх всех вкладок и из трея, открывающий NetworkConnectionAlertWindow при обнаружении алертов.

Выдай точный, готовый код XAML и C# с русскими комментариями. Запиши результат в docs/CODE_IMPLEMENTATION_NAV_AND_POLLER.md.
"""

AGENTS = [
    {
        "id": "gemini_4_firewall_ui",
        "name": "Google Gemini 4 Argon (Firewall UI Redesign)",
        "type": "direct_openrouter",
        "model": "google/gemini-4-argon",
        "prompt": PROMPT_GEMINI_4,
        "doc_file": os.path.join(DOCS_DIR, "CODE_IMPLEMENTATION_FIREWALL_UI.md"),
        "task": "Редизайн NetworkSecurityHubView (5 вкладок, списки, кибер-стиль)"
    },
    {
        "id": "deepseek_service_core",
        "name": "DeepSeek V4.1 Flash (0% CPU & Core Defense)",
        "type": "opencode_cli",
        "model": "opencode/deepseek-v4.1-flash-free",
        "bench_dir": os.path.join(BASE_DIR, "bench_impl1"),
        "prompt": PROMPT_DEEPSEEK,
        "doc_file": os.path.join(DOCS_DIR, "CODE_IMPLEMENTATION_SERVICE_CORE.md"),
        "task": "Серверная часть: 0.0% CPU Eco-Sensing, COM кэш с TTL 60с, списки"
    },
    {
        "id": "beam_nav_and_poller",
        "name": "Beam-free (Snapshots Nav & Global Poller)",
        "type": "opencode_cli",
        "model": "opencode/beam-free",
        "bench_dir": os.path.join(BASE_DIR, "bench_impl2"),
        "prompt": PROMPT_BEAM,
        "doc_file": os.path.join(DOCS_DIR, "CODE_IMPLEMENTATION_NAV_AND_POLLER.md"),
        "task": "Снимки системы в навигации MainWindow и глобальный поллер алертов"
    }
]

status_map = {}
for ag in AGENTS:
    if "bench_dir" in ag:
        os.makedirs(ag["bench_dir"], exist_ok=True)
    status_map[ag["id"]] = {
        "name": ag["name"],
        "task": ag["task"],
        "status": "pending",
        "elapsed_seconds": 0,
        "last_line": "Инициализация...",
        "char_count": 0
    }

def update_status():
    try:
        with open(STATUS_FILE, "w", encoding="utf-8") as f:
            json.dump(status_map, f, ensure_ascii=False, indent=2)
    except Exception:
        pass

update_status()

def run_gemini_direct(ag):
    import http.client
    aid = ag["id"]
    status_map[aid]["status"] = "running"
    status_map[aid]["last_line"] = "Отправка запроса в Google Gemini 4 Argon (OpenRouter)..."
    update_status()
    
    start_time = time.time()
    try:
        conn = http.client.HTTPSConnection("openrouter.ai", timeout=180)
        payload = json.dumps({
            "model": ag["model"],
            "messages": [
                {"role": "system", "content": "You are Google Gemini 4 Argon, an elite senior WPF/C# architect."},
                {"role": "user", "content": ag["prompt"]}
            ],
            "max_tokens": 2500,
            "temperature": 0.2
        })
        conn.request("POST", "/api/v1/chat/completions", payload, {
            "Authorization": f"Bearer {openrouter_key}",
            "Content-Type": "application/json"
        })
        res = conn.getresponse()
        raw = res.read().decode("utf-8")
        if res.status != 200:
            raise RuntimeError(f"OpenRouter HTTP {res.status}: {raw[:150]}")
        data = json.loads(raw)
        content = data["choices"][0]["message"]["content"]
        
        with open(ag["doc_file"], "w", encoding="utf-8") as f:
            f.write(content)
            
        status_map[aid]["status"] = "completed"
        status_map[aid]["char_count"] = len(content)
        status_map[aid]["last_line"] = f"Успешно сгенерировано {len(content)} символов кода"
    except Exception as ex:
        status_map[aid]["status"] = "error"
        status_map[aid]["last_line"] = f"Ошибка: {str(ex)[:100]}"
    finally:
        status_map[aid]["elapsed_seconds"] = int(time.time() - start_time)
        update_status()

def run_opencode_agent(ag):
    aid = ag["id"]
    status_map[aid]["status"] = "running"
    status_map[aid]["last_line"] = f"Запуск {ag['model']}..."
    update_status()
    
    start_time = time.time()
    cmd = ["opencode", "run", "--pure", "--dir", ag["bench_dir"], "-m", ag["model"]]
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
        
        lines = []
        for line in proc.stdout:
            clean = line.strip()
            if clean:
                status_map[aid]["last_line"] = clean[:140]
            lines.append(line)
            status_map[aid]["elapsed_seconds"] = int(time.time() - start_time)
            status_map[aid]["char_count"] = sum(len(l) for l in lines)
            update_status()
            
        proc.wait(timeout=3600)
        full_text = "".join(lines)
        with open(ag["doc_file"], "w", encoding="utf-8") as f:
            f.write(full_text)
            
        status_map[aid]["status"] = "completed" if proc.returncode == 0 else "error"
        status_map[aid]["last_line"] = f"Завершено за {int(time.time() - start_time)} сек"
    except Exception as ex:
        status_map[aid]["status"] = "error"
        status_map[aid]["last_line"] = f"Ошибка: {str(ex)[:100]}"
    finally:
        status_map[aid]["elapsed_seconds"] = int(time.time() - start_time)
        update_status()

def worker(ag):
    if ag["type"] == "direct_openrouter":
        run_gemini_direct(ag)
    else:
        run_opencode_agent(ag)

print("Старт реализации роем (Gemini 4 Argon + DeepSeek V4.1 + Beam-free)...")
with ThreadPoolExecutor(max_workers=len(AGENTS)) as executor:
    futures = [executor.submit(worker, ag) for ag in AGENTS]

print("Все агенты реализации завершили генерацию кода.")

