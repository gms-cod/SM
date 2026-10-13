import sys
from pathlib import Path

sys.path.insert(0, r"d:\Nas_sync\Projects\Gemeni+opencode")
from src.opencode_runner import run_subagent_task

ROOT_DIR = Path(r"d:\Nas_sync\Coding\StartupMonitor")

prompt = (
    "Ты — ведущий системный инженер .NET 8 / Windows internals.\n"
    "ЗАДАЧА: Реализовать автоматическое завершение фоновой службы (StartupMonitor.Service), "
    "когда пользователь завершает работу пользовательского интерфейса (StartupMonitor.UI).\n\n"
    "ТРЕБОВАНИЯ:\n"
    "1. В src/StartupMonitor.Shared/Models/PipeMessage.cs:\n"
    "   - Добавь MessageType константу: public const string ShutdownService = \"ShutdownService\";\n"
    "2. В src/StartupMonitor.Service/Pipe/PipeServer.cs:\n"
    "   - Внедри IHostApplicationLifetime (через конструктор или IServiceProvider).\n"
    "   - Добавь обработку MessageType.ShutdownService в ProcessMessage:\n"
    "     Служба логирует запрос на остановку и инициирует штатную остановку хоста (_lifetime.StopApplication()).\n"
    "     Возвращает клиенту успешный ответ (Success = true).\n"
    "3. В src/StartupMonitor.UI/Services/PipeClient.cs:\n"
    "   - Добавь метод: public async Task<bool> RequestServiceShutdownAsync(int timeoutMs = 3000)\n"
    "     который отправляет запрос ShutdownService на сервер.\n"
    "4. В src/StartupMonitor.UI/App.xaml.cs:\n"
    "   - В CreateTrayMenu при клике по пункту 'Выход' (exit.Click):\n"
    "     Вызови остановку службы: сначала попытка мягкой отправки PipeClient.RequestServiceShutdownAsync(), "
    "     затем вызов ServiceManager.StopService() для гарантированной остановки службы, после чего Current.Shutdown().\n"
    "   - В OnExit(ExitEventArgs e) также добавь защитный вызов ServiceManager.StopService().\n"
    "5. Убедись, что все проекты компилируются чисто и тесты не ломаются."
)

files = [
    str(ROOT_DIR / "src/StartupMonitor.Shared/Models/PipeMessage.cs"),
    str(ROOT_DIR / "src/StartupMonitor.Service/Pipe/PipeServer.cs"),
    str(ROOT_DIR / "src/StartupMonitor.UI/Services/PipeClient.cs"),
    str(ROOT_DIR / "src/StartupMonitor.UI/App.xaml.cs"),
]

print("Запуск субагента OpenCode для связки завершения UI и службы...")
result = run_subagent_task(
    prompt=prompt,
    files=files,
    work_dir=str(ROOT_DIR),
    variant="medium",
    auto_approve=True,
    timeout_seconds=450
)

print(f"Статус: {'Успех' if result.success else 'Сбой'}")
print(result.stdout if result.stdout else result.stderr)

