import sys
import os
import time

sys.path.insert(0, r"D:\Nas\Projects\Gemeni+opencode")
from src.opencode_runner import run_subagent_task

TASKS = [
    {
        "id": "toolbox_tests",
        "title": "Subagent 1: xUnit Tests for Toolbox Services",
        "output_file": r"D:\Nas\Coding\StartupMonitor\tests\StartupMonitor.Tests\Services\ToolboxServicesTests.cs",
        "files": [
            r"D:\Nas\Coding\StartupMonitor\src\StartupMonitor.Shared\Models\Toolbox\ToolboxModels.cs",
            r"D:\Nas\Coding\StartupMonitor\src\StartupMonitor.Service\Services\Toolbox\AwakeService.cs",
            r"D:\Nas\Coding\StartupMonitor\src\StartupMonitor.Service\Services\Toolbox\JunkCleanerService.cs",
            r"D:\Nas\Coding\StartupMonitor\src\StartupMonitor.Service\Services\Toolbox\FileLocksmithService.cs",
            r"D:\Nas\Coding\StartupMonitor\src\StartupMonitor.Service\Services\Toolbox\LanScannerService.cs",
            r"D:\Nas\Coding\StartupMonitor\src\StartupMonitor.UI\Services\HashAndSecurityHelper.cs",
        ],
        "prompt": (
            "Ты — ведущий инженер по автоматизированному тестированию .NET 8 (xUnit, Assert).\n"
            "Напиши полный набор высококачественных юнит-тестов для набора повседневных инструментов (Toolbox) StartupMonitor.\n\n"
            "НЕОБХОДИМЫЕ ТЕСТЫ:\n"
            "1. AwakeServiceTests:\n"
            "   - Проверка Activate на 30 минут: IsActive == true, RemainingMinutes > 0, KeepDisplayOn.\n"
            "   - Проверка Deactivate: IsActive == false, ModeText == 'Disabled'.\n"
            "   - Проверка GetStatus.\n"
            "2. JunkCleanerServiceTests:\n"
            "   - Проверка ScanJunk: возвращает непустой список категорий (user_temp, sys_temp, crash_dumps, explorer_thumbs, windows_update, recycle_bin).\n"
            "   - Проверка Clean с пустым списком категорий: возвращает корректный JunkCleanResultDto.\n"
            "3. FileLocksmithServiceTests:\n"
            "   - Проверка FindLockingProcesses с несуществующим файлом или пустым путем: IsLocked == false, не бросает исключений.\n"
            "   - Проверка структуры FileLockResultDto.\n"
            "4. LanScannerServiceTests:\n"
            "   - Проверка ScanLocalNetworkAsync: возвращает список (или локальное устройство), не падает с необработанным исключением.\n"
            "5. HashAndSecurityHelperTests:\n"
            "   - Проверка ComputeHashesAsync для временного тестового файла с известным содержимым: расчет SHA-256, SHA-1, MD5 и сверка с эталонными хешами.\n"
            "   - Проверка GeneratePassword: длина, включение цифр, спецсимволов, оценка энтропии CalculateEntropy.\n\n"
            "ТРЕБОВАНИЯ:\n"
            "- Чистый, компилируемый C# код без плейсхолдеров.\n"
            "- Пространство имен StartupMonitor.Tests.Services.\n"
            "- Не ломать существующие тесты, использовать стандартные xUnit атрибуты [Fact], [Theory].\n"
            "Выведи ТОЛЬКО исходный C# код файла без markdown разметки или внутри блока ```csharp."
        ),
    }
]

def run_task(task):
    print(f"[*] Starting {task['title']}...")
    res = run_subagent_task(
        prompt=task["prompt"],
        files=task["files"],
        variant="high"
    )
    if res.success:
        content = res.stdout
        # Очистка markdown если есть
        if "```csharp" in content:
            content = content.split("```csharp")[1].split("```")[0].strip()
        elif "```" in content:
            content = content.split("```")[1].split("```")[0].strip()
        
        with open(task["output_file"], "w", encoding="utf-8") as f:
            f.write(content)
        print(f"[+] Task {task['id']} finished successfully! Saved to {task['output_file']}")
    else:
        print(f"[-] Task {task['id']} failed: exit_code={res.exit_code}\nstderr={res.stderr}")

if __name__ == "__main__":
    for t in TASKS:
        run_task(t)

