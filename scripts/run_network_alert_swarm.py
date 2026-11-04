# -*- coding: utf-8 -*-
"""
Запуск гетерогенного роя субагентов OpenCode профиля TRIAD-REVIEW.
Исследование: почему при запущенных Service и UI нет всплывающих окон о сетевых соединениях,
включая сценарии сворачивания UI в трей и запуска с флагом --minimized.
Соответствует регламенту AI-Core Manifest 3.0:
- 3 независимых ИИ-движка с ортогональными ролями
- Изоляция песочниц (mkdtemp, копирование auth.json, подмена USERPROFILE/HOME)
- STDIN передача промпта
- Атомарный JSON-мониторинг logs/live_status.json
- Лимит времени 3600с без преждевременного обрыва
"""

import os
import sys
import time
import json
import shutil
import tempfile
import subprocess
from concurrent.futures import ThreadPoolExecutor

# Принудительная инициализация кодировки UTF-8 в консоли Windows
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

AGENTS = [
    {
        "id": "Agent-A-LogicContracts",
        "role": "Аудит логики фильтрации и контрактов сервиса",
        "model": "opencode/deepseek-v4.1-flash-free",
        "fallback_model": "opencode/beam-free",
        "prompt": """Ты — элитный C# системный архитектор. Проведи глубокий аудит серверной логики сетевых алертов в StartupMonitor.
Файлы для анализа:
- src/StartupMonitor.Service/Services/Firewall/NetworkConnectionAlertService.cs
- src/StartupMonitor.Service/Services/ProcessNetworkTracker.cs
- src/StartupMonitor.Service/Pipe/PipeServer.cs
- src/StartupMonitor.Service/Services/Runtime/SleepAwareLoop.cs

Проблема:
Пользователь сообщает: при запущенных службе и UI всплывающие окна о попытках сетевых соединений (NetworkConnectionAlertWindow) не появляются.

Проанализируй досконально:
1. Как работает ScanNewConnections(): почему сокеты могут отбрасываться? (Проверка LAN/Loopback, TrustedSystemProcesses, SilentPid0, HasExistingFirewallRuleCached, кэш правил брандмауэра).
2. Запускается ли реально фоновый опрос StartBackgroundScan() при старте службы? Попадает ли служба в вечный сон из-за SleepAwareLoop и ActivityGate?
3. Что происходит, если клиент UI подключается и запрашивает GetPendingNetworkAlertsAsync?
4. Какие баги, граничные условия или логические дефекты препятствуют созданию и доставке алертов?

Выдай конкретный технический отчет с точными номерами строк, найденными багами и готовым исправленным C# кодом. Никаких TODO и абстрактных советов.
"""
    },
    {
        "id": "Agent-B-ThreadingIpc",
        "role": "Аудит многопоточности, IPC-каналов и фоновых поллеров",
        "model": "opencode/beam-free",
        "fallback_model": "opencode/glm-5.3-flash-free",
        "prompt": """Ты — элитный C# инженер многопоточности и IPC. Проведи аудит передачи сетевых алертов между Service и UI в StartupMonitor.
Файлы для анализа:
- src/StartupMonitor.UI/ViewModels/MainViewModel.cs (методы StartNetworkAlertPoller, StopNetworkAlertPoller, PollNetworkAlertsAsync, SuspendAllBackgroundActivity, ResumeAllBackgroundActivity)
- src/StartupMonitor.UI/Services/PipeClient.cs (GetPendingNetworkAlertsAsync, ResolveNetworkAlertAsync)
- src/StartupMonitor.Service/Pipe/PipeServer.cs (обработка запросов GetPendingNetworkAlerts, ResolveNetworkAlert)
- src/StartupMonitor.Service/Services/Firewall/NetworkConnectionAlertService.cs

Проблема:
При запущенных службе и UI всплывающие окна о попытках сетевых соединений отсутствуют.

Проанализируй досконально:
1. Как устроен поллер _networkAlertPollerTimer в MainViewModel: когда он запускается, не уничтожается ли он при переподключении или ошибках Pipe?
2. Взаимные блокировки: нет ли дедлока или зависания в _isPollingNetworkAlerts (Interlocked) при таймауте IPC?
3. Почему используется поллинг раз в 2 секунды вместо событийного push-уведомления через EventPipeServer?
4. Что происходит с очередью алертов в сервисе при сбоях IPC?

Выдай конкретный технический отчет с точными номерами строк, найденными багами многопоточности/IPC и готовым исправленным C# кодом.
"""
    },
    {
        "id": "Agent-C-WpfTrayLifecycle",
        "role": "Аудит жизненного цикла WPF, сворачивания в трей и показа окон",
        "model": "openrouter/nvidia/nemotron-3.5-super-vl:free",
        "fallback_model": "opencode/nemotron-3.5-super-vl-free",
        "prompt": """Ты — элитный WPF и Windows UI архитектор. Проведи глубокий аудит отображения всплывающих окон при сворачивании в трей в StartupMonitor.
Файлы для анализа:
- src/StartupMonitor.UI/App.xaml.cs (OnStartup, IsMinimized, запуск MainWindow)
- src/StartupMonitor.UI/Views/MainWindow.xaml.cs (Loaded, IsVisibleChanged, StateChanged, SuspendAllBackgroundActivity, RestoreFromTray)
- src/StartupMonitor.UI/Views/Dialogs/NetworkConnectionAlertWindow.xaml и .xaml.cs (Topmost, ShowActivated, ShowInTaskbar, Show(), Positioning)
- src/StartupMonitor.UI/Views/NetworkSecurityHubView.xaml.cs (Activate, Deactivate, _alertPollerTimer)

Проблема:
Пользователь сообщает: при запущенных сервисе и UI нет всплывающих окон о попытках соединений. «Возможно виновато то что UI свёрнут в трей».

Проанализируй досконально:
1. Что происходит, если приложение стартует с ключом --minimized:
   В App.xaml.cs: if (!IsMinimized) { mainWindow.Show(); }
   Вызывается ли событие Loaded у MainWindow, если Show() не был вызван? Стартует ли вообще StartupSequence() и подключение к Pipe?
2. Что происходит, когда окно сворачивается в трей (WindowState == Minimized / IsVisible == false):
   Вызывается SuspendAllBackgroundActivity() и FirewallHubView.Deactivate().
   Останавливается ли опрос сетевых алертов? Где на самом деле должен жить глобальный поллер/подписка алертов?
3. Поведение окна NetworkConnectionAlertWindow:
   Если главное окно MainWindow скрыто (Hide() в трей), может ли отдельное окно Window со свойствами Topmost=true, ShowActivated=false показываться на рабочем столе Windows без видимого родительского окна? Есть ли проблемы с Application.Current.MainWindow или Dispatcher?

Выдай детальный технический отчет с выявленными дефектами жизненного цикла UI/трея и готовым исправленным C# кодом.
"""
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
        """Создает изолированную персональную песочницу для субагента."""
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

        # Попытка запуска с основной моделью, fallback при ошибке
        active_model = model
        for attempt_model in [model, fallback]:
            if not attempt_model:
                continue
            active_model = attempt_model
            cmd = ["opencode", "run", "--pure", "--dir", WORKSPACE, "-m", attempt_model]

            self._update_status(aid, role, "running", f"Запуск процесса ({active_model})...", time.time() - start_time)

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
                    bufsize=1
                )

                # Передаем полный промпт в STDIN и закрываем
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

                # Проверяем успешность
                if code == 0 and len(output.strip()) > 100:
                    out_file = os.path.join(LOGS_DIR, f"{aid}_report.md")
                    with open(out_file, "w", encoding="utf-8") as f:
                        f.write(output)
                    self._update_status(aid, role, "completed", f"Завершено успешно, отчет сохранен в {out_file}", time.time() - start_time)
                    shutil.rmtree(sandbox_dir, ignore_errors=True)
                    return True
                else:
                    self._update_status(aid, role, "warning", f"Сбой {active_model} (код {code}), пробую резервную...", time.time() - start_time)
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

