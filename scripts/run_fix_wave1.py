import os
import sys
import time
from concurrent.futures import ThreadPoolExecutor, as_completed
from pathlib import Path

# Подключаем раннер OpenCode
sys.path.insert(0, r"d:\Nas_sync\Projects\Gemeni+opencode")
from src.opencode_runner import run_subagent_task

ROOT_DIR = Path(r"d:\Nas_sync\Coding\StartupMonitor")

FIX_TASKS = [
    {
        "id": "fix_port_defender_p0",
        "title": "Fix Worker 1: Port Defender RFC1918 Protection, Whitelist & Firewall Reconciliation",
        "files": [
            str(ROOT_DIR / "src/StartupMonitor.Service/Services/Firewall/ProcessPortDefenderService.cs"),
            str(ROOT_DIR / "src/StartupMonitor.Service/Services/WindowsFirewallService.cs"),
            str(ROOT_DIR / "src/StartupMonitor.Shared/Models/Firewall/FirewallModels.cs"),
        ],
        "prompt": (
            "Ты — ведущий инженер по сетевой безопасности Windows и .NET 8.\n"
            "ИСПРАВЬ КРИТИЧЕСКИЕ ДЕФЕКТЫ P0 В PORT DEFENDER:\n"
            "1. В ProcessPortDefenderService.cs:\n"
            "   - Внедрить честную проверку TrustPrivateNetworks из конфигурации (_config.TrustPrivateNetworks).\n"
            "   - Если TrustPrivateNetworks == true, ЛЮБЫЕ адреса RFC 1918 (10.0.0.0/8, 172.16.0.0/12, 192.168.0.0/16, APIPA 169.254.0.0/16) НИ ПРИ КАКИХ ОБСТОЯТЕЛЬСТВАХ НЕ БАНЯТСЯ! Используй ProcessNetworkSocket.CheckIsPrivateAddress или FirewallGuard.\n"
            "   - Внедрить проверку глобального WhitelistIps (_config.WhitelistIps) и правила IsIpWhitelisted.\n"
            "   - В методе LoadActiveBans / старте службы: если бан просрочен, его необходимо УДАЛИТЬ из правила брандмауэра (_firewall.UnblockIp), а не просто пропускать! Устрани вечные 'зомби'-правила после перезагрузки.\n"
            "2. Внеси аккуратные изменения, сохрани все существующие публичные методы и контракты.\n"
            "3. Убедись, что код компилируется без ошибок."
        ),
    },
    {
        "id": "fix_scanner_access_denied",
        "title": "Fix Worker 2: SystemScanner Per-Key Exception Guards & Safe Cleanup",
        "files": [
            str(ROOT_DIR / "src/StartupMonitor.Service/Services/SystemScannerService.cs"),
            str(ROOT_DIR / "src/StartupMonitor.Service/Services/EntryOperations.cs"),
        ],
        "prompt": (
            "Ты — элитный разработчик Windows internals .NET 8.\n"
            "ИСПРАВЬ КРИТИЧЕСКИЕ СЛАБЫЕ МЕСТА СКАНИРОВАНИЯ И ОЧИСТКИ:\n"
            "1. В SystemScannerService.cs:\n"
            "   - В ScanHiveRegistry / ScanRegistry: оберни чтение Registry.Users.OpenSubKey(sid) и per-key baseKey.OpenSubKey(keyPath) в защитные try/catch, чтобы AccessDenied или выгрузка куста не роняли ScanAll.\n"
            "   - В ScanFolder: защити Directory.GetFiles(folder) от UnauthorizedAccessException.\n"
            "2. В EntryOperations.cs:\n"
            "   - В CleanupStaleEntries: добавь защиту от ложного удаления записей, если сканирование завершилось с ошибкой или список результатов пуст (Fail-closed guard).\n"
            "   - Удали мусорные XML-комментарии (/// <summary>RegistryPayload...) прямо посреди кода вызова Serialize.\n"
            "3. Сохрани работоспособность всех существующих тестов."
        ),
    },
    {
        "id": "fix_code_cleanup_and_versions",
        "title": "Fix Worker 3: Cleanup XML Artifacts, Duplicate sihost & Sync Fallback Versions",
        "files": [
            str(ROOT_DIR / "src/StartupMonitor.UI/App.xaml.cs"),
            str(ROOT_DIR / "src/StartupMonitor.Shared/Helpers/SystemProtectionHelper.cs"),
            str(ROOT_DIR / "src/StartupMonitor.Service/Services/Update/GitHubUpdateService.cs"),
            str(ROOT_DIR / "src/StartupMonitor.UI/Services/PipeClient.cs"),
            str(ROOT_DIR / "src/StartupMonitor.UI/Views/Dialogs/UpdateAvailableWindow.xaml.cs"),
            str(ROOT_DIR / "scripts/build-installer.ps1"),
            str(ROOT_DIR / "installers/StartupMonitor.iss"),
        ],
        "prompt": (
            "Ты — ведущий инженер качества кода .NET 8.\n"
            "ВЫПОЛНИ ЧИСТКУ И УНИФИКАЦИЮ ПРОЕКТА:\n"
            "1. В src/StartupMonitor.UI/App.xaml.cs: удали мусорные комментарии /// <summary>Point: Выполняет операцию Point.</summary> вокруг Point(0, 0) и Point(1, 1).\n"
            "2. В src/StartupMonitor.Shared/Helpers/SystemProtectionHelper.cs: удали дубликат 'sihost' из списка ProtectedProcessNames.\n"
            "3. Синхронизируй все дефолтные fallback-версии на '1.3.1':\n"
            "   - GitHubUpdateService.cs: fallback new Version(1, 3, 1);\n"
            "   - PipeClient.cs: fallback '1.3.1';\n"
            "   - UpdateAvailableWindow.xaml.cs: fallback '1.3.1';\n"
            "   - scripts/build-installer.ps1: $version = '1.3.1'.\n"
            "4. В installers/StartupMonitor.iss: удали дублирующий ярлык автозапуска из секции [Icons] ({autostartup}), так как UI сам корректно управляет автозапуском с флагом --minimized.\n"
            "5. Проверь синтаксическую чистоту."
        ),
    },
]

def run_fix_task(task):
    title = task["title"]
    files = task["files"]
    prompt = task["prompt"]

    print(f"[START] {title}...")
    start_time = time.time()
    try:
        result = run_subagent_task(
            prompt=prompt,
            files=files,
            work_dir=str(ROOT_DIR),
            variant="medium",
            auto_approve=True,
            timeout_seconds=450
        )
        duration = time.time() - start_time
        status_tag = "DONE" if result.success else "WARN"
        print(f"[{status_tag}] {title} ({duration:.1f}s)")
        return {"title": title, "success": result.success, "duration": duration, "output": result.stdout}
    except Exception as ex:
        duration = time.time() - start_time
        print(f"[ERROR] {title} ({duration:.1f}s): {ex}")
        return {"title": title, "success": False, "duration": duration, "error": str(ex)}

def main():
    print("=" * 70)
    print("ЗАПУСК ВОЛНЫ ИСПРАВЛЕНИЙ РОЕМ OPENCODE (3 ВОРКЕРА)")
    print("Модель: opencode/muse-spark-1.3-contributor-free")
    print("=" * 70)

    start_all = time.time()
    results = []

    # Запускаем параллельно 3 воркера на независимые области файлов
    with ThreadPoolExecutor(max_workers=3) as executor:
        futures = {executor.submit(run_fix_task, task): task for task in FIX_TASKS}
        for future in as_completed(futures):
            res = future.result()
            results.append(res)

    total_time = time.time() - start_all
    print("\n" + "=" * 70)
    print(f"ВСЕ ИСПРАВЛЕНИЯ ЗАВЕРШЕНЫ ЗА {total_time:.1f} сек!")
    print("=" * 70)

if __name__ == "__main__":
    main()
