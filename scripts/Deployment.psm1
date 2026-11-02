function Sync-StartupMonitorFiles {
    <#
    .SYNOPSIS
    Заменяет только четыре артефакта приложения и откатывает файлы при любом отказе установки.
    .PARAMETER SourceDirectory
    Каталог полностью собранных EXE и иконок.
    .PARAMETER DestinationDirectories
    Явные каталоги переносимой и установленной копий; конфигурация в них не изменяется.
    .PARAMETER BackupDirectory
    Новый каталог резервных копий, сохраняемый и после успешного обновления.
    .PARAMETER StopApplication
    Остановка службы и UI после подготовки резервных копий.
    .PARAMETER StartApplication
    Проверка SCM и запуск новой сборки после проверки файлов.
    .PARAMETER RestoreApplication
    Восстановление прежнего состояния SCM после отката файлов.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$SourceDirectory,
        [Parameter(Mandatory)][string[]]$DestinationDirectories,
        [Parameter(Mandatory)][string]$BackupDirectory,
        [Parameter(Mandatory)][scriptblock]$StopApplication,
        [Parameter(Mandatory)][scriptblock]$StartApplication,
        [Parameter(Mandatory)][scriptblock]$RestoreApplication
    )
    $ErrorActionPreference = 'Stop'
    $source = (Resolve-Path -LiteralPath $SourceDirectory).Path
    $backup = [IO.Path]::GetFullPath($BackupDirectory)
    if (Test-Path -LiteralPath $backup) { throw "Каталог резервной копии уже существует: $backup" }
    $names = @('StartupMonitor.Service.exe', 'StartupMonitor.UI.exe', 'app.ico', 'icon.svg')
    $hashes = @{}
    foreach ($name in $names) {
        $artifact = Get-Item -LiteralPath (Join-Path $source $name) -ErrorAction Stop
        $hashes[$name] = (Get-FileHash -LiteralPath $artifact.FullName -Algorithm SHA256 -ErrorAction Stop).Hash
    }
    $destinations = @($DestinationDirectories | ForEach-Object { [IO.Path]::GetFullPath($_) } | Select-Object -Unique)
    if ($destinations.Count -eq 0 -or $destinations -contains $source) { throw 'Не заданы независимые каталоги установки.' }
    $saved = @()
    # Все резервные файлы создаются до остановки приложения; ошибки здесь сохраняют работающую копию.
    for ($index = 0; $index -lt $destinations.Count; $index++) {
        $savedDirectory = Join-Path $backup $index
        New-Item -ItemType Directory -Path $savedDirectory -Force | Out-Null
        foreach ($name in $names) {
            $target = Join-Path $destinations[$index] $name
            $exists = Test-Path -LiteralPath $target -PathType Leaf
            $savedPath = Join-Path $savedDirectory $name
            $savedHash = $null
            if ($exists) {
                Copy-Item -LiteralPath $target -Destination $savedPath -Force
                $savedHash = (Get-FileHash -LiteralPath $savedPath -Algorithm SHA256 -ErrorAction Stop).Hash
                if ((Get-FileHash -LiteralPath $target -Algorithm SHA256 -ErrorAction Stop).Hash -ne $savedHash) {
                    throw "Не удалось подтвердить резервную копию $target."
                }
            }
            $saved += [pscustomobject]@{ Target = $target; Backup = $savedPath; Existed = $exists; Hash = $savedHash }
        }
    }
    try {
        & $StopApplication
        foreach ($destination in $destinations) {
            New-Item -ItemType Directory -Path $destination -Force | Out-Null
            foreach ($name in $names) {
                $target = Join-Path $destination $name
                Copy-StartupMonitorArtifact -Source (Join-Path $source $name) -Destination $target
                Unblock-File -LiteralPath $target -ErrorAction Stop
                if ((Get-FileHash -LiteralPath $target -Algorithm SHA256 -ErrorAction Stop).Hash -ne $hashes[$name]) {
                    throw "Не совпадает SHA-256 обновлённого файла $target."
                }
            }
        }
        & $StartApplication
    }
    catch {
        $updateFailure = $_
        $recoveryErrors = [Collections.Generic.List[string]]::new()
        # Новая служба могла частично запуститься: освобождаем бинарники перед восстановлением.
        try { & $StopApplication } catch { $recoveryErrors.Add("Остановка: $($_.Exception.Message)") }
        foreach ($item in $saved) {
            try {
                if ($item.Existed) {
                    Copy-StartupMonitorArtifact -Source $item.Backup -Destination $item.Target
                    if ((Get-FileHash -LiteralPath $item.Target -Algorithm SHA256 -ErrorAction Stop).Hash -ne $item.Hash) {
                        throw 'Не совпадает SHA-256 восстановленного файла.'
                    }
                }
                elseif (Test-Path -LiteralPath $item.Target -PathType Leaf) {
                    # Удаляется только конкретный созданный артефакт внутри заранее заданного каталога.
                    Remove-Item -LiteralPath $item.Target -Force
                }
            }
            catch { $recoveryErrors.Add("Восстановление $($item.Target): $($_.Exception.Message)") }
        }
        # Смешанная сборка не запускается: состояние службы восстанавливается только после подтверждённого отката файлов.
        if ($recoveryErrors.Count -eq 0) {
            try { & $RestoreApplication } catch { $recoveryErrors.Add("Восстановление SCM: $($_.Exception.Message)") }
        }
        if ($recoveryErrors.Count -gt 0) {
            throw "Обновление не выполнено: $($updateFailure.Exception.Message). Ошибки отката: $($recoveryErrors -join '; '). Резервная копия: $backup"
        }
        throw $updateFailure
    }
}

function Copy-StartupMonitorArtifact {
    <#
    .SYNOPSIS
    Копирует артефакт с ограниченным повтором только для кратковременной блокировки файла Windows.
    .PARAMETER Source
    Подготовленный исходный файл.
    .PARAMETER Destination
    Точный путь заменяемого артефакта.
    #>
    param([Parameter(Mandatory)][string]$Source, [Parameter(Mandatory)][string]$Destination)
    for ($attempt = 0; $attempt -lt 5; $attempt++) {
        try { Copy-Item -LiteralPath $Source -Destination $Destination -Force -ErrorAction Stop; return }
        catch {
            $code = $_.Exception.HResult -band 0xffff
            if ($attempt -eq 4 -or $code -notin @(32, 33)) { throw }
            Write-Verbose "Артефакт занят Windows; повтор копирования $Destination."
            Start-Sleep -Milliseconds (250 * ($attempt + 1))
        }
    }
}

function Start-StartupMonitorInterface {
    <#
    .SYNOPSIS
    Запускает установленный UI в интерактивной пользовательской сессии и активирует окно.
    .PARAMETER ExecutablePath
    Полный путь к проверяемому EXE. XML задачи сохраняет путь с пробелами без неоднозначного /tr.
    .PARAMETER EvidenceDirectory
    Каталог XML задачи и результатов диагностики запуска.
    .PARAMETER CloseAfterLaunch
    Проверяет штатный выход из UI; вызывающий код отдельно подтверждает продолжение работы службы.
    #>
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$ExecutablePath, [Parameter(Mandatory)][string]$EvidenceDirectory, [switch]$CloseAfterLaunch)
    $ErrorActionPreference = 'Stop'
    $exe = (Resolve-Path -LiteralPath $ExecutablePath).Path
    $interactiveUser = (Get-CimInstance Win32_ComputerSystem).UserName
    if ([string]::IsNullOrWhiteSpace($interactiveUser)) { throw 'Нет активной пользовательской сессии для запуска UI.' }
    $sid = ([Security.Principal.NTAccount]::new($interactiveUser)).Translate([Security.Principal.SecurityIdentifier]).Value
    $taskName = 'InteractiveLaunch_' + [Guid]::NewGuid().ToString('N').Substring(0, 8)
    New-Item -ItemType Directory -Path $EvidenceDirectory -Force | Out-Null
    $xmlPath = Join-Path $EvidenceDirectory ($taskName + '.xml')
    $resultPath = [IO.Path]::GetFullPath($xmlPath + '.window.json')
    $helper = Join-Path $PSScriptRoot 'launch-ui-interactive.ps1'
    $powershell = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
    $command = [Security.SecurityElement]::Escape($powershell)
    $helperArguments = '-NoProfile -NonInteractive -WindowStyle Hidden -ExecutionPolicy Bypass -File "' + $helper +
        '" -ExecutablePath "' + $exe + '" -ResultPath "' + $resultPath + '"'
    if ($CloseAfterLaunch) { $helperArguments += ' -CloseAfterLaunch' }
    $arguments = [Security.SecurityElement]::Escape($helperArguments)
    $directory = [Security.SecurityElement]::Escape([IO.Path]::GetDirectoryName($exe))
    $taskXml = @"
<?xml version="1.0" encoding="UTF-16"?>
<Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
  <Principals><Principal id="User"><UserId>$sid</UserId><LogonType>InteractiveToken</LogonType><RunLevel>HighestAvailable</RunLevel></Principal></Principals>
  <Settings><MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy><DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries><StopIfGoingOnBatteries>false</StopIfGoingOnBatteries><ExecutionTimeLimit>PT0S</ExecutionTimeLimit><Enabled>true</Enabled></Settings>
  <Actions Context="User"><Exec><Command>$command</Command><Arguments>$arguments</Arguments><WorkingDirectory>$directory</WorkingDirectory></Exec></Actions>
</Task>
"@
    [IO.File]::WriteAllText($xmlPath, $taskXml, [Text.Encoding]::Unicode)
    try {
        & schtasks.exe /create /tn $taskName /xml $xmlPath /f | Out-Null
        if ($LASTEXITCODE -ne 0) { throw 'Планировщик не создал задачу запуска UI.' }
        & schtasks.exe /run /tn $taskName | Out-Null
        if ($LASTEXITCODE -ne 0) { throw 'Планировщик не запустил задачу UI.' }
        for ($attempt = 0; $attempt -lt 100; $attempt++) {
            if (Test-Path -LiteralPath $resultPath) { break }
            Start-Sleep -Milliseconds 500
        }
        $info = Get-ScheduledTaskInfo -TaskName $taskName
        $info | Select-Object LastRunTime, LastTaskResult | ConvertTo-Json | Set-Content -LiteralPath ($xmlPath + '.result.json') -Encoding UTF8
        if (-not (Test-Path -LiteralPath $resultPath)) { throw "Нет результата интерактивного запуска; код планировщика: $($info.LastTaskResult)." }
        $evidence = Get-Content -LiteralPath $resultPath -Encoding UTF8 -Raw | ConvertFrom-Json
        if (-not $evidence.Success) { throw "Интерактивный запуск UI не выполнен: $($evidence.Error)" }
        # MainWindowHandle проверяет helper на Default, а консоль агента проверяет PID/путь/сессию без EnumWindows своего Desktop.
        if ($CloseAfterLaunch) {
            if (-not $evidence.Exited -or (Get-Process -Id $evidence.ProcessId -ErrorAction SilentlyContinue)) {
                throw 'Штатный выход проверяемого интерфейса не подтверждён.'
            }
        }
        else {
            $ui = Get-Process -Id $evidence.ProcessId -ErrorAction Stop
            if ($ui.Path -ne $exe -or $ui.SessionId -ne $evidence.SessionId -or $evidence.MainWindowHandle -eq 0) {
                throw 'Результат запуска не соответствует установленному пользовательскому процессу.'
            }
        }
        $evidence
    }
    finally {
        & schtasks.exe /delete /tn $taskName /f | Out-Null
        if ($LASTEXITCODE -ne 0) { Write-Warning "Не удалось удалить временную задачу $taskName." }
    }
}

Export-ModuleMember -Function Sync-StartupMonitorFiles, Start-StartupMonitorInterface
