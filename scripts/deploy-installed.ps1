<#
.SYNOPSIS
Явно устанавливает готовые бинарники в SMon2 и рабочую копию пользователя с откатом при ошибках.
.PARAMETER SourceDirectory
Каталог проверенной сборки с двумя EXE и двумя иконками.
.PARAMETER LaunchUI
Запускает установленный UI для интерактивной проверки после успешного обновления.
#>
param([Parameter(Mandatory)][string]$SourceDirectory, [switch]$LaunchUI)
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'Deployment.psm1') -Force
$repoDirectory = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$source = (Resolve-Path -LiteralPath $SourceDirectory).Path
$installedDirectory = 'C:\Program Files\StartupMonitor'
$destinations = @((Join-Path $repoDirectory 'SMon2'))
$hasInstalledCopy = Test-Path -LiteralPath $installedDirectory -PathType Container
if ($hasInstalledCopy) { $destinations += $installedDirectory }
[xml]$props = Get-Content -LiteralPath (Join-Path $repoDirectory 'Directory.Build.props') -Encoding UTF8
$expectedVersion = [version]($props.Project.PropertyGroup.Version + '.0')
foreach ($name in @('StartupMonitor.Service.exe', 'StartupMonitor.UI.exe')) {
    $file = Get-Item -LiteralPath (Join-Path $source $name)
    if ([version]$file.VersionInfo.FileVersion -ne $expectedVersion) {
        throw "Версия $name ($($file.VersionInfo.FileVersion)) не совпадает с $expectedVersion."
    }
}
$registered = Get-CimInstance Win32_Service -Filter "Name = 'StartupMonitor'" -ErrorAction Stop
if ($hasInstalledCopy -and -not $registered) { throw 'Рабочая копия существует, но служба StartupMonitor не зарегистрирована.' }
$previousPath = if ($registered) { $registered.PathName } else { $null }
$previousSecurity = if ($registered) { (& sc.exe sdshow StartupMonitor | Where-Object { $_ -match '^D:' }) -join '' } else { $null }
if ($registered -and [string]::IsNullOrWhiteSpace($previousSecurity)) { throw 'Не удалось сохранить права службы перед обновлением.' }
$wasRunning = $registered -and $registered.State -eq 'Running'
$targetUiPaths = @($destinations | ForEach-Object { Join-Path $_ 'StartupMonitor.UI.exe' })
$previousUiPaths = @(Get-Process -Name 'StartupMonitor.UI' -ErrorAction SilentlyContinue | ForEach-Object {
    try { if ($_.Path -in $targetUiPaths) { $_.Path } }
    finally { $_.Dispose() }
} | Select-Object -Unique)
$backupDirectory = Join-Path $repoDirectory ('out\deployment-backup-' + [Guid]::NewGuid().ToString('N'))

# Одинаковая остановка вызывается перед установкой и перед откатом, освобождая EXE после частичного старта.
$stopApplication = {
    # Сначала закрываем UI: иначе его reconnect успевает повторно запустить остановленную службу.
    Get-Process -Name 'StartupMonitor.UI' -ErrorAction SilentlyContinue | Where-Object { $_.Path -in $targetUiPaths } | ForEach-Object {
        try {
            $null = $_.CloseMainWindow()
            if (-not $_.WaitForExit(3000)) {
                Stop-Process -Id $_.Id -Force -ErrorAction Stop
                if (-not $_.WaitForExit(10000)) { throw "Процесс UI $($_.Id) не завершился после остановки." }
            }
        }
        finally { $_.Dispose() }
    }
    if ($registered) {
        $service = Get-Service -Name 'StartupMonitor' -ErrorAction Stop
        # SCM может сообщить Stopped до завершения .NET-host: ожидаем освобождение EXE самим процессом.
        # PID считывается заново при каждом вызове, включая откат после частичного запуска новой службы.
        $serviceProcessId = (Get-CimInstance Win32_Service -Filter "Name = 'StartupMonitor'" -ErrorAction Stop).ProcessId
        $serviceProcess = if ($serviceProcessId) { Get-Process -Id $serviceProcessId -ErrorAction SilentlyContinue } else { $null }
        try {
            if ($service.Status -ne 'Stopped') {
                Stop-Service -Name 'StartupMonitor' -ErrorAction Stop
                $service.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(30))
            }
            if ($serviceProcess -and -not $serviceProcess.WaitForExit(30000)) {
                throw "Процесс службы $serviceProcessId не завершился после остановки SCM; замена EXE отменена."
            }
        }
        finally {
            if ($serviceProcess) { $serviceProcess.Dispose() }
            $service.Dispose()
        }
    }
}.GetNewClosure()

# Отсутствие Program Files не препятствует восстановлению прежде запущенной службы с её прежним путём.
$startApplication = {
    if ($hasInstalledCopy) {
        $path = '"' + (Join-Path $installedDirectory 'StartupMonitor.Service.exe') + '"'
        $result = Invoke-CimMethod -InputObject $registered -MethodName Change -Arguments @{ PathName = $path }
        if ($result.ReturnValue -ne 0) { throw "SCM отклонил новый путь службы: $($result.ReturnValue)." }
        # Авторизация IPC не обходится через SCM: право остановки оставляется системе и администраторам.
        & sc.exe sdset StartupMonitor 'D:(A;;CCLCSWRPWPDTLOCRRC;;;SY)(A;;CCDCLCSWRPWPDTLOCRSDRCWDWO;;;BA)(A;;CCLCSWRPRC;;;IU)(A;;CCLCSWLOCRRC;;;SU)' | Out-Null
        if ($LASTEXITCODE -ne 0) { throw 'Не удалось применить права доступа службы.' }
    }
    if ($registered -and ($hasInstalledCopy -or $wasRunning)) {
        Start-Service -Name 'StartupMonitor' -ErrorAction Stop
        (Get-Service -Name 'StartupMonitor').WaitForStatus('Running', [TimeSpan]::FromSeconds(30))
        Start-Sleep -Milliseconds 1000
        if ((Get-Service -Name 'StartupMonitor').Status -ne 'Running') { throw 'Обновлённая служба завершилась после запуска.' }
    }
}.GetNewClosure()

# После восстановления файлов возвращаются прежний путь SCM и прежнее состояние приложения.
$restoreApplication = {
    if ($registered) {
        $result = Invoke-CimMethod -InputObject $registered -MethodName Change -Arguments @{ PathName = $previousPath }
        if ($result.ReturnValue -ne 0) { throw "SCM отклонил прежний путь службы: $($result.ReturnValue)." }
        & sc.exe sdset StartupMonitor $previousSecurity | Out-Null
        if ($LASTEXITCODE -ne 0) { throw 'Не удалось восстановить прежние права службы.' }
        if ($wasRunning) {
            Start-Service -Name 'StartupMonitor' -ErrorAction Stop
            (Get-Service -Name 'StartupMonitor').WaitForStatus('Running', [TimeSpan]::FromSeconds(30))
        }
    }
    foreach ($previousUiPath in $previousUiPaths) {
        Start-StartupMonitorInterface -ExecutablePath $previousUiPath -EvidenceDirectory $backupDirectory | Out-Null
    }
}.GetNewClosure()

Sync-StartupMonitorFiles -SourceDirectory $source -DestinationDirectories $destinations -BackupDirectory $backupDirectory `
    -StopApplication $stopApplication -StartApplication $startApplication -RestoreApplication $restoreApplication
Write-Output "Сборка $expectedVersion установлена. Резервная копия: $backupDirectory"
if ($LaunchUI) {
    $uiDirectory = if ($hasInstalledCopy) { $installedDirectory } else { $destinations[0] }
    Start-StartupMonitorInterface -ExecutablePath (Join-Path $uiDirectory 'StartupMonitor.UI.exe') -EvidenceDirectory $backupDirectory
}
