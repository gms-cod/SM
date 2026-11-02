<#
.SYNOPSIS
Выполняется планировщиком в пользовательском Desktop: запускает UI, активирует окно и сохраняет результат.
.PARAMETER ExecutablePath
Полный путь установленного интерфейса.
.PARAMETER ResultPath
Файл результата, читаемый оркестратором из изолированной консольной сессии.
.PARAMETER CloseAfterLaunch
Штатно закрывает проверяемое окно после запуска для проверки независимости системной службы от UI.
#>
param([Parameter(Mandatory)][string]$ExecutablePath, [Parameter(Mandatory)][string]$ResultPath, [switch]$CloseAfterLaunch)
$ErrorActionPreference = 'Stop'
$result = @{ Success = $false; Error = $null }
try {
    $exe = (Resolve-Path -LiteralPath $ExecutablePath).Path
    $sessionId = [Diagnostics.Process]::GetCurrentProcess().SessionId
    $null = Start-Process -FilePath $exe -WorkingDirectory ([IO.Path]::GetDirectoryName($exe)) -WindowStyle Normal -PassThru
    $ui = $null
    for ($attempt = 0; $attempt -lt 50; $attempt++) {
        $ui = Get-Process -Name 'StartupMonitor.UI' -ErrorAction SilentlyContinue |
            Where-Object { $_.Path -eq $exe -and $_.SessionId -eq $sessionId } | Select-Object -First 1
        if ($ui -and $ui.MainWindowHandle -ne [IntPtr]::Zero) { break }
        Start-Sleep -Milliseconds 500
    }
    if (-not $ui -or $ui.MainWindowHandle -eq [IntPtr]::Zero) { throw 'Интерфейс не создал окно на интерактивном Desktop.' }
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
/// <summary>Активирует окно из той же интерактивной сессии и того же Desktop.</summary>
public static class StartupMonitorInteractiveWindow {
    /// <summary>Восстанавливает свёрнутое окно.</summary>
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr window, int command);
    /// <summary>Поднимает окно над соседними окнами.</summary>
    [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr window);
    /// <summary>Запрашивает передачу фокуса окну.</summary>
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr window);
}
'@
    $null = [StartupMonitorInteractiveWindow]::ShowWindow($ui.MainWindowHandle, 9)
    $raised = [StartupMonitorInteractiveWindow]::BringWindowToTop($ui.MainWindowHandle)
    $focused = [StartupMonitorInteractiveWindow]::SetForegroundWindow($ui.MainWindowHandle)
    $result = @{ Success = $true; ProcessId = $ui.Id; SessionId = $ui.SessionId; ExecutablePath = $ui.Path
        MainWindowHandle = $ui.MainWindowHandle.ToInt64(); Raised = $raised; Focused = $focused; Error = $null }
    if ($CloseAfterLaunch) {
        Start-Sleep -Seconds 5
        if (-not $ui.CloseMainWindow()) { throw 'Окно не приняло запрос штатного закрытия.' }
        if (-not $ui.WaitForExit(15000)) { throw 'Интерфейс не завершился после закрытия окна.' }
        $result.Exited = $true
    }
}
catch { $result.Success = $false; $result.Error = $_.Exception.Message }
finally {
    # Готовый результат публикуется переименованием: оркестратор не читает недописанный JSON.
    [IO.File]::WriteAllText(($ResultPath + '.tmp'), ($result | ConvertTo-Json), [Text.Encoding]::UTF8)
    [IO.File]::Move(($ResultPath + '.tmp'), $ResultPath)
}
if (-not $result.Success) { exit 1 }
