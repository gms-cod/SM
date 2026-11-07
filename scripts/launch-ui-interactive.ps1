<#
.SYNOPSIS
Выполняется планировщиком в пользовательском Desktop: запускает UI, активирует окно и сохраняет результат.
.PARAMETER ExecutablePath
Полный путь установленного интерфейса.
.PARAMETER ResultPath
Файл результата, читаемый оркестратором из изолированной консольной сессии.
.PARAMETER CloseAfterLaunch
Проверяет крестик → трей → восстановление, затем завершает UI существующим пунктом «Выход» для проверки независимости службы.
#>
param([Parameter(Mandatory)][string]$ExecutablePath, [Parameter(Mandatory)][string]$ResultPath, [switch]$CloseAfterLaunch)
$ErrorActionPreference = 'Stop'
$result = @{ Success = $false; ContentReady = $false; Error = $null }
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
    /// <summary>Проверяет нативную видимость окна, не полагаясь на кэш Process.MainWindowHandle.</summary>
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr window);
}
'@
    $null = [StartupMonitorInteractiveWindow]::ShowWindow($ui.MainWindowHandle, 9)
    $raised = [StartupMonitorInteractiveWindow]::BringWindowToTop($ui.MainWindowHandle)
    $focused = [StartupMonitorInteractiveWindow]::SetForegroundWindow($ui.MainWindowHandle)
    # HWND и native visibility недостаточны: после Hide окно может быть видимым, пока WPF ещё не восстановил содержимое.
    # Проверяем реальный UI Automation descendant с ограниченным ожиданием; значения пользовательских полей не читаются.
    Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
    $contentReady = $false
    $contentWait = [Diagnostics.Stopwatch]::StartNew()
    do {
        $root = [Windows.Automation.AutomationElement]::FromHandle($ui.MainWindowHandle)
        if ($root -and $root.FindFirst([Windows.Automation.TreeScope]::Descendants, [Windows.Automation.Condition]::TrueCondition)) {
            $contentReady = $true
            break
        }
        Start-Sleep -Milliseconds 100
    } while ($contentWait.Elapsed.TotalSeconds -lt 10)
    if (-not $contentReady) { throw 'Окно интерфейса создано, но за 10 секунд не появилось содержимое WPF в UI Automation; интерактивный запуск не подтверждён.' }
    $result = @{ Success = $true; ProcessId = $ui.Id; SessionId = $ui.SessionId; ExecutablePath = $ui.Path
        MainWindowHandle = $ui.MainWindowHandle.ToInt64(); Raised = $raised; Focused = $focused; ContentReady = $true; Error = $null }
    if ($CloseAfterLaunch) {
        # Сохраняем прежнее ожидание первичной инициализации: оно исключает щелчок по меню под стартовым overlay.
        Start-Sleep -Seconds 5
        # Нативный Close теперь обязан скрыть окно в трей и сохранить PID, а не завершать интерфейс.
        $mainHandle = [IntPtr]$result.MainWindowHandle
        if (-not $ui.CloseMainWindow()) { throw 'Окно не приняло запрос штатного закрытия.' }
        $trayWait = [Diagnostics.Stopwatch]::StartNew()
        do {
            if ($ui.HasExited) { throw 'Крестик завершил UI вместо сворачивания в трей.' }
            if (-not [StartupMonitorInteractiveWindow]::IsWindowVisible($mainHandle)) { break }
            Start-Sleep -Milliseconds 100
        } while ($trayWait.Elapsed.TotalSeconds -lt 5)
        if ([StartupMonitorInteractiveWindow]::IsWindowVisible($mainHandle)) { throw 'Крестик не скрыл окно в трей.' }
        $result.TrayCloseConfirmed = $true

        # Штатный native Restore должен вернуть именно WPF-содержимое скрытого окна.
        $null = [StartupMonitorInteractiveWindow]::ShowWindow($mainHandle, 9)
        $restoreWait = [Diagnostics.Stopwatch]::StartNew()
        $exitElement = $null
        do {
            $root = [Windows.Automation.AutomationElement]::FromHandle($mainHandle)
            $exitElement = $root.FindFirst([Windows.Automation.TreeScope]::Descendants,
                (New-Object Windows.Automation.PropertyCondition([Windows.Automation.AutomationElement]::AutomationIdProperty, 'ExitApplication')))
            if ($exitElement -and -not $exitElement.Current.IsOffscreen) { break }
            Start-Sleep -Milliseconds 100
        } while ($restoreWait.Elapsed.TotalSeconds -lt 10)
        if (-not $exitElement -or $exitElement.Current.IsOffscreen) { throw 'После восстановления из трея отсутствует видимый пункт выхода WPF.' }
        $result.RestoredAfterTray = $true

        # Стандартный Invoke кнопки вызывает тот же Click, что мышь и клавиатура, без эмуляции входных сообщений.
        $invoke = $null
        if (-not $exitElement.TryGetCurrentPattern([Windows.Automation.InvokePattern]::Pattern, [ref]$invoke)) {
            throw 'Кнопка выхода не поддерживает доступное действие UI Automation Invoke.'
        }
        ([Windows.Automation.InvokePattern]$invoke).Invoke()
        if (-not $ui.WaitForExit(15000)) { throw 'Интерфейс не завершился после явного пункта выхода.' }
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
